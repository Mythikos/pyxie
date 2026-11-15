# Pyxie visual design

This folder is the design for Pyxie's three screens (Board, Repository, Settings) and its mark. Implement it in `Pyxie.Web` with Blazorise v2 Ant Design components, following [AGENTS.md](../../AGENTS.md) and the [coding standards](../coding-standards.md). Those documents win over anything here.

- `screens/board.html`, `screens/repository.html`, `screens/settings.html`, `screens/mark.html`: static reference pages. Open them in a browser to see the target. They are a visual spec, not code to paste: every value they use is listed below, and the markup is hand-built HTML, not Blazorise.
- `assets/pyxie-mark.svg` (eyes open), `assets/pyxie-mark-asleep.svg` (eyes closed), `assets/pyxie-mark-small.svg` (16 px and favicon; no inner ears, wider blaze and solid eyes so it reads at that size).

The app is named for Pyxie, a gray-and-white cat. The palette and mark come from her: gray coat, a white blaze up her face, green eyes, pink nose. Keep the mark exactly as drawn; do not redraw it, add expressions, or use it as decoration beyond the places listed under **The mark**.

## Tokens

Put these in one place as CSS custom properties (for example a `:root` block in the app's stylesheet) and map the Blazorise theme onto them. Look up the Blazorise v2 theming API with Context7 rather than from memory.

### Colors

| Token | Value | Use |
| --- | --- | --- |
| `--pyx-coat` | `#232429` | Header bar, dark badges, code editor background |
| `--pyx-coat-mid` | `#3A3B42` | Merge badge, running merge outline |
| `--pyx-coat-light` | `#7B7C85` | Mark fill |
| `--pyx-ground` | `#F6F6F3` | Page background, inset panels |
| `--pyx-surface` | `#FFFFFF` | Cards, panels |
| `--pyx-lane` | `#EDEDE9` | Board lane background, neutral chip background, row dividers |
| `--pyx-border` | `#E3E3DF` | Card and panel borders |
| `--pyx-border-strong` | `#D6D6D1` | Input and secondary-button borders |
| `--pyx-text` | `#1E1F23` | Body text |
| `--pyx-text-2` | `#4A4C53` | Secondary text, table headers |
| `--pyx-text-muted` | `#63656D` | Metadata (4.5:1 or better on white and on `--pyx-lane`) |
| `--pyx-header-text` | `#B9BAC2` | Inactive nav and metadata on the header bar |
| `--pyx-eye` | `#8DBE5A` | Mark eyes, progress fill, running-status border |
| `--pyx-primary` | `#3D7336` | Primary buttons, switches, checkboxes, selected tab underline, links |
| `--pyx-primary-hover` | `#2F5C29` | Primary hover, green text on tints |
| `--pyx-success` | `#4E8A34` | Passed icons, round trail success |
| `--pyx-success-tint` | `#EAF3E2` | Feature chip, Work/Review stage badges |
| `--pyx-nose` | `#E8939F` | Mark nose only |
| `--pyx-ear` | `#F0AEB8` | Mark inner ears only |
| `--pyx-pink-tint` | `#FBE9EC` | Bug chip, Resolve badge, argument-token highlight |
| `--pyx-pink-text` | `#8E2F42` | Text on the pink tint |
| `--pyx-error` | `#C23B3B` | Round trail setback |
| `--pyx-error-text` | `#A12D2D` | Error reason text, exhausted attempt count |
| `--pyx-error-tint` | `#FFF7F6` | Error card background |
| `--pyx-error-border` | `#E8AFAC` | Error card border |
| `--pyx-amber` | `#D19A2E` | Gates lane dot, running gate outline |
| `--pyx-amber-text` | `#B7791F` | Running gate spinner arc |
| `--pyx-switch-off` | `#CFCFCA` | Switch off, skipped round |

Lane dots: Queue `#8A8C94`, In Progress `#6FA34A`, Gates `#D19A2E`, Review `#D17F8F`, Complete `#3D7336`.

### Type

- UI: **Inter** 400/500/600/700. Mono (branches, commands, paths, SHAs, attempt counts, durations): **JetBrains Mono** 400/500. Wordmark and page titles only: **Outfit** 600.
- All three are SIL OFL. Pyxie runs locally, so self-host the woff2 files under `wwwroot` instead of loading Google Fonts, with system fallbacks (`system-ui, sans-serif`; `ui-monospace, monospace`).
- Sizes: card title 14/600, body 13–14, metadata 12–12.5, chips 11/600, section heading 17/600, lane heading 13/600, page title (Repository project name) Outfit 26/600, wordmark Outfit 22/600.

### Shape and spacing

- Radius: chips and pills 999 px; buttons and inputs 7–8 px; cards 10 px; lanes and panels 12–14 px.
- Card: 1 px `--pyx-border`, padding 12, inner gap 8–9. Running cards add `box-shadow: 0 1px 2px rgba(20,20,25,0.05)`. No other shadows.
- Page padding 20–24; gaps between lanes 14, between panels 16–20.
- Heights: header controls 36, lane and card buttons 28–30, table row inputs 18×18.

## The mark

Use the SVGs as they are (embed inline, or reference them from `wwwroot`).

- **Header**: mark at 32 px plus the `pyxie` wordmark, linking to the Board.
- **Pipeline status** (header, left of Start/Stop): a pill holding the mark at 22 px, eyes open while the pipeline runs (`Running`, with `n of Pipeline:Workers`), closed while it is stopped (`Stopped`). Running pill: background `rgba(141,190,90,0.14)`, border `rgba(141,190,90,0.35)`, label `#CFE6B8`. Stopped pill: background `rgba(255,255,255,0.06)`, border `rgba(255,255,255,0.14)`, label `#D9DAE0`.
- **Pyxie's own work**: the Merge round's badge on a card ("Merging") and merge commits Pyxie wrote in Repository history (the merges that land cards, and her merges of the target branch into a card's branch) use the mark as their avatar. Every other commit gets its author's initial in a `--pyx-lane` circle, including an agent's commit signed with Pyxie's name.
- **Favicon**: `pyxie-mark-small.svg`.

Nowhere else. No mascot in empty states or helper text (see UI restraint in AGENTS.md).

## App shell

One header bar in `--pyx-coat`, wrapping on narrow screens: mark and wordmark, nav (Board, Repository, Settings; active item white on `rgba(255,255,255,0.10)`, others `--pyx-header-text`), a flexible spacer, the open project's name with its target branch in a mono chip, then pipeline status and Start (primary green, play icon) or Stop (outlined white, square icon). Replace whatever header and nav the app has now; do not add a sidebar.

## Board

- Five lanes from `CardLifecycleRules.BoardLanes`, in a grid of `repeat(5, minmax(264px, 1fr))` inside a horizontal scroll box, so lanes scroll sideways on narrow screens rather than stacking.
- Lane header: colored dot, lane name, card count. **Add** (secondary button with plus icon) appears only on the Queue lane.
- Card, top row: stage badge (active lanes only), category chip, attempt count on the right in mono (`2/5`; Queue cards that have not run show only the limit). Then the name. Then a footer.
- Stage badges: Work and Review, a green tint with a dot; Resolve, a pink tint with a merge icon; Merge, a coat-dark pill with the mark, reading "Merging". A gate round shows the enabled gates as a small list on `--pyx-ground` (passed: green check; running: amber arc; waiting: dashed gray circle) with durations.
- Footer of active cards: the round trail (one 10×10 square per round, radius 3, latest 20 at most), the agent running the round, and elapsed time on the right in mono. Trail colors:
  - Completed, Passed, Approved, Merged, Resolved: `--pyx-success`.
  - Failed, Rejected, Conflicted, Errored, Blocked: `--pyx-error`.
  - Skipped and Interrupted: `--pyx-switch-off`.
  - Outdated: `--pyx-amber`.
  - The open round: a 2 px outline in its stage color (Work/Review green, Gate amber, Resolve `#D17F8F`, Merge `--pyx-coat-mid`).
  - Give the trail `role="img"` and an `aria-label` that lists the rounds in words.
- Queue cards: the worker and reviewer names ("Claude · Codex", or "No reviewer").
- A card in Error sits at the top of the Queue with `--pyx-error-tint` background, `--pyx-error-border`, the attempt count in `--pyx-error-text`, the stop reason under the name next to an alert icon, and Retry (primary, with a retry icon) and Edit (secondary) buttons.
- A resolve round also shows the conflicting files under the name ("Conflict in `docs/pipeline.md`").
- Complete cards: when they finished on the right, then "Merged" with a check and a Close button.
- Category chips: Feature green tint, Bug pink tint, Chore neutral. Categories are user-defined, so this needs a rule for the rest; see Open questions.

## Repository

- Two columns that wrap: Projects panel (`flex: 1 1 300px; max-width: 360px`) and the repository (`flex: 999 1 640px; min-width: 0`).
- Projects panel: title, then an import box on `--pyx-ground` with a Folder/URL segmented control, a labelled mono input and a dark Import button. Then the project list: the open project highlighted (`#F3F8EE` background, 3 px `--pyx-primary` left border, "Open" chip, Close button); others show Open. A background import shows "Importing" and a thin progress bar. Open and Close are disabled while the pipeline runs; follow the existing service rule and show its message.
- Repository header panel: project name in Outfit, the target branch in a mono chip with a lock icon, then Origin and Hub rows. The hub path is a mono code box with a Copy button.
- Tabs: Files, History, Branches (with count), with the selected tab underlined in `--pyx-primary`. To the right: a labelled branch select and a Zip download button.
- History lists the branch's first-parent line, which is what landed on it. Rows: avatar (the mark for Pyxie's merge commits, an initial otherwise), commit subject, a "Card N" chip on the merges that landed a card, author and relative time, and the short SHA in mono on the right.

## Settings

- A section nav (Agents, Gates, Categories, Sandbox) linking to anchors, beside one column of panels that stops at 1100 px wide.
- **Agents**: a table in a horizontal scroll box with the columns Name, Harness, Command (mono, with `{TOKEN}`s highlighted on the pink tint), Work, Review, Resolve, and an icon-only Edit button with an `aria-label`. Work and Review are checkboxes; Resolve is a radio group, because only one agent can be the merge agent. "Add agent" sits in the panel header.
- **Gates**: an ordered list with a drag handle, name, mono command, timeout, and an on/off switch (`role="switch"`). Disabled gates render in `--pyx-text-muted`. "Add gate" sits in the panel header.
- **Categories**: chips with a remove button each, plus an inline input and Add button.
- **Sandbox**: the Dockerfile in a dark mono editor (`--pyx-coat` background, `#E6E6E3` text). Under it: build status (check and "Built" plus the image tag in mono, or the end of the log when the build failed), then Build (secondary) and Save (primary). Beside it: Sign-ins, listing the Claude Code token and the Codex sign-in as Found (green check) or Not found.

## Open questions: raise these rather than invent an answer

1. **Live gate progress and elapsed time** on running cards need data the board summary may not expose yet. "Showing a running round's live output" is an open decision in `docs/pipeline.md`. If the data is not there, ship the cards without it and say so; do not add polling or new runtime plumbing without asking.
2. **Category colors.** Feature, Bug and Chore have colors; user-made categories need a rule. Suggested: neutral by default, or a color chosen in Settings. Ask before adding a model field.
3. **Inline role editing on the Agents table** writes immediately. Today's editor may be a dialog. Removing CanWork or CanReview can be refused (`AgentService`), so if the table edits inline it must show the `ServiceResult` message and revert. Otherwise keep the dialog and make the table read-only.
4. **The "Card N" chip on merge commits** needs the card id in Pyxie's merge commit, or a lookup. Use what exists; don't parse free text.
5. **Import progress** may not have a percentage. Use an indeterminate bar if it doesn't.

## Done means

- `dotnet build -warnaserror`, `dotnet test` and `dotnet format --verify-no-changes` pass. Report exactly what passed, failed, or could not run.
- No Blazorise parameter is named that v2 does not declare (`RazorComponentParameterTests`).
- The running app was checked against the reference pages at desktop width and at about 400 px wide, and the report says what was and was not verified visually.
- Text contrast is at least 4.5:1. Icon-only buttons have `aria-label`s. Controls are real buttons, inputs and links.
