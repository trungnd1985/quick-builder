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
    ./qb.ps1 seed-items -Collection <address name> [-Count 300] [-Publish 0] [-Site name]
                              Add generated items to a collection through the item API
                              (development only), publishing the given percentage of them.
    ./qb.ps1 seed-template -Collection <address name> -Parent <path> [-Site name]
                              Make a published template of a collection under a page, with a
                              heading and an image bound to the item's title and first image,
                              so every published item has its own page (development only).
    ./qb.ps1 seed-redirects [-Count 1000] [-Site name]
                              Import generated redirects through the redirect import, in files
                              of 5,000, for trying the redirect list and matching at scale
                              (a site holds at most 10,000).

    Needs Docker. `dev` and `test` also need the .NET 10 SDK and Node 24.
    On macOS and Linux run it with PowerShell 7: `pwsh ./qb.ps1`.
#>
#Requires -Version 7
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'stop', 'code', 'logs', 'dev', 'cloud', 'test', 'reset', 'seed', 'seed-items', 'seed-template', 'seed-redirects')]
    [string] $Command = 'start',

    # cloud: Azure Blob (Azurite) instead of S3 (LocalStack).
    [switch] $Azure,

    # test: skip the integration tests, which take several minutes.
    [switch] $Quick,

    # reset: do not ask before deleting.
    [switch] $Force,

    # start, cloud: do not open the browser.
    [switch] $NoBrowser,

    # seed: how many images to upload; seed-items: how many items to add (default 300);
    # seed-redirects: how many redirects to import (default 1000).
    [ValidateRange(1, 10000)]
    [int] $Count = 1000,

    # seed, seed-items, seed-template, seed-redirects: the site's name; asked for when there is more than one.
    [string] $Site,

    # seed-items, seed-template: the collection's address name.
    [string] $Collection,

    # seed-template: the address of the page the template goes under, such as /blog.
    [string] $Parent,

    # seed-items: the percentage of the new items to publish (default 0).
    [ValidateRange(0, 100)]
    [int] $Publish = 0
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

# The seed commands: sign in as an administrator through the API. QB_EMAIL and QB_PASSWORD skip
# the questions, for scripted runs.
function Connect-QuickBuilder([string] $Api) {
    try { $null = Invoke-WebRequest "http://127.0.0.1:$Port/health/ready" -UseBasicParsing -TimeoutSec 3 }
    catch { throw "QuickBuilder is not running on port $Port. Start it with ./qb.ps1 first." }

    if (-not ($env:QB_EMAIL -and $env:QB_PASSWORD)) { Say "Sign in as an administrator." }
    $email = if ($env:QB_EMAIL) { $env:QB_EMAIL } else { Read-Host 'Email' }
    $password = if ($env:QB_PASSWORD) { $env:QB_PASSWORD } else { Read-Host 'Password' -MaskInput }
    try {
        $null = Invoke-RestMethod "$Api/identity/sign-in" -Method Post -Headers @{ 'X-QuickBuilder-Request' = '1' } -SessionVariable session `
            -ContentType 'application/json' -Body (@{ email = $email; password = $password } | ConvertTo-Json)
    } catch { throw 'Sign-in failed. Check the email and password.' }
    $session
}

# The site to seed: the one named by -Site, the only one, or the one chosen.
function Select-Site([string] $Api, $Session) {
    $sites = @(Invoke-RestMethod "$Api/sites" -WebSession $Session)
    if ($sites.Count -eq 0) { throw 'There are no sites yet. Create one in the admin first.' }
    $target = if ($Site) { $sites | Where-Object { $_.name -eq $Site } | Select-Object -First 1 }
              elseif ($sites.Count -eq 1) { $sites[0] }
              else {
                  for ($i = 0; $i -lt $sites.Count; $i++) { Write-Host "  [$($i + 1)] $($sites[$i].name)" }
                  $sites[[int](Read-Host 'Which site') - 1]
              }
    if (-not $target) { throw "No site is named '$Site'." }
    $target
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
        $session = Connect-QuickBuilder $api
        $target = Select-Site $api $session
        $headers = @{ 'X-QuickBuilder-Request' = '1' }
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

    'seed-items' {
        # 015 tasks T084: items are created through the item API, like the item form does, with a
        # value of the right type for each field, and the given share published. A development
        # tool: it is never part of the product.
        if (-not $Collection) { throw 'Name the collection by its address name: ./qb.ps1 seed-items -Collection blog-posts' }
        if (-not $PSBoundParameters.ContainsKey('Count')) { $Count = 300 }

        $api = "http://127.0.0.1:$Port/api/v1"
        $session = Connect-QuickBuilder $api
        $target = Select-Site $api $session

        $found = (Invoke-RestMethod "$api/cms/sites/$($target.id)/collections" -WebSession $session).collections |
            Where-Object { $_.slug -eq $Collection } | Select-Object -First 1
        if (-not $found) { throw "Site '$($target.name)' has no collection with the address name '$Collection'." }

        # Every field gets a value of its type, and about 30 % of optional fields stay empty so the
        # list and previews have something to count. Image, file and reference fields stay empty:
        # there is nothing yet to point them at.
        $collection = Invoke-RestMethod "$api/cms/sites/$($target.id)/collections/$($found.id)" -WebSession $session
        $items = "$api/cms/sites/$($target.id)/collections/$($found.id)/items"
        $headers = @{ 'X-QuickBuilder-Request' = '1' }
        $random = [Random]::new(15)
        $stamp = Get-Date -Format 'HHmmss'
        $published = 0
        $failed = 0

        Say "Adding $Count items to '$($found.name)' on '$($target.name)', publishing about $Publish %…"
        for ($n = 1; $n -le $Count; $n++) {
            $values = [ordered]@{}
            foreach ($field in $collection.schema.fields) {
                if (-not $field.required -and $random.NextDouble() -lt 0.3) { continue }
                $value = switch ($field.type) {
                    'text' { "$($field.name) $n".Substring(0, [Math]::Min("$($field.name) $n".Length, $field.maxLength)) }
                    'richText' { @{ type = 'doc'; content = @(@{ type = 'paragraph'; content = @(@{ type = 'text'; text = "Seeded text $n" }) }) } }
                    'number' { [Math]::Max([double]($field.min ?? 0), [Math]::Min([double]($field.max ?? 1000), $n % 100)) }
                    'boolean' { $n % 2 -eq 0 }
                    'date' { if ($field.withTime) { (Get-Date '2026-01-01').AddHours($n).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") } else { (Get-Date '2026-01-01').AddDays($n % 365).ToString('yyyy-MM-dd') } }
                    'option' { if ($field.options.Count -gt 0) { $field.options[$n % $field.options.Count].id } }
                    default { $null }
                }
                if ($null -ne $value) { $values[$field.id] = $value }
            }

            $body = @{ slug = "seed-$stamp-$n"; schemaRevision = $collection.schemaRevision; values = @{ schemaVersion = 1; values = $values } } | ConvertTo-Json -Depth 10
            try {
                $item = Invoke-RestMethod $items -Method Post -WebSession $session -Headers $headers -ContentType 'application/json' -Body $body
                if ($random.Next(100) -lt $Publish) {
                    $publishBody = @{ revision = $item.revision; cancelSchedule = $false } | ConvertTo-Json
                    $null = Invoke-RestMethod "$items/$($item.id)/publish" -Method Post -WebSession $session -Headers $headers -ContentType 'application/json' -Body $publishBody
                    $published++
                }
            } catch { $failed++ }

            if ($n % 50 -eq 0) { Write-Progress -Activity 'Adding items' -Status "$n of $Count" -PercentComplete (100 * $n / $Count) }
        }
        Write-Progress -Activity 'Adding items' -Completed

        Say "Done: $($Count - $failed) added, $published published, $failed refused (a required field this script cannot fill, such as an image)."
    }

    'seed-redirects' {
        # 017 tasks T110: redirects arrive through the CSV import, as a member's file would, in
        # files of at most 5,000 rows. Exact and prefix rows alternate with permanent and
        # temporary ones. A development tool: it is never part of the product.
        if (-not $PSBoundParameters.ContainsKey('Count')) { $Count = 1000 }
        if ($Count -gt 10000) { throw 'A site holds at most 10,000 redirects.' }

        $api = "http://127.0.0.1:$Port/api/v1"
        $session = Connect-QuickBuilder $api
        $target = Select-Site $api $session
        $headers = @{ 'X-QuickBuilder-Request' = '1' }
        $stamp = Get-Date -Format 'HHmmss'
        $imported = 0
        $refused = 0

        Say "Importing $Count redirects into '$($target.name)'…"
        for ($start = 1; $start -le $Count; $start += 5000) {
            $end = [Math]::Min($Count, $start + 4999)
            $rows = [System.Text.StringBuilder]::new("source,target,kind,match,note`n")
            for ($n = $start; $n -le $end; $n++) {
                $kind = if ($n % 5 -eq 0) { 302 } else { 301 }
                if ($n % 10 -eq 0) {
                    $null = $rows.Append("/seed-$stamp/section-$n,/new-$stamp/section-$n,$kind,prefix,seeded`n")
                } else {
                    $null = $rows.Append("/seed-$stamp/page-$n.html,/new-$stamp/page-$n,$kind,exact,`n")
                }
            }

            $result = Invoke-RestMethod "$api/pages/sites/$($target.id)/redirects/import" -Method Post -WebSession $session -Headers $headers `
                -ContentType 'text/csv' -Body ([System.Text.Encoding]::UTF8.GetBytes($rows.ToString()))
            $imported += $result.added
            $refused += @($result.refused).Count
        }

        Say "Done: $imported imported, $refused refused. Open the site's Redirects page, or visit /seed-$stamp/page-1.html."
    }

    'seed-template' {
        # 016 tasks T095: a published template of a collection under a page, made through the
        # API as the admin does it, so every published item has its own page to look at. A
        # development tool: it is never part of the product.
        if (-not $Collection) { throw 'Name the collection by its address name: ./qb.ps1 seed-template -Collection blog-posts -Parent /blog' }
        if (-not $Parent -or -not $Parent.StartsWith('/') -or $Parent -eq '/') { throw 'Name the page it goes under by its address, such as -Parent /blog (not the home page).' }

        $api = "http://127.0.0.1:$Port/api/v1"
        $session = Connect-QuickBuilder $api
        $target = Select-Site $api $session
        $headers = @{ 'X-QuickBuilder-Request' = '1' }
        $pagesApi = "$api/pages/sites/$($target.id)/pages"

        $found = (Invoke-RestMethod "$api/cms/sites/$($target.id)/collections" -WebSession $session).collections |
            Where-Object { $_.slug -eq $Collection } | Select-Object -First 1
        if (-not $found) { throw "Site '$($target.name)' has no collection with the address name '$Collection'." }
        $schema = (Invoke-RestMethod "$api/cms/sites/$($target.id)/collections/$($found.id)" -WebSession $session).schema

        $parentPage = (Invoke-RestMethod $pagesApi -WebSession $session).pages | Where-Object { $_.path -eq $Parent.TrimEnd('/') } | Select-Object -First 1
        if (-not $parentPage) { throw "Site '$($target.name)' has no page at '$Parent'. Create it first." }

        # The heading reads the title field (or the first text field); the image the first image field.
        $title = if ($schema.titleFieldId) { $schema.titleFieldId } else { ($schema.fields | Where-Object { $_.type -eq 'text' } | Select-Object -First 1).id }
        $image = ($schema.fields | Where-Object { $_.type -eq 'image' } | Select-Object -First 1).id
        $blocks = @(@{ id = 'title'; type = 'heading'; props = @{ text = $found.name; level = 1 }; children = @() })
        if ($title) { $blocks[0].props.bind = @{ text = @{ value = @{ field = $title } } } }
        if ($image) {
            $blocks += @{ id = 'cover'; type = 'image'; props = @{ alt = ''; decorative = $true; bind = @{ media = @{ value = @{ field = $image }; empty = 'hide' } } }; children = @() }
        }

        $page = Invoke-RestMethod $pagesApi -Method Post -WebSession $session -Headers $headers -ContentType 'application/json' `
            -Body (@{ title = "$($found.name) page"; slug = "$($found.slug)-page"; parentId = $parentPage.id } | ConvertTo-Json)
        $made = Invoke-RestMethod "$pagesApi/$($page.id)/template" -Method Put -WebSession $session -Headers $headers -ContentType 'application/json' `
            -Body (@{ collectionId = $found.id; version = $page.version } | ConvertTo-Json)

        $document = @{
            schemaVersion = 1
            bindingVersion = 1
            root = @{ id = 'root'; type = 'root'; props = @{}; children = @(@{ id = 'section'; type = 'section'; props = @{}; children = $blocks }) }
        }
        $draft = Invoke-RestMethod "$pagesApi/$($page.id)/draft" -WebSession $session
        $saved = Invoke-RestMethod "$pagesApi/$($page.id)/draft" -Method Put -WebSession $session -Headers $headers -ContentType 'application/json' `
            -Body (@{ version = $draft.version; document = $document } | ConvertTo-Json -Depth 20)
        $null = Invoke-RestMethod "$pagesApi/$($page.id)/publish" -Method Post -WebSession $session -Headers $headers -ContentType 'application/json' `
            -Body (@{ draftVersion = $saved.version } | ConvertTo-Json)

        Say "Published a template of '$($found.name)' at $($made.page.path)."
        Write-Host "  Every published item is now at that address with its slug in place of {slug}."
        Write-Host "  Publish some with: ./qb.ps1 seed-items -Collection $Collection -Publish 50"
    }
}
