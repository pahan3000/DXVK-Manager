# DXVK-Manager
A playnite extension that simplifies the dxvk injection process.
## Features

- **One-click inject/remove** — manage DXVK for any game from a per-game panel or right-click menu.
- **Architecture detection** — reads the game exe's PE header to pick the correct x32/x64 DXVK build automatically.
- **DirectX version detection** — inspects the exe's import table (including delay-loaded imports, common with anti-cheat/anti-tamper loaders) to guess whether a game targets D3D9–D3D12, with a filename-based fallback for titles that resolve the API at runtime.
- **Per-module control** — choose which DLLs get injected (`d3d9`, `d3d10core`, `d3d11`, `dxgi`) instead of always injecting all four.
- **Standard and gplasync variants** — supports both regular [DXVK](https://github.com/doitsujin/dxvk) and the [dxvk-gplasync](https://gitlab.com/Ph42oN/dxvk-gplasync) fork with persisted shader cache.
- **Library scan** — a bulk "Scan library" action checks every installed game for DXVK compatibility in one pass and lets you inject the results in bulk.
- **Auto-inject** — optionally inject DXVK automatically into DX9–DX11 games when they're added to your library or finish installing, no manual step needed.
- **Manual exe override** — for multi-exe titles or launcher stubs where auto-detection guesses wrong, point the plugin at the correct exe once and it's remembered.
- **Local source folder** — points at a DXVK (or dxvk-gplasync) release you've already downloaded and extracted; the plugin validates the folder and doesn't fetch or manage downloads itself.

go to [releases](https://github.com/pahan3000/DXVK-Manager/releases) for the latest version
## Requirements

- Playnite (built against PlayniteSDK 6.16.0)
- A DXVK or dxvk-gplasync release, downloaded and extracted somewhere on disk (needs `x32/` and/or `x64/` subfolders)

## Building

### Prerequisites

- Visual Studio 2019/2022 (or the .NET Framework build tools) with the **.NET desktop development** workload
- .NET Framework 4.6.2 targeting pack
- NuGet package restore enabled

### Steps

1. Clone or extract this repository.
2. Open `DxvkInjector.csproj` in Visual Studio (or via `devenv`), or restore/build from the command line:
   ```
   nuget restore DxvkInjector.csproj
   msbuild DxvkInjector.csproj /p:Configuration=Release
   ```
3. If your IDE doesn't restore NuGet packages automatically, right-click the project → **Restore NuGet Packages** before building. This pulls in `Newtonsoft.Json` and `PlayniteSDK`.
4. Build in **Release** configuration. Output lands in `bin\Release\`, containing:
   - `DxvkInjector.dll`
   - `Newtonsoft.Json.dll` (must ship alongside the plugin — see the note in `DxvkInjector.csproj`)
   - `extension.yaml`
