# Getting started

## Choose an install type

Download the current release from the repository’s **Releases** page.

### Installer (recommended)

Run `AutoClicker-Setup-x64.exe`. It uses the standard Inno Setup installer, adds an Apps & Features uninstaller, and can create a desktop shortcut. Your settings are stored per user in:

```text
%LocalAppData%\AutoClicker
```

Uninstalling deliberately leaves this folder in place so preferences and saved sequences can survive a reinstall. Delete it manually only if you want a completely clean start.

### Portable

Extract `AutoClicker-Portable-x64.zip` anywhere and run `AutoClicker.exe`. No installation is required. The included `portable.flag` keeps settings beside the program in:

```text
Data
```

Keep the `Data` folder with the executable when copying, moving, or updating the portable copy. If the folder cannot be written, AutoClicker falls back to `%LocalAppData%\AutoClicker`.

## First run

1. Pick an interval. The default is **100 ms**.
2. Choose an action such as **Left click**, **Space**, or **Pick a key**.
3. Choose whether to repeat until stopped or a set number of times.
4. Press **Start**, or use the global hotkey shown in the title bar (F6 by default).
5. Stop with **Stop** or the same global hotkey.

The app starts centred on screen. Use the pin icon to keep it above other windows, the theme icon to switch dark/light mode, and the centre bar above the footer to hide or show the configuration area.

## Important defaults

- Default global hotkey: **F6**.
- Default interval: **100 ms**.
- Default run mode: repeat until stopped.
- Default position: current cursor position.
- RGB lighting: disabled until explicitly configured.

Use **Set as default** to make the current main-window choices the startup defaults. The confirmation explains that **Settings → Reset to defaults** can restore the original values.
