# Gryphmark — full build: web bundle, icon, manuals (PDF), self-contained publish, installer.
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 [-SkipNpm] [-Art]
#   -SkipNpm  keep the web bundle in src/Gryphmark/wwwroot/lib as it is
#   -Art      redraw the icons and the installer pictures (they are committed; needs Python with
#             Pillow, numpy, scipy and opencv-python)
# The version comes from <Version> in src/Gryphmark/Gryphmark.csproj, the only place it is written.
# Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
# Date: November 10, 2026
# SPDX-License-Identifier: GPL-2.0-only
param([switch]$SkipNpm, [switch]$Art)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$Version = ([xml](Get-Content (Join-Path $root "src/Gryphmark/Gryphmark.csproj") -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $Version) { throw "No <Version> in Gryphmark.csproj" }
$out = Join-Path $root "out"
$pub = Join-Path $out "publish"
$www = Join-Path $root "src/Gryphmark/wwwroot"

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }

# 1. Web bundle (editor + GitHub renderer)
if (-not $SkipNpm) {
    Step "npm bundle"
    Push-Location (Join-Path $root "_npm")
    npm ci --silent
    if ($LASTEXITCODE) { throw "npm ci failed" }
    New-Item -ItemType Directory -Force (Join-Path $www "lib") | Out-Null
    npx esbuild entry.js --bundle --minify --format=iife --outfile="$www/lib/bundle.js"
    # TOAST UI 3.2.2 reads the first row of a <tbody> without checking that there is one, so a table with an
    # empty <tbody> (pandoc writes one for every one-row table) threw and left the page blank. Patched here;
    # the build stops if the code it fixes is not found exactly once.
    $bundle = "$www/lib/bundle.js"; $js = [IO.File]::ReadAllText($bundle)
    $re = 'var (\w+)=(\w+)\.querySelectorAll\("tr"\),(\w+)=\1\[0\]\.children\.length'
    if ([regex]::Matches($js, $re).Count -ne 1) { throw "TOAST UI <tbody> patch: the code to fix was not found once in bundle.js" }
    $js = [regex]::Replace($js, $re, 'var $1=$2.querySelectorAll("tr"),$3=$1.length?$1[0].children.length:0')
    [IO.File]::WriteAllText($bundle, $js, (New-Object System.Text.UTF8Encoding $false))
    $nm = "node_modules"
    Copy-Item "$nm/@toast-ui/editor/dist/toastui-editor.css", "$nm/@toast-ui/editor/dist/theme/toastui-editor-dark.css",
              "$nm/@toast-ui/editor-plugin-code-syntax-highlight/dist/toastui-editor-plugin-code-syntax-highlight.css",
              "$nm/github-markdown-css/github-markdown-light.css", "$nm/github-markdown-css/github-markdown-dark.css" "$www/lib/"
    Copy-Item "$nm/highlight.js/styles/github.min.css" "$www/lib/hljs-light.css"
    Copy-Item "$nm/highlight.js/styles/github-dark.min.css" "$www/lib/hljs-dark.css"
    Copy-Item "$nm/mermaid/dist/mermaid.min.js", "$nm/katex/dist/katex.min.css" "$www/lib/"
    New-Item -ItemType Directory -Force "$www/lib/fonts" | Out-Null
    Copy-Item "$nm/katex/dist/fonts/*.woff2" "$www/lib/fonts/"
    Pop-Location
}

# 2. Icon and installer artwork (only with -Art: the drawn files are in the repository)
if ($Art) {
Step "icon + wizard art"
python "$root/tools/make_icon_griffin.py" "$root/assets/griffin_schongauer_met_cc0.jpg" "$root/src/Gryphmark/Gryphmark.ico" "$root/assets/gryphmark_base.png" "232,96,44" "1060,400,900" "12"
# theme colours from that orange base; the default (gold) variant becomes Gryphmark.ico and assets/gryphmark.png
python "$root/tools/make_theme_icons.py"
python "$root/tools/make_wizard.py"
}

# 3. Manuals -> PDF (a Chromium browser, headless)
Step "manuals"
python "$root/tools/make_manual.py" $Version
# any Chromium browser prints the PDF: Edge, Chrome or Brave
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
          "$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
          "$env:ProgramFiles\BraveSoftware\Brave-Browser\Application\brave.exe", "$env:LOCALAPPDATA\BraveSoftware\Brave-Browser\Application\brave.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw "No Chromium browser (Edge, Chrome or Brave) to print the manuals" }
foreach ($l in "EN", "ES") {
    $html = Join-Path $root "docs/Gryphmark_Manual_$l.html"
    $pdf = Join-Path $root "docs/Gryphmark_Manual_$l.pdf"
    if (Test-Path $pdf) { Remove-Item $pdf -Force }
    Start-Process -FilePath $edge -Wait -WindowStyle Hidden -ArgumentList @("--headless=new", "--disable-gpu", "--no-first-run", "--user-data-dir=`"$env:TEMP\gm_pdf_profile`"", "--no-pdf-header-footer", "--print-to-pdf=`"$pdf`"", ([uri]$html).AbsoluteUri)
    $t = 0; while (-not (Test-Path $pdf) -and $t -lt 60) { Start-Sleep -Milliseconds 500; $t++ }
    if (-not (Test-Path $pdf)) { throw "PDF not generated: $pdf" }
}

# 4. Publish (self-contained, win-x64)
Step "dotnet publish"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish "$root/src/Gryphmark/Gryphmark.csproj" -c Release -o $pub -p:Version=$Version -nologo -v q
if ($LASTEXITCODE) { throw "dotnet publish failed" }
New-Item -ItemType Directory -Force "$pub/docs" | Out-Null
Copy-Item "$root/docs/Gryphmark_Manual_EN.pdf", "$root/docs/Gryphmark_Manual_ES.pdf" "$pub/docs/"

# Optional code signing: set GM_SIGN_PFX (path to a .pfx) and GM_SIGN_PASS before building.
function Sign($file) {
    if (-not $env:GM_SIGN_PFX) { return }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "x64" } | Select-Object -Last 1
    if (-not $signtool) { throw "signtool.exe not found (install the Windows SDK)" }
    & $signtool.FullName sign /f $env:GM_SIGN_PFX /p $env:GM_SIGN_PASS /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $file
    if ($LASTEXITCODE) { throw "signing failed: $file" }
}
Sign "$pub/Gryphmark.exe"

# 5. Installer
Step "installer"
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }
& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$pub" "$root/installer/Gryphmark.iss"
if ($LASTEXITCODE) { throw "ISCC failed" }
Sign "$out/Gryphmark-Setup-$Version.exe"
Get-Item "$out/Gryphmark-Setup-$Version.exe" | Select-Object Name, Length
