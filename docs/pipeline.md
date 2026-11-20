# Pipeline

Agent rounds receive immutable card [media](media.md) snapshots and can publish files independently of their work verdict. User requests for screenshots or recordings are ordinary description instructions; missing media introduces no new lane, outcome, or completion gate. Publications are retained with their producing round, including unsuccessful and interrupted rounds, and the latest outputs are supplied to later agents.

Pyxie is a software factory: people queue work, and the pipeline carries each card through work, automated gates, agent review, and a merge into the target branch, with no person in the loop. A merge that conflicts is resolved by the project's merge agent, when it has one, and the resolution is gated and reviewed again before it merges. Agents never move cards. The pipeline invokes them, reports what happened, and Pyxie's state machine decides where the card goes and records the history.

This document is the design contract. `PipelineRules`, `CardPipelineService`, `CardBriefingBuilder`, `PipelineContract`, and the runtime in `Pyxie.Runtime` implement it; change them and this document together.

## Lanes

| Lane | Who acts | Rounds that run |
| --- | --- | --- |
| Queue | People add, edit, delete, and retry cards | None |
| In Progress | The worker agent, or the merge agent resolving a conflict | Work, Resolve |
| Gates | Automated checks: tests, linters, analysis | Gate |
| Review | The reviewer agent, then Pyxie merges | Review, Merge |
| Complete | People close cards out | None |
| Closed | Nobody; history only, not on the board | None |

People can edit, delete, and retry a card only in the Queue and close it out only from Complete (`CardLifecycleRules`). In the active lanes (In Progress, Gates, Review) the pipeline owns the card and people can only open it to review it. Cheap automated gates run before the agent review, so a broken build never costs a review round. The Gates and Review lanes together are the card's pull request: when both pass, Pyxie merges the card's branch. A resolution is new agent-written code on the card, so a Resolve round runs in In Progress and the card goes through Gates and Review again.

## Card state

A card is `Ready` or in `Error` (`CardState`). Only Queue cards can be in Error. The pipeline claims Ready cards from the top of the Queue and skips cards in Error, which wait for a person:

- The pipeline stops a card by moving it to the **top** of the Queue in Error. It was claimed before the cards behind it, so once retried it runs next. The board marks it in red with the reason.
- **Retry** (`ICardService.RetryAsync`) makes it Ready again with a fresh set of attempts. It resumes where it stopped (`WorkCard.ResumeRound`): the stage that was blocked or kept erroring, Resolve after a run of send-backs that ended in a merge conflict handed to the merge agent, or Work after any other run of send-backs.
- Editing a stopped card's task (name, description, category, or agents) clears `ResumeRound`, so a changed task always starts again at Work; saving it unchanged, or with only a new attempt limit, keeps it.

## Attempts

Each card has an attempt limit (`WorkCard.AttemptLimit`, default 5, at most 10), set when it is created or edited. `WorkCard.Attempt` counts attempts in the card's current run: a claim from the Queue starts attempt 1 and Retry starts a new run. Each round records the attempt it belongs to.

An attempt is used up by every setback: a send-back (gate failure, rejection, merge conflict) and every recoverable error. A setback opens the next attempt (a new work round after a gate failure or a rejection, a new resolve round after a merge conflict when the project has a merge agent and a new work round when it has none, a new round of the same kind after an error) until the attempt that just ended was the last one, and then the card stops in Error. A merge conflict uses an attempt like any other send-back, whether the worker or the merge agent takes it on. Nothing else uses an attempt: interruptions, skipped stages, and a stale base (Outdated) do not.

So a card cannot make the harnesses run without bound: each run has at most `AttemptLimit` work and resolve rounds together and as many review rounds, and only a person's Retry starts another run.

## Rounds

Every pass through a stage is a round (`CardRound`), appended to the card's history and never edited afterwards. A round records:

- its kind (work, gate, review, merge, resolve), its sequence in the card's history, and its attempt;
- a copy of the agent that ran it (name, harness, authentication method, command, arguments), taken when the round opened (the card's worker, its reviewer, or the project's merge agent), so history survives agents being renamed, reconfigured, or deleted, and the pipeline invokes exactly what it recorded, with the arguments kept as configured and their tokens filled in when the round runs;
- the briefing the agent was given (work, review, and resolve rounds);
- start and end times, the outcome, a short note, and a report (the worker's handoff, the gate output, the reviewer's findings, the merge result, or the merge agent's handoff followed by the resolution as a diff).

A card has at most one open round, and only while a pipeline worker owns it.

## State machine

The pipeline reports a `RoundResult` for the round it owns. `PipelineRules.OutcomeOf` turns it into the round's outcome, and `PipelineRules.Next` decides what follows from the outcome and the card's progress (`CardProgress`: attempt, limit, whether any gate is enabled, whether a reviewer is assigned, whether the project has a merge agent, and whether the current work was already approved).

| Round | Result | Outcome | Next |
| --- | --- | --- | --- |
| Work | Succeeded | Completed | Gate round |
| Gate | Succeeded | Passed | Review round, or Merge round when the current work was already approved |
| Gate | Failed | Failed | Setback: Work round |
| Review | Succeeded | Approved | Merge round |
| Review | Failed | Rejected | Setback: Work round |
| Merge | Succeeded | Merged | Complete |
| Merge | Failed | Conflicted | Setback: Resolve round when the project has a merge agent, otherwise Work round |
| Merge | Outdated | Outdated | Gate round |
| Resolve | Succeeded | Resolved | Gate round |
| Any | Errored | Errored | Setback: a round of the same kind |
| Any | Blocked | Blocked | Stop in Error at once, resuming this stage |

- A setback on the card's last attempt stops it in Error instead (resuming Resolve after a conflict handed to the merge agent, Work after any other send-back, the same stage after an error).
- A work or resolve round cannot fail: a worker or merge agent that cannot finish reports `Errored` (worth another attempt) or `Blocked` (needs a person). `Outdated` is only valid for merge rounds.
- `Failed`, `Rejected`, `Conflicted`, `Errored`, and `Blocked` require a note, which shows in history and leads the next briefing. A resolve round that succeeded needs none; its note is the agent's one line for history.
- **Approval.** A review approves the work as it stands. A work round or a resolve round changes the work, so after either the card goes through the review again even when an earlier review approved it: after a conflict the card goes Resolve, Gate, Review, Merge. Pyxie's own clean merge of a target branch that moved (Outdated) is no agent's code, so it keeps the approval and only the gates run again. No agent can bypass the gates or the review, and only Pyxie writes the target branch.
- **Merge agent.** The project's merge agent is the agent with `CanMerge`; at most one agent has it, which the agent service enforces and a unique index backs. Nothing protects it: it can be deleted or lose the flag at any time. A conflict that finds the project without a merge agent goes to the card's worker as a Work round (`PipelineRules.Next`), as in a project that never had one. A card that was waiting at Resolve when the merge agent was removed (it stopped there and was retried, its Resolve error is tried again, or a Resolve round it was interrupted in is resumed) has its Resolve stage open as a Work round instead (`PipelineRules.KindToOpen`). A resolve round that is already running keeps the agent copy it recorded and finishes.
- **Skipped stages.** A gate round opens only when at least one gate is enabled, and a review round only when the card has a reviewer. Otherwise the stage is recorded as a `Skipped` round with the reason ("No gates are enabled.", "No reviewer is assigned.") and the machine moves on as if it had passed or been approved. A card without a reviewer goes from the gates straight to the merge.
- **Worker required.** Every card has a worker (`CardInputRules`), and an agent cannot be deleted or lose its work or review role while a card that is not complete uses it in that role. If a work round would still open without a worker, it is recorded as `Blocked` and the card stops.
- **Interrupted.** `RecoverInterruptedAsync` (at start-up) and `InterruptAsync` (when the pipeline is stopped or Pyxie shuts down) close running rounds as `Interrupted`. The card stays in its lane, and `ClaimNextAsync` resumes it at the same stage and attempt before taking new work (an interrupted Resolve round resumes as a Work round when the project no longer has a merge agent).

## Errors

The runtime decides whether a failure is worth another attempt:

| Errored (uses an attempt, retried automatically) | Blocked (stops the card at once) |
| --- | --- |
| The harness exited with a non-zero code or timed out | The harness or a gate command could not start (not found, bad configuration) |
| The agent wrote no result file, an unreadable one, or reported `errored` | The agent reported `blocked`: it needs a person, for example the task is unclear, or the card's change and the target branch's cannot both hold |
| Committing the worker's or merge agent's changes failed (for example a hook rejected them) | No repository is connected, or the card's sandbox cannot be made (its image does not build, its agent has no sign-in, or the merge of a resolve round cannot be started in it) |
| The worker's or merge agent's commits could not be taken back into the hub | git failed on the card's branch or the target branch (for example the card's branch is gone) |
| A resolution failed a check on the way back into the hub: the merge was not completed, it is not one merge commit of the card's branch and the target branch, or conflict markers are left in the files that conflicted | |

A gate that exits with a non-zero code or times out is a gate failure (`Failed`), not an error: the worker gets its output.

## Workspaces and the merge

Each project has one repository (`Repository`), and everything about the project lives in its folder, `<Workspace:Root>\projects\<name>\`: its database (agents, categories, gates, cards and their history and sandbox settings), the hub, the host side of each card's sandboxes, and round logs. The machine database (`<Workspace:Root>\pyxie.db`) lists the projects and which one is open. One project is open at a time and the pipeline runs for it; a project is opened or closed only while the pipeline is stopped, opening one while another is open closes that one first, and closing keeps everything, so a project's cards and its code never come apart.

Importing a local folder or a URL creates a project: Pyxie clones the source as a bare repository in the project folder, the **hub**, and works only there: the folder you work in is never changed (the hub shares no files with it). Imports run in the background (`RepositoryImports`), so leaving the Repository page does not stop one. The folder or URL is recorded as the project's origin, but the hub keeps no URL for it, and nothing is pushed to or fetched from it yet. Agents never reach the hub: every agent round runs in a sandbox with a repository of its own, and the card's work comes back as a bundle Pyxie checks and fetches ([agent sandboxes](sandboxing.md)). Card work leaves the hub only through Pyxie's merge, and people pull it from there. The target branch, where finished work lands, is the branch checked out in the source when it was imported.

The hub is an ordinary git remote. It has no files of its own: cards work in their own sandboxes, and the Repository page reads from git. People work in clones of it, like any shared repository:

- `git clone <hub>` (the Repository page shows the hub's path, with a Copy button) gives the whole project with its history, and `git pull` brings in cards as they are merged.
- `git push` to the hub brings a person's own commits in. A card whose merge finds the target branch moved on merges it in and goes back through the gates (after the conflict is resolved, if there is one), so the next cards build on those commits.
- People own their branches in the hub: they create, force-push, and delete them as they like. The hub's `update` hook (`HubHooks`) keeps two kinds safe: the target branch only moves forward and cannot be deleted, because card branches are built on it, and branches under `pyxie/` take no pushes at all, because they belong to the pipeline. Pyxie's own merges and branch cleanup are not pushes, so the hook never sees them. The hub names its own hooks folder (`core.hooksPath`), so a hooks folder in someone's global git config cannot switch the protection off.
- The Repository page's History follows a branch's first parent, so it lists what landed on the branch: on the target branch, each card is the one merge commit that landed it, among people's own commits. A merge's parents lead to the commits it brought in.
- The Repository page also downloads a branch's files as a zip, for a quick look without git.

- Every work, gate, review, and resolve round runs in a sandbox, a Docker Linux container with the card's repository in it ([agent sandboxes](sandboxing.md)). The card's work is on branch `pyxie/card-<id>` in the hub (`PipelineContract.BranchName`), created from the target branch by its first work round. Pyxie hands the card in as a bundle and takes the agent's commits back as one; no agent ever writes to the hub, and the pipeline does not run while Docker cannot be reached.
- A card's worker sandbox is kept between its work rounds; gate, review, and resolve rounds each get a fresh sandbox on the card's last commit, so gates check and do not fix, nothing a reviewer changes is kept, and a resolution always starts from the card's last commit. The card's repository has a `.pyxie` folder for the briefing and the result file, which is never committed.
- After a work round, Pyxie commits whatever the worker left, inside the sandbox, and takes the commits back into the hub only if they build on the commit the round started from.
- **Merge round.** In the hub, without a sandbox or a checkout. When the card's branch already contains the target branch, Pyxie integrates it as a merge commit on the target branch, moved into place with a compare-and-swap, so a branch that moved meanwhile is reported as `Outdated`; moving the branch is the whole merge, one atomic step, so a crash leaves it either done or not started. Otherwise Pyxie merges the target branch into the card's branch with `git merge-tree`: a conflict is a setback (`Conflicted`, below); a clean merge becomes a merge commit on the card's branch, and since the combination has not been tested the card goes back through the gates (`Outdated`) without another review.
- **Pyxie's commits.** Every commit Pyxie writes, in the hub or in a sandbox, is authored and committed as `Pyxie <pyxie@localhost>` (`PyxieIdentity`), whatever identity the repository or the machine has configured. The merge commit that lands a card on the target branch reads `Merge <branch>: <card name>` and ends with a `Pyxie-Card: <id>` trailer (`PipelineContract.CardTrailer`); git reads the trailer back into `GitCommit.CardId`, which is how the conflict report and the Repository history know which card a commit landed. Nothing parses the subject. The Repository history marks Pyxie's merge commits, those that land cards and those that merge the target branch into a card's branch, by her identity and their parents (`GitCommit.IsPyxieMerge`), because a card's other commits carry her identity too: an agent's sandbox is set up with it, and Pyxie commits what an agent left uncommitted.
- **Conflicts.** The report of a conflicted merge lists the conflicting files and what landed on the target branch since the card's branch last took it in: the commits the target branch gained (first-parent history, up to 30 entries, one line each) and, for every commit with a `Pyxie-Card` trailer, that card's name and task, so whoever resolves it can tell what the other side was for. The report does not say what to do about it; the briefing of the next round does. With a merge agent the setback opens a Resolve round. Without one it opens a Work round, and the worker merges the target branch in its sandbox.
- **Resolve round.** The merge agent works in a fresh sandbox on the card's last commit; nothing an earlier attempt left is reused. Pyxie starts the merge there: the target branch, at the commit Pyxie read when it opened the sandbox, is merged into the card's branch without committing, so the working tree holds the conflicts. Commits that land on the target branch meanwhile wait for the next merge round. The agent resolves the conflicts as its briefing asks, and when it finishes Pyxie ends what it left running and commits the merge unless the agent already did. The merge must come back as exactly one merge commit, so before the card's branch moves, Pyxie checks in the hub that the merge was completed, that the commit's parents are the card's last commit and the pinned target commit and nothing else, and that none of the files that conflicted still holds conflict markers (a marker line that stood in either parent's version of the file does not count). A failed check is an `Errored` round with the reason, and the next resolve round starts over. Otherwise the branch moves to the merge commit with a compare-and-swap and the round is `Resolved`. Its report is the agent's handoff followed by the resolution as `git show --remerge-diff` shows it in the hub, which is how each conflict was resolved compared with what git produced on its own, clipped with a marker when it is very long. That diff is what the reviewer checks next. The agent's note is the round's note, with a default when it left none. A resolve round that finds the target branch already merged into the card's branch (an earlier resolve round moved the branch and was cut off before its result was recorded) runs no agent: it is `Resolved` with the same report, and the gates and the review check the merge as they would have.
- Integrations run one at a time. After a merge, the card's sandboxes, their volumes, and its branch are removed. What cards that were deleted or finished left behind is removed when the pipeline starts. So are the sandbox images and build cache Pyxie's image builds left that nothing needs, which also go while the pipeline is stopped, after a build ([agent sandboxes](sandboxing.md#images)).
- A gate, review, resolve, or merge round never starts a new branch: if the card's branch is missing, there is no work to check and the round is blocked.
- A git command that changes the hub is never cancelled part-way (stopping the pipeline waits for it, within its own timeout), because git killed mid-write leaves locks and half-written files. A command in a sandbox that times out or is stopped spoils the sandbox, which is removed; a worker's volumes survive, so the next attempt carries on from what it left, minus a merge left half-done. A resolve sandbox has no volumes, so its next attempt starts over.
- Nothing is synchronized with `origin` yet: merged work stays in the hub, and new commits in the source do not reach it (see Open decisions). People move work in and out through the hub.

## Briefings

When a work, review, or resolve round opens, `CardBriefingBuilder` derives the agent's context from the card and its finished rounds. Nobody maintains it by hand.

- Every briefing: the card name, category, stage, attempt (of the limit), the task (description), the workspace (branch and target branch), one line per earlier round (the latest 20), and how to report the result.
- Work rounds: the latest setback (its note in full and its report), so the worker sees the exact failing output, findings, or conflicting files; and how the previous attempt ended if it errored, was blocked, or was interrupted. When the latest setback is a merge conflict, which reaches a worker only when the project has no merge agent, the briefing also tells the worker to merge the target branch with `git merge <target>`, resolve the conflicts so the card's change and the target branch's changes both still hold, and finish the merge; Pyxie commits it with the rest of the work. The line is left out once a completed work round or a resolution has followed the conflict, since the merge is then done (a person who edits a card that stopped after its merge was resolved is not asked to merge again). A gate failure or a rejection gets no such line.
- Resolve rounds: a narrow brief. The workspace says Pyxie started merging the target branch into the card's branch and stopped on conflicts (`git status` lists them), and asks the agent to resolve them so both sides still hold, keeping the intent of both and adding nothing else (fixing what the combination breaks is part of the merge). Pyxie commits the merge when the agent finishes, or the agent can run `git commit --no-edit`; it must stay one merge commit, so no aborting, starting over, rebasing, other commits, or pushing. Then "Why the merge stopped" (the latest conflicted merge round: its note and report, with the conflicting files and what landed on the target branch together with the names and tasks of the cards that landed it), "The card's work" (the worker's latest handoff, so the agent understands the card's side), and how the previous resolve attempt ended if it errored, was blocked, or was interrupted. The agent reports `resolved`, `errored`, or `blocked` (the target branch's changes and the card's cannot both hold).
- Review rounds: what there is to review, the gate results it passed, and the previous rejection, so the reviewer can check the findings were addressed. What there is to review is the worker's latest handoff ("Work to review") or, when the card's latest change is a resolution, "Merge to review": after the card's work was approved, the target branch moved on and conflicted, and the merge agent resolved it in the merge commit that is the branch's last commit; `git show --remerge-diff HEAD` shows how each conflict was resolved, and the reviewer checks that both sides still hold. "The resolution" follows (the resolve round's note and report, which include that diff), then "The card's work" for reference.
- A quoted report arrives in full up to 15,000 characters. A longer one keeps its first 5,000 characters and its end, where gate output usually puts its summary, with a marker saying how much of the middle was cut. A review of a resolution quotes four reports instead of three, so there the resolution keeps the full 15,000 and the work, the gate results, and the previous rejection are held to 10,000 (the first 4,000 and the end). The whole briefing is capped at `PyxieLimits.MaxBriefingLength`, so it stays bounded after many loops.
- Repository conventions (`CLAUDE.md`, `AGENTS.md`) are not repeated: the harness loads them from the card's repository. A reviewer gets the target branch's version of the files that steer a harness, so a worker cannot steer its own review.

The briefing is stored on the round, so history shows exactly what each agent knew.

## Harness contract

`PipelineContract` owns the file names, branch names, prompt, and result words; the briefing and the runtime both read it.

- Pyxie writes the briefing to `.pyxie/briefing.md` in the card's repository in its sandbox and starts the agent's command with its arguments there; the command is the one the sandbox's image provides. The prompt is short and fixed: read the briefing and write the result. All harnesses read it on standard input. Long context never travels on the command line.
- An agent's arguments can carry tokens that Pyxie fills in for each round: `{CARD_ID}`, `{CARD_NAME}`, `{BRANCH}` (the card's branch, `pyxie/card-<id>`), `{STAGE}` (Work, Review, or Resolve), and `{ATTEMPT}`. A Claude Code agent with `--name "{CARD_NAME}"`, for example, names its session after the card. The arguments are split first and each argument is filled in once, so a value with spaces or quotes stays one argument and a value is never filled in again; a token can sit inside an argument, and `--name={CARD_NAME}` keeps a card name that starts with `-` the flag's value. A value is not quoted for a shell, so a `sh -c` script takes it as one of the script's own arguments (`sh -c "... \"$1\"" sh {CARD_NAME}`). `${NAME}` is left for a shell; any other name in capitals between braces is refused when the agent is saved and blocks a round that would run it. `AgentArgumentTokens` owns the list.
- Permission flags are the agent's own configuration: an agent whose arguments do not grant a non-interactive permission mode will have its tool calls denied.
- The agent writes `.pyxie/result.json`: `{ "outcome": "...", "note": "...", "report": "..." }`. Work rounds use `completed`, `errored`, or `blocked`; review rounds use `approved`, `rejected`, `errored`, or `blocked`; resolve rounds use `resolved`, `errored`, or `blocked`. A missing or unreadable file is an error.
- Each round has a timeout (`Pipeline:RoundTimeoutMinutes`, default 60); each gate has its own. Timed-out and stopped processes are killed with their children. On Windows every process Pyxie starts belongs to a job that ends with Pyxie, so no agent or gate outlives it, even when Pyxie is killed. The output of every round is kept in the project folder's `runs` folder, up to a size limit; a log that cannot be written never stops the round.

## Gates

Gates are commands people manage in Settings: a name, a command and arguments, a timeout, and whether it is enabled. A gate round runs the enabled gates in order in a fresh sandbox on the card's last commit and stops at the first that exits with a non-zero code or times out. The report lists each gate's result and quotes the failing gate's output.

## Concurrency

Several pipeline workers run at once, and people edit, delete, and close cards meanwhile. `WorkCard.Version` increments with every write that changes a card's lane, state, fields, agent assignment, or rounds (renumbering `SortOrder` alone does not). Pipeline operations read the version, check their preconditions, and then, as the first statement of their transaction, write the card only if the version is unchanged. That write also takes the row's lock until commit. A writer that loses matches no row and re-reads, so two workers never claim the same card, a stale worker never records into a newer round, and a person's edit and a claim never both apply. People's edits keep their lane conditions (`UpdateAsync`, `DeleteAsync`, `RetryAsync`, and `CloseAsync` write only while the card is still in the lane and state they checked) and also increment the version.

## Service API

`ICardPipelineService` is the pipeline's only way in:

| Operation | Use |
| --- | --- |
| `RecoverInterruptedAsync` | Once at start-up, before any worker runs |
| `ClaimNextAsync` | A free worker takes the next card: interrupted cards first (later stages first), then the top Ready card of the Queue. Returns nothing while no repository is connected |
| `RecordResultAsync(roundId, result, report)` | Report the result of the round the worker owns; returns the next round for the same worker, or none when the card's run ends (complete or stopped) |
| `InterruptAsync(roundId, note)` | Give back a round that is running when the pipeline is stopped or Pyxie shuts down |

`ICardService` is for people: create, edit, delete, retry, close, the board (with each card's history summary), and a card's full history.

## Orchestrator

`PipelineOrchestrator` is a hosted service in `Pyxie.Runtime`. It starts **stopped** every time Pyxie launches, so starting the app never spends harness usage; the header's Start and Stop control it. A failure it cannot handle (the database is unavailable, say) is logged and retried; it never stops the web app. While started and a repository is connected, it keeps up to `Pipeline:Workers` cards running at once (default 5), and the header shows how many workers have a card (`PipelineController.RunningRounds`) out of that number. Each worker claims a card and runs its rounds one after another (agent, gates, or merge) until the card's run ends. Stopping kills running rounds and records them as interrupted; starting again resumes them first. The board refreshes as the pipeline moves cards.

## Open decisions

- Sandboxes for projects that need Windows ([agent sandboxes](sandboxing.md)).
- Synchronizing the hub with `origin` when it is a remote such as GitHub: fetching its new commits before work starts and pushing finished cards to it (directly, or as a branch and pull request where the target branch is protected), with the person's own git credentials. A folder source stays a one-time import.
- Intercepting or stopping a single card's run while the pipeline keeps going.
- Showing a running round's live output.
- Keeping the target branch's first-parent line. A person who merges the hub's target branch into their own commits (a `git pull` that merges) and pushes makes their side the first parent, so the cards that landed meanwhile drop out of the Repository history and of the conflict report's list of what landed. The hub's `update` hook could refuse such a push, at the cost of people rebasing instead.
