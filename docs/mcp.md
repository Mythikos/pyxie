# External agents

In **Settings → Global**, turn on **Enable MCP access** and copy the Streamable HTTP endpoint. The Global section is available before importing a project. Use that endpoint in a local MCP client while Pyxie is running. The default launch profile uses `http://localhost:5280/mcp`.

For clients that use an `mcpServers` JSON configuration, the entry looks like this. Substitute the endpoint shown in Settings if the app uses a different port.

```json
{
  "mcpServers": {
    "pyxie": {
      "type": "http",
      "url": "http://localhost:5280/mcp"
    }
  }
}
```

MCP starts disabled on a new workspace. Enabling it allows local clients to perform operator actions without a token. Pyxie checks the connecting peer, HTTP host and any browser origin. Keep the endpoint on loopback and out of reverse proxies. The switch is persisted as `mcp.enabled` under the workspace root and applies across projects and restarts. Disabling it rejects subsequent requests. Pipeline work and image builds already started have their own stop and cancel controls.

## Workflows

| Task | Tools |
| --- | --- |
| Set up a repository | Start with `list_projects`. Use `import_repository` for an absolute local worktree path or `clone_repository` for an HTTP, HTTPS, SSH or Git URL. Import opens the new project and returns its `projectId` for subsequent calls. |
| Find feature ideas and queue one | Read branches, directories, files, commits and patches with the repository tools. Use `get_project_settings` for assignment IDs, then `create_card`. |
| Summarize the board | Page through `get_board` by passing `nextAfterId` as `afterId` until it is null. Each card includes its lane, sort order, assignments, attempts and history summary. Use `get_card` and `get_card_history` for details and round reports. |
| Review or close completed work | `list_completed_cards` filters by successful merge time and includes closed cards. `close_completed_cards` closes all currently Complete cards atomically. `close_card` closes one by ID. |
| Configure workers, gates and categories | Use `save_agent`, `save_gate`, `move_gate`, and the category tools. When editing or deleting an existing definition, pass its `revision` from `get_project_settings` as `expectedRevision`. For a DeepSeek API-key worker, select `DeepSeek` and `ApiKey` and omit the command and arguments to use the same defaults as Settings. |
| Configure harness credentials | `get_harness_setup` gives account and API-key setup instructions, paths, availability, and command defaults. Put the credential in the indicated machine-local source. Agent arguments are included in settings exports. |
| Prepare captures | `get_capture_setup` describes the base image and attachment contract. Inspect the current Dockerfile before proposing changes. If asked to apply them, call `save_dockerfile` with its version. Pass the ID returned by `build_project_image` to `get_image_build` to follow that build or to `cancel_image_build` to stop it. Put the capture request in the card description. |
| Read a capture | Use `list_card_media` to find the file. Pass `nextAfterFileId` as `afterFileId` for more attachments. Use `read_card_media` with the project, card and file IDs. Small images can be displayed directly. For larger files, read successive chunks using the returned byte offset, then verify the length and SHA-256 from the listing. |
| Transfer settings | `export_settings` returns the normal Pyxie settings file as text. Pass file text to `preview_settings_import`, then use `import_settings` with the same text and a map from each chosen section to its preview ID. Review the listed removals before replacing a section. |

To download a large recording directly, use `GET /media/{projectId}/files/{fileId}?card={cardId}&download=1` on the same Pyxie host with the IDs from the media listing. This endpoint supports HTTP byte ranges and checks the open project and owning card.

A project-scoped tool holds the named open project for its call. A stale project ID produces a tool error. The ordinary card, pipeline, project and settings services enforce the same lane, assignment, version and import rules as the UI. The project settings file contains the project's configuration. The global switch and machine-local credentials stay in the workspace.
