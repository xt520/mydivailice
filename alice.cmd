@echo off
setlocal

rem 优先使用同目录下的已发布 alicec.exe
if exist "%~dp0alicec.exe" (
  "%~dp0alicec.exe" %*
  exit /b %ERRORLEVEL%
)

rem 开发态回退：dotnet run
set "PROJ=%~dp0src\alicec"
dotnet run --project "%PROJ%" -- %*
