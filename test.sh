#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"
dotnet run --project Koren.Tests/Koren.Tests.csproj -c Release
