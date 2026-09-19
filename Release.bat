@echo off
setlocal

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%ArtificialInnocence.sln"
set "GAME_PROJECT=%ROOT%src\ArtificialInnocence.Game\ArtificialInnocence.Game.csproj"
set "TEST_PROJECT=%ROOT%tests\ArtificialInnocence.Tests\ArtificialInnocence.Tests.csproj"
set "OUTPUT_DIR=%ROOT%out"
set "STAGE_DIR=%ROOT%tmp\release-stage"

pushd "%ROOT%" || goto :error

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: The .NET SDK was not found in PATH.
    goto :error
)

echo [1/3] Restoring locked dependencies...
dotnet restore "%SOLUTION%" --locked-mode
if errorlevel 1 goto :error

echo [2/3] Running regression tests...
dotnet run --project "%TEST_PROJECT%" --configuration Release --no-restore
if errorlevel 1 goto :error

if exist "%STAGE_DIR%" rmdir /s /q "%STAGE_DIR%"

echo [3/3] Publishing Windows x64 release...
dotnet publish "%GAME_PROJECT%" ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output "%STAGE_DIR%" ^
    -p:NuGetLockFilePath=obj\release.packages.lock.json ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false
if errorlevel 1 goto :error

if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
move "%STAGE_DIR%" "%OUTPUT_DIR%" >nul
if errorlevel 1 goto :error

echo.
echo Release completed: "%OUTPUT_DIR%"
popd
exit /b 0

:error
echo.
echo ERROR: Release build failed. Existing out directory was not replaced.
popd
exit /b 1
