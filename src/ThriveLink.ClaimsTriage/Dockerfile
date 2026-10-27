# Build context: repository root.  az acr build -f src/ThriveLink.ClaimsTriage/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a project file changes.
COPY Directory.Build.props .editorconfig ./
COPY src/ThriveLink.Domain/ThriveLink.Domain.csproj src/ThriveLink.Domain/
COPY src/ThriveLink.Data/ThriveLink.Data.csproj src/ThriveLink.Data/
COPY src/ThriveLink.ClaimsTriage/ThriveLink.ClaimsTriage.csproj src/ThriveLink.ClaimsTriage/
RUN dotnet restore src/ThriveLink.ClaimsTriage/ThriveLink.ClaimsTriage.csproj

COPY src/ThriveLink.Domain/ src/ThriveLink.Domain/
COPY src/ThriveLink.Data/ src/ThriveLink.Data/
COPY src/ThriveLink.ClaimsTriage/ src/ThriveLink.ClaimsTriage/
RUN dotnet publish src/ThriveLink.ClaimsTriage/ThriveLink.ClaimsTriage.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
# Built-in non-root user in .NET images.
USER $APP_UID
ENTRYPOINT ["dotnet", "ThriveLink.ClaimsTriage.dll"]
