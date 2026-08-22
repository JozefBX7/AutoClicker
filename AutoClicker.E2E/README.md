# AutoClicker desktop tests

These tests launch the real WPF application and drive it through Windows UI Automation.
They cover Simple and Advanced configuration, editor scopes, profile lifecycle and
inheritance, hotkey actions and multi-selection, custom sequences, settings/reset/backup
flows, and starting, stopping, concurrently running, and hotkey-toggling macros.

Run the suite from the repository root on Windows:

```powershell
dotnet test AutoClicker.E2E\AutoClicker.E2E.csproj --configuration Release
```

Each test creates a temporary configuration directory, starts AutoClicker with a unique
instance ID, and deletes the directory afterward. End-to-end mode also:

- uses process-scoped mutex/event names and never reads or writes the user's configuration;
- disables global hotkey registration by default, and always disables low-level mouse hooks,
  crash recovery, Quick Start, OpenRGB startup, and OpenRGB device discovery;
- replaces native mouse/keyboard macro output and cursor movement with an event journal;
- restricts file-dialog overrides to the isolated test directory; and
- scopes dialog and popup discovery to the launched AutoClicker process.

Two keyboard-listener tests explicitly opt into real Windows `RegisterHotKey` registration and
send exact Ctrl+Shift+F6/F7 chords. This covers operating-system delivery, selected-action
re-registration, and non-selected action bindings. Native macro input remains replaced by the
event journal, mouse hooks remain disabled, and the registered chords are released when the
isolated process exits.

The suite can therefore exercise real worker lifecycles and toggle behavior without sending
macro output to Windows, moving the pointer as part of a macro, connecting to lighting hardware,
or controlling another application. UI Automation itself can move the pointer over AutoClicker's
own test window for ordinary clicks, and the listener tests send only their documented chords.

External boundaries such as mouse-hotkey hooks, selecting an arbitrary desktop window, accepting
a global position-picker click, contacting GitHub Updates, opening browser links, and controlling
a real OpenRGB device are intentionally not exercised until they have dedicated safe test seams.
Their surrounding dialogs, cancellation paths, settings persistence, and validation are
covered without crossing those boundaries.

When extending the suite:

- Seed deterministic settings through `ProfileE2EFixture`.
- Drive controls through stable `AutomationProperties.AutomationId` values.
- Verify both the visible UI and the persisted configuration after restarting the app.
- Keep tests non-parallel because desktop keyboard and pointer input are process-global.
- Never bypass `AppRuntime` for worker input, external-device discovery, or file pickers.
