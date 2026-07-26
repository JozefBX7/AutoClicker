# Custom sequences

A custom sequence is an ordered list of input events. Each full sequence is repeated using the main interval; waits inside the list occur exactly where they are placed.

## Build a sequence

Open **Open custom sequence editor** from the Input menu.

- The left, middle, and right mouse buttons add their event immediately.
- The keyboard icon waits for the next key press, then adds that key event.
- The delay button adds a separate wait event. If an event is selected, the delay is placed after it.
- Use the up/down arrows or drag and drop to reorder events.
- Use × to remove the selected event.

Events are displayed in execution order. A delay is independent of the key or click before it, which makes a routine easier to inspect and rearrange.

> **Screenshot placeholder — Sequence editor**
>
> Add one screenshot with a short mixed sequence selected, showing the action buttons, a delay event, and the saved-sequences area.

## Use and save a sequence

Click **Use sequence** to put the edited sequence into the main window. Then select **Custom sequence** as the action.

Saved presets are kept in the sequence library. Select **Custom sequence** in the main Input menu and the saved-preset flyout opens on its right. Choosing a preset copies its steps into the active custom sequence; the visible action remains simply **Custom sequence**.

If no saved presets exist, the flyout says so. The editor also shows a clear placeholder when there are no events.

## Back up and share presets

The sequence editor provides library actions for saving or managing presets. Sequence data is retained automatically with your application configuration.

For a complete safety copy, use **Settings → Configuration backup → Export full backup**. That file includes the whole sequence library as well as all other settings. Full backups use a schema version so future versions can preserve compatibility.

Use the editor’s sequence import/export controls when you only want to move sequences. Use a full backup when you want to preserve the entire app state.

## Practical example

To repeat a left click followed by Space:

1. Add a left-click event.
2. Add a delay event if a pause is needed.
3. Add a keyboard event and press Space.
4. Use the sequence, choose **Custom sequence** in the main window, then start it.

The main interval controls the gap before the next complete run; the explicit delay controls the gap inside the run.
