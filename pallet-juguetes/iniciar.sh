#!/usr/bin/env bash
# Inicia Control Pallet de Juguetes (requiere .NET 8 SDK).
cd "$(dirname "$0")/src/PalletControl" && exec dotnet run -c Release
