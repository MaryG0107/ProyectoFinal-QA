<#
.SYNOPSIS
    Sincroniza el archivo .env entre Windows (Visual Studio) y la VM de Ubuntu.

.DESCRIPTION
    El archivo .env se edita en Windows y se envía a la VM, donde Docker Compose lo usa.
    El .env nunca se sube a GitHub (está en .gitignore).

.PARAMETER Pull
    Trae el .env de la VM a Windows (úselo la primera vez, para tener las claves reales).

.EXAMPLE
    .\scripts\sync-env.ps1 -Pull
    Copia ~/ProyectoFinal-QA/.env de la VM a la carpeta del proyecto en Windows.

.EXAMPLE
    .\scripts\sync-env.ps1
    Envía el .env de Windows a la VM, corrige finales de línea, protege el archivo
    y aplica los cambios con docker compose up -d.
#>
param(
    [switch]$Pull,
    [string]$VmUser = "mary",
    [string]$VmHost = "localhost",
    [int]$Port = 2222,
    [string]$RemoteDir = "~/ProyectoFinal-QA"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$localEnv = Join-Path $projectRoot ".env"
$remote = "$VmUser@$VmHost"

if ($Pull) {
    Write-Host "Descargando .env desde la VM..." -ForegroundColor Cyan
    scp -P $Port "${remote}:$RemoteDir/.env" "$localEnv"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo copiar el .env desde la VM." }
    Write-Host "OK: .env de la VM guardado en $localEnv" -ForegroundColor Green
    exit 0
}

if (-not (Test-Path $localEnv)) {
    throw "No existe $localEnv. Ejecute primero: .\scripts\sync-env.ps1 -Pull"
}

Write-Host "Enviando .env a la VM..." -ForegroundColor Cyan
scp -P $Port "$localEnv" "${remote}:$RemoteDir/.env"
if ($LASTEXITCODE -ne 0) { throw "No se pudo copiar el .env a la VM." }

# En la VM: quitar finales de línea de Windows (CRLF), proteger el archivo y aplicar cambios.
$remoteCommand = 'cd ' + $RemoteDir + ' && sed -i ''s/\r$//'' .env && chmod 600 .env && docker compose up -d && docker compose ps'

Write-Host "Aplicando cambios en la VM..." -ForegroundColor Cyan
ssh -p $Port $remote $remoteCommand
if ($LASTEXITCODE -ne 0) { throw "Falló la aplicación de cambios en la VM." }

Write-Host "OK: .env sincronizado y servicios actualizados." -ForegroundColor Green
