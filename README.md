# FlatOut 4 Save Editor

A WinUI 3 editor for FlatOut 4 and FlatOut VR profile saves.

## Save format support

- Reads every profile layout from V82 to the current V95 and writes V95, the layout the live Steam build reads. Older layouts are migrated in memory the same way the game's `FOSaveMigration` does.
- Reads V96 saves written by RC10-RC17 builds and converts them back to V95 like the game does: the telemetry opt-in moves out of the gameplay options into the tagged extension slots.
- Understands the tagged extension slots in `m_aOther` (`FXT1` tag, Steam Workshop livery ID, telemetry opt-in). Slots it does not know yet are listed and always written back unchanged, because new profile settings are stored there.
- Never bumps the save version, so edited saves stay readable by the shipped game.

### Damaged, cut-off and newer saves

The editor reads what it can instead of refusing a file:

- **Newer layouts** (a build that added options or grew `FOSerializableExt`): each 4-byte value is matched against the V95 layout by what a sensible value looks like (binding ids, enum ranges, real floats, lap times, the `FOO ` and `FXT1` markers), preferring the places new data gets added. Every known value is editable; the unknown bytes and the file's own version are written back exactly as they were.
- **Cut-off or padded files**: a known version is resized and migrated exactly. Anything missing starts as a new profile has it (the game's `Reset()` values), and trailing junk is dropped.
- **Broken markers or version stamp**: read normally; saving writes a valid V95 file.
- Files that do not match at all (random data, all zeros) are refused with an explanation.

**Save health** lists what happened when the file was read, plus every value the game would never write: bindings to controls that do not exist, choices outside their list, NaN floats, on/off bytes that are neither, and uninitialised memory (`0xCDCDCDCD`) left by old debug builds. Repair sets them to new-profile values, one at a time, per group or all at once.

## Finding saves

- Steam Cloud saves under `Steam\userdata\<steamid>\<appid>\remote` for app ids `3844750` (FlatOut 4 VR, `Flatout VR\Save.dat`) and `402130` (FlatOut 4, `Save`).
- Local saves under `Documents\My Games\Flatout VR` and the other usual folders.
- Developer test slots (`My Games\Flatout VR Test <slot>\DEBUGSave<slot>`) from non-Retail builds.

The start page lists the saves you opened recently, then everything it finds, grouped by location, newest first. The window reopens where you left it (size, position, maximized), or centred on the main screen if that spot is no longer on a connected display.

## Editing

- **Overview**: file details, at-a-glance tiles (credits, career medals, trophies, garage, tracks, races, play time, challenge score) and a card for every section.
- **Career**: credits, a medal picker for each of the 48 career events grouped by class and tier, the cup in progress and the raw event result slots.
- **Garage**: unlock state and upgrade level per car, selected paint, loadouts, tracks, skins, drivers, boost effects and horns, all by name, plus Unlock all / Own all per table.
- **Challenges, Trophies, Records, Statistics, Favorites**: trophies with their in-game names and descriptions, lap records as `m:ss.fff` per named track, stats grouped by topic, per-mode tables.
- **Options**: gameplay, camera, comfort, steering wheel, VR motion steering, input, audio and telemetry, with sliders and pickers that respect the game's ranges.
- **Controls**: race and menu bindings shown as key, button and axis names for keyboard, gamepad and wheel, plus device tuning.
- **Extensions** and **All fields** for the raw view: every value with its C++ name, offset and type.

Ctrl+Tab and Ctrl+Shift+Tab step through the sections; More › Keyboard shortcuts lists every shortcut. Search (Ctrl+F) finds sections, settings, cars, tracks, trophies, career events and bindings and jumps straight to them. Long pages have a jump bar that follows the scroll position, and the navigation shows how many unsaved changes each section holds.

**Review changes** (or the unsaved-changes pill) lists every edit as old → new value, grouped by section; select one to jump to it or revert just that value. Changed cards and table rows also carry their own revert button.

Every edit can be undone: **Undo** / **Redo** on the toolbar or Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z). The arrow beside Undo lists recent changes and undoes back to any of them. A slider drag is one step, and bulk actions (Unlock All, Gold all, Reset bindings, Repair, Revert) are one step each. Unlock All applies the same fields as the game's debug unlock path. Nothing is written until you save, Revert goes back to the file on disk, and closing with unsaved changes asks first.

## Save safety

Keep **Back up before saving** on unless you are editing a copy: it copies the file to `<name>.bak-<date>` before writing. Save a copy writes to a new file instead.

## Download

Use the latest GitHub release and download the `FlatOut4SaveEditor-v<version>-Setup.exe` asset. It extracts the app to `%LOCALAPPDATA%\FlatOut4SaveEditor\app` and launches it.

## Build

Requirements: Windows 10 1809 or newer, .NET 8 SDK.

```powershell
dotnet build .\FlatOut4SaveEditor.csproj -p:Platform=x64
```

Release build:

```powershell
dotnet publish .\FlatOut4SaveEditor.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true
```

One-file release asset:

```powershell
.\scripts\build_launcher.ps1 -Version 0.4.0
```

Every configuration carries the Windows App SDK runtime, so a Debug build runs without a matching runtime installed.

`scripts/gen_control_names.py` regenerates the binding names from the engine's `InputStates.h`, and `scripts/gen_customization_names.py` regenerates the skin, driver, boost and horn names from the game's factory configs and `LOCALISATION.h`.

### Command line

- `FlatOut4SaveEditor.exe <save>` or `--open <save>` opens a save at startup.
- `--snapshot <folder>` renders every page to PNG off-screen and exits (for checking the UI without touching the desktop). Options: `--theme light|dark`, `--size 1360x880`, `--scroll <steps>`, `--search <text>`, `--unlock`. A save is never written in this mode, and `snapshot.log` lists any value a binding changed on its own.
- `--snapshot <folder> --selftest` instead drives the real editors (switches, pickers, sliders, number and text boxes, table cells, medals, trophies) off-screen, checks the bytes they write, reverts, and logs `SELFTEST` results.
