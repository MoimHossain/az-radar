# CloudLens Teams notification app

The app is a notification-only team-scoped bot. A tenant administrator uploads the generated package
to the organization's Teams app catalog, approves it, and installs it in each central platform
channel that should receive Service Health notifications.

Create the upload package after deploying the Azure Bot:

```powershell
.\package.ps1 -BotClientId '<bot-uami-client-id>'
```

The bot registers an installed channel as disabled in CloudLens. A CloudLens administrator must select
one or more Service Health event families and enable the destination before messages are dispatched.
After installing the app in a team, mention the bot from each target channel and send `register`.
This captures the channel-specific conversation reference; installation alone is only team-scoped.
