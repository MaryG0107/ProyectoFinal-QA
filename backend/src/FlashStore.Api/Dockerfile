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

# ---------- Etapa 2: ejecución (imagen "chiseled": sin shell ni apt, usuario sin privilegios) ----------
# trivy encontró vulnerabilidades HIGH en apt/libapt de la imagen Debian; la variante chiseled
# no incluye gestor de paquetes. "-extra" trae ICU y tzdata (fechas y cultura es-GT).
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "FlashStore.Api.dll"]
