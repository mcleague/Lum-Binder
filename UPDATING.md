# Publishing updates

Lum Binder can check GitHub Releases for free; it does not need a separate website or VPS. The repository must be public so the app can query and download releases without an access token.

1. Create a public GitHub repository and upload the project, including `.github/workflows/release.yml`.
2. Push a version tag such as `v1.0.1`. GitHub Actions builds the self-contained Windows app and attaches `Lum-Binder-win-x64.zip` to a release.
3. Download and install the first published `Lum Binder.exe` manually. On startup it checks the latest release, downloads a newer package, applies it after closing the running process, and restarts the app.

The workflow embeds its own GitHub repository name into each release build. Local builds use a placeholder until the repository is created and the workflow publishes the first release.

The app needs write access to its installation directory to apply updates. Install it in a user-writable folder rather than `Program Files`.

## Workshop catalog

The in-app Workshop reads `workshop/catalog.json` from the `main` branch and downloads JSON packages from `workshop/packs/`. Use catalog entries with type `profile` for full profiles (`tabs`) or `binds` for bind collections (`binds`). Keep package files small; the app rejects workshop JSON larger than 1 MiB. Profiles install as separate profiles and bind collections append to the selected tab.
