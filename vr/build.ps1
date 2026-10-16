# SPDX-License-Identifier: LicenseRef-BRIDGE
<#
.SYNOPSIS
    Builds BRIDGE VR (the BRIDGE's Next-Gen Factory Digital Twin Simulation for Robotics), installs
    it, and puts its shortcut on the desktop.

.DESCRIPTION
    The Unity project's sources live here, in vr/project (scripts, shaders, the editor setup) and
    vr/tools (the asset exporter). Unity works on a copy outside OneDrive, so its cache never syncs:
      1. mirror vr/project/Assets/Bridge and the package manifest into the working copy
         (%LOCALAPPDATA%\BridgeVR\project; made from Unity's URP template on the first run);
      2. export the repository's models, textures, fonts and icons into it (export_assets.py);
      3. run BridgeSetup.Setup in batch mode (render pipeline, OpenXR, fonts, materials, scene);
      4. run BridgeSetup.Build: a Windows player in %LOCALAPPDATA%\BridgeVR\build;
      5. install it to %LOCALAPPDATA%\Programs\BRIDGE VR and make the desktop shortcut.

    Needs: Unity 6000.6 with Windows build support, uv (or Python with numpy, Pillow, fontTools and
    brotli), Microsoft Edge (it draws the canvas textures and icons).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File vr\build.ps1
    powershell -ExecutionPolicy Bypass -File vr\build.ps1 -Test robot      # build, then run the robot checks
    powershell -ExecutionPolicy Bypass -File vr\build.ps1 -SkipExport -SkipSetup
#>
param(
    [switch]$SkipExport,
    [switch]$SkipSetup,
    [switch]$SkipBuild,
    [switch]$NoInstall,
    [string]$Test,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$Vr = $PSScriptRoot
$Repo = Split-Path $Vr -Parent
$Root = Join-Path $env:LOCALAPPDATA 'BridgeVR'
$Work = Join-Path $Root 'project'
$Out = Join-Path $Root 'build'
$Logs = Join-Path $Root 'logs'
$App = Join-Path $env:LOCALAPPDATA 'Programs\BRIDGE VR'
$Exe = 'BRIDGE VR.exe'
$Shortcut = "BRIDGE's Next-Gen Factory Digital Twin VR.lnk"
New-Item -ItemType Directory -Force $Root, $Logs | Out-Null

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

function Find-Unity {
    $hub = 'C:\Program Files\Unity\Hub\Editor'
    $editor = Get-ChildItem $hub -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '6000.6*' } | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $editor) { $editor = Get-ChildItem $hub -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '6000.*' } | Sort-Object Name -Descending | Select-Object -First 1 }
    if (-not $editor) { throw "No Unity 6 editor under $hub. Install Unity 6000.6 with Windows build support from Unity Hub." }
    return (Join-Path $editor.FullName 'Editor\Unity.exe')
}

function Invoke-Unity($unity, [string]$method, [string]$log, [string[]]$extra = @()) {
    $unityArgs = @('-batchmode', '-quit', '-projectPath', "`"$Work`"", '-logFile', "`"$log`"", '-executeMethod', $method) + $extra
    $p = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru -WindowStyle Hidden -Wait
    $text = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
    $errors = [regex]::Matches($text, '(?m)^.*error CS\d+.*$') | ForEach-Object { $_.Value } | Select-Object -Unique
    if ($errors) { $errors | Select-Object -First 30 | ForEach-Object { Write-Host $_ -ForegroundColor Red } }
    if ($p.ExitCode -ne 0) {
        $bridge = [regex]::Matches($text, '(?m)^\[bridge\].*$') | ForEach-Object { $_.Value }
        $bridge | Select-Object -Last 10 | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
        throw "Unity $method failed (exit $($p.ExitCode)); see $log"
    }
    [regex]::Matches($text, '(?m)^\[bridge\].*$') | ForEach-Object { Write-Host $_.Value }
}

$unity = Find-Unity
Write-Host "Unity: $unity"

# ------------------------------------------------------------------ 1. the working copy
Step 'Working copy'
if (-not (Test-Path (Join-Path $Work 'ProjectSettings\ProjectSettings.asset'))) {
    $template = Get-ChildItem (Join-Path (Split-Path (Split-Path $unity -Parent) -Parent) 'Editor\Data\Resources\PackageManager\ProjectTemplates') -Filter 'com.unity.template.urp-blank-*.tgz' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $template) { $template = Get-ChildItem (Join-Path (Split-Path $unity -Parent) 'Data\Resources\PackageManager\ProjectTemplates') -Filter 'com.unity.template.urp-blank-*.tgz' | Select-Object -First 1 }
    if (-not $template) { throw 'The URP blank template is missing from the Unity editor.' }
    $tmp = Join-Path $Root 'template'
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $tmp | Out-Null
    tar -xzf $template.FullName -C $tmp
    $data = Join-Path $tmp 'package\ProjectData~'
    New-Item -ItemType Directory -Force (Join-Path $Work 'Assets\Settings'), (Join-Path $Work 'Packages') | Out-Null
    Copy-Item (Join-Path $data 'ProjectSettings') $Work -Recurse -Force
    Copy-Item (Join-Path $data 'Assets\Settings\*') (Join-Path $Work 'Assets\Settings') -Force
    Remove-Item $tmp -Recurse -Force
    Write-Host "Made $Work from $($template.Name)"
}
# TextMesh Pro's essentials (its shaders and settings), unpacked rather than imported: Unity imports
# a package asynchronously, and a batch run can quit before that finishes.
if (-not (Test-Path (Join-Path $Work 'Assets\TextMesh Pro\Resources\TMP Settings.asset'))) {
    $tmp = Join-Path (Split-Path $unity -Parent) 'Data\Resources\PackageManager\BuiltInPackages\com.unity.ugui\Package Resources\TMP Essential Resources.unitypackage'
    if (-not (Test-Path $tmp)) { throw "TMP Essential Resources.unitypackage not found at $tmp" }
    python -I (Join-Path $Vr 'tools\unpack_unitypackage.py') $tmp $Work
    if ($LASTEXITCODE -ne 0) { throw 'Unpacking TextMesh Pro essentials failed.' }
}
robocopy (Join-Path $Vr 'project\Assets\Bridge') (Join-Path $Work 'Assets\Bridge') /MIR /XF *.meta /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
Copy-Item (Join-Path $Vr 'project\Packages\manifest.json') (Join-Path $Work 'Packages\manifest.json') -Force
Write-Host "Synced vr/project into $Work"

# ------------------------------------------------------------------ 2. assets
if (-not $SkipExport) {
    Step 'Exporting models, textures, fonts and icons'
    $gen = Join-Path $Work 'Assets\BridgeGen'
    $exporter = Join-Path $Vr 'tools\export_assets.py'
    if (Get-Command uv -ErrorAction SilentlyContinue) {
        uv run --no-project --with numpy --with pillow --with fonttools --with brotli python $exporter --out $gen
    } else {
        python $exporter --out $gen
    }
    if ($LASTEXITCODE -ne 0) { throw 'The asset export failed.' }
}

# ------------------------------------------------------------------ 3, 4. Unity
if (-not $SkipSetup) {
    Step 'Unity: setting the project up'
    Invoke-Unity $unity 'Bridge.EditorTools.BridgeSetup.Setup' (Join-Path $Logs 'setup.log')
}
if (-not $SkipBuild) {
    Step 'Unity: building the Windows player'
    Invoke-Unity $unity 'Bridge.EditorTools.BridgeSetup.Build' (Join-Path $Logs 'build.log') @('-bridgeOut', "`"$Out`"")
}

# ------------------------------------------------------------------ 5. install
if (-not $NoInstall) {
    Step 'Installing'
    Get-Process -Name 'BRIDGE VR' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $App | Out-Null
    # Unity's debug folders sit beside the player and are not part of it.
    robocopy $Out $App /MIR /XD '*_DoNotShip' '*_ButDontShipItWithYourGame' /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }
    Get-ChildItem $App -Directory | Where-Object { $_.Name -like '*_DoNotShip' -or $_.Name -like '*_ButDontShipItWithYourGame' } | Remove-Item -Recurse -Force
    $icon = Join-Path $Work 'Assets\BridgeGen\app_icon.ico'
    if (Test-Path $icon) { Copy-Item $icon (Join-Path $App 'BRIDGE VR.ico') -Force }
    $desktop = [Environment]::GetFolderPath('Desktop')
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut((Join-Path $desktop $Shortcut))
    $lnk.TargetPath = Join-Path $App $Exe
    $lnk.WorkingDirectory = $App
    $lnk.Description = "BRIDGE's Next-Gen Factory Digital Twin Simulation for Robotics (VR) by Awaiz Ahmed. Put on your Meta Quest (2 or 3) with Quest Link on."
    if (Test-Path (Join-Path $App 'BRIDGE VR.ico')) { $lnk.IconLocation = (Join-Path $App 'BRIDGE VR.ico') + ',0' }
    $lnk.Save()
    Write-Host "Installed to $App; shortcut: $(Join-Path $desktop $Shortcut)"
}

# ------------------------------------------------------------------ checks and launch
if ($Test) {
    Step "Running the app's checks: $Test"
    $shots = Join-Path $Root "autotest-$Test"
    Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
    $target = if ($NoInstall) { Join-Path $Out $Exe } else { Join-Path $App $Exe }
    $log = Join-Path $Logs "autotest-$Test.log"
    $p = Start-Process -FilePath $target -ArgumentList @('-desktop', '-autotest', $Test, '-shots', "`"$shots`"", '-screen-width', '1920', '-screen-height', '1080', '-screen-fullscreen', '0', '-logFile', "`"$log`"") -PassThru -Wait
    Get-Content $log | Where-Object { $_ -match '^\[autotest\]' } | ForEach-Object { if ($_ -match 'FAIL') { Write-Host $_ -ForegroundColor Red } else { Write-Host $_ } }
    Write-Host "Screenshots and report: $shots"
    if ($p.ExitCode -ne 0) { throw "Checks failed (exit $($p.ExitCode))" }
}
if ($Run) { Start-Process (Join-Path $App $Exe) -WorkingDirectory $App }
