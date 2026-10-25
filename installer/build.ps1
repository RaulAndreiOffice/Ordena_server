# Builds a release of Ordena: the tests, a self-contained publish (the customer needs no .NET), optional
# signing, and the installer. Run from anywhere:
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1 -CertThumbprint <SHA-1 thumbprint>
# Output in artifacts\: OrdenaSetup-<version>.exe and the customer's installation note.
#
# Signing is skipped unless -CertThumbprint is given. It needs signtool.exe (Windows SDK) and a code-signing
# certificate in the current user's store (an OV certificate on its USB token, say). For Azure Trusted
# Signing, replace the signtool arguments in Sign-File with its /dlib and /dmdf options.
#
# Needs Inno Setup 6: winget install --id JRSoftware.InnoSetup -e --scope user
# If the tests report "Zero tests ran" with 0x800711C7, Smart App Control blocked the fresh test build:
# rebuild it with -p:Deterministic=false and run again.
param([string]$CertThumbprint, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$project = Join-Path $root 'src\Ordena.Frontend\Ordena.Frontend.csproj'
$tests = Join-Path $root 'tests\Ordena.Tests'

# The version comes from the project, so the footer, the exe and the installer always agree.
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $project" }

function Invoke-Step($name, [scriptblock]$command) {
  Write-Host "== $name"
  & $command
  if ($LASTEXITCODE -ne 0) { throw "$name failed (exit code $LASTEXITCODE)" }
}

function Find-Iscc {
  $candidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe")
  $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $found) { throw 'Inno Setup 6 is not installed: winget install --id JRSoftware.InnoSetup -e --scope user' }
  $found
}

function Sign-File($path) {
  if (-not $CertThumbprint) { return }
  $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
  if (-not $signtool) { throw 'signtool.exe not found: install the Windows SDK (signing tools).' }
  Invoke-Step "sign $(Split-Path -Leaf $path)" {
    & $signtool.FullName sign /sha1 $CertThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $path
  }
}

$iscc = Find-Iscc
Write-Host "Ordena $version"
# Emptied rather than deleted: a shell may have the folder open.
if (Test-Path $artifacts) { Get-ChildItem $artifacts -Force | Remove-Item -Recurse -Force }

if (-not $SkipTests) {
  Invoke-Step 'tests' { dotnet test $tests -c Release -nologo }
}

# No ReadyToRun: it recompiles every library, which strips Microsoft's signatures from them (EF Core, the
# Extensions...), and Smart App Control then refuses to load them. Seen on the dev PC; startup is fast without.
Invoke-Step 'publish' {
  dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish -nologo
}
Sign-File (Join-Path $publish 'Ordena.exe')

Invoke-Step 'installer' {
  & $iscc /Q "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$artifacts" (Join-Path $PSScriptRoot 'Ordena.iss')
}
$setup = Join-Path $artifacts "OrdenaSetup-$version.exe"
Sign-File $setup

# The customer's note goes next to the setup.
Get-ChildItem $PSScriptRoot -Filter 'LEEME*.txt' | Copy-Item -Destination $artifacts

$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host ''
Write-Host "Done: $setup"
Write-Host ("  {0:N1} MB, SHA-256 {1}" -f ((Get-Item $setup).Length / 1MB), $hash)
Write-Host ("  signed: {0}" -f $(if ($CertThumbprint) { 'yes' } else { 'no (the customer sees a SmartScreen warning)' }))
