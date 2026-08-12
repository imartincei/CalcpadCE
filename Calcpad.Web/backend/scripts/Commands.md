# Commands

## Start Docker Container

cd Calcpad.Web/backend
docker compose up --build

## Start Linux Dev Server

./scripts/restart-dev-server.sh

## Build Linux Bundle

./scripts/build-linux.sh

## Build Slim Bundle (no platform runtimes)

./scripts/build-slim-bundle.sh

## Build Wpf

dotnet build Calcpad.Wpf\Calcpad.wpf.csproj
