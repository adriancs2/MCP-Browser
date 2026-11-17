<p align="center">
  <img src="assets/icon.png" width="128" height="128" alt="MCP Browser icon">
</p>

<h1 align="center">MCP Browser</h1>

<p align="center">
  <b>A real, visible web browser that your AI agent can drive.</b><br>
  Open source freeware for Windows · Model Context Protocol (MCP) server · Chromium / WebView2
</p>

---

MCP Browser gives an AI agent — Claude, or any other MCP client — its own Chromium
browser window. The agent can open pages, look at them, click, type, fill in forms,
upload files, read the console and run JavaScript, across as many steps as the job
needs. You see everything it does, in an ordinary window on your desktop.

It is a single small Windows program: no Node.js, no Python, no browser driver to
install, and no separate browser download. It uses the Microsoft Edge WebView2
runtime that already ships with Windows 10 and 11.

## Why use it

- **You can watch.** The browser is a normal window with a taskbar button, not a
  hidden headless process. You always know what the agent is looking at.
- **It stays out of your way.** Nothing appears until the agent actually needs the
  browser. Minimize the window and the agent keeps working. Close it and the next
  request simply opens it again.
- **It keeps its session.** Cookies, logins and local storage persist between calls
  and between restarts, so the agent can work inside sites you have signed in to.
- **It is built to be cheap on tokens.** Screenshots are resized and JPEG-compressed
  by default, pages can be read as text instead of images, and a whole sequence of
  actions can be sent in one batched call.
- **It finds things the way a person does.** Click a button by its visible text, or
  fill a field by its label — useful on modern sites where CSS class names are
  generated and unstable.

## How the window behaves

| Situation | What happens |
|---|---|
| The MCP server starts | No window. The program waits quietly in the background. |
| The agent uses the browser for the first time | The window opens, comes to the front and takes focus. |
| You minimize the window | The agent keeps working. When it takes a screenshot the window is restored — without taking keyboard focus away from what you are doing. |
| You close the window | The program keeps running. The next browser request opens a new window and reloads the pages that were open. |
| The AI client exits | The program exits with it. |

The window title is **MCP Browser for AI**. It has back, forward and reload buttons,
an address bar you can type into yourself, and an **About** button.

## Requirements

- Windows 10 or Windows 11 (64-bit)
- .NET Framework 4.8 — included with Windows 10 (1903 and later) and Windows 11
- Microsoft Edge WebView2 Runtime — included with Windows 11 and current
  Windows 10; otherwise available from
  [Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/)

## Getting it

Build it from source with Visual Studio 2022 version 17.13 or later (the
solution uses the `.slnx` format), with the ".NET desktop development" workload:

1. Clone this repository.
2. Open `src/MCP-Browser/MCP-Browser.slnx`.
3. Restore NuGet packages if Visual Studio asks, choose the **Release**
   configuration, and build.

The program is produced in `src/MCP-Browser/bin/Release/`. Keep the whole folder
together — `MCP-Browser.exe` needs the files beside it.

## Connecting it to your AI client

MCP Browser is a standard **stdio** MCP server: the client starts
`MCP-Browser.exe` and talks to it over standard input and output. Point your client
at the full path of the executable.

**Claude Desktop** — add this to `claude_desktop_config.json`
(Settings → Developer → Edit Config), then restart Claude Desktop:

```json
{
  "mcpServers": {
    "mcp-browser": {
      "command": "C:\\Tools\\MCP-Browser\\MCP-Browser.exe"
    }
  }
}
```

**Claude Code**

```bash
claude mcp add mcp-browser -- "C:\Tools\MCP-Browser\MCP-Browser.exe"
```

**Google Gemini Antigravity** — add this to `%USERPROFILE%\.gemini\config\mcp_config.json`
(create the file if it does not exist), then restart Antigravity:

```json
{
  "mcpServers": {
    "mcp-browser": {
      "command": "C:\\Tools\\MCP-Browser\\MCP-Browser.exe",
      "args": []
    }
  }
}
```

If the file already lists other servers, add the `"mcp-browser"` entry inside the
existing `"mcpServers"` object rather than adding a second one.

**Any other MCP client** — register a stdio server whose command is the path to
`MCP-Browser.exe`. It takes no arguments and needs no environment variables.

Then just ask: *"Open example.com and tell me what's on the page."*

## Tools

Twenty-nine tools, all prefixed `browser_`.

| Group | Tools | What they do |
|---|---|---|
| Navigate | `navigate` `back` `forward` `reload` | Open a URL and move through history. Bare addresses such as `example.com` or `localhost:8080` are accepted. |
| Tabs | `new_tab` `switch_tab` `close_tab` `list_tabs` | Work with several pages in one shared session. |
| See | `screenshot` | Capture the viewport or the full page as JPEG or PNG, with a width cap and quality setting to control token cost. |
| Read | `get_text` `get_inputs` `find_text` `find_field` `get_console` | Read page text, list form fields, locate elements by visible text or label, and read console messages and failed network requests. |
| Act | `click` `click_text` `click_at` `type` `upload_file` `scroll` `set_viewport` | Click by CSS selector, by visible text, or at a pixel from a screenshot; type into fields by selector or label; upload files; scroll and resize. |
| Wait | `wait` `wait_for` | Pause, or wait for an element to appear. |
| Script | `execute_js` `http_request` | Run JavaScript in the page, or make an HTTP request with the page's cookies and origin. |
| Batch | `batch` | Run a chain of the actions above in a single call. Stops at the first failure and returns a structured result with any console and network errors. |
| Housekeeping | `clear_cache` `show_window` `hide_window` `downloads` | Wipe cache and site data; bring the window to the front; minimize it; list downloaded files and wait for them to finish. |

### Things that differ from a hand-driven browser

- **New windows become tabs.** A link with `target="_blank"` or a `window.open`
  call opens a new tab in the same window, and that tab becomes the active one.
- **Pop-up dialogs answer themselves.** `alert`, `confirm`, `prompt` and "leave this
  page?" dialogs are accepted automatically, because an open dialog would freeze
  the page and leave the agent waiting forever. Each one is recorded in the
  console log. Note that this means a `confirm()` always answers OK. Set
  `auto_accept_dialogs` to `false` if you would rather click them yourself.
- **Downloads save themselves.** There is no Save dialog and no download panel: every
  download goes straight into `download_folder` (a number is added if the name is
  taken). The next tool result reports it, with a `[downloads]` line, when it starts
  and when it finishes (with its full path and size) or fails. `browser_downloads`
  lists them and can wait for one that is still in progress.

## Configuration

On first run the program writes `mcp-config.json` next to the executable. Every
setting is optional. Restart the MCP server after changing the file.

| Setting | Default | Meaning |
|---|---|---|
| `window_width`, `window_height` | `1350`, `700` | Size of the page area when the window opens, in pixels. |
| `bring_to_front_on_every_call` | `false` | `false`: the window comes to the front only when it opens, reopens, or the agent calls `browser_show_window`. `true`: every browser action raises and focuses it. |
| `restore_tabs_on_reopen` | `true` | After you close the window, reopen the same pages the next time the agent needs the browser. |
| `auto_accept_dialogs` | `true` | Accept JavaScript dialogs automatically (see above). |
| `default_screenshot_max_width` | `1400` | Screenshots wider than this are scaled down. `0` disables the cap. |
| `default_screenshot_format` | `"jpg"` | `"jpg"` or `"png"`. |
| `default_jpeg_quality` | `70` | JPEG quality for single screenshots, 1–100. |
| `batch_jpeg_quality` | `80` | JPEG quality for screenshots taken inside a batch. |
| `batch_action_delay_ms` | `250` | Pause between batch steps so the page can react. |
| `navigation_timeout_ms` | `30000` | How long to wait for a page to load. |
| `user_data_folder` | `null` | Where the browser profile (cookies, logins, cache) is kept. `null` uses `%LOCALAPPDATA%\McpBrowser\WebView2Data`. |
| `download_folder` | `null` | Where downloads are saved, without asking. `null` uses `%USERPROFILE%\Downloads\McpBrowser`. Environment variables such as `%TEMP%` are allowed. |
| `debug_logging` | `false` | Write `mcp_debug.log` and `browser_debug.log` next to the executable. |

The text the server sends to the AI as usage guidance lives in
`system-prompts.txt`, also next to the executable. Edit it to suit your own
workflow.

## Privacy and safety

- MCP Browser runs entirely on your computer. Its own code contains no telemetry
  and contacts no server; the network traffic is the browsing the agent does.
  (The WebView2 runtime underneath is Microsoft's Edge engine and follows
  Microsoft's own policies.)
- The browser profile is separate from your everyday Edge or Chrome profile. The
  agent can only use logins you have made inside the MCP Browser window.
- An agent with a browser can do whatever a person at that browser could do,
  including on any site you are signed in to. Sign in only where you are
  comfortable letting the agent act, and review what it does on sites that matter.

## Troubleshooting

**The agent reports that WebView2 did not initialize.**
Install the WebView2 Runtime from the link under Requirements.

**The window does not come to the front.**
Windows sometimes refuses to let a background program take focus, and flashes the
taskbar button instead. Click it once; the window is open and working.

**Nothing is written to `mcp-config.json`.**
The program cannot write to its own folder (for example under `Program Files`).
It runs on default settings. Move it to a folder you own to customize it.

**I want to see what is going on inside.**
Set `debug_logging` to `true` and look at the two log files next to the executable.

## Project layout

```
src/MCP-Browser/
  Program.cs        Entry point: starts the MCP server and the (windowless) UI thread
  Core/             JSON-RPC and MCP protocol, tool discovery, configuration
  Browser/          The window, tab management, WebView2 operations, diagnostics
  Tools/            One class per MCP tool
  system-prompts.txt  Usage guidance sent to the AI on connect
```

Adding a tool means adding one class that derives from `BrowserTool`; it is
discovered automatically.

## License

MCP Browser is free software released into the public domain under
[The Unlicense](LICENSE). Use it, change it and share it however you like.

Project home: <https://github.com/adriancs2/MCP-Browser>
