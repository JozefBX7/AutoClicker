# Using AutoClicker

## Interval

Set hours, minutes, seconds, and milliseconds. Clicking one of these fields selects its current value, ready to replace. The entered delay is the time between individual actions, or between complete custom-sequence runs.

For demanding games or applications, Windows scheduling can still introduce small variation. AutoClicker uses native input and waitable timing where appropriate, but no desktop application can guarantee an exact millisecond interval under system load.

## Actions

The **Input** menu offers:

- **Left click**, **Middle click**, and **Right click**.
- **Space** and **Enter**.
- **Custom key** to reuse a previously chosen key.
- **Pick a key…** to capture the next key pressed.
- **Custom sequence** to run the current sequence, or choose a saved sequence from the flyout at its right.
- **Open custom sequence editor** to create or edit sequences.

For mouse actions, choose **Single** or **Double** in the neighbouring Action menu. Keyboard actions use a normal key down/up pair.

When changing a key, the old global hotkey is temporarily not allowed to trigger AutoClicker. Press Escape to cancel key capture.

## Repeat and position

Choose one repeat mode:

- **Repeat until stopped** keeps running until Stop or the global hotkey is used.
- **Repeat _X_ times** completes the requested number of runs and stops itself.

For mouse actions, select either:

- **Current cursor position** — follows the pointer at the moment each action is sent.
- **Fixed** — sends clicks at the supplied X/Y screen coordinates.

Keyboard and custom-sequence actions do not need a cursor position, though a sequence can still contain mouse events.

## Global hotkey

The hotkey in the title bar starts and stops AutoClicker from anywhere. Click the pencil to capture a new one; the control changes to a back-arrow while capture is active, allowing cancellation. The hotkey is included in **Set as default** and full backups.

## Live test area

The live area is intentionally small. While running mouse input, hover it to see its counter and the latest measured interval. The counter resets after roughly three seconds with no observed clicks.

While running a keyboard action, focus the field in the live area instead. It records received key presses and shows the measured interval. Custom sequences do not use the live tester because a sequence can contain several different event types.

## Start and stop

The Start and Stop buttons are deliberately separate. This prevents the first generated click from immediately pressing the same control and stopping the run. Disabled buttons use a distinct subdued appearance and do not react to hover.

Opening Settings is blocked while AutoClicker is active. Stop the current run first.
