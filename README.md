# FlatOut 4 Save Editor

A small WinUI 3 utility for inspecting and editing FlatOut 4 save files.

## Features

- Opens FlatOut 4 `Save` files directly.
- Finds local Steam Cloud saves under `Steam\userdata\<steamid>\<appid>\remote\Save` and the current VR path `remote\Flatout VR\Save.dat`.
- Checks both app ids currently used by this editor: `402130` and `3844750`.
- Prompts you to choose when multiple supported saves are found across Steam users, app ids, or offline folders.
- Also checks common offline save folders under Documents and LocalAppData.
- Shows save values with friendly names instead of raw offsets.
- Validates edited values as you type.
- Creates an optional backup before writing changes.
- Supports V82 through V95 save layouts, migrating older supported saves in memory before writing.

## Download

Use the latest GitHub release and download the `FlatOut4SaveEditor-win-x64.zip` asset.

Extract the zip and run `FlatOut4SaveEditor.exe`.

## Build

Requirements:

- Windows 10 version 1809 or newer
- .NET 8 SDK
- Windows App SDK compatible environment

Build:

```powershell
dotnet build .\FlatOut4SaveEditor.csproj -p:Platform=x64
```

Publish a release build:

```powershell
dotnet publish .\FlatOut4SaveEditor.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true
```

## Save Safety

The editor can write directly to the selected save file. Keep `Create backup on save` enabled unless you are editing a separate copy.
