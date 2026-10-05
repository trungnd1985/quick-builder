<#
.SYNOPSIS
    QuickBuilder in one command.

.DESCRIPTION
    ./qb.ps1                  Build and start QuickBuilder, then open the admin.
    ./qb.ps1 stop             Stop it. Your data is kept.
    ./qb.ps1 code             Show the one-time setup code again.
    ./qb.ps1 logs             Follow the application's log.
    ./qb.ps1 dev              Work on the code: backend and admin with hot reload.
    ./qb.ps1 cloud [-Azure]   Two instances sharing S3 (MinIO) or Azure (Azurite) storage.
    ./qb.ps1 test [-Quick]    Run the tests (-Quick: unit, architecture and admin only).
    ./qb.ps1 reset            Delete everything, database and files included, and start over.

    Needs Docker. `dev` and `test` also need the .NET 10 SDK and Node 24.
    On macOS and Linux run it with PowerShell 7: `pwsh ./qb.ps1`.
#>
#Requires -Version 7
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'stop', 'code', 'logs', 'dev', 'cloud', 'test', 'reset')]
    [string] $Command = 'start',

    # cloud: Azure Blob (Azurite) instead of S3 (MinIO).
    [switch] $Azure,

    # test: skip the integration tests, which take several minutes.
    [switch] $Quick,

    # reset: do not ask before deleting.
    [switch] $Force,

    # start, cloud: do not open the browser.
    [switch] $NoBrowser
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$Port = if ($env:QUICKBUILDER_PORT) { $env:QUICKBUILDER_PORT } else { '8080' }
$Admin = "http://127.0.0.1:$Port/admin"
$DevDatabase = 'Host=localhost;Port=5432;Database=quickbuilder;Username=quickbuilder;Password=change-me-before-real-use'

function Say([string] $Text) { Write-Host "› $Text" -ForegroundColor Cyan }

function Require([string] $Tool, [string] $Hint) {
    if (-not (Get-Command $Tool -ErrorAction SilentlyContinue)) {
        throw "$Tool was not found. $Hint"
    }
}

function Compose {
    docker compose @args
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($args -join ' ') failed." }
}

function Wait-Ready([string] $Url, [int] $Seconds = 180) {
    Say "Waiting for QuickBuilder to be ready…"
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            if ((Invoke-WebRequest "$Url/health/ready" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Seconds 2
    }
    throw "QuickBuilder did not become ready within $Seconds seconds. See: ./qb.ps1 logs"
}

function Show-SetupCode {
    # Prints the code while the installation is unclaimed, and says so once it is set up.
    # The image's database driver notes once that Kerberos is not installed; it is not used.
    docker compose exec -T app dotnet QuickBuilder.Host.dll identity setup-code 2>&1 |
        Where-Object { $_ -notmatch 'libgssapi_krb5' }
}

function Open-Browser([string] $Url) {
    if ($NoBrowser) { return }
    try {
        if ($IsWindows -or $env:OS -eq 'Windows_NT') { Start-Process $Url }
        elseif ($IsMacOS) { & open $Url }
        else { & xdg-open $Url 2>$null }
    } catch { }
}

function Show-Tips {
    Write-Host ""
    Write-Host "  Admin:  $Admin" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Tip: give each site a host name ending in .localhost, such as shop.localhost."
    Write-Host "  Browsers send *.localhost to this computer, so http://shop.localhost:$Port"
    Write-Host "  shows the site's published pages with no other set-up."
    Write-Host ""
}

switch ($Command) {
    'start' {
        Require docker 'Install Docker Desktop: https://www.docker.com/products/docker-desktop/'
        Say "Building and starting QuickBuilder (the first build takes a few minutes)…"
        # --remove-orphans: leaving `cloud` mode stops its MinIO, Azurite and second instance.
        Compose up -d --build --remove-orphans
        Wait-Ready "http://127.0.0.1:$Port"
        Write-Host ""
        Show-SetupCode
        Show-Tips
        Open-Browser $Admin
    }

    'stop' {
        Compose -f docker-compose.yml -f docker-compose.storage.yml down --remove-orphans
        Say "Stopped. Your sites, pages and files are kept; ./qb.ps1 starts it again."
    }

    'code' { Show-SetupCode }

    'logs' { docker compose logs -f app }

    'reset' {
        if (-not $Force) {
            $answer = Read-Host "This deletes every site, page, user and uploaded file. Type 'delete' to continue"
            if ($answer -ne 'delete') { Say "Nothing was deleted."; return }
        }
        Compose -f docker-compose.yml -f docker-compose.storage.yml down -v --remove-orphans
        Say "Everything was deleted. ./qb.ps1 starts a fresh installation."
    }

    'cloud' {
        Require docker 'Install Docker Desktop: https://www.docker.com/products/docker-desktop/'
        $env:QB_STORAGE = if ($Azure) { 'AzureBlob' } else { 'S3' }
        Say "Starting two instances on $($env:QB_STORAGE) storage…"
        Compose build app
        Compose -f docker-compose.yml -f docker-compose.storage.yml up -d
        Wait-Ready "http://127.0.0.1:$Port"
        Write-Host ""
        Show-SetupCode
        Show-Tips
        Write-Host "  Second instance:  http://127.0.0.1:8081/admin (same sites and files)"
        if (-not $Azure) { Write-Host "  MinIO console:    http://127.0.0.1:9001 (minioadmin / minioadmin)" }
        Write-Host "  Check storage:    docker compose exec app dotnet QuickBuilder.Host.dll media check-storage"
        Write-Host ""
        Open-Browser $Admin
    }

    'dev' {
        Require docker 'Install Docker Desktop: https://www.docker.com/products/docker-desktop/'
        Require dotnet 'Install the .NET 10 SDK: https://dotnet.microsoft.com/download'
        Require npm 'Install Node.js 24: https://nodejs.org/'

        # The admin dev server proxies to port 8080, so the app container must not hold it.
        Say "Starting the database…"
        docker compose stop app 2>$null | Out-Null
        Compose -f docker-compose.yml -f docker-compose.dev.yml up -d db

        if (-not (Test-Path src/admin/node_modules)) {
            Say "Installing the admin app's packages…"
            Push-Location src/admin; npm ci; Pop-Location
        }

        Say "Starting the backend on http://127.0.0.1:8080 …"
        $env:ConnectionStrings__Default = $DevDatabase
        $backend = Start-Process dotnet -PassThru -NoNewWindow -ArgumentList @(
            'run', '--project', 'src/QuickBuilder.Host', '--', '--urls', 'http://127.0.0.1:8080')

        try {
            Wait-Ready 'http://127.0.0.1:8080' 300
            Write-Host ""
            Write-Host "  Admin with hot reload:  http://localhost:5173/admin/" -ForegroundColor Green
            Write-Host "  Setup code, on a fresh database: look for SETUP CODE in the backend output above."
            Write-Host "  Press Ctrl+C to stop both."
            Write-Host ""
            Push-Location src/admin
            try { npm run dev } finally { Pop-Location }
        }
        finally {
            if ($backend -and -not $backend.HasExited) {
                Say "Stopping the backend…"
                $backend.Kill($true)
            }
        }
    }

    'test' {
        Require dotnet 'Install the .NET 10 SDK: https://dotnet.microsoft.com/download'
        Require npm 'Install Node.js 24: https://nodejs.org/'

        if ($Quick) {
            dotnet test tests/QuickBuilder.UnitTests
            if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
            dotnet test tests/QuickBuilder.ArchitectureTests
            if ($LASTEXITCODE -ne 0) { throw 'Architecture tests failed.' }
        } else {
            Say "Running every .NET test (needs Docker; about ten minutes)…"
            dotnet test QuickBuilder.slnx --filter "FullyQualifiedName!~SmokeTests"
            if ($LASTEXITCODE -ne 0) { throw '.NET tests failed.' }
        }

        Push-Location src/admin
        try {
            if (-not (Test-Path node_modules)) { npm ci }
            npm run lint; if ($LASTEXITCODE -ne 0) { throw 'Admin lint failed.' }
            npm run type-check; if ($LASTEXITCODE -ne 0) { throw 'Admin type check failed.' }
            npm test; if ($LASTEXITCODE -ne 0) { throw 'Admin tests failed.' }
        } finally { Pop-Location }

        Say "All tests passed."
    }
}
