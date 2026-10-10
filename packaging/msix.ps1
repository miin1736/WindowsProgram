# SnapContext MSIX proof-of-concept helper (ASCII only on purpose: Windows PowerShell 5.1 mangles non-ASCII in BOM-less files).
#
#   powershell -ExecutionPolicy Bypass -File packaging\msix.ps1 -Action build
#   powershell -ExecutionPolicy Bypass -File packaging\msix.ps1 -Action register     (needs Windows Developer Mode ON)
#   powershell -ExecutionPolicy Bypass -File packaging\msix.ps1 -Action unregister
#
# build      : dotnet publish (self-contained, win-x64) -> packaging\out\layout, copy manifest + assets,
#              then pack packaging\out\SnapContext.msix with makeappx.exe if it can be found.
# register   : register the layout folder as an installed (unsigned, loose-file) package for testing.
# unregister : remove that test package again.
param(
  [ValidateSet("build", "register", "unregister")] [string]$Action = "build",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$pkg = Join-Path $root "packaging"
$out = Join-Path $pkg "out"
$layout = Join-Path $out "layout"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$identityName = "SnapContext.Dev"

function Find-MakeAppx {
  $cache = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools"
  if (Test-Path $cache) {
    $hit = Get-ChildItem $cache -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match "\\x64\\" } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($hit) { return $hit.FullName }
  }
  $kits = "C:\Program Files (x86)\Windows Kits\10\bin"
  if (Test-Path $kits) {
    $hit = Get-ChildItem $kits -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
      Where-Object { $_.FullName -match "\\x64\\" } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($hit) { return $hit.FullName }
  }
  return $null
}

switch ($Action) {
  "build" {
    New-Item -ItemType Directory -Force $out | Out-Null
    & $dotnet publish (Join-Path $root "src\SnapContext\SnapContext.csproj") `
      -c $Configuration -r win-x64 --self-contained true -o $layout -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    Copy-Item (Join-Path $pkg "AppxManifest.xml") $layout -Force
    Copy-Item (Join-Path $pkg "Assets") $layout -Recurse -Force
    $size = [math]::Round(((Get-ChildItem $layout -Recurse | Measure-Object Length -Sum).Sum) / 1MB)
    Write-Host "layout ready: $layout ($size MB)"

    $makeappx = Find-MakeAppx
    if ($makeappx) {
      $msix = Join-Path $out "SnapContext.msix"
      & $makeappx pack /d $layout /p $msix /o
      if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed" }
      Write-Host "package ready: $msix"
    } else {
      Write-Host "makeappx.exe not found - skipped .msix packing (the layout folder can still be registered for testing)."
    }
  }
  "register" {
    Add-AppxPackage -Register (Join-Path $layout "AppxManifest.xml")
    Get-AppxPackage -Name $identityName | Format-List Name, Version, InstallLocation, Status
  }
  "unregister" {
    Get-AppxPackage -Name $identityName | Remove-AppxPackage
    Write-Host "removed $identityName (if it was installed)"
  }
}
