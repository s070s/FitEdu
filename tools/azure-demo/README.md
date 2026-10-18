# Azure demo

`Deploy-AzureDemo.ps1` puts a public FitEdu demo on Azure using only free offers, and redeploys it
after code changes.

| Part | Offer | Why it costs nothing |
|---|---|---|
| Web app | App Service plan **F1 (Free)**, Linux, .NET 10 | The F1 tier is free; it has no paid add-ons. |
| Database | Azure SQL Database **free offer**, serverless | 100,000 vCore-seconds and 32 GB a month; set to **pause until next month** when used up, never to bill. |
| Database sign-in | The web app's managed identity | No password to store; the server accepts Microsoft Entra sign-ins only. |

## Before the first run

1. An Azure subscription. A new free account asks for a card for identity checks only. After its
   30 days, upgrade it to pay-as-you-go or Azure disables it with the demo; the two offers above
   stay free after the upgrade. Optionally add a budget alert of 1 in **Cost Management**.
2. Azure CLI (`winget install Microsoft.AzureCLI`), then `az login`.
3. PowerShell 7.4 or newer, and the .NET 10 SDK.

## Run

```powershell
./tools/azure-demo/Deploy-AzureDemo.ps1 -AppName <unique-name>             # first time
./tools/azure-demo/Deploy-AzureDemo.ps1 -AppName <unique-name> -DeployOnly # code updates
```

The first run asks for two passwords: the admin's (`admin@fitedu.local`, keep it private) and the
demo members' (for example `alice.johnson@fitedu.local`, meant to be shared with visitors). It
generates the Jwt key itself and prints no secret. `-Location` (default `northeurope`) and
`-ResourceGroup` (default `fitedu-demo-rg`) are optional; if a region refuses the free plan or a new
SQL server, delete the resource group and rerun with another region.

## What visitors get

The demo runs in Production with the sample programmes, the exercise library and 10 sample
members (see [docs/DEPLOYMENT.md](../../docs/DEPLOYMENT.md#public-demo)). Its limits:

- the AI coach is off (it needs a local Ollama);
- emails are not sent, so new sign-ups cannot confirm their address: use a demo member;
- payments are switched off outside Development;
- after 20 idle minutes the free tier stops the app and the database pauses 15 minutes later,
  so the first visit after a quiet spell takes up to a minute;
- F1 allows 60 CPU minutes a day and 5 WebSocket connections at a time;
- a visitor signed in as a demo member can change that member's password.

## Remove it

```powershell
az group delete --name fitedu-demo-rg
```
