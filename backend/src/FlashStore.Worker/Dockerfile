# ---------- Etapa 1: compilación ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY backend/src/FlashStore.Domain/FlashStore.Domain.csproj backend/src/FlashStore.Domain/
COPY backend/src/FlashStore.Infrastructure/FlashStore.Infrastructure.csproj backend/src/FlashStore.Infrastructure/
COPY backend/src/FlashStore.Worker/FlashStore.Worker.csproj backend/src/FlashStore.Worker/
RUN dotnet restore backend/src/FlashStore.Worker/FlashStore.Worker.csproj

COPY backend/src/ backend/src/
RUN dotnet publish backend/src/FlashStore.Worker/FlashStore.Worker.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Etapa 2: ejecución ----------
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "FlashStore.Worker.dll"]
