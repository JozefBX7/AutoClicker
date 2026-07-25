# Troubleshooting

## AutoClicker will not start or a second window closes immediately

Only one AutoClicker instance can run. Starting another copy focuses the already-running window. Look for the existing window or close it before launching again.

If the app failed before showing a window, inspect `AutoClicker.log` beside the executable or in `%LocalAppData%\AutoClicker`.

## My chosen hotkey does not work

Another application may have registered the same global hotkey. Choose a less common combination in the title-bar hotkey control. Escape cancels hotkey capture.

## The timing varies slightly

Small variation is normal under Windows scheduling, especially at very short intervals or under game load. AutoClicker uses native input and efficient waits, but it cannot force the operating system to schedule every event at an exact instant.

## OpenRGB cannot find my keyboard

1. Make sure OpenRGB is running and its SDK server is enabled on port 6742.
2. In AutoClicker Settings, click **Refresh keyboards**.
3. Rescan devices in OpenRGB.
4. Try starting OpenRGB as administrator.
5. If desired, enable automatic OpenRGB startup in AutoClicker.

If OpenRGB is running but its SDK server is off, AutoClicker will not start a second copy. Enable the server in the existing OpenRGB process instead.

## OpenRGB finds the keyboard but cannot light the hotkey

The keyboard may not expose individual key LEDs through OpenRGB, or its key name may not map to the configured hotkey. Try a standard key such as F6, refresh keyboards, and use **Test hotkey lighting**. The test always attempts to restore the original colour.

## The update check says no release is available

The repository needs a published GitHub Release with a valid version tag, such as `v1.0.1`. A source commit alone is not a release. See [Updates and releases](Updates-and-Releases).

## How do I report a crash?

Include the current `AutoClicker.log`, Windows version, AutoClicker version, the selected action, and a short description of what happened. Do not include sensitive information in issue reports.
