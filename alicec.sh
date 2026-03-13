#!/usr/bin/env sh
set -e
dotnet run --project "$(dirname "$0")/src/alicec" -- "$@"
