# ---------- Etapa 1: compilación ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Primero solo los .csproj para aprovechar la cache de Docker en el restore.
COPY Directory.Build.props ./
COPY backend/src/FlashStore.Domain/FlashStore.Domain.csproj backend/src/FlashStore.Domain/
COPY backend/src/FlashStore.Infrastructure/FlashStore.Infrastructure.csproj backend/src/FlashStore.Infrastructure/
COPY backend/src/FlashStore.Api/FlashStore.Api.csproj backend/src/FlashStore.Api/
RUN dotnet restore backend/src/FlashStore.Api/FlashStore.Api.csproj

COPY backend/src/ backend/src/
RUN dotnet publish backend/src/FlashStore.Api/FlashStore.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Etapa 2: ejecución (imagen pequeña, usuario sin privilegios) ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "FlashStore.Api.dll"]
