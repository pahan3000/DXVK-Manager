# DXVK-Manager
A playnite extension that simplifies the dxvk injection process.
## Features

- **One-click inject/remove** — manage DXVK for any game from the topbar icon button or right-click menu.
- **Auto-inject** — optionally inject DXVK automatically into DX9–DX11 games when they're added to your library or finish installing, no manual step needed.

go to [releases](https://github.com/pahan3000/DXVK-Manager/releases) for the latest version
## Requirements

- Playnite
- Extracted dxvk

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
