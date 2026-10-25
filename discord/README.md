# Discord workshop integration

The app reads the approved catalogue from this public GitHub repository. This Discord interaction worker queues uploaded JSON as a GitHub issue; only the configured Discord administrator can approve and publish a submission. The app's **Обновить каталог** button fetches the current `main` catalogue.

The worker runs on Cloudflare Workers free tier; no VPS or Discord gateway process is needed. GitHub and Discord credentials stay in Cloudflare/local environment secrets and are never embedded in the app or checked into source.

Submissions are stored in GitHub Issues in this public repository while pending, so their titles, descriptions, submitter IDs, and attached JSON should be treated as public.

## Create the Discord application

1. Create an application in the Discord Developer Portal and add a bot. Enable `applications.commands` and install the app in the desired server.
2. Copy the Application ID and Public Key from General Information. Copy the bot token only for command registration; do not commit it or send it in chat.
3. Set the interaction endpoint after deploying the Worker: `https://<worker-name>.<account>.workers.dev/discord`.
4. Give the Cloudflare Worker secrets `DISCORD_PUBLIC_KEY`, `ADMIN_DISCORD_USER_ID`, and `GITHUB_TOKEN`. `ADMIN_DISCORD_USER_ID` is the numeric Discord user ID authorized to approve submissions.
5. Create a fine-grained GitHub token for `mcleague/Lum-Binder` with repository contents read/write and issues read/write permissions, then add it as the Worker secret `GITHUB_TOKEN`.

## Deploy the Worker

From `discord/worker`, run:

```powershell
npx wrangler@latest login
npx wrangler@latest secret put DISCORD_PUBLIC_KEY
npx wrangler@latest secret put ADMIN_DISCORD_USER_ID
npx wrangler@latest secret put GITHUB_TOKEN
npx wrangler@latest deploy
```

Set each secret value when prompted. The repository name is configured in `wrangler.toml`; it is not secret.

## Publish binds from the app with an HWID

The in-app **Админка** can publish the selected tab's binds directly to the catalogue. The Worker checks the SHA-256 hash of the Windows `MachineGuid`; the GitHub token remains on the Worker and is never included in the desktop app.

1. Build the private admin version with the deployed Worker URL, for example from the repository root:

   ```powershell
   dotnet publish BinderApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:UpdateRepository=mcleague/Lum-Binder -p:WorkshopApiUrl=https://lum-binder-workshop.<account>.workers.dev -p:WorkshopAdminBuild=true -o D:\RPbinder
   ```

   Replace `<account>` with the Cloudflare Workers subdomain for your account. Keep this admin build private. GitHub release builds set `WorkshopAdminBuild=false` and hide the admin panel; private admin builds do not auto-update from public releases.
2. From `discord/worker`, set the hash as a Worker secret and deploy:

   ```powershell
   npx wrangler@latest secret put ADMIN_HWID_SHA256
   npx wrangler@latest deploy
   ```

3. In the GitHub repository settings, add the Actions variable `WORKSHOP_API_URL` with the Worker base URL (for example, `https://lum-binder-workshop.<account>.workers.dev`). Release builds embed this public endpoint. Local builds can override it with `-p:WorkshopApiUrl=<worker-url>`.
4. Reopen **Админка** and check access. Enter a name and description, select the tab to publish, and press **Опубликовать бинды выбранной вкладки**. The package is added directly to `workshop/catalog.json`.
5. To add a photo post, enter its title and description, select a JPG or PNG image up to 3 MB, and press **Опубликовать фото-запись**. New photo files are stored under `workshop/images/` and displayed as read-only cards in the workshop. Publishing only appends a new catalogue entry; the app has no edit or delete operation for published entries.

The HWID is a hash of `MachineGuid`, not a hardware-backed credential; it can be spoofed. Use this HWID-only gate only for a low-risk public workshop. `/approve` remains restricted to `ADMIN_DISCORD_USER_ID`.

## Register slash commands

Set `DISCORD_APPLICATION_ID` and `DISCORD_BOT_TOKEN` in the current PowerShell session, then run:

```powershell
.\register-commands.ps1
```

## Moderate and publish a package

1. A member runs `/submit`, fills in the title, description and type, and attaches a `.json` file. A profile JSON contains a `tabs` array; a bind collection contains a `binds` array.
2. The worker validates the JSON size and shape and opens a GitHub issue with the pending package.
3. Review the issue, then run `/approve issue:<number>` in Discord. Only the configured administrator can run this command.
4. The worker publishes the package and updated `workshop/catalog.json` together in one Git commit. Users press **Обновить каталог** in Lum Binder to load it.

Packages are limited to 40 KB at submission, 20 tabs per profile, 100 binds per tab/package, 25 steps per bind, and 1,000 characters per step. Packages are JSON data only.
