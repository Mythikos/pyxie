# Settings file

A settings file carries a project's settings to another project, or to the same project after its database was recreated. It holds the agents, the gates in the order they run, the categories, and the sandbox Dockerfile. It never holds cards, their history, the repository, or sign-ins.

Settings → Export downloads the open project's settings as `<project>.pyxie-settings.json` (`GET settings/export?project=<id>`). Settings → Import reads one, lists the sections it holds, and imports the ones the person leaves ticked.

`Pyxie.Data/Transfer/Files` owns the file contract. Versions 1 and 2 share a JSON shape. Version 3 records authentication separately from the harness. `ISettingsTransferService` applies the import.

## Format (version 3)

```json
{
  "format": "pyxie.project-settings",
  "version": 3,
  "agents": [
    {
      "authentication": "account",
      "name": "Claude",
      "harness": "claudeCode",
      "command": "claude",
      "arguments": "-p --name {CARD_NAME}",
      "canWork": true,
      "canReview": true,
      "canMerge": true
    }
  ],
  "gates": [
    {
      "name": "Build",
      "command": "dotnet",
      "arguments": "build -warnaserror",
      "timeoutMinutes": 30,
      "enabled": true
    }
  ],
  "categories": [
    {
      "name": "Bug",
      "color": "#FBE9EC"
    }
  ],
  "sandbox": {
    "dockerfile": "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\n"
  }
}
```

- The file is UTF-8 without a byte order mark, with LF line breaks, a two-space indent, and a final line break, on every machine. Reading accepts a byte order mark.
- `format` and `version` are required; the writer puts them first, and a reader takes members in any order. A file whose `format` is anything else is not a settings file.
- A section that is missing, or `null`, is not in the file and is not offered on import. An empty list is a section that holds nothing: importing it removes every item.
- Every field of every item is required; `arguments` is `""` when there are none.
- `harness` is one of `claudeCode`, `codex`, `deepSeek`, `other`.
- `authentication` is `account` or `apiKey` for a built-in harness. A custom `other` harness uses `none`.
- `color` is `#RRGGBB`, read in either case and written in upper case.
- `dockerfile` is the Dockerfile in effect, normalized as a save normalizes it: the saved one, or the default when the project never saved one.
- Gates are listed in the order they run. Agents and categories are listed by name; their order means nothing.
- Agent arguments are exported exactly as typed, including anything secret a person typed there.

## Versions

Versions 1 and 2 import Claude Code and Codex agents with Account authentication. Version 2 DeepSeek agents keep the method encoded in their old harness name. The retired `openCode` entry in version 1 imports as Other with its saved command and arguments. If an older Codex agent used `codex login --with-api-key`, select **API key** after import and save the key in `<Workspace:Root>\secrets\openai-api-key`. Version 3 exports the selected method for each agent. Credentials stay on the machine.

- The shape and vocabulary of a version are frozen. Adding, renaming, or removing a field or a section, changing an encoding, or adding or removing a harness name makes a new version, read using its own vocabulary and the JSON shape defined for it; every older version keeps importing. The version 1 fixture in `SettingsFileCompatibilityTests` is never edited.
- A file names its version before anything else is read, so a file from a newer Pyxie is refused as one ("exported by a newer Pyxie") instead of as a damaged file.
- Within a version, anything the version does not name is refused: an unknown member, a repeated member, a comment, a trailing comma, or a value of the wrong type.

## Problems

- Problems with the file itself refuse the whole file. A file that is empty, too large (over 1 MB), not UTF-8, not JSON, not a settings file, too new, or not shaped as its version requires (an unknown or repeated member, a comment, a trailing comma, or a value of the wrong type) is refused at the first such problem, with its line, and its path where it has one. In a well-shaped file, every missing field, empty item, unknown harness, and bad color is listed, one per line.
- The rules the editors apply are checked section by section when the file is imported: name, command, and argument lengths, argument tokens, gate timeouts, the Dockerfile starting `FROM ${PYXIE_BASE}`, names unique ignoring case, and at most one merge agent. These rules can tighten between Pyxie versions without a new file version, so a section that breaks one can be left out while the others import.

## Import

- Each ticked section replaces the project's. Items match by name, ignoring case (an exact name first), and are updated in place, so cards keep referring to them. The file's other items are added; the project's other items are removed. A rename in a file is a removal and an addition.
- The import dialog shows what each section would remove. A section that cannot be imported shows why, and Import waits until the person leaves it out.
- An import is all or nothing, under the editors' rules: an agent that a card that is not complete still has as its worker or reviewer cannot be removed or lose that role, and a category any card uses, closed ones included, cannot be removed. Complete and closed cards let go of a removed agent, as a delete does.
- An import checks the current contents of each selected section against its preview. If someone edits a selected definition, including one the file would already have replaced, the dialog refreshes the preview for review. Changes to an unselected section leave the preview usable.
- The sandbox Dockerfile is written only when it differs, so an editor that has it open can still save when nothing changed.

## Regenerated migrations

Until Pyxie's first real deployment, a model change regenerates a single `InitialCreate` per database, which existing project databases cannot open. Export each project's settings with the build you have before updating, then import them into the project recreated after it. A database the update already broke can only be exported with the build that made it.

Opening a project from another build is refused on its Repository row before any migration writes to its database. An unreadable database is refused there too. If that project was open at startup, Pyxie closes it and logs the reason, then starts with nothing open; the project and its files stay in place. Press Open to see the reason. Recover its settings with the build that created it, or restore a working backup for a damaged database. To start over, Remove the closed project and import its repository again; removal deletes its hub, cards, history, and settings.
