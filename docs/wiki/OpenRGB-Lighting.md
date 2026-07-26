# OpenRGB lighting

AutoClicker can optionally light the configured global hotkey on a compatible RGB keyboard while AutoClicker is active. It communicates only with a local OpenRGB SDK server at `127.0.0.1:6742`; it does not need iCUE, Synapse, or another vendor background application.

## Set it up

1. Install and start [OpenRGB](https://openrgb.org/).
2. Enable its SDK server (port 6742).
3. In **Settings → Keyboard lighting**, enable **Light the hotkey with OpenRGB while AutoClicker is active**.
4. Click **Refresh keyboards** and select the desired keyboard.
5. Choose an indicator colour and effect.
6. Use **Test hotkey lighting** to flash the selected hotkey three times. Its previous colour is restored afterwards.
7. Save settings.

> **Screenshot placeholder — Keyboard lighting settings**
>
> Add one screenshot of the Keyboard lighting section with a selected keyboard, colour, and effect. Do not include device serial numbers or unrelated desktop content.

AutoClicker recognises keyboards reported as keyboards and also attempts sensible matches for common vendor/model names such as Corsair devices. It remembers a selected keyboard by name and gracefully uses a single detected keyboard if the exact device index changes.

## Automatic OpenRGB startup

**Start OpenRGB automatically when needed** starts a normal local OpenRGB process with its SDK server when no OpenRGB process is already running. It never launches a second instance.

If **Stop OpenRGB if AutoClicker started it** is enabled, AutoClicker stops only the OpenRGB process it launched itself when closing. It never stops a process that was already running.

When AutoClicker starts OpenRGB, the main status label reports that briefly.

## Colours and effects

Use the colour control to open the native Windows colour picker. After confirming a colour, the configuration dialog can immediately flash the chosen hotkey as a safe preview. The picker preview always restores the original LED state.

Effects:

- **Constant** — key stays lit while AutoClicker is active.
- **Blink** — key alternates on/off. Adjust the state duration.
- **Pulse** — smooth fade in/out. It uses 12 blend steps per cycle and is capped at 12 LED updates per second.

## Compatibility and limits

OpenRGB support depends on the keyboard, its firmware, and its OpenRGB device mapping. AutoClicker tries common aliases for modifiers, media keys, numpad keys, and punctuation, but no application can guarantee per-key control for every keyboard.

If OpenRGB says it cannot map the key, choose a standard keyboard key, refresh keyboards, and test again. If it says no RGB keyboard was reported, rescan devices in OpenRGB and try running OpenRGB as administrator. An SDK-server error means OpenRGB is not running with the server enabled.
