# Quillforge — full build: web bundle, icon, manuals (PDF), self-contained publish, installer.
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Version 1.0.0] [-SkipNpm]
param([string]$Version = "1.0.0", [switch]$SkipNpm)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "out"
$pub = Join-Path $out "publish"
$www = Join-Path $root "src/Quillforge/wwwroot"

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }

# 1. Web bundle (editor + GitHub renderer)
if (-not $SkipNpm) {
    Step "npm bundle"
    Push-Location (Join-Path $root "_npm")
    npm ci --silent
    New-Item -ItemType Directory -Force (Join-Path $www "lib") | Out-Null
    npx esbuild entry.js --bundle --minify --format=iife --outfile="$www/lib/bundle.js"
    $nm = "node_modules"
    Copy-Item "$nm/@toast-ui/editor/dist/toastui-editor.css", "$nm/@toast-ui/editor/dist/theme/toastui-editor-dark.css",
              "$nm/@toast-ui/editor-plugin-code-syntax-highlight/dist/toastui-editor-plugin-code-syntax-highlight.css",
              "$nm/github-markdown-css/github-markdown-light.css", "$nm/github-markdown-css/github-markdown-dark.css" "$www/lib/"
    Copy-Item "$nm/highlight.js/styles/github.min.css" "$www/lib/hljs-light.css"
    Copy-Item "$nm/highlight.js/styles/github-dark.min.css" "$www/lib/hljs-dark.css"
    Pop-Location
}

# 2. Icon and installer artwork
Step "icon + wizard art"
python "$root/tools/make_icon.py" "$root/src/Quillforge/Quillforge.ico" "$root/assets/quillforge.png"
python "$root/tools/make_wizard.py"

# 3. Manuals -> PDF (Edge headless)
Step "manuals"
python "$root/tools/make_manual.py" $Version
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
foreach ($l in "EN", "ES") {
    $html = Join-Path $root "docs/Quillforge_Manual_$l.html"
    $pdf = Join-Path $root "docs/Quillforge_Manual_$l.pdf"
    if (Test-Path $pdf) { Remove-Item $pdf -Force }
    Start-Process -FilePath $edge -Wait -WindowStyle Hidden -ArgumentList @("--headless=new", "--disable-gpu", "--no-pdf-header-footer", "--print-to-pdf=`"$pdf`"", ([uri]$html).AbsoluteUri)
    $t = 0; while (-not (Test-Path $pdf) -and $t -lt 60) { Start-Sleep -Milliseconds 500; $t++ }
    if (-not (Test-Path $pdf)) { throw "PDF not generated: $pdf" }
}

# 4. Publish (self-contained, win-x64)
Step "dotnet publish"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish "$root/src/Quillforge/Quillforge.csproj" -c Release -o $pub -p:Version=$Version -nologo -v q
if ($LASTEXITCODE) { throw "dotnet publish failed" }
New-Item -ItemType Directory -Force "$pub/docs" | Out-Null
Copy-Item "$root/docs/Quillforge_Manual_EN.pdf", "$root/docs/Quillforge_Manual_ES.pdf" "$pub/docs/"

# 5. Installer
Step "installer"
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }
& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$pub" "$root/installer/Quillforge.iss"
if ($LASTEXITCODE) { throw "ISCC failed" }
Get-Item "$out/Quillforge-Setup-$Version.exe" | Select-Object Name, Length
