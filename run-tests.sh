#!/bin/bash
set -e

echo "============================================"
echo " MeshChat - Running All Tests"
echo "============================================"
echo ""

cd "$(dirname "$0")"

echo "[1/3] Restoring NuGet packages..."
dotnet restore MeshChat.Core/MeshChat.Core.csproj
dotnet restore MeshChat.Tests/MeshChat.Tests.csproj

echo ""
echo "[2/3] Building MeshChat.Core + MeshChat.Tests..."
dotnet build MeshChat.Tests/MeshChat.Tests.csproj -c Debug --no-restore

echo ""
echo "[3/3] Running tests..."
dotnet test MeshChat.Tests/MeshChat.Tests.csproj -c Debug --no-build -v normal --logger "console;verbosity=detailed"

echo ""
echo "============================================"
echo " ALL TESTS PASSED"
echo "============================================"
