# Publishing updates

Lum Binder can check GitHub Releases for free; it does not need a separate website or VPS. The repository must be public so the app can query and download releases without an access token.

1. Create a public GitHub repository and upload the project, including `.github/workflows/release.yml`.
2. Push a version tag such as `v1.0.1`. GitHub Actions builds the self-contained Windows app and attaches `Lum-Binder-win-x64.zip` to a release.
3. Download and install the first published `Lum Binder.exe` manually. On startup it checks the latest release, downloads a newer package, applies it after closing the running process, and restarts the app.

The workflow embeds its own GitHub repository name into each release build. Local builds use a placeholder until the repository is created and the workflow publishes the first release.

The app needs write access to its installation directory to apply updates. Install it in a user-writable folder rather than `Program Files`.

Release builds also embed the public `WORKSHOP_API_URL` GitHub Actions variable. Set it to the deployed Cloudflare Worker base URL to enable the workshop integration. GitHub releases set `WorkshopAdminBuild=false`, so public builds do not show the admin panel. For a private local admin build, set `WorkshopAdminBuild=true` and provide `WorkshopApiUrl` as MSBuild properties. Private admin builds do not auto-update from public releases.

The private admin panel can publish photo posts with JPG or PNG images up to 3 MB and manage all catalogue entries. Editing changes only the entry name and description. Removing an entry hides it from the catalogue but retains its package/image files and Git history.

## Workshop catalog

The in-app Workshop reads `workshop/catalog.json` from the `main` branch and downloads JSON packages from `workshop/packs/`. Use catalog entries with type `profile` for full profiles (`tabs`) or `binds` for bind collections (`binds`). Keep package files small; the app rejects workshop JSON larger than 1 MiB. Profiles install as separate profiles and bind collections append to the selected tab.

The **Админка** publishes the selected tab through the Cloudflare Worker. Configure its `ADMIN_HWID_SHA256` secret with the SHA-256 hash shown in the app. This hash is based on Windows `MachineGuid` and is spoofable; it is not strong authentication.
