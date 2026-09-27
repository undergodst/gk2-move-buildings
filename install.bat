<# : batch part - runs the PowerShell below; PowerShell skips everything up to the closing marker
@echo off
setlocal
title GK2 Move Buildings - installer
set "GK2_SELF=%~f0"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$script = Get-Content -LiteralPath '%~f0' -Raw -Encoding UTF8; Invoke-Expression $script"
echo.
pause
exit /b
#>

# GK2 Move Buildings — one-click setup.
# Installs BepInEx 5.4.23.5 and GK2 Workshop Loader into Graveyard Keeper 2, then opens the mod's
# Workshop page so you can subscribe. Safe to run again: it skips whatever is already installed.
# Source: https://github.com/undergodst/gk2-move-buildings

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$AppId          = '4358690'
$ModItemId      = '3809174815'
$LoaderItemId   = '3807346541'
$BepInExUrl     = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'
$BepInExSha256  = '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4'
$LoaderApi      = 'https://api.github.com/repos/Zoriten/-GK2-WorkshopLoader/releases/latest'
$DryRun         = $env:GK2_DRYRUN -eq '1'

$ru = (Get-UICulture).Name -like 'ru*'
function T($en, $rus) { if ($ru) { $rus } else { $en } }
function Say($en, $rus) { Write-Host (T $en $rus) }
function Ok($en, $rus) { Write-Host ('  [OK] ' + (T $en $rus)) -ForegroundColor Green }
function Warn($en, $rus) { Write-Host ('  [!]  ' + (T $en $rus)) -ForegroundColor Yellow }
function Fail($en, $rus) { Write-Host ('  [X]  ' + (T $en $rus)) -ForegroundColor Red; throw 'stop' }

function Get-SteamLibraries {
    $steam = $null
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        $props = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        if ($props) {
            $candidate = if ($props.SteamPath) { $props.SteamPath } else { $props.InstallPath }
            if ($candidate -and (Test-Path $candidate)) { $steam = $candidate; break }
        }
    }
    $libraries = New-Object System.Collections.Generic.List[string]
    if ($steam) {
        $libraries.Add((Resolve-Path $steam).Path)
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $path = $m.Groups[1].Value -replace '\\\\', '\'
                if ((Test-Path $path) -and -not $libraries.Contains($path)) { $libraries.Add($path) }
            }
        }
    }
    return $libraries
}

function Find-Game {
    # Run from inside the game folder? Use that.
    $here = Split-Path -Parent $env:GK2_SELF
    if ($here -and (Test-Path (Join-Path $here 'GraveyardKeeper2.exe'))) { return @{ Game = $here; Library = $null } }
    foreach ($lib in Get-SteamLibraries) {
        $game = Join-Path $lib 'steamapps\common\Graveyard Keeper 2'
        if (Test-Path (Join-Path $game 'GraveyardKeeper2.exe')) { return @{ Game = $game; Library = $lib } }
    }
    return $null
}

try {
    Write-Host ''
    Write-Host '=== GK2 Move Buildings ===' -ForegroundColor Cyan
    if ($DryRun) { Warn 'Dry run: nothing will be changed.' 'Пробный запуск: ничего не будет изменено.' }
    Write-Host ''

    # 1. Game folder
    $found = Find-Game
    if (-not $found) {
        Warn 'Could not find Graveyard Keeper 2 automatically.' 'Не удалось найти Graveyard Keeper 2 автоматически.'
        $manual = Read-Host (T 'Paste the game folder (the one with GraveyardKeeper2.exe)' 'Вставьте путь к папке игры (где лежит GraveyardKeeper2.exe)')
        $manual = $manual.Trim('" ')
        if (-not (Test-Path (Join-Path $manual 'GraveyardKeeper2.exe'))) { Fail 'GraveyardKeeper2.exe is not in that folder.' 'В этой папке нет GraveyardKeeper2.exe.' }
        $found = @{ Game = $manual; Library = (Split-Path (Split-Path (Split-Path $manual))) }
    }
    $game = $found.Game
    Ok "Game: $game" "Игра: $game"

    if (Get-Process -Name 'GraveyardKeeper2' -ErrorAction SilentlyContinue) {
        Fail 'The game is running. Close it and run this file again.' 'Игра запущена. Закройте её и запустите этот файл снова.'
    }

    # 2. BepInEx
    $bepCore = Join-Path $game 'BepInEx\core\BepInEx.dll'
    if ((Test-Path $bepCore) -and (Test-Path (Join-Path $game 'winhttp.dll'))) {
        Ok 'BepInEx is already installed.' 'BepInEx уже установлен.'
    } else {
        Say '  Downloading BepInEx 5.4.23.5 from github.com ...' '  Скачиваю BepInEx 5.4.23.5 с github.com ...'
        $zip = Join-Path $env:TEMP 'BepInEx_win_x64_5.4.23.5.zip'
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $zip -UseBasicParsing
        $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
        if ($hash -ne $BepInExSha256) { Remove-Item $zip -Force; Fail "BepInEx download is damaged (SHA256 $hash). Try again." "Архив BepInEx повреждён (SHA256 $hash). Попробуйте ещё раз." }
        if (-not $DryRun) { Expand-Archive -Path $zip -DestinationPath $game -Force }
        Remove-Item $zip -Force
        Ok 'BepInEx installed (checksum verified).' 'BepInEx установлен (контрольная сумма совпала).'
    }
    if (-not $DryRun) {
        foreach ($dir in 'BepInEx\patchers', 'BepInEx\plugins', 'BepInEx\config') { New-Item -ItemType Directory -Force -Path (Join-Path $game $dir) | Out-Null }
    }

    # 3. Workshop Loader: prefer the Workshop copy (kept up to date by Steam), else the latest GitHub release
    $loaderTarget = Join-Path $game 'BepInEx\patchers\GK2_WorkshopLoader.dll'
    $workshopRoot = if ($found.Library) { Join-Path $found.Library "steamapps\workshop\content\$AppId" } else { $null }
    $loaderSource = if ($workshopRoot) { Join-Path $workshopRoot "$LoaderItemId\BepInEx\patchers\GK2_WorkshopLoader.dll" } else { $null }
    if ($loaderSource -and (Test-Path $loaderSource)) {
        $origin = T 'Steam Workshop' 'мастерской Steam'
    } else {
        Say '  Downloading GK2 Workshop Loader from github.com ...' '  Скачиваю GK2 Workshop Loader с github.com ...'
        $release = Invoke-RestMethod -Uri $LoaderApi -UseBasicParsing -Headers @{ 'User-Agent' = 'gk2-move-buildings-installer' }
        $asset = $release.assets | Where-Object { $_.name -eq 'GK2_WorkshopLoader.dll' } | Select-Object -First 1
        if (-not $asset) { Fail 'Workshop Loader release has no GK2_WorkshopLoader.dll.' 'В релизе Workshop Loader нет GK2_WorkshopLoader.dll.' }
        $loaderSource = Join-Path $env:TEMP 'GK2_WorkshopLoader.dll'
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $loaderSource -UseBasicParsing
        $origin = "GitHub $($release.tag_name)"
    }
    $same = (Test-Path $loaderTarget) -and ((Get-FileHash $loaderTarget).Hash -eq (Get-FileHash $loaderSource).Hash)
    if ($same) {
        Ok 'Workshop Loader is already installed and up to date.' 'Workshop Loader уже установлен, версия актуальная.'
    } else {
        if (-not $DryRun) { Copy-Item $loaderSource $loaderTarget -Force }
        Ok "Workshop Loader installed from $origin." "Workshop Loader установлен из $origin."
    }

    # 4. The mod itself comes from the Workshop subscription
    $manualCopy = Join-Path $game 'BepInEx\plugins\GK2MoveBuildings'
    if (Test-Path $manualCopy) {
        Warn 'A manually installed copy exists in BepInEx\plugins\GK2MoveBuildings. Delete it if you use the Workshop version.' 'Есть копия, установленная вручную: BepInEx\plugins\GK2MoveBuildings. Удалите её, если ставите мод из мастерской.'
    }
    $subscribed = $workshopRoot -and (Test-Path (Join-Path $workshopRoot $ModItemId))
    if ($subscribed) {
        Ok 'GK2 Move Buildings is subscribed.' 'Подписка на GK2 Move Buildings есть.'
    } else {
        Warn 'Not subscribed to GK2 Move Buildings yet - opening its Workshop page, click Subscribe.' 'Подписки на GK2 Move Buildings ещё нет - открываю страницу в мастерской, нажмите «Подписаться».'
        if (-not $DryRun) { Start-Process "steam://url/CommunityFilePage/$ModItemId" }
    }

    Write-Host ''
    Say 'Done. Start the game: the Workshop Loader will ask to allow the mod - click Yes.' 'Готово. Запустите игру: Workshop Loader спросит, разрешить ли мод, - нажмите Yes.'
    Say 'If the game seems stuck at start, the question is behind it: press Alt+Tab.' 'Если игра будто зависла на старте, окно с вопросом спряталось за ней: нажмите Alt+Tab.'
} catch {
    if ($_.Exception.Message -ne 'stop') {
        Write-Host ''
        Write-Host ('  [X]  ' + $_.Exception.Message) -ForegroundColor Red
    }
    Write-Host ''
    Say 'Nothing is broken; fix the problem above and run the file again.' 'Ничего не сломано: исправьте проблему выше и запустите файл ещё раз.'
}
