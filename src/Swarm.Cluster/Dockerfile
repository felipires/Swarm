FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/Swarm.Cluster/Swarm.Cluster.csproj", "src/Swarm.Cluster/"]
COPY ["src/Swarm.Node.Sdk/Swarm.Node.Sdk.csproj", "src/Swarm.Node.Sdk/"]
COPY ["Directory.Packages.props", "."]

# Restore dependencies
WORKDIR /src/src/Swarm.Cluster
RUN dotnet restore "Swarm.Cluster.csproj"

# Copy source code
COPY . /src

# Build
RUN dotnet build "Swarm.Cluster.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Swarm.Cluster.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 5000
ENTRYPOINT ["dotnet", "Swarm.Cluster.dll"]
