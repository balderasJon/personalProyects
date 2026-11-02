@echo off
REM Genera una carpeta lista para usar en Windows que NO necesita tener .NET instalado.
cd /d "%~dp0"
dotnet publish src\PalletControl\PalletControl.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publicado\win-x64
echo.
echo Listo. Copien la carpeta publicado\win-x64 a la computadora principal y ejecuten PalletControl.exe
pause
