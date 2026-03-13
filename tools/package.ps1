# PowerShell packaging script (Windows)

param(
  [string]$Rid = 'win-x64',
  [string]$Configuration = 'Release',
  [string]$OutRoot = 'artifacts',
  [switch]$SkipSelfTest,
  [switch]$FrameworkOnly,
  [switch]$SelfContainedOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Copy-IfExists([string]$Path, [string]$DestDir) {
  if (Test-Path $Path) {
    Copy-Item -Force -Recurse $Path $DestDir
  }
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $repoRoot

$alicecProj = Join-Path $repoRoot 'src\alicec\alicec.csproj'
if (-not (Test-Path $alicecProj)) {
  throw "Missing project file: $alicecProj"
}

$outRootFull = Join-Path $repoRoot $OutRoot
New-Item -ItemType Directory -Force $outRootFull | Out-Null

Write-Host "== package.ps1 =="
Write-Host "RID=$Rid  Config=$Configuration"
Write-Host "OutRoot=$outRootFull"

Write-Host "[1/5] dotnet build..."
dotnet build (Join-Path $repoRoot 'Alice.sln') -c $Configuration | Out-Host

if (-not $SkipSelfTest) {
  Write-Host "[2/5] selftest..."
  dotnet run --project (Join-Path $repoRoot 'src\alicec') -- selftest | Out-Host
}

function Publish-AliceC([string]$publishDir, [bool]$selfContained) {
  if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
  }
  New-Item -ItemType Directory -Force $publishDir | Out-Null

  $sc = if ($selfContained) { 'true' } else { 'false' }
  Write-Host "dotnet publish selfContained=$sc -> $publishDir"
  dotnet publish $alicecProj -c $Configuration -r $Rid --self-contained $sc -p:UseAppHost=true -o $publishDir | Out-Host
}

function Publish-Runner([string]$publishDir, [bool]$selfContained) {
  $runnerProj = Join-Path $repoRoot 'src\Alice.PackedRunner\Alice.PackedRunner.csproj'
  if (-not (Test-Path $runnerProj)) {
    throw "Missing runner project file: $runnerProj"
  }

  if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
  }
  New-Item -ItemType Directory -Force $publishDir | Out-Null

  $sc = if ($selfContained) { 'true' } else { 'false' }
  Write-Host "dotnet publish runner selfContained=$sc -> $publishDir"
  dotnet publish $runnerProj -c $Configuration -r $Rid --self-contained $sc -p:PublishSingleFile=true -p:UseAppHost=true -o $publishDir | Out-Host
}

function Assemble-Dist([string]$publishDir, [string]$distDir) {
  if (Test-Path $distDir) {
    Remove-Item -Recurse -Force $distDir
  }
  New-Item -ItemType Directory -Force $distDir | Out-Null

  Copy-IfExists (Join-Path $repoRoot 'std') $distDir
  Copy-IfExists (Join-Path $repoRoot 'docx') $distDir

  Copy-IfExists (Join-Path $repoRoot 'alice.cmd') $distDir
  Copy-IfExists (Join-Path $repoRoot 'alice.ps1') $distDir
  Copy-IfExists (Join-Path $repoRoot 'alice') $distDir

  Copy-IfExists (Join-Path $repoRoot 'ailice.cmd') $distDir
  Copy-IfExists (Join-Path $repoRoot 'ailice.ps1') $distDir
  Copy-IfExists (Join-Path $repoRoot 'ailice') $distDir

  $runnersDir = Join-Path $distDir 'runners'
  New-Item -ItemType Directory -Force $runnersDir | Out-Null
  Copy-IfExists (Join-Path $outRootFull ("runner-$Rid\\alice-runner-$Rid.exe")) $runnersDir
  Copy-IfExists (Join-Path $outRootFull ("runner-$Rid-selfcontained\\alice-runner-$Rid-selfcontained.exe")) $runnersDir

  Copy-IfExists (Join-Path $publishDir 'alicec.exe') $runnersDir

  Copy-Item -Force -Recurse (Join-Path $publishDir '*') $distDir
}

function Zip-Dist([string]$distDir, [string]$zipPath) {
  if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
  }
  Compress-Archive -Path (Join-Path $distDir '*') -DestinationPath $zipPath -Force
}

$publishFramework = Join-Path $outRootFull "alicec-publish-$Rid"
$publishSelfContained = Join-Path $outRootFull "alicec-publish-$Rid-selfcontained"

$runnerFramework = Join-Path $outRootFull "runner-$Rid"
$runnerSelfContained = Join-Path $outRootFull "runner-$Rid-selfcontained"

$distFramework = Join-Path $outRootFull "alicec-dist-$Rid-framework"
$distSelfContained = Join-Path $outRootFull "alicec-dist-$Rid-selfcontained"

$zipFramework = Join-Path $outRootFull "alicec-dist-$Rid-framework.zip"
$zipSelfContained = Join-Path $outRootFull "alicec-dist-$Rid-selfcontained.zip"

Write-Host "[3/5] publish..."

if (-not $SelfContainedOnly) {
  Publish-AliceC $publishFramework $false
}

if (-not $FrameworkOnly) {
  Publish-AliceC $publishSelfContained $true
}

if (-not $SelfContainedOnly) {
  Publish-Runner $runnerFramework $false
  $runnerExe = Get-ChildItem -Recurse $runnerFramework -Filter *.exe | Select-Object -First 1
  if ($runnerExe) {
    Copy-Item -Force $runnerExe.FullName (Join-Path $outRootFull ("runner-$Rid\\alice-runner-$Rid.exe"))
  }
}

if (-not $FrameworkOnly) {
  Publish-Runner $runnerSelfContained $true
  $runnerExe2 = Get-ChildItem -Recurse $runnerSelfContained -Filter *.exe | Select-Object -First 1
  if ($runnerExe2) {
    Copy-Item -Force $runnerExe2.FullName (Join-Path $outRootFull ("runner-$Rid-selfcontained\\alice-runner-$Rid-selfcontained.exe"))
  }
}

Write-Host "[4/5] assemble dist..."
if (-not $SelfContainedOnly) {
  Assemble-Dist $publishFramework $distFramework
}
if (-not $FrameworkOnly) {
  Assemble-Dist $publishSelfContained $distSelfContained
}

Write-Host "[5/5] zip..."
if (-not $SelfContainedOnly) {
  Zip-Dist $distFramework $zipFramework
  Write-Host "OK: $zipFramework"
}
if (-not $FrameworkOnly) {
  Zip-Dist $distSelfContained $zipSelfContained
  Write-Host "OK: $zipSelfContained"
}

Write-Host "Done."
