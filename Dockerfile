# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY NEURA.sln .
COPY src/Web/Web.csproj src/Web/
COPY src/Shared/Shared.csproj src/Shared/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/Modules/AgentManagement/AgentManagement.csproj src/Modules/AgentManagement/
COPY src/Modules/ProviderIntegration/ProviderIntegration.csproj src/Modules/ProviderIntegration/
COPY src/Modules/Orchestration/Orchestration.csproj src/Modules/Orchestration/
COPY src/Modules/ContextManagement/ContextManagement.csproj src/Modules/ContextManagement/
COPY src/Modules/Handoff/Handoff.csproj src/Modules/Handoff/
COPY src/Modules/Memory/Memory.csproj src/Modules/Memory/
COPY src/Modules/KnowledgeGraph/KnowledgeGraph.csproj src/Modules/KnowledgeGraph/
COPY src/Modules/Observability/Observability.csproj src/Modules/Observability/
COPY src/Modules/Execution/Execution.csproj src/Modules/Execution/
COPY tests/Neura.Tests/Neura.Tests.csproj tests/Neura.Tests/
RUN dotnet restore src/Web/Web.csproj

COPY . .
RUN dotnet publish src/Web/Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN useradd -m neura \
    && mkdir -p /app/dp-keys \
    && chown -R neura:neura /app/dp-keys
COPY --from=build /app/publish .
RUN chown -R neura:neura /app/dp-keys
USER neura
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Neura.Web.dll"]
