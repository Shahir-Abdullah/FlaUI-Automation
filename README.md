# FlaUI Desktop Automation Toolkit

A three-project sample built on [FlaUI](https://github.com/FlaUI/FlaUI) for
observing and driving Windows desktop apps via UI Automation:

- **Recorder** — the user drives a real GUI manually and the tool logs every
  UIA event into a JSON Lines file.
- **Runner** — the tool reads a YAML workflow and *performs* UI steps
  (clicks, typing, grid edits, etc.) itself.

Both tools are process-agnostic. The bundled `DemoApp` is convenient for
trying them out, but they work against **any Windows app** (Notepad,
Microsoft Teams, Outlook, your own WPF / WinForms / WinUI app, …) without
changing a single line of code.

```
FlaUIDemo.slnx
├── DemoApp/             # Sample WPF application to drive while testing
├── UiEventRecorder/     # Observes the user and logs UIA events to JSONL
└── UiWorkflowRunner/    # Performs UI steps from a YAML workflow file
```

---

## 1. Prerequisites

| What                              | Why                                                                                       |
| --------------------------------- | ----------------------------------------------------------------------------------------- |
| **Windows 10 or 11**              | UI Automation is a Windows API. The tool cannot run on Linux/macOS.                       |
| **.NET 10 SDK**                   | Both projects target `net10.0-windows`. Install from <https://dotnet.microsoft.com>.      |
| **An interactive desktop session**| UIA needs a logged-in user and a visible desktop. Won't work in headless CI containers.   |
| **PowerShell** (any modern build) | All commands in this README are PowerShell. Adapt syntax if you use cmd or another shell. |

Verify your tooling:

```powershell
dotnet --version    # should print 10.x.x or newer
```

---

## 2. Get the code and build

```powershell
# 1. Clone or copy this repo, then open the folder
cd C:\path\to\FlaUI

# 2. Restore + compile both projects
dotnet build FlaUIDemo.slnx
```

A clean build prints `Build succeeded.` at the end. The two outputs you care
about are:

- `DemoApp\bin\Debug\net10.0-windows\DemoApp.exe`
- `UiEventRecorder\bin\Debug\net10.0-windows\UiEventRecorder.exe`

---

## 3. Quick start — record the bundled DemoApp

You need two PowerShell windows.

**Terminal 1 — start the demo app you'll click around in:**

```powershell
dotnet run --project DemoApp
```

A WPF window titled **"FlaUI Demo App"** appears. It has three sections:
Greeter, Counter, and a People DataGrid.

**Terminal 2 — start the recorder:**

```powershell
dotnet run --project UiEventRecorder
```

You'll see something like:

```
UI Event Recorder
  processes  : DemoApp
  output     : C:\path\to\FlaUI\events.jsonl
  echo       : True

[tracker] attached  pid=12345  DemoApp              hwnd=0x10A0C  "FlaUI Demo App"
Currently tracking 1 window(s):
  - DemoApp (pid 12345) "FlaUI Demo App"
Tracking - press Ctrl+C to stop.
```

Now go back to the demo app window and use it normally — type into the text
boxes, change the language, tick the checkbox, click Greet, edit grid cells,
tick the Active checkbox, click Delete on a row, etc. Watch the recorder's
console: every interaction shows up live, and the same events are appended
to `events.jsonl` in the directory you started the recorder from.

When you're done, press **Ctrl+C** in the recorder terminal (or just close
the demo app). The recorder flushes the log and prints:

```
Wrote 173 events to 'C:\path\to\FlaUI\events.jsonl'.
```

---

## 4. Recording any other Windows application

The recorder doesn't depend on `DemoApp` at all. To record another app you
need exactly two things:

1. The application's **process name** (the executable name, without `.exe`).
2. Permission to read its UIA tree (same elevation level — see
   [Troubleshooting](#7-troubleshooting)).

### 4.1 Find the target process name

Start the application you want to record (so it shows up in the process list),
then in any PowerShell window run:

```powershell
# Show every running process that owns a visible top-level window.
Get-Process |
    Where-Object { $_.MainWindowTitle -ne '' } |
    Select-Object ProcessName, Id, MainWindowTitle |
    Sort-Object ProcessName |
    Format-Table -AutoSize
```

Example output (truncated):

```
ProcessName     Id MainWindowTitle
-----------     -- ---------------
chrome        8412 New Tab - Google Chrome
DemoApp      12345 FlaUI Demo App
explorer      4960 (multiple)
ms-teams     19288 Chat | Microsoft Teams
notepad      27160 Untitled - Notepad
OUTLOOK       9924 Inbox - you@example.com - Outlook
WINWORD       6112 Document1 - Word
```

The string under **ProcessName** is what you pass to `--process`. Common
ones worth knowing:

| Application              | Process name (`--process …`) |
| ------------------------ | ---------------------------- |
| Notepad (classic)        | `notepad`                    |
| Microsoft Teams (new)    | `ms-teams`                   |
| Microsoft Teams (classic)| `Teams`                      |
| Microsoft Outlook        | `OUTLOOK`                    |
| Microsoft Word           | `WINWORD`                    |
| Microsoft Excel          | `EXCEL`                      |
| File Explorer            | `explorer`                   |
| Calculator (modern)      | `CalculatorApp`              |

> Process names are **case-insensitive** when passed to `--process`.

### 4.2 Run the recorder against it

Two terminals again. In the first one start the app you want to record (or
have it already running). In the second one:

```powershell
dotnet run --project UiEventRecorder -- --process <name>
```

The `--` between `UiEventRecorder` and `--process` tells `dotnet run` "stop
parsing your own arguments and pass the rest to the program."

The recorder will:

- Attach to **every top-level window** of every process matching `<name>`
  that's already open.
- Keep listening — if the user opens a new window inside that app (a Teams
  chat popup, a Word document, an Outlook compose window), the recorder
  automatically attaches to it too.
- Detach again when a window closes.

Stop with **Ctrl+C** when you're done. The JSONL file is flushed and closed.

### 4.3 Worked example: Notepad

```powershell
# Terminal 1
notepad

# Terminal 2
dotnet run --project UiEventRecorder -- --process notepad --output notepad.jsonl
```

Type a few characters, open File → Save As, cancel the dialog, close Notepad.
Ctrl+C the recorder. `notepad.jsonl` will contain:

- `WindowAttached` for the main Notepad window
- `FocusChanged` to the edit area
- `TextChanged` / `PropertyChanged Value` for every keystroke
- `WindowOpened` and `WindowAttached` when the Save As dialog appeared
- `Invoke` events for the dialog buttons you clicked
- `WindowClosed` / `WindowDetached` when the dialog and Notepad closed

### 4.4 Worked example: Microsoft Teams

```powershell
# Start Teams normally from the Start menu, then:
dotnet run --project UiEventRecorder -- --process ms-teams --output teams.jsonl
```

For Teams you'll reliably see window lifecycle, focus transitions, and
clicks on chrome (titlebar, navigation rail, etc.). Inside the chat surface
itself Teams is a WebView2 (Chromium) host — see the WebView2 caveat in
[Troubleshooting](#7-troubleshooting).

### 4.5 Recording several applications at once

`--process` can be repeated **or** comma-/semicolon-separated:

```powershell
dotnet run --project UiEventRecorder -- --process ms-teams --process outlook
dotnet run --project UiEventRecorder -- --process ms-teams,outlook,winword
```

A single `events.jsonl` will contain interleaved events from all of them;
each event carries the originating `ProcessName`, `ProcessId`, and
`WindowTitle` so they can be separated again later.

---

## 5. CLI reference

```powershell
dotnet run --project UiEventRecorder -- [options]
```

| Flag                  | Short | Default          | Description                                                                                                  |
| --------------------- | ----- | ---------------- | ------------------------------------------------------------------------------------------------------------ |
| `--process <name...>` | `-p`  | `DemoApp`        | Process name(s) to track (no `.exe`). Repeatable, and comma/semicolon-separated within one value works.      |
| `--output  <path>`    | `-o`  | `events.jsonl`   | Path of the JSONL log file (relative to the working dir).                                                    |
| `--quiet`             | `-q`  | off              | Suppress the per-event console summary; only write to the log file. Useful for chatty apps like Teams.       |
| `--help`              | `-h`  | —                | Print usage and exit.                                                                                        |

---

## 6. What gets recorded and what the log looks like

Subscribed event categories (verbose by design):

- **Pattern events** — `Invoke`, `SelectionItemSelected`,
  `SelectionItemAdded` / `Removed`, `SelectionInvalidated`, `TextChanged`,
  `TextSelectionChanged`.
- **Element events** — `MenuOpened` / `Closed`, `ToolTipOpened` / `Closed`,
  `LayoutInvalidated`, `LiveRegionChanged`, `SystemAlert`.
- **Window events** — `WindowOpened`, `WindowClosed`, plus the tracker's own
  `WindowAttached` / `WindowDetached` lifecycle markers.
- **Property changes** on every descendant of every tracked window —
  `Name`, `HasKeyboardFocus`, `IsEnabled`, `IsOffscreen`, `BoundingRectangle`,
  `HelpText`, `ItemStatus`, `Value.Value`, `Value.IsReadOnly`,
  `RangeValue.Value`, `Toggle.ToggleState`, `SelectionItem.IsSelected`,
  `ExpandCollapse.ExpandCollapseState`, `Window.WindowVisualState`,
  `Window.WindowInteractionState`, `Window.IsModal`.
- **Structure changes** — children added / removed / reordered on the subtree
  (so adding/removing grid rows or tabs is captured).
- **Global focus changes** — routed to whichever tracked process owns the
  focused element.

Each line in the log is a single self-contained JSON object:

```json
{"Timestamp":"2026-06-10T12:34:56.789+06:00","EventType":"WindowAttached","ProcessName":"ms-teams","ProcessId":12345,"WindowTitle":"Chat | Jane Doe | Microsoft Teams"}
{"Timestamp":"2026-06-10T12:34:57.012+06:00","EventType":"FocusChanged","ProcessName":"ms-teams","ProcessId":12345,"WindowTitle":"Chat | Jane Doe | Microsoft Teams","Name":"Type a new message","ControlType":"Edit"}
{"Timestamp":"2026-06-10T12:34:58.300+06:00","EventType":"Invoke","ProcessName":"ms-teams","ProcessId":12345,"WindowTitle":"Chat | Jane Doe | Microsoft Teams","Name":"Send","ControlType":"Button"}
```

### Inspecting / filtering the log

Pretty-print every event with PowerShell:

```powershell
Get-Content events.jsonl | ForEach-Object { $_ | ConvertFrom-Json }
```

Show only button invokes:

```powershell
Get-Content events.jsonl |
    ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.EventType -eq 'Invoke' } |
    Select-Object Timestamp, ProcessName, WindowTitle, AutomationId, Name
```

Group counts by event type:

```powershell
Get-Content events.jsonl |
    ForEach-Object { ($_ | ConvertFrom-Json).EventType } |
    Group-Object | Sort-Object Count -Descending
```

If you have [`jq`](https://stedolan.github.io/jq/) installed, the same kind
of filtering is one-liners:

```powershell
jq -c 'select(.EventType=="Invoke")' events.jsonl
jq -r '.EventType' events.jsonl | sort | uniq -c | sort -rn
```

---

## 7. Troubleshooting

**“No matching windows are open yet …”**
The process name doesn't match anything currently running. Re-check with the
`Get-Process` snippet in §4.1 — the value goes into `--process` **without**
`.exe`, case-insensitive. If the app spawns its main window via a launcher
(e.g. some Electron / WebView2 apps), the visible window's owning process may
not be the one you started — use the snippet to confirm.

**Recorder runs but no events appear when I click**
Two likely causes:

1. **Elevation mismatch.** If the target app runs as Administrator and the
   recorder doesn't (or vice versa), UIA refuses to cross the boundary.
   Start the recorder from an **elevated** PowerShell (`Run as
   Administrator`) when targeting elevated apps.
2. **WebView2 / Chromium content.** Modern Teams, new Outlook, Edge, Slack
   etc. render their main content in a WebView2 (Chromium). UIA can see
   the host window chrome but not the in-page DOM unless the host bridges
   its accessibility tree out (most don't, fully). You'll still get window
   lifecycle, focus events, and chrome clicks reliably.

**Console is flooded with `PropertyChanged BoundingRectangle` lines**
Some apps (especially Teams, Office) recompute layout constantly. Pass
`--quiet` to silence the live echo — the JSONL file is always written, and
you can post-filter it.

**`The term 'dotnet' is not recognized…`**
.NET 10 SDK isn't installed or isn't on `PATH`. Install from
<https://dotnet.microsoft.com> and reopen PowerShell.

**Build fails with an `NU1900` warning about a `codeartifact` feed**
That's an internal NuGet vulnerability-data source that isn't reachable
from outside the corporate network. It's a warning, not an error — the
build still succeeds and the package itself is restored from the public
NuGet feed.

**Nothing happens when I open a brand-new window of the tracked app**
The recorder attaches on `WindowOpenedEvent` from the desktop. If the new
window has no native HWND yet (rare, but happens with some splash screens),
it's skipped. Once a real top-level window appears it will be picked up.

---

## 8. Replaying workflows with `UiWorkflowRunner`

The runner is the recorder's twin: instead of *observing* what a human does,
it *performs* a scripted sequence of UI steps you describe in YAML. You can
use it to:

- Automate a repetitive task ("fill out this form and submit").
- Pick up where a human left off — the user does some work in the app, then
  starts the runner to finish the rest.
- Drive an app to a known state for testing or demos.

### 8.1 Quick start

The runner attaches to the same kind of live application the recorder does.
Run the demo app, then the runner:

```powershell
# Terminal 1
dotnet run --project DemoApp

# Terminal 2 - run the sample workflow that ships with the repo
dotnet run --project UiWorkflowRunner -- --file .\workflows\demo-add-and-delete.yaml
```

You'll see the runner attach, then a per-step log:

```
UI Workflow Runner
  workflow : C:\Users\you\FlaUI\workflows\demo-add-and-delete.yaml
  dry-run  : False
  report   : (none)

Target process: DemoApp (pid 12345)
Target window : "FlaUI Demo App" (AutomationId='MainWindow')

Workflow: Greet Ada and tidy the People grid    steps: 14

  ok    [clear-greeter] click              (43 ms)
  ok    #2              setText            (12 ms)
  ok    #3              setText            (10 ms)
  ok    #4              selectComboItem    (76 ms)
  ok    #5              setCheck           (28 ms)
  ok    #6              click              (35 ms)
  ok    #7              waitForText        (412 ms)
  ok    [add-row]       click              (40 ms)
  ok    #9              setText            (24 ms)
  ok    #10             setText            (22 ms)
  ok    #11             setCheck           (31 ms)
  ok    #12             sleep              (400 ms)
  ok    [delete-row]    click              (33 ms)
  ok    #14             assert             (11 ms)

Done: 14 ok, 0 failed, 0 skipped (1.91s).
```

Watch the demo app: the runner is typing into the text boxes, ticking the
checkbox, editing grid cells, and clicking buttons — exactly as if a user
were sitting at the keyboard.

> **Tip:** run the recorder in a third terminal at the same time
> (`dotnet run --project UiEventRecorder`) to see every event the runner
> triggers, logged just like a real user session.

### 8.2 Workflow YAML schema

A workflow has three top-level sections: `target`, `defaults`, and `steps`.

```yaml
name: "Friendly workflow name"            # optional, shown in logs
description: "Free-form, multi-line ok"   # optional

target:
  process: DemoApp                # required - process name without .exe
  windowTitle: "FlaUI Demo App"   # optional - case-insensitive substring
  startIfNotRunning:              # optional - launch the app if needed
    path: ".\\DemoApp\\bin\\Debug\\net10.0-windows\\DemoApp.exe"
    arguments: ""                 # optional command-line args
    workingDirectory: "."         # optional CWD for the process
    waitForReady: 10s             # how long to wait for a window

defaults:
  timeout: 5s                     # default per-step timeout (locating elements)
  retry: 2                        # retries for transient UIA errors
  pauseBetweenSteps: 200ms        # delay inserted after every step

steps:
  - id: optional-step-id          # any string, shown in logs/reports
    action: click                 # see "Action reference" below
    target: { automationId: GreetButton }
    timeout: 3s                   # optional per-step override
    # ...action-specific fields below
```

### 8.3 Target locators

Every action that needs an element takes a `target:` map. Three flavours,
combinable inside one locator:

```yaml
# 1) Plain UIA properties (AND'd together)
target:
  automationId: FirstNameTextBox
  # other optional filters:
  # name: "Greet"
  # controlType: Button   # any FlaUI ControlType: Button, Edit, CheckBox, ...
  # className: TextBox

# 2) XPath against the target window
target:
  xpath: "//Button[@AutomationId='GreetButton']"

# 3) Grid cell (DataGrid, ListView, table, ...)
target:
  grid: PeopleGrid            # AutomationId of the grid
  row: last                   # 0, "first", "last", or columnEquals (below)
  column: Name                # header text, or 0-based index
```

Row selectors:

```yaml
row: 0                              # by index
row: first                          # same as 0
row: last                           # last row currently in the grid
row: { columnEquals: { Name: "Ada Lovelace" } }   # first row whose
                                                  # "Name" cell equals this
```

### 8.4 Action reference

| Action            | Required fields                       | Notes                                                                          |
| ----------------- | ------------------------------------- | ------------------------------------------------------------------------------ |
| `click`           | `target`                              | UIA `Invoke` (or `SelectionItem.Select`). On grid cells, drills into a Button. |
| `mouseClick`      | `target`                              | Real cursor click at the element's centre. Use when `Invoke` isn't supported.  |
| `setText`         | `target`, `value`                     | Sets value via `ValuePattern`; falls back to keyboard for read-only-protected. |
| `appendText`      | `target`, `value`                     | Focuses, jumps to End, then types the value.                                   |
| `clearText`       | `target`                              | Empties the field (same fallback path as `setText`).                           |
| `selectComboItem` | `target`, `value`                     | Selects by display text, or by integer index.                                  |
| `setCheck`        | `target`, `value: true / false`       | On grid cells, drills into a CheckBox descendant.                              |
| `focus`           | `target`                              | Gives the element keyboard focus.                                              |
| `keys`            | `keys` (string) and optional `target` | Raw keystrokes typed via FlaUI's keyboard helper.                              |
| `waitFor`         | `target` (+ optional `timeout`)       | Waits until the element exists and (if applicable) is enabled.                 |
| `waitForText`     | `target`, `expected`                  | Polls until the element's Value/Name equals the expected string.               |
| `assert`          | `target`, `expected`, `property`      | `property` ∈ name / value / automationId / className / isChecked / isEnabled.  |
| `sleep`           | `duration`                            | Pause for a duration string (`500ms`, `5s`, …).                                |
| `screenshot`      | `file`                                | Saves a PNG of the target element (or the window if no `target`).             |

Duration strings: `300ms`, `5s`, `2m`, `1h`. A plain number is taken as
milliseconds.

### 8.5 CLI

```powershell
dotnet run --project UiWorkflowRunner -- --file <workflow.yaml> [options]
```

| Flag                   | Default | Description                                                       |
| ---------------------- | ------- | ----------------------------------------------------------------- |
| `--file <path>`        | —       | Path to the YAML workflow (required).                             |
| `--dry-run`            | off     | Resolve every locator but skip the actual actions.                |
| `--report <path>`      | —       | Write a JSON run summary (per-step status, duration, error text). |
| `--verbose`            | off     | Per-step locator details on stdout.                               |
| `--continue-on-error`  | off     | Keep running after a step fails (default aborts the run).         |

Exit codes: `0` success, `2` workflow parse error, `3` at least one step
failed, `4` runtime/setup error.

### 8.6 Writing a workflow for *your* application

The flow is the same as with the recorder — find the process name, find the
AutomationIds of the controls you want to drive, then describe the steps.

1. **Find the process name.** Same `Get-Process` snippet from §4.1.

2. **Find the controls you want to drive.** Easiest options:
   - Use the bundled recorder (`UiEventRecorder`) and click around: every
     event line tells you the `AutomationId`, `ControlType`, and `Name` of
     the element you just touched.
   - Or use Microsoft's free [Accessibility Insights for Windows](https://accessibilityinsights.io/)
     / [Inspect.exe](https://learn.microsoft.com/en-us/windows/win32/winauto/inspect-objects)
     to point-and-inspect any element.

3. **Author the YAML** (use `workflows/demo-add-and-delete.yaml` as a template):

   ```yaml
   target:
     process: notepad
   defaults:
     timeout: 5s
     pauseBetweenSteps: 100ms
   steps:
     - action: setText
       target: { controlType: Edit }   # Notepad has one Edit control
       value: "Hello from the workflow runner!\nSecond line."
     - action: screenshot
       file: notepad.png
   ```

4. **Run it (dry-run first to validate locators)**:

   ```powershell
   notepad                                                      # terminal 1
   dotnet run --project UiWorkflowRunner -- --file .\my.yaml --dry-run
   dotnet run --project UiWorkflowRunner -- --file .\my.yaml --report run.json
   ```

If a step fails the console shows the offending locator and the runner
writes the failure into the JSON report.

---

## 9. Required NuGet packages

Both projects pull `FlaUI` from NuGet (already declared in the csproj
files):

- `FlaUI.Core` 5.0.0
- `FlaUI.UIA3` 5.0.0
- `YamlDotNet` 16.2.1 (workflow runner only)

---

## 10. Project quick reference

### `DemoApp` (WPF, `net10.0-windows`)

Single-window playground with three sections so the recorder has plenty of
different UIA patterns to capture:

| Section  | Control                  | AutomationId            |
| -------- | ------------------------ | ----------------------- |
| Window   | Main window              | `MainWindow`            |
|          | Status bar text          | `StatusLabel`           |
| Greeter  | First name text box      | `FirstNameTextBox`      |
|          | Last name text box       | `LastNameTextBox`       |
|          | Language combo box       | `LanguageComboBox`      |
|          | Uppercase check box      | `UppercaseCheckBox`     |
|          | Greet button             | `GreetButton`           |
|          | Clear button             | `ClearButton`           |
|          | Greeting label           | `GreetingLabel`         |
| Counter  | Increment button         | `IncrementButton`       |
|          | Decrement button         | `DecrementButton`       |
|          | Reset button             | `ResetButton`           |
|          | Count label              | `CountLabel`            |
| People   | Editable data grid       | `PeopleGrid`            |
|          | Per-row Delete button    | `DeleteRowButton`       |
|          | Add row button           | `AddRowButton`          |
|          | Toggle all active button | `ToggleAllActiveButton` |
|          | Clear grid button        | `ClearGridButton`       |

### `UiEventRecorder` (Console, `net10.0-windows`)

| File                 | Responsibility                                                                |
| -------------------- | ----------------------------------------------------------------------------- |
| `Program.cs`         | CLI parsing, lifecycle, Ctrl+C handling, summary on exit.                     |
| `WindowTracker.cs`   | Discovers existing matching windows and listens for newly opened/closed ones. |
| `EventRecorder.cs`   | Per-window subscription to UIA events + property + structure changes.         |
| `WindowContext.cs`   | Identifies the originating window for every emitted event.                    |
| `RecordedEvent.cs`   | Shape of a single JSON line in the log.                                       |
| `JsonlEventSink.cs`  | Thread-safe, background-flushed JSONL writer with optional console echo.      |

### `UiWorkflowRunner` (Console, `net10.0-windows`)

| File                                  | Responsibility                                                                   |
| ------------------------------------- | -------------------------------------------------------------------------------- |
| `Program.cs`                          | CLI parsing, target window resolution (with optional auto-launch).               |
| `Workflow/WorkflowDefinition.cs`      | POCOs for the YAML schema (`target`, `defaults`, `steps`, `TargetSpec`).         |
| `Workflow/WorkflowLoader.cs`          | YamlDotNet deserialiser + basic validation.                                      |
| `Workflow/DurationParser.cs`          | `"500ms"` / `"5s"` / `"2m"` ➜ `TimeSpan`.                                        |
| `Execution/StepContext.cs`            | Shared state passed to every action during a run.                                |
| `Execution/ElementLocator.cs`         | `TargetSpec` ➜ `AutomationElement` (with polling).                               |
| `Execution/GridCellLocator.cs`        | Resolves `{grid, row, column}` locators (incl. `columnEquals` row search).       |
| `Execution/StepActions.cs`            | Every supported action (`click`, `setText`, `setCheck`, `assert`, …).            |
| `Execution/WorkflowRunner.cs`         | Orchestrates the per-step loop, retries, pause-between-steps, abort/continue.    |
| `Reporting/RunReport.cs`              | JSON run report writer.                                                          |
