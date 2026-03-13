#!/usr/bin/env pwsh

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$alice = Join-Path $here 'alice.ps1'
& $alice @Args
exit $LASTEXITCODE
