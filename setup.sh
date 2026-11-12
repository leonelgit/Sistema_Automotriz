#!/usr/bin/env bash
# AutoFix - preparación inicial (macOS / Linux / Git Bash)
set -e
echo "1) Herramienta dotnet-ef"
dotnet tool install --global dotnet-ef 2>/dev/null || dotnet tool update --global dotnet-ef
echo "2) Restaurar paquetes y compilar"
dotnet restore AutoFix.sln
dotnet build AutoFix.sln
echo "3) Migraciones"
if [ ! -d "AutoFix.Api/Migrations" ]; then
  dotnet ef migrations add InicialAutoFix --project AutoFix.Api
fi
dotnet ef database update --project AutoFix.Api
echo "Listo. Ejecute la API y el MVC (en VS Code: Run and Debug > 'API + MVC')."
