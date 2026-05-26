<div align="center">

<img src="docs/assets/logo.png" alt="ShelfDesk" width="120" />

# ShelfDesk (Shelf)

**Customizable widget dock for Windows desktop**

[![License: MIT](https://img.shields.io/badge/license-MIT-brightgreen.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows%2010%2B-blue.svg)](#system-requirements)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Build](https://github.com/bridges-net-ua/shelf/actions/workflows/build.yml/badge.svg)](https://github.com/bridges-net-ua/shelf/actions/workflows/build.yml)
[![Latest Release](https://img.shields.io/github/v/release/bridges-net-ua/shelf?include_prereleases&label=release)](https://github.com/bridges-net-ua/shelf/releases/latest)

[Download](https://github.com/bridges-net-ua/shelf/releases/latest) ·
[Website](https://shelf.bridges.net.ua/en/) ·
[Report a bug](https://github.com/bridges-net-ua/shelf/issues/new?template=bug_report.md) ·
[Українська](README.md)

</div>

---

## What it is

**ShelfDesk** (technical name **Shelf**) is a side dock that lives on the right or left edge of your Windows screen and hosts a set of small, useful widgets: clock, notes, todo list, photo slideshow, internet radio, weather, timer, stopwatch, holidays calendar.

The dock reserves its screen space through the Windows AppBar API - so when you maximize any window, it sits **next to** ShelfDesk rather than **underneath** it.

## Screenshots

> Screenshots will be added in upcoming releases.

## Features

- **9 built-in widgets** (see below).
- **Dark and light themes**, live switch without restart.
- **Ukrainian and English** UI languages.
- **Auto-hide** with mouse-hover slide-in.
- **Pinned widgets** zone (sticky at the top, never scrolls).
- **Drag-and-drop** reordering.
- **Automatic state saving** to `%APPDATA%\Shelf\settings.json`.
- **Works across virtual desktops**.
- **System auto-start** (opt-in).

## Widgets

| Widget | Description |
|---|---|
| Clock | Time and date in various formats. |
| Notes | Plain-text notepad with autosave. |
| Todo | Task list with multi-line paste, completed items sink to bottom. |
| Photo slideshow | Browses a folder, Ken Burns effect, multiple transitions. |
| Internet radio | Streams radio stations (built-in list + your own). |
| Weather | Current weather + tomorrow forecast (Open-Meteo, no API key). |
| Timer | Countdown with audible signal. |
| Stopwatch | Minutes/seconds/cs + laps. |
| Holidays | Calendar of state, religious, and your own holidays across 3 days (yesterday/today/tomorrow). |

## Download

Pre-built releases are on the **[Releases](https://github.com/bridges-net-ua/shelf/releases/latest)** page:

1. Download `Shelf-vX.Y.Z-win-x64.zip`.
2. Extract anywhere.
3. Run `Shelf.exe`.

> On first launch, Windows SmartScreen may warn «Windows protected your PC» - this is normal for new open-source apps without a commercial code-signing certificate. Click «More info» → «Run anyway». We plan to sign future releases so the warning goes away.

### System requirements

- Windows 10 (1809+) or Windows 11
- x64 architecture
- ~150 MB free disk space

The build is self-contained - no need to install .NET 8 separately.

## Build from source

If you want to build it yourself (e.g. to add your own widget):

```powershell
git clone https://github.com/bridges-net-ua/shelf.git
cd shelf
dotnet build Shelf.sln -c Debug
Start-Process bin\Debug\net8.0-windows\Shelf.exe
```

Requires **.NET 8 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/8.0)).

Architecture details and how to write your own widget - see [CONTRIBUTING.md](CONTRIBUTING.md).

## Tech stack

- **.NET 8** (`net8.0-windows`)
- **WPF** for UI, **WinForms** for the system tray (`NotifyIcon`)
- **Win32 AppBar API** to reserve desktop space
- Widget external dependencies - **none** (HTTP, JSON, media playback - all via the standard library)

## License

ShelfDesk is released under the **MIT License**. See [LICENSE](LICENSE) for the full text.

In short: do whatever you want with the code, including in commercial products, as long as derivative works keep the original copyright notice.

## Contributing

Pull requests are welcome. Before starting a large change, please open an [Issue](https://github.com/bridges-net-ua/shelf/issues) for discussion.

Details (how to build, how to submit PRs, code conventions) are in [CONTRIBUTING.md](CONTRIBUTING.md).

All contributors are expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Author and contacts

Developed by **Bridges Community**.

- Website: [shelf.bridges.net.ua](https://shelf.bridges.net.ua/en/)
- Email: [shelf@bridges.net.ua](mailto:shelf@bridges.net.ua)
- GitHub Issues: [bridges-net-ua/shelf/issues](https://github.com/bridges-net-ua/shelf/issues)

---

<div align="center">
<sub>© 2026 Bridges Community · MIT License</sub>
</div>
