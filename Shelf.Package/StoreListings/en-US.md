# Store listing — en-US

Paste each field into the matching input in Partner Center →
ShelfDesk → Submissions → (current draft) → **Store listings** → **English (United States)**.

---

## Display name
```
ShelfDesk
```

## Short description (max 200 characters)
```
A free, open-source dock bar for your Windows desktop. 10 customizable widgets, light & dark themes, multi-monitor support, no telemetry.
```

## Description (max 10,000 characters)
```
ShelfDesk turns the unused edge of your screen into a productive dock bar packed with widgets — without ever covering your other windows. Through the Windows AppBar API, ShelfDesk reserves screen real estate, so when you maximise any application, it lays out beside the dock instead of under it. Your browser, IDE, document — all stay fully visible.

ShelfDesk is completely free, open source under the MIT License, and ad-free. No telemetry, no analytics, no account required.

WHAT'S INSIDE

10 built-in widgets cover the everyday small needs:

- Clock — time and date in 12/24-hour formats with tabular numerals
- Notes — quick text scratchpad with autosave
- Tasks — to-do list with multi-line paste, completed items animate to the bottom
- Photos — slideshow from any folder with Ken Burns animation and four transition styles
- Internet Radio — built-in Ukrainian stations plus your own stream URLs (.pls and .m3u supported)
- Weather — current weather and tomorrow's forecast via Open-Meteo (no API key required)
- Timer — countdown with audible alarm and visual flash
- Stopwatch — minutes, seconds, hundredths, lap tracking
- Holidays — Ukrainian holidays (state, religious, professional, international) plus your own dates and birthdays, with import and export
- NBA — playoff results, schedule, and series standings for the league

Each widget can be renamed (double-click the title), pinned to the top of the dock, configured via right-click, and reordered by drag-and-drop. Lock the order from accidental moves with a single click.

LIGHT AND DARK THEMES

Switch themes live, without restarting the app. The whole panel — including the system tray menu and every dialog — re-themes instantly.

MULTI-MONITOR SUPPORT

Connect multiple displays? Each monitor can host its own ShelfDesk panel with its own side (left/right), width, auto-hide, and widget set. Move any widget between monitors through a right-click submenu. Plug in a new screen — the dock appears automatically; disconnect — the widgets remember where they belonged and return when the monitor is back.

VIRTUAL DESKTOPS

ShelfDesk pins itself to all Windows virtual desktops so it never disappears when you switch.

PRIVACY

All settings stay locally on your computer in %APPDATA%\Shelf\. The only network requests are made by widgets you explicitly add: Weather sends a city name to Open-Meteo, Radio streams audio from station URLs you provide, NBA pulls game data from public ESPN endpoints. No data ever goes to Bridges Community servers. See the full Privacy Policy at https://shelf.bridges.net.ua/privacy/.

OPEN SOURCE

Source code under MIT License: https://github.com/bridges-net-ua/shelf
Report bugs, request features, or build your own widget — contributions are welcome.

LANGUAGES

Interface available in Ukrainian and English. The active language is chosen in Settings.
```

## What's new in this version
```
Initial release.

- 10 built-in widgets: Clock, Notes, Tasks, Photos, Radio, Weather, Timer, Stopwatch, Holidays, NBA
- Light and dark themes with live switching
- Ukrainian and English UI
- Multi-monitor support with per-display configuration
- Pinned widgets, drag-and-drop reordering, lock-order toggle
- Settings migration from portable installs
```

## Product features (one per line, up to 20)
```
Sidebar dock that reserves screen edge — maximised windows lay out beside it, not under it
10 built-in widgets: Clock, Notes, Tasks, Photos, Radio, Weather, Timer, Stopwatch, Holidays, NBA
Light and dark themes with instant live switching, no restart required
Ukrainian and English interface
Multi-monitor support — each display can host its own dock with its own widgets
Per-widget settings via right-click context menu
Drag-and-drop widget reordering with order-lock toggle
Pin frequently used widgets to the top of the dock
Auto-hide mode — slides off screen until you hover the edge
Pinned to all Windows virtual desktops
Photo slideshow with Ken Burns effect and four transition styles
Internet radio with built-in stations and custom .pls/.m3u stream URLs
Open-Meteo weather widget — no API key required
Holidays widget with Ukrainian dates and personal birthdays
Free and open source under MIT License, no ads, no telemetry
```

## Search terms (up to 7, comma-separated in Partner Center)
```
widgets, dock, sidebar, desktop, productivity, weather, clock
```

## Copyright and trademark info (max 200 characters)
```
© 2026 Bridges Community. Licensed under the MIT License. ShelfDesk is open source: https://github.com/bridges-net-ua/shelf
```

## Additional license terms (optional)
```
MIT License — https://github.com/bridges-net-ua/shelf/blob/main/LICENSE
```

## Website
```
https://shelf.bridges.net.ua/
```

## Support contact info
```
shelf@bridges.net.ua
```

## Privacy policy URL
```
https://shelf.bridges.net.ua/privacy/
```

---

## Screenshots (uploaded separately under "Product screenshots")

Upload these 5 PNG files from `D:\project\Shelf\ScreenShots\`, in order. The caption goes into the "Description" field of each screenshot in Partner Center (max 200 chars).

| # | File | Caption |
|---|---|---|
| 1 | `Знімок екрана 2026-05-29 144836.png` | Compact dock panel with Clock, Weather, Internet Radio, Photos slideshow, and Notes - all five widgets visible at once on the screen edge |
| 2 | `Знімок екрана 2026-05-29 144505.png` | All five widgets in Ukrainian UI - Clock, Weather for Kyiv, Internet Radio with built-in stations, Photos slideshow, Notes |
| 3 | `Знімок екрана 2026-05-29 144943.png` | Settings - enable, reorder and remove any widget instance. Configure each one by right-clicking it directly on the panel |
| 4 | `Знімок екрана 2026-05-29 145007.png` | Per-monitor configuration - choose screen, side (left/right), width and auto-hide independently. Multi-monitor setup fully supported |
| 5 | `Знімок екрана 2026-05-29 145018.png` | Stopwatch and Timer widgets with tabular numerals - digits stay aligned while counting. Quick presets for 1, 5, 10, 25 minutes |

---

## Category and subcategory (in Properties tab, not Store listings)
```
Category:    Productivity
Subcategory: Personal finance      ← NO, pick a different one
             Personalisation       ← BEST FIT (dock customisation)
             Tools                 ← acceptable alternative
```

## System requirements (in Properties tab)
```
Minimum:
  - OS: Windows 10 version 2004 (build 19041) or later, Windows 11
  - Architecture: x64
  - RAM: 100 MB free
  - Disk: 200 MB

Recommended:
  - Windows 11 22H2 or later
  - Multi-monitor setup
  - Internet connection (only required for Weather, Radio, and NBA widgets)
```

## Notes for certification (in Submission options)
```
Open-source widget dock for the Windows desktop. No telemetry, no analytics, no user accounts.

Network requests are made only by user-added widgets:
  - Weather: HTTP GET to api.open-meteo.com and geocoding-api.open-meteo.com (city name → coordinates → forecast)
  - Internet Radio: HTTP streaming from URLs in the user's station list
  - NBA: HTTP GET to public ESPN endpoints for game scores and schedules

No data is sent to Bridges Community servers. See Privacy Policy URL for full details.

The app uses runFullTrust capability because it is a hybrid WPF + Windows Forms application that calls Win32 APIs (SHAppBarMessage for screen-edge docking, NotifyIcon for the system tray, registry for non-Store builds).

Source code: https://github.com/bridges-net-ua/shelf
```
