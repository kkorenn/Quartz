#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"
dotnet run --project Quartz.Tests/Quartz.Tests.csproj -c Release
