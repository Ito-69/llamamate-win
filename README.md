# LlamaMate for Windows

Local LLM server manager for [llama.cpp](https://github.com/ggml-ai/llama.cpp) on Windows — tray app, model browser, server settings, log viewer.

## Features

- **System tray icon** — green/gray status indicator, right-click menu
- **Server management** — start/stop/restart llama-server, auto-start at logon via Task Scheduler
- **Model browser** — browse Hugging Face, download GGUF files, set active model
- **Server settings** — GPU layers, context size, flash attention, KV cache quantization, profiles
- **Log viewer** — live tail of server.log with auto-scroll
- **Auto-update** — checks GitHub releases for app and llama.cpp updates
- **Install/Uninstall** — PowerShell scripts

## Quick Install

```powershell
# From PowerShell (admin not required):
powershell -NoProfile -ExecutionPolicy Bypass -Command "iwr -Uri 'https://github.com/Ito-69/llamamate-win/releases/latest/download/Install-LlamaMate.ps1' -OutFile Install-LlamaMate.ps1; .\Install-LlamaMate.ps1 -Download"
```

Or via winget once submitted:

```powershell
winget install Ito-69.LlamaMate
```

## Manual Install

1. Download `LlamaMate-*.msix` from [Releases](https://github.com/Ito-69/llamamate-win/releases)
2. Double-click the MSIX to install
3. If SmartScreen blocks: click **More info** → **Run anyway**

## Development

### Prerequisites

- .NET 8 SDK
- Windows 10 SDK (for MSIX packaging)
- PowerShell 5.1+

### Build

```powershell
dotnet build src/LlamaMate.App/LlamaMate.App.csproj -c Release -r win-x64
```

### Package MSIX

```powershell
.\packaging\make-msix.ps1 -Configuration Release
```

## Architecture

```
src/
├── LlamaMate.App/       WPF .NET 8 app (tray + windows)
│   ├── Services/        ConfigManager, ServerManager, ModelManager, etc.
│   └── Views/           ModelsWindow, SettingsWindow, LogViewerWindow
└── LlamaMate.Installer/ PowerShell scripts for setup
packaging/               MSIX manifest + winget manifests
```

## Roadmap

- [x] Tray app with system menu
- [x] Server management via Task Scheduler
- [x] Model browser (Hugging Face API)
- [x] Settings window with profiles
- [x] Log viewer
- [x] App update check
- [x] MSIX packaging
- [ ] GPU/CUDA build variant support
- [ ] Code signing certificate
- [ ] Winget submission

## License

MIT — see [LICENSE](LICENSE)
