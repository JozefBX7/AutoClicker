# AutoClicker

A compact Windows auto clicker and keyboard spammer, with custom sequences, global hotkeys, backups, and optional OpenRGB lighting.

Detailed user documentation is available in the [Wiki source](docs/wiki/Home.md) and is mirrored to the repository Wiki when it is enabled.

<img width="522" height="558" alt="image" src="https://github.com/user-attachments/assets/25f152cf-0899-41e2-9f9a-0997649fc8aa" />

## Download

Get both options from the [latest release](../../releases/latest).

- **Installer (recommended):** `AutoClicker-Setup-x64.exe` installs AutoClicker normally, adds an Apps & Features uninstaller, and optionally creates a desktop shortcut.
- **Portable:** `AutoClicker-Portable-x64.zip` contains a self-contained `AutoClicker.exe`; unzip it anywhere and run it without installing.

The installer stores user settings in `%LocalAppData%\AutoClicker`. The portable ZIP contains a `portable.flag`, so its settings, sequence library, appearance, RGB configuration, and crash history live in a sibling `Data` folder. Keep that folder with the portable executable to retain everything when moving or updating it.

Uninstalling deliberately preserves your user settings and backups. Delete `%LocalAppData%\AutoClicker` (installed) or the portable `Data` folder only if you want to remove them.

## Updates

In **Settings → Updates**, users can manually check GitHub Releases; AutoClicker never checks in the background. For an installed copy, a newer version can be explicitly downloaded and passed to the normal installer after confirmation. Portable copies open the matching ZIP in the browser so users can extract it over the portable folder while preserving `Data`.

The in-app update check needs the GitHub release to be public. A private repository cannot be queried anonymously without shipping a secret token, which would be unsafe. For private releases, the **Open Releases** button still works for users signed into GitHub with repository access. To share updates with friends without access, make the releases public or use another public download page such as Google Drive.

Use AutoClicker only where automated input is permitted. Press the configured global hotkey (F6 by default) at any time to stop.

## Publish a release

The repository includes a GitHub Actions release workflow. Push a version tag and it runs tests, builds both packages, and attaches them to a GitHub Release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

If GitHub Actions are disabled for the repository, enable them under **Settings → Actions → General**. The download link above will then automatically point to the newest release.

For a local release build, install [Inno Setup](https://jrsoftware.org/isinfo.php) 6.6 or later, then run:

```powershell
.\scripts\Build-Release.ps1 -Version 1.0.0
```

It creates `dist\AutoClicker-Setup-x64.exe` and `dist\AutoClicker-Portable-x64.zip`.

## Trust and code signing

The installer uses Inno Setup's standard modern wizard and built-in uninstaller. It does not use a custom downloader or bootstrapper. However, a new unsigned executable may still trigger Microsoft SmartScreen until it earns reputation. For the most trustworthy public release, obtain a Windows code-signing certificate (EV gives the strongest SmartScreen experience) and configure the Inno Setup `SignTool` directive plus the GitHub workflow with your certificate secret. Do not upload a certificate or private key to the repository.

## Development

```powershell
dotnet run --project C:\repos\AutoClicker\AutoClicker.csproj
dotnet test C:\repos\AutoClicker\AutoClicker.Tests\AutoClicker.Tests.csproj
```
