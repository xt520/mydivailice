@echo off
setlocal
dotnet run --project "%~dp0src\alicec" -- %*
