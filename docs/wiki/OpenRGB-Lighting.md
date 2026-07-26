# OpenRGB lighting

AutoClicker can light the configured hotkey on a compatible RGB keyboard while it is active. It connects to OpenRGB on your PC, so vendor software is not required.

## Set it up

1. Install and start [OpenRGB](https://openrgb.org/).
2. Enable its SDK server.
3. In **Settings → Keyboard lighting**, enable **Light the hotkey with OpenRGB while AutoClicker is active**.
4. Click **Refresh keyboards** and choose your keyboard.
5. Pick a colour and effect, then use **Test hotkey lighting**.
6. Save settings.

> **Screenshot placeholder — Keyboard lighting settings**
>
> Add one screenshot of the Keyboard lighting section with a selected keyboard, colour, and effect. Do not include device serial numbers or unrelated desktop content.

## Options

**Start OpenRGB automatically when needed** can start OpenRGB with its SDK server if it is not already running. **Stop OpenRGB if AutoClicker started it** only closes the copy launched by AutoClicker.

Choose a **Constant**, **Blink**, or **Pulse** effect. The test flashes the selected key briefly and then restores its previous colour.

## If it does not work

OpenRGB support varies by keyboard. Refresh keyboards, test a standard key such as F6, and make sure the SDK server is enabled. See [Troubleshooting](Troubleshooting) for more help.
