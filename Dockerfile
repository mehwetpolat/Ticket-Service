FROM node:22-bookworm-slim AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY TickeTask.sln ./
COPY src/TickeTask.Domain/TickeTask.Domain.csproj src/TickeTask.Domain/
COPY src/TickeTask.Application/TickeTask.Application.csproj src/TickeTask.Application/
COPY src/TickeTask.Infrastructure/TickeTask.Infrastructure.csproj src/TickeTask.Infrastructure/
COPY src/TickeTask.Web/TickeTask.Web.csproj src/TickeTask.Web/
RUN dotnet restore src/TickeTask.Web/TickeTask.Web.csproj
COPY src/ src/
COPY --from=frontend /src/src/TickeTask.Web/wwwroot ./src/TickeTask.Web/wwwroot
RUN dotnet publish src/TickeTask.Web/TickeTask.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8787 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    OLLAMA_URL=http://ollama:11434 \
    MODEL_CONFIG_DIR=/ollama-home \
    MODEL_PRIMARY_DIR=/model-roots/primary \
    MODEL_SECONDARY_DIR=/model-roots/secondary
COPY --from=build /app/publish ./
RUN mkdir -p /app/data /app/.ticketask-runtime /ollama-home /model-roots/primary /model-roots/secondary
EXPOSE 8787
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=10 \
    CMD curl -fsS http://127.0.0.1:8787/api/health || exit 1
ENTRYPOINT ["dotnet", "TickeTask.Web.dll"]
