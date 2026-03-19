# Automation

GitMishigeh includes a small local automation server for development work. It exists so we can drive the UI, inspect state, and capture screenshots while iterating on the app.

## Availability

The automation server is available in `Debug` builds only.

- Debug builds: automation is enabled automatically
- Release builds: automation is disabled and does not start

The server binds to `127.0.0.1` only. It is never exposed on the network.

## Port

Default port:

- `38457`

Optional override:

- environment variable `GITMISHIGEH_AUTOMATION_PORT`

Example:

```bash
GITMISHIGEH_AUTOMATION_PORT=38458 dotnet run --project GitMishigeh/GitMishigeh.csproj
```

## Endpoints

### `GET /health`

Returns a simple health payload plus a snapshot of the current UI state.

Example:

```bash
curl -s http://127.0.0.1:38457/health
```

### `POST /command`

Accepts a JSON payload with a `command` field and optional command-specific properties.

Example:

```bash
curl -s -X POST http://127.0.0.1:38457/command \
  -H 'Content-Type: application/json' \
  -d '{"command":"get_state"}'
```

## Supported Commands

### `ping`

Checks that the automation server is alive.

Payload:

```json
{ "command": "ping" }
```

### `get_state`

Returns the current view-model-backed UI state.

Payload:

```json
{ "command": "get_state" }
```

### `open_repo`

Opens a local repository path without using the folder picker.

Payload:

```json
{ "command": "open_repo", "path": "/absolute/path/to/repo" }
```

### `refresh`

Refreshes the current repository state.

Payload:

```json
{ "command": "refresh" }
```

### `show_working_tree`

Switches the UI into working copy mode.

Payload:

```json
{ "command": "show_working_tree" }
```

### `show_history`

Switches the UI into history mode.

Payload:

```json
{ "command": "show_history" }
```

### `select_changed_file`

Selects a visible changed file by path and loads its diff.

Payload:

```json
{ "command": "select_changed_file", "path": "relative/or/displayed/path" }
```

### `select_commit`

Selects a commit by short hash.

Payload:

```json
{ "command": "select_commit", "short_hash": "abc1234" }
```

### `set_commit_message`

Updates the commit message textbox.

Payload:

```json
{ "command": "set_commit_message", "message": "My commit message" }
```

### `capture_screenshot`

Captures the app window and saves a PNG under the automation artifacts folder.

Payload:

```json
{ "command": "capture_screenshot", "name": "my-shot" }
```

Notes:

- If `name` is omitted, a timestamped filename is generated
- On macOS, screenshots are captured as the actual app window
- On non-macOS platforms, the current fallback is Avalonia window rendering

## Response Shape

Most commands return:

```json
{
  "ok": true,
  "state": {
    "repositoryPath": "…",
    "currentBranch": "…",
    "statusSummary": "…",
    "outputMessage": "…",
    "selectedDiffHeader": "…",
    "selectedDiff": "…",
    "commitMessage": "",
    "isBusy": false,
    "isHistoryMode": false,
    "selectedFile": null,
    "selectedCommit": null,
    "visibleFiles": [],
    "recentCommits": [],
    "diffSectionCount": 0
  }
}
```

`capture_screenshot` also includes:

```json
{
  "path": "/absolute/path/to/png"
}
```

## Screenshot Output

Automation screenshots are written to:

- `GitMishigeh/artifacts/automation/`

That directory is gitignored through the repository-wide `artifacts/` ignore rule.

## Implementation Notes

- Main startup wiring: [App.axaml.cs](/Users/alexfriedman/Developer/NetProjects/GitMishigeh/GitMishigeh/App.axaml.cs)
- Automation server: [LocalAutomationServer.cs](/Users/alexfriedman/Developer/NetProjects/GitMishigeh/GitMishigeh/Services/LocalAutomationServer.cs)
- UI automation helpers: [MainWindowViewModel.cs](/Users/alexfriedman/Developer/NetProjects/GitMishigeh/GitMishigeh/ViewModels/MainWindowViewModel.cs)

## Maintenance Rule

Whenever the automation surface changes, update this document in the same change. That includes:

- new commands
- removed commands
- payload changes
- response shape changes
- screenshot behavior changes
- port or startup behavior changes
