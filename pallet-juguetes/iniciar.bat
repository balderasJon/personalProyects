@echo off
REM Inicia Control Pallet de Juguetes (requiere .NET 8 SDK).
cd /d "%~dp0src\PalletControl"
start "" http://localhost:5080
dotnet run -c Release
pause
