FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Swarm.Node.csproj", "."]
RUN dotnet restore "Swarm.Node.csproj"
COPY . .
RUN dotnet build "Swarm.Node.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Swarm.Node.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Swarm.Node.dll"]
