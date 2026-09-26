# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
WORKDIR /src

COPY ["global.json", "NuGet.config", "Directory.Build.props", "Directory.Packages.props", "stylecop.json", "rules.ruleset", "./"]
COPY ["RagAgent.Api/RagAgent.Api.csproj", "RagAgent.Api/"]
COPY ["RagAgent.Agents/RagAgent.Agents.csproj", "RagAgent.Agents/"]
COPY ["RagAgent.Core/RagAgent.Core.csproj", "RagAgent.Core/"]
COPY ["RagAgent.HackerNews/RagAgent.HackerNews.csproj", "RagAgent.HackerNews/"]
COPY ["RagAgent.InMemory/RagAgent.InMemory.csproj", "RagAgent.InMemory/"]
COPY ["RagAgent.Qdrant/RagAgent.Qdrant.csproj", "RagAgent.Qdrant/"]
COPY ["RagAgent.Redis/RagAgent.Redis.csproj", "RagAgent.Redis/"]
COPY ["RagAgent.S3Vectors/RagAgent.S3Vectors.csproj", "RagAgent.S3Vectors/"]
RUN dotnet restore "RagAgent.Api/RagAgent.Api.csproj" -a "$TARGETARCH"

COPY . .

FROM build AS publish
RUN dotnet publish "RagAgent.Api/RagAgent.Api.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --arch "$TARGETARCH" \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "RagAgent.Api.dll"]