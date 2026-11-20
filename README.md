# Pyxie

Pyxie ("Pyx") is a locally run software factory for one application. People queue cards on a kanban board (Queue, In Progress, Gates, Review, Complete), and a pipeline carries each card through work by an agent, automated gates, and agent review, then merges it into the target branch, with nobody in the loop. A merge that conflicts goes to the project's merge agent, if you set one, and its resolution is gated and reviewed again before it merges; without one, the card's worker resolves it. Agents are your local harnesses (Claude Code, Codex, DeepSeek Harness, or other commands), with an authentication method selected per agent. Every round is recorded in the card's history.

- **Board**: every card has a name, a description, a category, a worker, an optional reviewer (without one, review is skipped), and an attempt limit (5 by default). Cards the pipeline stops (out of attempts, or blocked) wait at the top of the Queue in red until someone retries them.
- **Repository**: your projects. Each project is one application: import its repository from a local folder or a URL, open it, and open another to switch to it; closing keeps everything. Pyxie keeps its own bare clone, the hub, so your working folder is never changed. The hub is a git remote: clone it to work on the project, pull to get finished cards, and push your own commits so cards build on them. Browse files, history, and branches, or download a branch as a zip.
- **Settings**: the open project's categories, agents (each can work, review, or resolve merge conflicts, and only one can resolve them), gates (the commands, such as build, tests, and linters, that every card must pass before review), and the Dockerfile of its sandbox image.

The pipeline starts stopped every time Pyxie launches; start it from the header. Its design is in [docs/pipeline.md](docs/pipeline.md).

## Run

```
dotnet run --project src/Pyxie.Web
```

The app listens on <http://localhost:5280> and applies database migrations at startup. The `http` launch profile runs in the Development environment. Git must be on `PATH`, and the pipeline needs Docker running Linux containers (Docker Desktop on Windows): every round runs in a sandbox container ([agent sandboxes](docs/sandboxing.md)).

Agents run unattended in their sandbox, which is the boundary, so their arguments run the harness in print mode with every permission: `-p --dangerously-skip-permissions` for Claude Code and `exec --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check` for Codex. All harnesses receive the prompt on standard input.

In Settings → Agents, select **Account** or **API key** for Claude Code, Codex, or DeepSeek. For an account, save the token from `claude setup-token` in `secrets\claude-oauth-token`, sign into Codex with ChatGPT using `codex login` and file storage, or sign into DeepSeek with `dsh web`. For an API key, save the provider key in `secrets\anthropic-api-key`, `secrets\openai-api-key`, or `secrets\deepseek-api-key` under the workspace root. Settings → Sandbox shows which credentials are present. If both are available, the agent uses the selected method. A missing selected credential blocks the round.

## Configuration

To connect a local external agent, open **Settings → Global**, enable MCP access, and copy the Streamable HTTP endpoint into the client. This applies to all projects and works before opening one. See [external agent setup and workflows](docs/mcp.md).

Set in `src/Pyxie.Web/appsettings.json`, an environment-specific file, or environment variables (for example `Workspace__Root`).

| Key | Default | Purpose |
| --- | --- | --- |
| `Database:Provider` | `Sqlite` | Database provider. Unknown values fail at startup |
| `Pipeline:Workers` | `5` | Cards that run at once (1-32) |
| `Pipeline:RoundTimeoutMinutes` | `60` | How long an agent may run in one round (1-1440) |
| `Pipeline:PollIntervalSeconds` | `5` | How often idle workers look for work (1-300) |
| `Workspace:Root` | `%LOCALAPPDATA%\Pyxie` | Where Pyxie keeps its data: the machine database (`pyxie.db`, the list of projects), the sign-ins agents' sandboxes get (`secrets`), and one folder per project under `projects` (its database, hub, the host side of its card sandboxes, and round logs). Keep it outside every repository |
| `AllowedHosts` | `localhost;127.0.0.1;[::1]` | Host names the app answers to. Pyxie has no sign-in and runs commands, so it only answers local names, which stops other web pages reaching it through DNS rebinding |
| `Blazorise:ProductToken` | empty | Optional Blazorise product token; keep it in user secrets or an environment variable, not source control |

## Develop

```
dotnet build -warnaserror
dotnet test
dotnet format --verify-no-changes
```

See [AGENTS.md](AGENTS.md) and [docs/coding-standards.md](docs/coding-standards.md).
