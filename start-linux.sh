#!/usr/bin/env bash
set -e
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
dotnet run --project src/LaunchForge.Desktop
