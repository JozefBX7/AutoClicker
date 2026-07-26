# Settings, data, and backups

## Appearance and window behaviour

On a fresh installation, AutoClicker follows Windows’ app light/dark preference. The title-bar theme button then switches between the matching dark and light themes and saves that explicit choice for later starts. The layout is intentionally identical in both modes; only colours and contrast change.

The pin button keeps AutoClicker above other windows. Pin state is remembered. The centre bar at the footer edge hides or shows the configuration area; compact mode also hides **Set as default** and is remembered between runs. The app starts centred.

## Defaults

**Set as default** stores the full current main-window setup, including:

- Interval, action, click type, repeat rule, and fixed position.
- Chosen keyboard input or custom sequence.
- Global hotkey and its modifiers.
- RGB settings.

In **Settings**, **Reset to defaults** asks for confirmation and restores every setting to the original state, including F6, a 100 ms interval, disabled RGB lighting, and enabled crash recovery.

## Full configuration backup

Use **Settings → Configuration backup** for a portable JSON copy of your setup.

A full backup includes:

- Main defaults and the current custom sequence.
- RGB/OpenRGB configuration.
- Theme, pin, and compact-window preference.
- Every saved sequence preset.

The backup has a schema version, allowing later app releases to retain backward compatibility. Importing applies the backup after validation; stop AutoClicker before importing.

## Where files live

Installed copies use `%LocalAppData%\AutoClicker`. Portable copies use a `Data` folder beside `AutoClicker.exe`.

The app uses separate JSON files for defaults, RGB configuration, appearance, UI preferences, and the sequence library. You generally should use the backup controls rather than hand-editing these files.

## Logs

AutoClicker writes useful startup, error, watchdog, and exception context to `AutoClicker.log` beside the executable when possible. If that location is read-only, the log is written to `%LocalAppData%\AutoClicker\AutoClicker.log`.

Logs rotate at about 2 MB: the previous log becomes `AutoClicker.previous.log`. Attach the current log when filing a bug report.
