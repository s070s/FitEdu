# Deployment notes

Three facts about this app only exist as source comments today (MAINT-4). Anyone deploying
from the README alone would discover each one at runtime — the first as *every page silently
rendering empty*, the worst possible debugging entry point. This page hoists them out.

Required secrets are validated together at startup (`Startup/StartupConfigValidation.cs`,
MAINT-5): missing/invalid values are reported in one exception listing everything, not one
restart at a time. See `.env.template` for the full local-dev list.

## Required secrets

| Key | Notes |
|---|---|
| `Jwt:Key` | Must be ≥ 32 bytes (HMAC-SHA256). Env var: `Jwt__Key`. |
| `AdminUser:Password` | Seeds the admin account on first boot. Env var: `AdminUser__Password`. |
| `ConnectionStrings:DefaultConnection` | SQL Server connection string. |
| `Email:Smtp:Host` (+ credentials) | Required outside Development — see [Email transport](#email-transport) below. |
| `SeedData:StudentPassword` | Only required when sample content is seeded (see [Public demo](#public-demo)). |

## SelfBaseUrl and the loopback API call

Blazor Server renders pages by calling the app's **own** REST API over an in-process
`HttpClient` (`Startup/ServiceRegistrationExtensions.cs`, `ResolveSelfBaseUrl`) — a deliberate
architectural tradeoff (ARCH-1), not a bug. Three things follow:

1. **`SelfBaseUrl` must be set to the real bound base address outside Development.** The app
   fails fast at startup if it's unset or still points at `localhost` in a non-Development
   environment.
2. **The loopback HTTPS certificate must be trusted by that same in-process `HttpClient`.**
   This is *not* validated at startup — if the cert isn't trusted, every API-backed page
   renders silently empty (`ApiClientBase` swallows non-success responses) instead of
   erroring visibly.
3. **The app's own calls must be recognised by the rate limiters.** Outside Development the
   loopback call goes to `SelfBaseUrl`, the public address, so it reaches the app from an outside
   address. Its `HttpClient`s therefore send the header `X-FitEdu-Self-Call` with a secret, to the
   app's own address only, and the `"public"` and `"auth"` limiters skip a request that carries the
   matching secret (`Startup/SelfCallMarker.cs`). A random secret is made at every start; set
   `SelfCall:Secret` (≥ 32 characters, the same on every instance) only when several instances
   call each other through a load balancer. Without the marker every visitor's page load would
   share one limit and a busy site would answer its own pages with 429. The secret is never logged.

The limiters still never limit a loopback caller, so a reverse proxy on the same host **must send
`X-Forwarded-For`**; without it every visitor looks like loopback and the rate limits do nothing.
nginx: `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;`. Caddy sends it by default.
IIS ARR: Server Proxy Settings, "Preserve client IP in the following header: X-Forwarded-For".
Outside Development the app logs one warning per process when a loopback request arrives with
neither the self-call secret nor `X-Forwarded-For` (`Startup/LoopbackProxyWarning.cs`).

## DataProtection key ring

Keys are persisted to the shared SQL database (`PersistKeysToDbContext<ApplicationDbContext>`),
not the local filesystem — this is what makes auth cookies and the `/auth/complete` handshake
ticket valid across a redeploy or container rebuild onto a fresh disk, and across more than one
instance reading the same database.

The keys sign the auth cookies and encrypt the assessment answers (`AnswersJson`, `SummaryJson`),
so how they are protected at rest matters. Three cases, chosen at startup
(`Startup/DataProtectionKeyProtection.cs`):

- **With a certificate (recommended, any OS).** Set `DataProtection:CertificatePath` (a `.pfx`
  file) and `DataProtection:CertificatePassword`, or `DataProtection:CertificateThumbprint` (a
  certificate with a private key in the CurrentUser or LocalMachine `My` store). The keys are then
  encrypted with that certificate, and every instance or host that has the certificate can read
  the shared key ring. Startup fails when the file is missing, the password is wrong, the
  certificate has no private key or the thumbprint is not found, so a typo never silently drops
  the protection. Back the certificate up offline, apart from the database backup: without it a
  restored database has keys nobody can read.
- **Windows without a certificate.** DPAPI (`ProtectKeysWithDpapi()`), as before. **Only this
  Windows account on this machine can read the keys**: a database restored onto another host or
  account loses every sign-in session and every encrypted assessment answer.
- **Linux without a certificate.** The keys are stored in the database unencrypted: anyone who can
  read the database can forge auth cookies and decrypt the assessment answers.

Outside Development, a start without a certificate logs one warning naming the risk of its case.
Startup is not refused, so the demo deployment (below) keeps working without one. To add a
certificate to an existing deployment, set the settings and restart: the old keys stay in the ring:
the current key keeps protecting new data until Data Protection replaces it (about 2 days before
its 90-day lifetime ends); only keys made after that are protected by the certificate. Anything
encrypted with an older key (sign-in cookies until then, the assessment answers written before,
permanently) stays readable only by this Windows account on this machine (on Linux, by anyone who
can read the database); nothing is re-encrypted.

## Response compression

The app does not compress its HTML or JSON responses on purpose: the pages carry antiforgery
tokens and reflect request input, which makes compressed HTTPS responses open to BREACH-style
attacks. The fingerprinted static assets (CSS, JavaScript, fonts) are already served precompressed.
If you want compressed pages anyway, enable it at the reverse proxy (for example nginx `gzip on;`
for `text/html`) and accept that trade-off there.

## Email transport

`SmtpEmailService` (MailKit) is used whenever `Email:Smtp:Host` is configured — point it at
Papercut/Mailhog in Development or a real relay (SES/SendGrid SMTP/etc.) in production.
Development falls back to `StubEmailService` (logs only) when no SMTP host is configured; any
other environment **fails fast at startup** so emails are never silently swallowed in
production. Full SMTP options: `Email:Smtp:{Host,Port,UseStartTls,Username,Password,FromAddress,FromName,TimeoutSeconds}`.

Emails are queued in memory and sent in the background by `EmailDispatcher`, so a slow relay never
holds up a sign-up or reset request. One SMTP command may take `Email:Smtp:TimeoutSeconds` (default
15, at most 300). Port 465 connects with TLS from the first byte; elsewhere `UseStartTls` (default
true) upgrades the connection. Outside Development, startup refuses a `Username` on a connection
without TLS. A message still queued when the app stops is lost; the member asks for a new link.

`Email:Smtp:FromAddress` must be on a domain whose DNS passes SPF and DKIM for the relay you use,
or mail providers file the confirmation and reset emails as spam. If emails do not arrive, see
[Email not arriving](runbook.md#email-not-arriving) in the runbook.

## Public demo

A demo deployment needs the sample programmes, exercises and members, which normally seed only in
Development. Set both `SeedData:Enabled=true` and `SeedData:AllowOutsideDevelopment=true` (plus
`SeedData:StudentPassword`, the password of the sample members); the published app reads the JSON
from its `SeedJson/` folder. Never set the second key on a real deployment. Without Ollama, set
`Assistant:Enabled=false`: the coach button then disappears.

[`tools/azure-demo/`](../tools/azure-demo/README.md) sets all of this up on Azure's free offers
(App Service F1 on Linux and the Azure SQL Database free offer, which pauses instead of billing)
and lists the demo's limits. On Linux the key ring is stored in the database without a certificate (the start logs the
warning described above); only the web app's managed identity can sign in to that database.
