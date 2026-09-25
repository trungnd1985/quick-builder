# QuickBuilder — single image containing the backend and the prebuilt admin assets
# (FR-035). An operator never runs Node, never runs a build toolchain, and never runs a
# second process for the frontend.

# --- Stage 1: build the admin app -------------------------------------------------
# Node exists only here. It is not in the runtime image.
FROM node:24-alpine AS admin
WORKDIR /admin

# Copy manifests first so `npm ci` is cached independently of source changes.
COPY src/admin/package.json src/admin/package-lock.json ./
RUN npm ci

COPY src/admin/ ./
# Vite writes content-hashed filenames (FR-046), which is what stops an upgrade serving
# a stale mix of old and new assets.
RUN npm run build -- --outDir /admin/dist --emptyOutDir

# --- Stage 2: publish the backend -------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore against the manifests alone, so a source-only change does not re-download.
# .editorconfig is a build input, not an editor preference: it carries the analyzer
# severities and suppressions. Leaving it out makes the image build behave differently
# from the local build, which is exactly the kind of drift CI is supposed to catch.
COPY Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY QuickBuilder.slnx ./
COPY src/QuickBuilder.Host/*.csproj                                   src/QuickBuilder.Host/
COPY src/Shared/QuickBuilder.Shared.Kernel/*.csproj                   src/Shared/QuickBuilder.Shared.Kernel/
COPY src/Shared/QuickBuilder.Shared.Infrastructure/*.csproj           src/Shared/QuickBuilder.Shared.Infrastructure/
COPY src/Modules/Sites/QuickBuilder.Sites.Contracts/*.csproj          src/Modules/Sites/QuickBuilder.Sites.Contracts/
COPY src/Modules/Sites/QuickBuilder.Sites/*.csproj                    src/Modules/Sites/QuickBuilder.Sites/
COPY src/Modules/Identity/QuickBuilder.Identity.Contracts/*.csproj    src/Modules/Identity/QuickBuilder.Identity.Contracts/
COPY src/Modules/Identity/QuickBuilder.Identity/*.csproj              src/Modules/Identity/QuickBuilder.Identity/
COPY src/Modules/Media/QuickBuilder.Media.Contracts/*.csproj          src/Modules/Media/QuickBuilder.Media.Contracts/
COPY src/Modules/Media/QuickBuilder.Media/*.csproj                    src/Modules/Media/QuickBuilder.Media/
RUN dotnet restore src/QuickBuilder.Host/QuickBuilder.Host.csproj

# The .NET folders only. src/admin/ is built by the Node stage above; copying it here
# would make every admin-only change invalidate this layer and re-run the publish.
COPY src/QuickBuilder.Host/ src/QuickBuilder.Host/
COPY src/Shared/            src/Shared/
COPY src/Modules/           src/Modules/

# The admin build output goes into wwwroot before publish, so it ships inside the image.
COPY --from=admin /admin/dist/ src/QuickBuilder.Host/wwwroot/admin/

RUN dotnet publish src/QuickBuilder.Host/QuickBuilder.Host.csproj \
      --configuration Release \
      --no-restore \
      --output /app

# --- Stage 3: runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# FR-036: non-root. The official aspnet images already ship an `app` user, so this costs
# one line rather than creating and chowning a user by hand.
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    # Logs belong on stdout, where the orchestrator collects them.
    Logging__Console__FormatterName=json

EXPOSE 8080

# FR-038: no secrets in the image. The connection string arrives at runtime through
# ConnectionStrings__Default and has no default.
COPY --from=build --chown=app:app /app ./

# Readiness is the meaningful probe: it is only healthy once configuration is valid,
# migrations are complete and the database is reachable (FR-027).
HEALTHCHECK --interval=10s --timeout=3s --start-period=40s --retries=5 \
  CMD wget --quiet --spider http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "QuickBuilder.Host.dll"]
