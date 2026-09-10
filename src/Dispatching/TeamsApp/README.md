# CloudLens Teams notification app

The app is a team-scoped notification bot that also accepts channel registration mentions.
A tenant administrator uploads the generated package to the organization's Teams app catalog,
approves it, and installs it in each central platform team that should receive Service Health notifications.

Create the upload package after deploying the Azure Bot:

```powershell
.\package.ps1 -BotClientId '<bot-uami-client-id>'
```

Run the script with PowerShell 7 on Windows (icon generation uses System.Drawing).
The package is written to `artifacts\CloudLens-Teams-App.zip`. Its only entries are
`manifest.json`, `color.png`, and `outline.png`.

The source artwork is `cloud-computing.png`, copied from the repository's
`assets\images\cloud-computing.png`. Keeping a copy here makes dispatch packaging self-contained.
The script produces a 192x192 color icon with the original blue/cyan artwork centered within
a 120x120 safe area on white, and a 32x32 white-on-transparent version of the same artwork.

## Install or update in Teams

1. Open [Teams admin center](https://admin.teams.microsoft.com) and select **Teams apps > Manage apps**.
2. For a new installation, select **Upload new app** (under **Actions** where applicable) and upload the ZIP.
3. If CloudLens Alerts already exists, open its app details and select **Upload file** to upload the updated ZIP instead. Keep the same app/bot ID and increment the manifest version for each update.
4. Allow the app and make it available to the intended users through your tenant's app access controls.
5. In Teams, find **CloudLens Alerts** under your organization's apps and add it to the target team.
6. In each target standard channel, select the actual bot mention and send `@CloudLens Alerts register`.

Version 1.0.1 replaces the placeholder icons. An icon-only update does not require an Azure
deployment or re-registering existing destinations. Teams may cache the old icon temporarily;
restart the Teams client after the updated app becomes available if necessary.

The bot registers an installed channel as disabled in CloudLens. A CloudLens administrator must select
one or more Service Health event families and enable the destination before messages are dispatched.
After installing the app in a team, mention the bot from each target channel and send `register`.
This captures the channel-specific conversation reference; installation alone is only team-scoped.
