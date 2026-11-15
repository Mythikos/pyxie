# Pyxie agent guide

Pyxie ("Pyx") is a locally run web app that manages a software-factory pipeline as a kanban board. Each project is one application with its own folder; one project is open at a time. Keep this file short; update the owning document when behavior or setup changes.

## Required guidance

- Follow [coding standards](docs/coding-standards.md), including one type per file and folders that mirror namespaces.
- Use Context7 for current library documentation (Blazorise v2, EF Core 10, xUnit v3) instead of relying on memory.

## Layout

| Location | Ownership |
| --- | --- |
| `src/Pyxie.Data/` | Entities, `ProjectDbContext` and `MachineDbContext` and their configuration, service interfaces and implementations, result and input types. Depends on EF Core only |
| `src/Pyxie.Data.Sqlite/` | SQLite provider registration, the open project's context factory, design-time factories, and EF migrations. Each provider gets its own assembly |
| `src/Pyxie.Runtime/` | Everything that touches the machine: processes, git, the sandboxes rounds run in (`Sandboxes/`, with Pyxie's base image), card rounds in them (`Workspaces/`), the harness, gate, and merge runners, repository setup and browsing, and the hosted pipeline (`Orchestration/`) |
| `src/Pyxie.Web/` | Blazor Server composition root (`Hosting/`) and Blazorise Ant Design UI (`Components/`) |
| `tests/Pyxie.Tests/` | xUnit v3 tests: services, persistence, runtime, and convention checks |
| `tests/Pyxie.FakeHarness/` | A stand-in agent harness the runtime tests run instead of a real agent |
| `docs/` | Coding standards, the [pipeline design](docs/pipeline.md), the [agent sandboxes](docs/sandboxing.md), and the [design system](docs/design/README.md) with its reference screens |

## Product decisions

- Five board lanes: Queue, In Progress, Gates, Review, Complete, plus Closed as history only. People add, edit, delete, and retry cards in the Queue and close out Complete cards; every other move belongs to the pipeline ([pipeline design](docs/pipeline.md)). A card requires a Name, a Description, a Category, and a Worker; the Reviewer is optional, and without one review is skipped and recorded as skipped. Each card has an attempt limit (default 5, at most 10).
- A card is Ready or in Error (`CardState`). The pipeline stops a card that used its last attempt or was blocked: it goes to the top of the Queue in Error, the board marks it red, and the pipeline skips it until a person retries it (`ICardService.RetryAsync`), which gives it a fresh set of attempts and resumes it where it stopped. Editing a stopped card makes it start again at work.
- Every work, gate, review, and resolve round runs in a sandbox, a Docker Linux container ([agent sandboxes](docs/sandboxing.md)), never on the host; the pipeline needs Docker and has no fallback. The card's work is on `pyxie/card-<id>` in the hub, handed into the sandbox and taken back as git bundles; no agent writes to the hub or reaches Pyxie. When the gates and the review pass on the same commit, Pyxie merges the card's branch into the target branch itself, as `Pyxie <pyxie@localhost>` with a `Pyxie-Card: <id>` trailer that the Repository history reads; the Gates and Review lanes are the pull request. Nothing is pushed to or fetched from `origin` yet. Runtime tests use the local stand-in sandboxes in `tests/Pyxie.Tests/Support`.
- A merge conflict uses an attempt and goes to the project's merge agent in a Resolve round (in In Progress), or to the card's worker when the project has none. A resolution is new agent-written code, so it goes through the gates and the review again before Pyxie merges; no agent can bypass them, and only Pyxie writes the target branch ([pipeline design](docs/pipeline.md)).
- Projects: the machine database (`MachineDbContext`, `<root>\pyxie.db`) lists the projects and which one is open, and will hold other machine-level settings. Everything about a project lives in its folder, `<root>\projects\<name>\`: its database (`ProjectDbContext`: agents, categories, gates, cards and their history, and the repository it came from), its sandbox settings (the project image's Dockerfile), the hub, the host side of card sandboxes, and round logs. Agents, categories, and gates are per project. One project is open at a time and the pipeline runs for it; projects are opened and closed on the Repository page, only while the pipeline is stopped, and closing keeps everything. Data services reach the open project's database through `IDbContextFactory<ProjectDbContext>`, which refuses when no project is open.
- Each project has one repository, imported on the Repository page from a local folder or a URL as a bare clone in the project folder, the hub. Pyxie only ever writes to the hub; the source is never changed. The hub keeps no URL for the source; the project records where it came from, without credentials (`Project.Source`), to show it. The hub is a git remote people clone, pull finished work from, and push their own commits to. People rewrite and delete their own branches as they like; the hub's update hook refuses force-pushes to and deletion of the target branch, and every push to `pyxie/` branches. Gate commands are managed in Settings. The pipeline starts stopped on every launch and is started and stopped from the header, which shows how many of the `Pipeline:Workers` workers have a card.
- Users edit the Category list, each category with a color (Neutral, Green, or Pink), and the Agent list. Agents are local harnesses (Claude Code, Codex, OpenCode, Other) with Name, Harness, Command, Arguments, CanWork, CanReview, CanMerge; one shared list serves all three roles. At most one agent per project has CanMerge, chosen in the Settings agents table, which moves it in one write (`IAgentService.SetMergeAgentAsync`; `AgentService` and a unique index enforce it): the merge agent, which resolves merge conflicts. It can be deleted or lose the flag at any time, and conflicts then go to the worker. The pipeline runs them unattended in the card's sandbox, with the command the sandbox image provides; their Arguments carry the print mode and permission flags, which are the user's choice (the defaults grant every permission, since the sandbox is the boundary), and can use tokens such as `{CARD_NAME}` that each round fills in ([pipeline design](docs/pipeline.md)).
- Deleting a category still referenced by cards (closed ones count) is refused with a clear message, as a typed result (`Cards.CategoryId` is NOT NULL with a Restrict foreign key). An agent cannot be deleted, or lose its CanWork/CanReview role, while a card that is not complete uses it in that role: every unfinished card needs its worker, and clearing a reviewer would silently skip a review. CanMerge has no such protection. Otherwise the reference is cleared on complete and closed cards, bumping `UpdatedUtc` and `Version` in the same transaction (the FKs are SET NULL and the service also clears explicitly). History is unaffected because rounds keep their own agent snapshot.
- The pipeline drives cards through `ICardPipelineService` by reporting round results; `PipelineRules` is the state machine. Nothing else moves a card between lanes: there is no `MoveAsync`, no drag and drop, and no UI action that changes lanes except Add, Close, and the pipeline's own operations. New cards join the bottom of the Queue and the pipeline takes from the top (FIFO). Add exists only on the Queue lane.
- Card history is append-only `CardRound` rows: kind (work, gate, review, merge, or resolve), a snapshot of the agent that ran it, the briefing, start and end, outcome, note, and report. `ICardService.GetHistoryAsync` returns it, and the board shows each card's latest 20 rounds as a trail.
- Writes are conditional so a card changed by someone else mid-request is refused with a typed result, never overwritten. Pipeline operations write the card only while `WorkCard.Version` is unchanged, as the first statement of their transaction; `UpdateAsync`, `DeleteAsync`, `RetryAsync`, and `CloseAsync` also write only while the card is still in the lane (and state) they checked, and every write that changes a card increments `Version`.
- Editing and deleting a card are locked outside the Queue: once the pipeline picks a card up, people can only open it read-only to review it. `CardLifecycleRules` owns the rule, `CardService` refuses `UpdateAsync` and `DeleteAsync` with a typed result, and the card dialog reads the same rule. Stopping or intercepting a single card's run is future work; stopping the whole pipeline interrupts every running round, which resumes when it starts again.
- People close out a Complete card with `ICardService.CloseAsync`: it leaves the board and stays in the database as history (`CardLane.Closed`, `ClosedUtc`). Closed is not a board lane, so the board renders `CardLifecycleRules.BoardLanes`, never every `CardLane` value.
- A worker must have `CanWork`; a reviewer must have `CanReview`. Both are validated in the service.
- Name, Description, Category, and Worker are required on every card. `CardInputRules` and `CardService` reject blank or missing values on create and update, and `Cards.CategoryId` is NOT NULL. The card editor lists None first in Category, Worker, and Reviewer; None means unassigned for the reviewer, and for Category and Worker it blocks saving with an inline message.
- Tests never start a real agent harness: runtime tests use `tests/Pyxie.FakeHarness` and git repositories they create in temporary folders.
- Category and agent names are unique, case-insensitively, enforced by the services.
- Timestamps are UTC `DateTime`. Default categories Feature, Bug, and Chore are seeded; no agents are seeded.
- Components use services that create contexts from `IDbContextFactory<ProjectDbContext>` (or `IDbContextFactory<MachineDbContext>`); never a circuit-scoped `DbContext`.

## Working rules

- Choose the correct, maintainable solution. Do not let implementation time or effort decide the design; validate risk with tests.
- One type per file; folders and namespaces mirror each other. Nested helper types are not allowed. The convention tests enforce this; do not exempt files to make them pass.
- Expected failures return `ServiceResult` / `ServiceResult<T>`, not exceptions.
- Do not initialize Git, commit, or push without explicit authorization. Do not add attribution or generated-by text anywhere.
- Never hardcode the Blazorise product token; it comes from `Blazorise:ProductToken` configuration.
- Before handoff run `dotnet build -warnaserror`, `dotnet test`, and `dotnet format --verify-no-changes`, and report exactly what passed, failed, or could not run. After a change to sandboxes, run `dotnet test` with `PYXIE_DOCKER_TESTS=1` too, so the real Docker tests run.
- Pyxie has no deployed data yet, so after a model change the migrations are regenerated as a single `InitialCreate` per context (`Migrations/Machine` and `Migrations/Project` in `src/Pyxie.Data.Sqlite`) until the first real deployment. Generated migrations are never hand-edited.

## UI restraint

- Default to concise labels, relevant controls, and necessary state. Let the layout and interaction communicate the workflow.
- Do not add subtitles, instructional footers, helper paragraphs, repeated headings, or explanations of obvious controls by default. Additional text must resolve a specific ambiguity or help the user make a current decision.
- Prefer contextual validation over permanently listing rules. Keep clear labels, accessibility, actionable errors, and consequential warnings.
- Every additional action must serve a distinct need. Do not add redundant buttons, presets, or shortcuts.
- Before handoff, review every caption and secondary action: if removing it leaves the task, state, and next step equally clear, remove it. Treat removed text and actions as lasting decisions.
