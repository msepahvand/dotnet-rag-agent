FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
ARG APP_VERSION=local
WORKDIR /src

COPY ["global.json", "NuGet.config", "Directory.Build.props", "Directory.Packages.props", "stylecop.json", "rules.ruleset", "./"]
COPY ["src/RagAgent.AgentHost/RagAgent.AgentHost.csproj", "src/RagAgent.AgentHost/"]
COPY ["src/RagAgent.Agents/RagAgent.Agents.csproj", "src/RagAgent.Agents/"]
COPY ["src/RagAgent.Core/RagAgent.Core.csproj", "src/RagAgent.Core/"]
COPY ["src/RagAgent.HackerNews/RagAgent.HackerNews.csproj", "src/RagAgent.HackerNews/"]
COPY ["src/RagAgent.Qdrant/RagAgent.Qdrant.csproj", "src/RagAgent.Qdrant/"]
COPY ["src/RagAgent.Redis/RagAgent.Redis.csproj", "src/RagAgent.Redis/"]
COPY ["src/RagAgent.S3Vectors/RagAgent.S3Vectors.csproj", "src/RagAgent.S3Vectors/"]
RUN dotnet restore "src/RagAgent.AgentHost/RagAgent.AgentHost.csproj" -a "$TARGETARCH"

COPY . .

FROM build AS publish
RUN dotnet publish "src/RagAgent.AgentHost/RagAgent.AgentHost.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --arch "$TARGETARCH" \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG APP_VERSION=local
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 8080
ENV APP_VERSION=$APP_VERSION
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "RagAgent.AgentHost.dll"]
