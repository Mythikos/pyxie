# Card media

Cards accept files as context and retain files published by their agents. Media appears above History in the card dialog. Context can be added or removed only while the card is in Queue. Uploads are staged until Save; Cancel discards the editor session. Save applies fields and the complete context selection in one version-checked transaction. A changed selection restarts stopped work at Work. An upload failure must be dismissed before saving without that file.

Images, videos, and audio with recognized signatures can be previewed; other files download. Outputs are grouped by producing round, newest first. Earlier output is available on demand. Removing context retains it when a historical round used it. Complete and closed cards keep their media; the Card link in Repository history opens the owning card. A link never switches the open project automatically.

Requests such as “attach a screenshot” or “record the console output” belong in the Description. They are ordinary work instructions. There is no media-required flag or separate completion gate. Pyxie neither installs capture tools nor supplies a display server. The project's Dockerfile and agent configuration determine which tools are available, including any browser, terminal recorder, desktop environment, virtual display, or encoder.

## Sandbox exchange

Agent rounds (Work, Review, Resolve) receive an immutable snapshot at:

```
/pyxie/in/media/manifest.json
/pyxie/in/media/files/<object-id>
```

The input mount is read-only. The manifest lists the original filename, caption, source, byte length, SHA-256 digest, and path relative to `/pyxie/in/media`. The snapshot contains active user context and the latest output-producing round from each agent stage. A review therefore receives the worker's output; another work attempt also receives the latest review evidence. Older files remain on the card. Pyxie verifies stored bytes before hand-in. Missing or altered context stops the round with an actionable error.

To attach an existing file, an agent runs:

```
pyxie-attach /work/repo/screenshot.png --caption "Updated settings dialog"
```

This helper only copies a file and publishes its metadata. An agent may instead write files under `/pyxie/out/media/files/` and atomically replace `/pyxie/out/media/manifest.json`:

```json
{
  "version": 1,
  "files": [
    {
      "path": "files/screenshot.png",
      "name": "screenshot.png",
      "caption": "Updated settings dialog"
    }
  ]
}
```

Only listed files are imported. Finish writing the bytes before publishing the manifest. Paths are relative, begin with `files/`, and cannot contain traversal, links, device files, or ambiguous path syntax. Entries have unique paths; `name` is a printable filename of at most 240 characters and `caption` is optional, at most 1,000 characters. The manifest is at most 128 KiB and uses version 1. Pyxie derives the card, round, time, content type, size, hash, and accepted commit itself; agent-supplied provenance is not authoritative.

Pyxie ends agent processes before collecting output and imports finalized publications on successful, rejected, errored, blocked, and interrupted agent rounds. An attachment problem is included in the round report and does not overturn an accepted code result. Files from unfinished work carry no claim that they depict a committed revision. Interrupted host imports have durable receipts and resume without duplicate objects. On startup, leftover containers are stopped before recovery reads their output; workspace volumes are retained. A host-owned round marker prevents old output from being attributed to a newer round.

## Storage and limits

Metadata lives in the project's SQLite database; bytes live outside the repository and web root:

```
<project>/media/objects/<object-id>
<project>/media/staging/<temporary-file-or-round-receipt>
```

Objects are immutable and identified independently of display filenames. Uploads and imports stream to bounded temporary files, calculate their hashes, and atomically publish complete objects. Files are never loaded wholesale into a Blazor circuit. Downloads require the current project and the matching card or unexpired upload session. Responses disable caching and content sniffing. HTML, SVG, PDF, and unrecognized types download as opaque files. Audio and video previews use native controls without autoplay and support byte-range requests. Pyxie does not transcode or generate thumbnails on the server.

The contract permits 50 context files per card and 50 outputs per round, at most 256 MiB per file and 1 GiB per context selection or output batch. Upload sessions expire after 24 hours. Host configuration can lower transfer budgets and set the project storage quota:

```json
{
  "Media": {
    "MaxFileBytes": 268435456,
    "MaxBatchBytes": 1073741824,
    "MaxProjectBytes": 10737418240
  }
}
```

`MaxBatchBytes` bounds an upload session or output collection. Active uploads reserve their allowed byte budget until receiving ends; completed files count by actual size. The project quota includes unpublished objects until cleanup reclaims them. Maintenance runs on startup and every five minutes for the open project, removing expired sessions, abandoned temporary files, completed receipts, and objects without metadata or a pending receipt. Historical input snapshots protect removed context. Closing or switching projects cancels uploads; removing a closed project waits for storage writes before deleting its folder.

Media and upload sessions are excluded from settings import/export. There is no settings-file version change. This model change regenerates the predeployment project `InitialCreate`; existing project databases must be recreated. Export settings with the previous build before recreating projects, then import those settings.
