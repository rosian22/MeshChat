@echo off
echo ============================================
echo  MeshChat - Running All Tests
echo ============================================
echo.

cd /d "%~dp0"

echo [1/3] Restoring NuGet packages...
dotnet restore MeshChat.Core\MeshChat.Core.csproj
dotnet restore MeshChat.Tests\MeshChat.Tests.csproj
if %ERRORLEVEL% neq 0 (
    echo ERROR: Package restore failed
    pause
    exit /b 1
)

echo.
echo [2/3] Building MeshChat.Core + MeshChat.Tests...
dotnet build MeshChat.Tests\MeshChat.Tests.csproj -c Debug --no-restore
if %ERRORLEVEL% neq 0 (
    echo ERROR: Build failed
    pause
    exit /b 1
)

echo.
echo [3/3] Running tests...
dotnet test MeshChat.Tests\MeshChat.Tests.csproj -c Debug --no-build -v normal --logger "console;verbosity=detailed"
if %ERRORLEVEL% neq 0 (
    echo.
    echo SOME TESTS FAILED - see output above
    pause
    exit /b 1
)

echo.
echo ============================================
echo  ALL TESTS PASSED
echo ============================================
pause
