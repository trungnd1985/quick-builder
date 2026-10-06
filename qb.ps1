<#
.SYNOPSIS
    QuickBuilder in one command.

.DESCRIPTION
    ./qb.ps1                  Build and start QuickBuilder, then open the admin.
    ./qb.ps1 stop             Stop it. Your data is kept.
    ./qb.ps1 code             Show the one-time setup code again.
    ./qb.ps1 logs             Follow the application's log.
    ./qb.ps1 dev              Work on the code: backend and admin with hot reload.
    ./qb.ps1 cloud [-Azure]   Two instances sharing S3 (LocalStack) or Azure (Azurite) storage.
    ./qb.ps1 test [-Quick]    Run the tests (-Quick: unit, architecture and admin only).
    ./qb.ps1 reset            Delete everything, database and files included, and start over.
    ./qb.ps1 seed [-Count 1000] [-Site name]
                              Fill a site's media library with generated images, for trying
                              search, folders and the picker at scale.

    Needs Docker. `dev` and `test` also need the .NET 10 SDK and Node 24.
    On macOS and Linux run it with PowerShell 7: `pwsh ./qb.ps1`.
#>
#Requires -Version 7
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'stop', 'code', 'logs', 'dev', 'cloud', 'test', 'reset', 'seed')]
    [string] $Command = 'start',

    # cloud: Azure Blob (Azurite) instead of S3 (LocalStack).
    [switch] $Azure,

    # test: skip the integration tests, which take several minutes.
    [switch] $Quick,

    # reset: do not ask before deleting.
    [switch] $Force,

    # start, cloud: do not open the browser.
    [switch] $NoBrowser,

    # seed: how many images to upload.
    [ValidateRange(1, 10000)]
    [int] $Count = 1000,

    # seed: the site's name; asked for when there is more than one.
    [string] $Site
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

$script:Crc = $null

function Get-Crc32([byte[]] $Bytes) {
    if (-not $script:Crc) {
        $script:Crc = [uint32[]]::new(256)
        for ($n = 0; $n -lt 256; $n++) {
            $c = [uint32]$n
            for ($k = 0; $k -lt 8; $k++) { $c = if ($c -band 1) { 0xEDB88320 -bxor ($c -shr 1) } else { $c -shr 1 } }
            $script:Crc[$n] = $c
        }
    }
    $crc = [uint32]::MaxValue
    foreach ($b in $Bytes) { $crc = $script:Crc[($crc -bxor $b) -band 0xFF] -bxor ($crc -shr 8) }
    return $crc -bxor [uint32]::MaxValue
}

function New-Png([int] $Width, [int] $Height, [byte] $Red, [byte] $Green, [byte] $Blue) {
    # A real PNG of one colour, made without an image library: rows of RGB pixels, each row
    # starting with filter byte 0, compressed with zlib.
    $row = [byte[]]::new(1 + $Width * 3)
    for ($x = 0; $x -lt $Width; $x++) { $row[1 + $x * 3] = $Red; $row[2 + $x * 3] = $Green; $row[3 + $x * 3] = $Blue }
    $raw = [System.IO.MemoryStream]::new()
    $zlib = [System.IO.Compression.ZLibStream]::new($raw, [System.IO.Compression.CompressionLevel]::Optimal)
    for ($y = 0; $y -lt $Height; $y++) { $zlib.Write($row, 0, $row.Length) }
    $zlib.Dispose()

    $png = [System.IO.MemoryStream]::new()
    $png.Write([byte[]](137, 80, 78, 71, 13, 10, 26, 10), 0, 8)
    $chunk = {
        param([string] $Type, [byte[]] $Data)
        $length = [BitConverter]::GetBytes([uint32]$Data.Length); [Array]::Reverse($length)
        $typed = [Text.Encoding]::ASCII.GetBytes($Type) + $Data
        $crc = [BitConverter]::GetBytes((Get-Crc32 $typed)); [Array]::Reverse($crc)
        $png.Write($length, 0, 4); $png.Write($typed, 0, $typed.Length); $png.Write($crc, 0, 4)
    }
    $w = [BitConverter]::GetBytes([uint32]$Width); [Array]::Reverse($w)
    $h = [BitConverter]::GetBytes([uint32]$Height); [Array]::Reverse($h)
    & $chunk 'IHDR' ($w + $h + [byte[]](8, 2, 0, 0, 0))
    & $chunk 'IDAT' $raw.ToArray()
    & $chunk 'IEND' ([byte[]]@())
    # The comma keeps the bytes one array: a function otherwise returns them one by one.
    return , $png.ToArray()
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
        # --remove-orphans: leaving `cloud` mode stops its emulator and second instance.
        Compose up -d --build --remove-orphans
        Wait-Ready "http://127.0.0.1:$Port"
        Write-Host ""
        Show-SetupCode
        Show-Tips
        Open-Browser $Admin
    }

    'stop' {
        Compose -f docker-compose.yml -f docker-compose.storage.yml --profile s3 --profile azure down --remove-orphans
        Say "Stopped. Your sites, pages and files are kept; ./qb.ps1 starts it again."
    }

    'code' { Show-SetupCode }

    'logs' { docker compose logs -f app }

    'reset' {
        if (-not $Force) {
            $answer = Read-Host "This deletes every site, page, user and uploaded file. Type 'delete' to continue"
            if ($answer -ne 'delete') { Say "Nothing was deleted."; return }
        }
        Compose -f docker-compose.yml -f docker-compose.storage.yml --profile s3 --profile azure down -v --remove-orphans
        Say "Everything was deleted. ./qb.ps1 starts a fresh installation."
    }

    'cloud' {
        Require docker 'Install Docker Desktop: https://www.docker.com/products/docker-desktop/'
        $env:QB_STORAGE = if ($Azure) { 'AzureBlob' } else { 'S3' }
        Say "Starting two instances on $($env:QB_STORAGE) storage…"
        Compose build app
        $storageProfile = if ($Azure) { 'azure' } else { 's3' }
        Compose -f docker-compose.yml -f docker-compose.storage.yml --profile $storageProfile up -d --remove-orphans
        Wait-Ready "http://127.0.0.1:$Port"
        Write-Host ""
        Show-SetupCode
        Show-Tips
        Write-Host "  Second instance:  http://127.0.0.1:8081/admin (same sites and files)"
        if (-not $Azure) { Write-Host "  S3 (LocalStack):  http://127.0.0.1:4566, bucket qb-media" }
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

    'seed' {
        $api = "http://127.0.0.1:$Port/api/v1"
        try { $null = Invoke-WebRequest "http://127.0.0.1:$Port/health/ready" -UseBasicParsing -TimeoutSec 3 }
        catch { throw "QuickBuilder is not running on port $Port. Start it with ./qb.ps1 first." }

        # QB_EMAIL and QB_PASSWORD skip the questions, for scripted runs.
        if (-not ($env:QB_EMAIL -and $env:QB_PASSWORD)) { Say "Sign in as an administrator." }
        $email = if ($env:QB_EMAIL) { $env:QB_EMAIL } else { Read-Host 'Email' }
        $password = if ($env:QB_PASSWORD) { $env:QB_PASSWORD } else { Read-Host 'Password' -MaskInput }
        $headers = @{ 'X-QuickBuilder-Request' = '1' }
        try {
            $null = Invoke-RestMethod "$api/identity/sign-in" -Method Post -Headers $headers -SessionVariable session `
                -ContentType 'application/json' -Body (@{ email = $email; password = $password } | ConvertTo-Json)
        } catch { throw 'Sign-in failed. Check the email and password.' }

        $sites = @(Invoke-RestMethod "$api/sites" -WebSession $session)
        if ($sites.Count -eq 0) { throw 'There are no sites yet. Create one in the admin first.' }
        $target = if ($Site) { $sites | Where-Object { $_.name -eq $Site } | Select-Object -First 1 }
                  elseif ($sites.Count -eq 1) { $sites[0] }
                  else {
                      for ($i = 0; $i -lt $sites.Count; $i++) { Write-Host "  [$($i + 1)] $($sites[$i].name)" }
                      $sites[[int](Read-Host 'Which site') - 1]
                  }
        if (-not $target) { throw "No site is named '$Site'." }
        $files = "$api/media/sites/$($target.id)/files"

        # A handful of folders, so folder browsing and searching inside a folder have work to do.
        # Seeding again reuses them.
        $folderApi = "$api/media/sites/$($target.id)/folders"
        $existing = @((Invoke-RestMethod $folderApi -WebSession $session).folders)
        $folders = foreach ($name in 'Events', 'Products', 'Team', 'Blog', 'Archive' | ForEach-Object { "$_ (seed)" }) {
            $found = $existing | Where-Object { $_.name -eq $name -and -not $_.parentId } | Select-Object -First 1
            if ($found) { $found }
            else {
                Invoke-RestMethod $folderApi -Method Post -WebSession $session -Headers $headers `
                    -ContentType 'application/json' -Body (@{ name = $name; parentId = $null } | ConvertTo-Json)
            }
        }
        $folders = @($folders)

        # Varied, searchable names; a few with accents, to try the accent-insensitive search.
        $subjects = 'beach', 'museum', 'team', 'office', 'product', 'map', 'header', 'night', 'café', 'été',
                    'mountain', 'city', 'garden', 'kitchen', 'concert', 'logo', 'banner', 'portrait', 'river', 'market'
        $moods = 'sunny', 'blue', 'quiet', 'busy', 'early', 'late', 'winter', 'summer', 'golden', 'misty'
        $palette = @((29, 78, 216), (4, 120, 87), (180, 83, 9), (190, 18, 60), (109, 40, 217),
                     (15, 118, 110), (51, 65, 85), (161, 98, 7), (2, 132, 199), (162, 28, 175))
        [byte[][]] $images = foreach ($c in $palette) { , [byte[]](New-Png 400 300 $c[0] $c[1] $c[2]) }

        Say "Uploading $Count images to '$($target.name)'…"
        $failed = 0
        $watch = [Diagnostics.Stopwatch]::StartNew()
        for ($i = 1; $i -le $Count; $i++) {
            $name = '{0}-{1}-{2:D4}.png' -f $moods[$i % $moods.Count], $subjects[($i * 7) % $subjects.Count], $i
            $url = $files
            if ($folders.Count -gt 0 -and $i % 3 -ne 0) { $url += "?folderId=$($folders[$i % $folders.Count].id)" }
            try {
                $file = Invoke-RestMethod $url -Method Post -WebSession $session -ContentType 'image/png' `
                    -Headers @{ 'X-QuickBuilder-Request' = '1'; 'X-QuickBuilder-File-Name' = [Uri]::EscapeDataString($name) } `
                    -Body $images[$i % $images.Count]
                if ($i % 3 -eq 0) {
                    $alt = '{0} {1} photo' -f $moods[$i % $moods.Count], $subjects[($i * 7) % $subjects.Count]
                    $null = Invoke-RestMethod "$files/$($file.id)" -Method Patch -WebSession $session -Headers $headers `
                        -ContentType 'application/json' -Body (@{ altText = $alt } | ConvertTo-Json)
                }
            } catch {
                $failed++
                if ($failed -eq 1) { Write-Warning "First failed upload ($name): $($_.ErrorDetails.Message ?? $_.Exception.Message)" }
            }
            if ($i % 25 -eq 0 -or $i -eq $Count) {
                Write-Progress -Activity 'Uploading images' -Status "$i of $Count" -PercentComplete ($i * 100 / $Count)
            }
        }
        Write-Progress -Activity 'Uploading images' -Completed

        Say ("Done in {0:N0} s: {1} uploaded, {2} failed, in {3} folders and the top level." -f `
            $watch.Elapsed.TotalSeconds, ($Count - $failed), $failed, $folders.Count)
        Write-Host "  Try searching for 'ete' or 'cafe' (accents are ignored), or open the image picker in the editor."
    }
}
