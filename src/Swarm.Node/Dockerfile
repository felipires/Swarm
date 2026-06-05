FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/Swarm.Node/Swarm.Node.csproj", "src/Swarm.Node/"]
COPY ["src/Swarm.Node.Sdk/Swarm.Node.Sdk.csproj", "src/Swarm.Node.Sdk/"]
COPY ["Directory.Packages.props", "."]

# Restore dependencies
WORKDIR /src/src/Swarm.Node
RUN dotnet restore "Swarm.Node.csproj"

# Copy source code
COPY . /src

# Build
RUN dotnet build "Swarm.Node.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Swarm.Node.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=build /src/src/Swarm.Node/Data/Migrations ./Data/Migrations

ENTRYPOINT ["dotnet", "Swarm.Node.dll"]
