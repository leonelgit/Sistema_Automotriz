# AutoFix - preparación inicial (Windows / PowerShell)
# Ejecutar desde la carpeta raíz:  .\setup.ps1
$ErrorActionPreference = "Stop"

Write-Host "1) Herramienta dotnet-ef" -ForegroundColor Cyan
dotnet tool install --global dotnet-ef 2>$null
if ($LASTEXITCODE -ne 0) { dotnet tool update --global dotnet-ef }

Write-Host "2) Restaurar paquetes y compilar" -ForegroundColor Cyan
dotnet restore AutoFix.sln
dotnet build AutoFix.sln

Write-Host "3) Migraciones" -ForegroundColor Cyan
if (-not (Test-Path "AutoFix.Api/Migrations")) {
    dotnet ef migrations add InicialAutoFix --project AutoFix.Api
}
dotnet ef database update --project AutoFix.Api

Write-Host "Listo. Ejecute la API y el MVC (en VS Code: Run and Debug > 'API + MVC')." -ForegroundColor Green
