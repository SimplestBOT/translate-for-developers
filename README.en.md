# Translate for Developers

[简体中文](README.md) · **English** · [日本語](README.ja.md) · [한국어](README.ko.md)

Select English text, press a hotkey, and the Chinese translation pops up instantly. Reading code comments, English docs, or paper abstracts — **works in any application**.

Stuck on an English comment in MATLAB? An English error message in VS Code? A tricky paragraph in a PDF?
Select → hotkey → translation appears right away.

## Why this tool

Dictionary-style popup translators rely on reading the highlighted selection via UI automation, which **fails in apps like MATLAB (Java controls)**.
This tool uses the **clipboard approach** instead: after you select text it sends `Ctrl+C` automatically → reads the clipboard → translates → shows a popup.
It works in **every app that supports copying** — MATLAB, VS Code, browsers, PDF readers…

## Features

- Select text in any app → press the hotkey to translate (default `Ctrl+Alt+T`, changeable anytime)
- **All-new dark UI (v1.1)**: rendered by the system's built-in WebView2 — glassy cards, entrance animations, scanning-beam loader, skeleton screens, springy keycap effects; translation is **asynchronous**, so the window opens instantly and animations never stutter
- **Automatic source-language detection** (manual selection also available), **30+ target languages** (Chinese, English, Japanese, Korean, French, German, Spanish, Russian…)
- Clipboard is backed up and restored automatically — **never destroys** what you had copied
- Choose your translation service: **MyMemory** (free, no registration) / **Baidu Translate** (free tier, better quality)
- Long text is auto-split to bypass Baidu's 6000-byte per-request limit
- Polished interactions: draggable title bar, `Esc` to close, `Enter` to copy, copy button turns green with a checkmark, toast notifications in the corner (no more clunky message boxes)
- Tray menu: **change the hotkey (just press it on your keyboard)**, **switch translation service**, pick languages, exit
- Fully portable: C# host + WebView2 rendering (system-provided runtime), runs without installation

## Usage

1. Run `start-translator.bat` (or directly run `src/bridge/translator-ui.exe`) — a "T" icon appears in the system tray
2. **Select English text** in any app → press **`Ctrl+Alt+T`** → the translation window pops up
3. In the window: drag the title bar to move it; `Esc` closes; `Enter` or "Copy" copies the translation
4. Tray icon (T) → right-click menu: change hotkey / switch service / pick language / exit

## Configuration

Config file `config.conf` (in the `scripts/` folder next to `bridge/`, auto-created on first run):

```ini
hotkey=^!d              ; translation hotkey (changeable from the tray menu)
src_lang=auto           ; source language: auto = detect, or zh-CN/en/ja/ko/…
tgt_lang=zh-CN          ; target language (default: Simplified Chinese)
provider=mymemory       ; mymemory or baidu
baidu_appid=            ; Baidu Translate APP ID (required for Baidu)
baidu_secret=           ; Baidu Translate secret (required for Baidu)
```

> Changing the hotkey or service from the tray menu writes to this file automatically — no manual editing needed.

## Supported languages

Source language is **auto-detected** by default (manual selection available); 30+ target languages:

| Language | Code | Language | Code |
|---|---|---|---|
| Simplified Chinese | zh-CN | English | en |
| Traditional Chinese | zh-TW | Japanese | ja |
| Korean | ko | French | fr |
| German | de | Spanish | es |
| Portuguese | pt | Russian | ru |
| Italian | it | Arabic | ar |
| Hindi | hi | Thai | th |
| Vietnamese | vi | Indonesian | id |
| Turkish | tr | Dutch | nl |
| Polish | pl | Ukrainian | uk |
| Greek | el | Czech | cs |
| Swedish | sv | Hungarian | hu |
| Romanian | ro | Danish | da |
| Finnish | fi | Norwegian | no |
| Malay | ms | Filipino | fil |
| Bengali | bn | Urdu | ur |
| Persian | fa | Hebrew | he |

> Tray menu → "Source language" / "Target language" submenus; your choice is saved automatically.

## System requirements

- Windows 10 1903+ (up to date) or Windows 11 (.NET Framework 4.8 runtime is built into the OS)
- WebView2 Runtime (**preinstalled on Win10/11** — most machines need no installation at all)

## Translation services

| Service | Cost | Registration | Notes |
|---|---|---|---|
| MyMemory | Free | Not required | ~50,000 chars/day, ~1s response |
| Baidu Translate | Free | Required | More consistent quality, long-text splitting supported |

**To enable the free Baidu tier**: fanyi-api.baidu.com → sign in → Console → Create app (General Text Translation / Standard)
→ copy the APP ID and secret → tray menu "Switch service" → Baidu → enter your credentials.

## Build from source

Requires the [.NET SDK](https://dotnet.microsoft.com) (any recent version; target is net48):

```
dotnet build src/csharp/TranslatorHost/TranslatorHost.csproj -c Release
```

Output lands in `src/csharp/TranslatorHost/bin/Release/net48/win-x64/`; copy that directory's contents into
`bridge/` (matching the path in `start-translator.bat`) and run. The frontend pages
(`src/webui/`, Vite + React 19 + TS) are built per page; the host automatically loads the output `webui/dist/<page>.html`.

### Debug switches

```
translator-ui.exe --selftest            ; headless self-test (WebView2 / config read-write)
translator-ui.exe --open result,text    ; debug window (auto-exits after 60s safety timeout)
TFD_HEADLESS=1                          ; no tray, no hotkey (sandbox e2e)
TFD_PIPE_NAME=<name>                    ; instance isolation key (parallel test instances)
TFD_TEST_REUSE=1                        ; auto-drive the result-window reuse flow
```

## Architecture (v1.5, migration complete)

- **C# host** (`src/csharp/TranslatorHost`, net48 + WinForms + WebView2): the one and only host —
  tray, global hotkey, selection capture (Ctrl+C injection), window lifecycle, WebView2, DWM rounded corners, native dragging.
  WinForms handles Windows integration only; no business logic in the Form.
- **Core library** (`src/csharp/TranslatorCore`): translation/providers/config/clipboard/HTTP/JSON,
  all async + CancellationToken.
- **UI pages** (`src/webui/`, React 19 + TypeScript): settings/result/capture/config, built as single files per page;
  communicates with the host over a JSON message protocol (contract in `docs/protocol.md`).
- The AHK version and the migration-era Named Pipe bridge were removed in v1.4/v1.5 respectively (backups not included in the repo).

## Directory layout

```
translate-for-developers/
├── README.md
├── LICENSE
├── .gitignore
├── start-translator.bat        # launcher (starts the C# host)
├── docs/                       # architecture / protocol / known-issues
└── src/
    ├── csharp/                 # C# host + core library + self-tests
    │   ├── TranslatorHost/     # WinForms/WebView2/tray/hotkey/selection capture
    │   ├── TranslatorCore/     # translation/providers/config/clipboard (class library)
    │   ├── TranslatorCore.Tests/  # built-in assertion self-tests (dotnet run)
    │   └── build-bridge.ps1    # build + deploy to bridge/
    ├── webui/                  # React 19 + TS frontend (single-file per page)
    │   ├── src/                # settings/result/capture/config + bridge protocol layer
    │   └── dist/               # build output <page>.html (self-contained)
    ├── icon.ico
    └── WebView2Loader.dll
```

## FAQ

- **Hotkey does nothing**: check the T icon is in the tray; make sure text containing English is selected
- **"No selected text detected"**: select the text first, then press the hotkey (in some apps, click the window once first so it has focus)
- **"Network request failed"**: check your network; MyMemory occasionally times out — click "Retry" in the window
- **Want a different service**: tray menu → switch translation service
- **Hotkey conflicts with another app**: tray menu → change hotkey → press your preferred combination
- **How to move the window**: drag it by the title bar at the top (the row with the logo)
- **"WebView2 not found" on first launch**: install the Evergreen Runtime once from [Microsoft's site](https://developer.microsoft.com/microsoft-edge/webview2/) (a normal Win10/11 doesn't need this)
- **Launch at startup (optional)**: put a shortcut to translator.exe into `shell:startup`

## License

[MIT](LICENSE)
