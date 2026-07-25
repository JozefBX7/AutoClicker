# Updates and releases

## Checking for updates

AutoClicker never checks for updates automatically. Open **Settings → Updates** and choose **Check GitHub Releases** whenever you want to check.

If a newer version is published:

- An **installed** copy offers **Download & install**. After confirmation, it downloads the expected installer only from the project’s official GitHub Release URL, launches the normal setup program, then closes AutoClicker cleanly.
- A **portable** copy opens the matching ZIP in the browser. Extract it over the portable folder after closing AutoClicker, keeping the `Data` folder to preserve settings.

If the project has no published release yet, the app says so clearly. The update check uses public GitHub Releases; it does not require an access token.

## Publishing a release (maintainers)

Push a version tag. The included GitHub Actions workflow runs tests, builds both packages, and creates the release assets:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

Version tags must look like `v1.0.1` (or `1.0.1`) so the app can compare them. The workflow produces the exact filenames AutoClicker expects:

- `AutoClicker-Setup-x64.exe`
- `AutoClicker-Portable-x64.zip`

Enable GitHub Actions for the repository if they are disabled under **Settings → Actions → General**.

## Trust and signing

The installer is a normal Inno Setup installer, not a custom bootstrapper. Newly released unsigned Windows executables can still receive SmartScreen warnings until they establish reputation.

For the strongest trust signal, sign released binaries with a Windows code-signing certificate. Keep certificates and private keys out of the repository; configure signing through the release workflow’s secrets instead.
