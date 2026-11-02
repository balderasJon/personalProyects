# --- Compilación ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/PalletControl/PalletControl.csproj src/PalletControl/
RUN dotnet restore src/PalletControl/PalletControl.csproj
COPY src/ src/
RUN dotnet publish src/PalletControl/PalletControl.csproj -c Release -o /app --no-restore

# --- Ejecución ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:5080 \
    ConnectionStrings__Pallet="Data Source=/app/datos/pallet.db" \
    TZ=America/Mexico_City
VOLUME /app/datos
EXPOSE 5080
ENTRYPOINT ["dotnet", "PalletControl.dll"]
