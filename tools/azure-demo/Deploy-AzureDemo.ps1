#Requires -Version 7.4
<#
.SYNOPSIS
    Creates the free FitEdu public demo on Azure and deploys the app to it.

.DESCRIPTION
    Uses only offers that cost nothing:
      - App Service plan F1 (Free) on Linux, .NET 10;
      - Azure SQL Database free offer (serverless) that pauses until next month when the
        monthly free limit runs out, instead of billing.
    The web app reaches the database with its managed identity, so no database password exists.
    The Jwt key is generated here; the admin password and the demo member password are typed in.
    No secret is printed.

    First run: creates everything, sets the settings and deploys.
    Later runs with -DeployOnly: publishes and deploys the current code only.

.EXAMPLE
    ./tools/azure-demo/Deploy-AzureDemo.ps1 -AppName fitedu-demo-gs
.EXAMPLE
    ./tools/azure-demo/Deploy-AzureDemo.ps1 -AppName fitedu-demo-gs -DeployOnly
#>
[CmdletBinding()]
param(
    # Becomes https://<AppName>.azurewebsites.net, so it must be unique across Azure.
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{1,40}[a-z0-9]$')]
    [string] $AppName,

    [string] $Location = 'northeurope',

    [string] $ResourceGroup = 'fitedu-demo-rg',

    [switch] $DeployOnly
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$planName = "$AppName-plan"
$sqlServerName = "$AppName-sql"
$databaseName = 'fitedu'
$siteUrl = "https://$AppName.azurewebsites.net"

# Serverless limits for the free database: a small maximum and the shortest pause delay keep
# each visit's use of the 100,000 free vCore-seconds a month low.
$databaseMaxVCores = 1
$databaseMinVCores = 0.5
$autoPauseDelayMinutes = 15

function Assert-AzureCliSignedIn {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw 'Azure CLI is not installed. Run: winget install Microsoft.AzureCLI'
    }
    $account = az account show --query '{name:name, user:user.name}' -o json 2>$null | ConvertFrom-Json
    if (-not $account) {
        throw 'Not signed in to Azure. Run: az login'
    }
    Write-Host "Subscription: $($account.name) ($($account.user))"
}

function Read-Password([string] $prompt) {
    $secure = Read-Host -Prompt $prompt -AsSecureString
    $plain = [System.Net.NetworkCredential]::new('', $secure).Password
    # The app's Identity rules: 8+ characters with a digit, an upper-case and a lower-case letter.
    if ($plain.Length -lt 8 -or $plain -notmatch '\d' -or $plain -cnotmatch '[A-Z]' -or $plain -cnotmatch '[a-z]') {
        throw 'The password needs 8+ characters with a digit, an upper-case and a lower-case letter.'
    }
    return $plain
}

function New-JwtKey {
    $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48)
    return [Convert]::ToBase64String($bytes)
}

function New-Infrastructure {
    Write-Host 'Creating the resource group, the free plan and the web app…'
    az group create --name $ResourceGroup --location $Location --output none
    az appservice plan create --resource-group $ResourceGroup --name $planName --is-linux --sku F1 --output none
    az webapp create --resource-group $ResourceGroup --plan $planName --name $AppName `
        --runtime 'DOTNETCORE:10.0' --assign-identity '[system]' --https-only true --output none
    # Blazor Server talks to the browser over WebSockets.
    az webapp config set --resource-group $ResourceGroup --name $AppName `
        --web-sockets-enabled true --startup-file 'dotnet FitEdu.Web.dll' --output none

    # The database admin is the web app itself (its managed identity), so no password exists.
    $principalId = az webapp identity show --resource-group $ResourceGroup --name $AppName --query principalId -o tsv
    $identityClientId = az ad sp show --id $principalId --query appId -o tsv

    Write-Host 'Creating the database server and the free database…'
    az sql server create --resource-group $ResourceGroup --name $sqlServerName --location $Location `
        --enable-ad-only-auth --external-admin-principal-type Application `
        --external-admin-name $AppName --external-admin-sid $identityClientId --output none
    # 0.0.0.0 is Azure's "allow Azure services" rule; the server has no other way in.
    az sql server firewall-rule create --resource-group $ResourceGroup --server $sqlServerName `
        --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --output none
    az sql db create --resource-group $ResourceGroup --server $sqlServerName --name $databaseName `
        --edition GeneralPurpose --family Gen5 --compute-model Serverless `
        --capacity $databaseMaxVCores --min-capacity $databaseMinVCores --auto-pause-delay $autoPauseDelayMinutes `
        --use-free-limit true --free-limit-exhaustion-behavior AutoPause `
        --backup-storage-redundancy Local --output none
}

function Set-AppSettings([string] $adminPassword, [string] $demoMemberPassword) {
    $connectionString = "Server=tcp:$sqlServerName.database.windows.net,1433;Database=$databaseName;" +
        'Authentication=Active Directory Managed Identity;Encrypt=True;'
    $settings = [ordered]@{
        ASPNETCORE_ENVIRONMENT                 = 'Production'
        # TLS ends at Azure's front end; this lets the app see the original https scheme.
        ASPNETCORE_FORWARDEDHEADERS_ENABLED    = 'true'
        AllowedHosts                           = "$AppName.azurewebsites.net"
        SelfBaseUrl                            = $siteUrl
        Sitemap__BaseUrl                       = $siteUrl
        ConnectionStrings__DefaultConnection   = $connectionString
        Jwt__Key                               = New-JwtKey
        AdminUser__Password                    = $adminPassword
        SeedData__Enabled                      = 'true'
        SeedData__AllowOutsideDevelopment      = 'true'
        SeedData__StudentPassword              = $demoMemberPassword
        # No Ollama on Azure: the coach button stays hidden.
        Assistant__Enabled                     = 'false'
        # No mail account: emails fail to send instead of reaching anyone.
        Email__Smtp__Host                      = 'smtp.invalid'
    }

    # A file keeps passwords off the command line and away from cmd.exe quoting rules.
    $settingsFile = New-TemporaryFile
    try {
        # The same shape as "az webapp config appsettings list" prints.
        $settingsList = $settings.GetEnumerator() |
            ForEach-Object { [ordered]@{ name = $_.Key; value = $_.Value; slotSetting = $false } }
        ConvertTo-Json @($settingsList) | Set-Content -Path $settingsFile -Encoding utf8NoBOM
        az webapp config appsettings set --resource-group $ResourceGroup --name $AppName `
            --settings "@$settingsFile" --output none
    }
    finally {
        Remove-Item $settingsFile -Force
    }
}

function Publish-App {
    $publishDir = Join-Path ([System.IO.Path]::GetTempPath()) "fitedu-azure-publish"
    $zipPath = "$publishDir.zip"
    Remove-Item $publishDir, $zipPath -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host 'Publishing the app…'
    dotnet publish (Join-Path $repoRoot 'src\FitEdu.Web\FitEdu.Web.csproj') --configuration Release `
        --output $publishDir --nologo --verbosity quiet

    # The portable build carries native files for every OS; Linux needs none of the Windows or macOS ones.
    Get-ChildItem (Join-Path $publishDir 'runtimes') -Directory |
        Where-Object { $_.Name -like 'win*' -or $_.Name -like 'osx*' } |
        Remove-Item -Recurse -Force

    # .NET's zip writer uses forward slashes, which Linux needs.
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipPath)
    Remove-Item $publishDir -Recurse -Force
    return $zipPath
}

function Deploy-App {
    $zipPath = Publish-App
    try {
        Write-Host 'Uploading (the first start runs the migrations and seeds the demo content)…'
        az webapp deploy --resource-group $ResourceGroup --name $AppName --src-path $zipPath --type zip --output none
    }
    finally {
        Remove-Item $zipPath -Force
    }
}

Assert-AzureCliSignedIn

if (-not $DeployOnly) {
    $adminPassword = Read-Password 'Password for admin@fitedu.local (kept private)'
    $demoMemberPassword = Read-Password 'Password for the demo members, e.g. alice.johnson@fitedu.local (shown publicly)'
    New-Infrastructure
    Set-AppSettings -adminPassword $adminPassword -demoMemberPassword $demoMemberPassword
}

Deploy-App
Write-Host "Done: $siteUrl"
