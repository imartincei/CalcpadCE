# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY Calcpad.Core/ Calcpad.Core/
COPY Calcpad.OpenXml/ Calcpad.OpenXml/
COPY Calcpad.Web/s3-server/Calcpad.S3Server/ Calcpad.Web/s3-server/Calcpad.S3Server/
RUN dotnet publish Calcpad.Web/s3-server/Calcpad.S3Server/Calcpad.S3Server.csproj -c Release -o /app/publish
# Keep only the linux-x64 native runtimes (SkiaSharp, SQLite).
RUN find /app/publish/runtimes -mindepth 1 -maxdepth 1 -type d ! -name 'linux-x64' -exec rm -rf {} + || true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Data__Path=/app/data/s3server.db

RUN groupadd -r calcpad && useradd -r -g calcpad calcpad \
    && mkdir -p /app/data && chown -R calcpad:calcpad /app/data
USER calcpad

EXPOSE 8080
ENTRYPOINT ["dotnet", "Calcpad.S3Server.dll"]
