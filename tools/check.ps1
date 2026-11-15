#Requires -Version 7.4
<#
.SYNOPSIS
    Runs every FitEdu check locally: restore, warning-free build, code style, tests.

.DESCRIPTION
    Checks run on this PC only (no GitHub Actions). Steps, stopping at the first failure:
      1. dotnet restore --locked-mode  (a silent package-version drift fails loudly)
      2. dotnet build -c Release -warnaserror  (the build is warning-free)
      3. dotnet format style --verify-no-changes --severity warn  (.editorconfig style gate)
      4. dotnet test -c Release  (all test projects)
      5. With -Migrations: applies the whole migration chain to a throwaway LocalDB database
         and drops it afterwards. The test suite runs on InMemory/SQLite, so this catches a
         migration that is valid there but broken on SQL Server.

    The tests need a few settings that Program.cs requires at startup. Harmless dummy values
    are set only when they are not already set, and the previous values are put back at the
    end, so a pwsh window that ran the script keeps its own settings.

    SDK pin: $PinnedSdkVersion below is the SDK the committed lock files were refreshed with.
    A different SDK resolves another SDK-supplied Microsoft.AspNetCore.App.Internal.Assets
    version in src/FitEdu.Web/packages.lock.json and fails the locked-mode restore. To move
    to a new SDK: dotnet restore FitEdu.sln --force-evaluate, then commit the refreshed lock
    file and the new $PinnedSdkVersion together, never one alone.

.EXAMPLE
    ./tools/check.ps1
.EXAMPLE
    ./tools/check.ps1 -Migrations
#>
[CmdletBinding()]
param(
    # Also apply the migration chain to a throwaway LocalDB database.
    [switch] $Migrations
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = 'FitEdu.sln'
$localDbServer = '(localdb)\MSSQLLocalDB'
# The SDK the committed packages.lock.json files match (it bundles ASP.NET Core 10.0.15).
$PinnedSdkVersion = '10.0.404'
# Dummy, non-secret values so the integration host (WebApplicationFactory<Program>) can boot.
$dummySettings = [ordered]@{
    'Jwt__Key'            = 'local-test-signing-key-not-a-secret-min-32-bytes!!'
    'Jwt__Issuer'         = 'FitEdu.Tests'
    'Jwt__Audience'       = 'FitEdu.Tests'
    'AdminUser__Password' = 'Local-Test-Admin-1!'
}

# Runs one dotnet command and stops the script when it exits with a non-zero code.
function Invoke-Dotnet {
    param([string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

# Sets an environment variable for this process only, unless it already has a value.
function Initialize-DummyEnvironmentVariable {
    param([string] $Name, [string] $Value)

    if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($Name))) {
        [Environment]::SetEnvironmentVariable($Name, $Value)
    }
}

function Test-PinnedSdk {
    Write-Host '== SDK =='
    # dotnet fails here when the SDK named in global.json is not installed.
    $sdkVersion = & dotnet --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        $globalJson = Get-Content -Raw (Join-Path $repositoryRoot 'global.json')
        throw "The SDK required by global.json is not installed. Install it, then run again.`n$globalJson`n$sdkVersion"
    }
    Write-Host "dotnet $sdkVersion"
    if ("$sdkVersion" -eq $PinnedSdkVersion) {
        return
    }
    $refreshAdvice = "refresh the lock files with SDK $sdkVersion (dotnet restore FitEdu.sln --force-evaluate) and set `$PinnedSdkVersion in tools/check.ps1 in the same commit."
    $pinnedIsInstalled = @(& dotnet --list-sdks | Where-Object { $_ -like "$PinnedSdkVersion *" }).Count -gt 0
    if ($pinnedIsInstalled) {
        throw "SDK $PinnedSdkVersion is installed, but global.json rolls forward to SDK $sdkVersion. To move to it, $refreshAdvice"
    }
    throw "dotnet resolves SDK $sdkVersion, but the lock files match SDK $PinnedSdkVersion. Install SDK $PinnedSdkVersion, or $refreshAdvice"
}

function Invoke-MigrationCheck {
    $timestamp = Get-Date -Format 'yyyyMMddHHmmss'
    $databaseName = "FitEduMigrationCheck_$timestamp"
    $previousConnection = $env:ConnectionStrings__DefaultConnection
    # The design-time DbContext factory reads this variable, so dotnet ef targets the throwaway database.
    $env:ConnectionStrings__DefaultConnection = "Server=$localDbServer;Database=$databaseName;Trusted_Connection=True;TrustServerCertificate=True"
    $efArguments = @('--project', 'src/FitEdu.Infrastructure', '--startup-project', 'src/FitEdu.Web')

    try {
        Write-Host "== Migrations: tool restore =="
        Invoke-Dotnet @('tool', 'restore')

        Write-Host "== Migrations: apply the whole chain to $databaseName =="
        Invoke-Dotnet (@('ef', 'database', 'update') + $efArguments)
    }
    finally {
        Write-Host "== Migrations: drop $databaseName =="
        & dotnet ef database drop --force @efArguments
        if ($LASTEXITCODE -ne 0) {
            # A warning, not a throw: an error from the update above must stay the one reported.
            Write-Warning "Could not drop $databaseName on $localDbServer; drop it by hand (sqllocaldb or SSMS)."
        }
        $env:ConnectionStrings__DefaultConnection = $previousConnection
    }
}

$previousSettings = @{}
foreach ($name in $dummySettings.Keys) {
    $previousSettings[$name] = [Environment]::GetEnvironmentVariable($name)
}

Push-Location $repositoryRoot
try {
    Test-PinnedSdk

    foreach ($name in $dummySettings.Keys) {
        Initialize-DummyEnvironmentVariable $name $dummySettings[$name]
    }

    Write-Host '== Restore (locked mode) =='
    Invoke-Dotnet @('restore', $solution, '--locked-mode')

    Write-Host '== Build (Release, warnings as errors) =='
    Invoke-Dotnet @('build', $solution, '-c', 'Release', '--no-restore', '-warnaserror')

    Write-Host '== Code style (dotnet format style) =='
    Invoke-Dotnet @('format', 'style', $solution, '--verify-no-changes', '--severity', 'warn', '--no-restore')

    Write-Host '== Tests (Release) =='
    Invoke-Dotnet @('test', $solution, '-c', 'Release', '--no-build')

    if ($Migrations) {
        Invoke-MigrationCheck
    }

    Write-Host 'All checks passed.'
}
finally {
    foreach ($name in $previousSettings.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousSettings[$name])
    }
    Pop-Location
}
