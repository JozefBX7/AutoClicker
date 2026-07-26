# Using AutoClicker

## Interval

Set hours, minutes, seconds, and milliseconds. The delay is between individual actions, or between complete custom-sequence runs. Small variation can happen at very short intervals or while Windows is busy.

## Choose an action

The **Input** menu offers:

- Left, middle, and right click.
- Space, Enter, or a picked key.
- A custom sequence or the sequence editor.

For mouse actions, choose **Single** or **Double**. To change a picked key, choose **Pick a key…** and press the key you want. Press Escape to cancel.

## Input pulse

Use the **Pulse** button to choose how long each generated click or key press stays down. The default is **3 ms**; choose **Off** to send the original immediate down/up packet, or choose 1–5 ms when a game needs a short held input to recognize it.

## Repeat and position

Choose **Repeat until stopped** or **Repeat X times**. For mouse actions, choose the current cursor position or enter fixed X/Y coordinates.

## Target window

Targeting is optional. Leave **Executable name** blank to send input to whichever window is active. Enter an executable name such as `notepad.exe` to run only while any window from that program is active.

Use the search icon beside **Executable name** to open the visible-window chooser when you need a more specific target. Select a window from the dialog; the selection fills in its executable name and also matches its title. If that window loses focus, AutoClicker skips clicks and keys until it is active again.

For fixed mouse coordinates, AutoClicker also skips the click unless the point is inside the active target window's client area. This prevents a moved or resized window from causing a click in another app. Target-window mode does not support **Hold** actions.

## Hotkey and test area

The title-bar hotkey starts and stops AutoClicker from anywhere. Click the pencil to change it; click the back arrow to cancel.

While running mouse input, hover the live area to see its counter and latest interval. For keyboard input, focus the field instead. Custom sequences do not use the live tester.

## Start and stop

Start and Stop are separate buttons so a generated click cannot stop the app immediately. Settings are unavailable while AutoClicker is active; stop the current run first.
