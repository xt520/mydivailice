#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$exe = Join-Path $here 'alicec.exe'
if (Test-Path $exe) {
  & $exe @Args
  exit $LASTEXITCODE
}

$proj = Join-Path (Join-Path $here 'src') 'alicec'

dotnet run --project $proj -- @Args
