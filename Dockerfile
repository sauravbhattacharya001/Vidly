# Vidly — ASP.NET MVC 5 (.NET Framework 4.5.2)
# Multi-stage build: restore + build, then deploy to IIS

# ============================================
# Stage 1: Build
# ============================================
FROM mcr.microsoft.com/dotnet/framework/sdk:4.8 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY Vidly.sln .
COPY Vidly/Vidly.csproj Vidly/
COPY Vidly/packages.config Vidly/

# Restore NuGet packages (cached unless packages.config changes)
RUN nuget restore Vidly.sln

# Copy remaining source
COPY Vidly/ Vidly/

# Build in Release mode
RUN msbuild Vidly/Vidly.csproj /p:Configuration=Release /p:DeployOnBuild=true /p:PublishProfile=FolderProfile /p:PublishUrl=/app /p:WebPublishMethod=FileSystem /p:DeleteExistingFiles=true /verbosity:minimal

# ============================================
# Stage 2: Runtime (IIS)
# ============================================
FROM mcr.microsoft.com/dotnet/framework/aspnet:4.8 AS runtime

# Remove default IIS site content
RUN powershell -Command Remove-Item -Recurse -Force C:\inetpub\wwwroot\*

# Copy published application
WORKDIR /inetpub/wwwroot
COPY --from=build /app .

# IIS runs on port 80 by default
EXPOSE 80

# Healthcheck — verify IIS is responding
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
    CMD powershell -Command "try { $response = Invoke-WebRequest -Uri http://localhost -UseBasicParsing -TimeoutSec 5; if ($response.StatusCode -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }"
