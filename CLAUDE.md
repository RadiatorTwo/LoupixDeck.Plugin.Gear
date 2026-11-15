# LoupixDeck.Plugin.Gear

LoupixDeck plugin that detects gaming mice (Pulsar, Logitech over HID++ 2.0) and shows battery, polling rate and DPI.

## Plugin skeleton

- **Assembly / folder name:** `LoupixDeck.Plugin.Gear`; namespace `LoupixDeck.Plugin.Gear`
- **Plugin class:** `GearPlugin` derives from `LoupixPlugin`
- **Manifest:** `plugin.json` (id = `gear`, sdkVersion = `1.31`, entryAssembly = `LoupixDeck.Plugin.Gear.dll`)
- **Target framework:** `net10.0`
- **SDK package:** `LoupixDeck.PluginSdk` 1.31.1 with `<ExcludeAssets>runtime</ExcludeAssets>`. The host provides the SDK DLL; never ship it.
- **RenderKit:** `LoupixDeck.RenderKit` 1.0.0 without ExcludeAssets: its DLL ships inside the plugin package.
- **Feeds:** nuget.org only (see `nuget.config`).

## Build, test, pack

- `dotnet build -c Release` builds the plugin; `dotnet test` runs `tests/LoupixDeck.Plugin.Gear.Tests`.
- Hardware smoke tests use `[SkippableFact]` and run only with `GEAR_HW=1`.
- Never send write commands (memory write, DPI write) to real hardware from tests; use a fake transport.
- `loupix pack` validates `plugin.json` and writes the Plugin Store files to `dist/`.
- Store release: publish a GitHub release tagged `v<version>` (see `.github/workflows/release.yml`).

## Rules

1. Exactly **one** concrete `LoupixPlugin` subclass per assembly.
2. `Metadata.Id`, `plugin.json#id` and the plugin folder name must be identical (lowercase).
3. `CommandName` is a **stable public API**: never rename it after a release.
4. `Execute` does **not** run on the UI thread: never touch Avalonia objects directly, wrap the body in `try/catch`, do not block.
5. Keep `Metadata.Version` and `plugin.json#version` in sync.
6. UI strings are English, with `strings.de.json` and `strings.es.json` complete.
