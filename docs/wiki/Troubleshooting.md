# Troubleshooting

## A second window closes immediately

Only one AutoClicker window can run. The existing window should be brought forward instead. If the app fails before opening, check `AutoClicker.log` beside the app or in `%LocalAppData%\AutoClicker`.

## My hotkey does not work

Another app may already use it. Choose a different combination in the title-bar hotkey control. Press Escape to cancel while choosing a new hotkey.

## The timing varies slightly

This is normal at short intervals or while the PC is busy. Windows cannot guarantee every action at the exact same millisecond.

## OpenRGB cannot find or light my keyboard

1. Make sure OpenRGB is running and its SDK server is enabled.
2. Click **Refresh keyboards** in AutoClicker Settings.
3. Rescan devices in OpenRGB and try again.
4. Test a standard key such as F6.

Some keyboards do not expose individual keys through OpenRGB.

## Reporting a problem

Include your AutoClicker version, what you were doing, and `AutoClicker.log` if you can. Do not include private information.
