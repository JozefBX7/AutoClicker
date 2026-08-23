# AutoClicker

A compact Windows auto clicker and keyboard spammer with two ways of working: a focused one-hotkey Simple mode, or Advanced profiles with up to 15 independently configurable hotkeys running together. It includes custom sequences, press-and-hold hotkey execution, configurable input pulses and jitter, backups, optional OpenRGB lighting, and foreground-window targeting that keeps input in the app you choose.

Detailed guides are available in the [AutoClicker Wiki](https://github.com/JozefBX7/AutoClicker/wiki).

<img width="522" height="580" alt="image" src="https://github.com/user-attachments/assets/5bb348c5-f2d3-42df-85ea-9710cee069eb" />

## Project layout

- `Automation/` contains action, hotkey, profile, sequence, and worker logic.
- `Configuration/` contains persisted settings, backup, and reset rules.
- `Services/` contains integrations such as OpenRGB, updates, crash recovery, and window targeting.
- `Presentation/` and `Views/` contain theming, converters, and WPF dialogs/pickers.
- `AutoClicker.Tests/` mirrors those responsibilities for unit tests.

## Choose a mode

- **Simple mode** keeps one action and one global hotkey visible, ideal for a quick clicker or key spammer on one key.
- **Advanced mode** keeps named profiles of hotkeys. Actions may run concurrently, can be enabled or disabled individually, and inherit interval, repeat, position, targeting, jitter, pulse, and lighting from profile or Advanced defaults until overridden.

Profiles are saved deliberately, so experimenting does not overwrite a saved setup. They can be duplicated, reordered, imported/exported individually, or copied into another profile. Each Advanced hotkey can also have a separate binding that enables or disables it; disabling stops that action, and re-enabling a While-held action starts it immediately when its run trigger is already down.

## Input and targeting

Leave the target blank for normal global input, enter an executable name to run only while that app is active, or choose a specific visible window. AutoClicker pauses its clicks and keys whenever the selected target is not in the foreground.

Choose left, middle, or right clicks; Space, Enter, or a picked key; or a custom sequence. Mouse buttons beyond left/right may also be used as global hotkeys.

Custom-sequence events can be normal presses, held inputs, matching releases, or explicit waits. The editor supports multi-selection, grouped drag-and-drop, clipboard-style editing, automatic matching releases, and a non-running timeline preview. Balanced hold/release events make it possible to build real key chords and timed mouse holds while retaining automatic cleanup if a run is stopped.

The Action menu supports **Single**, **Double**, **Hold**, and **While held**. Hold keeps one generated input pressed until the action is stopped. While held instead repeats complete clicks, key presses, or custom sequences at the configured interval for only as long as the action hotkey remains physically held.

## Keep your setup portable

Settings can export or restore **Everything**, **Simple mode**, **Advanced mode and profiles**, or **Custom sequences**. Full backups are versioned for future compatibility, while profile export makes it easy to share one named setup.

Window pinning can be remembered independently from whether it is applied immediately at startup. Delayed pinning lets the app launch normally and become always-on-top only after the first interaction. The main window also returns to its last normal position and is moved back into a visible work area if the monitor layout has changed.

OpenRGB keyboard lighting can also keep an optional idle OpenRGB profile selection in full backups and full resets. Keyboard discovery uses OpenRGB's device type and generic keyboard characteristics rather than a hard-coded vendor or model list.

## Download

Get AutoClicker from the [latest release](../../releases/latest).

- **Installer (recommended):** `AutoClicker-Setup-x64.exe` installs the app and includes an uninstaller.
- **Portable:** `AutoClicker-Portable-x64.zip` runs without installation. Keep its `Data` folder with the app to retain your settings.

Uninstalling keeps your settings and saved sequences. You can remove them manually later if you wish.

## Updates

In **Settings → Updates**, choose **Check GitHub Releases** whenever you want to look for a newer version. AutoClicker never checks in the background. Installed copies can download the normal installer; portable copies open the matching ZIP in your browser.

Use AutoClicker only where automated input is permitted. Press the configured global hotkey (F6 by default) at any time to stop.
