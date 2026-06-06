# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ["src/Swarm.Cluster/Swarm.Cluster.csproj", "src/Swarm.Cluster/"]
COPY ["src/Swarm.Node.Sdk/Swarm.Node.Sdk.csproj", "src/Swarm.Node.Sdk/"]
COPY ["Directory.Packages.props", "."]

WORKDIR /src/src/Swarm.Cluster
RUN dotnet restore "Swarm.Cluster.csproj"

WORKDIR /src
COPY . .

WORKDIR /src/src/Swarm.Cluster
RUN dotnet publish "Swarm.Cluster.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0

RUN apt-get update && \
    apt-get install -y openssl && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .

# Generate a self-signed certificate for local testing
RUN openssl req \
    -x509 \
    -newkey rsa:2048 \
    -sha256 \
    -days 365 \
    -nodes \
    -keyout /tmp/key.pem \
    -out /tmp/cert.pem \
    -subj "/CN=localhost" && \
    openssl pkcs12 -export \
    -out /tmp/devcert.pfx \
    -inkey /tmp/key.pem \
    -in /tmp/cert.pem \
    -password pass:password

ENV ASPNETCORE_URLS=https://+:5000
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/tmp/devcert.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=password

EXPOSE 5000

ENTRYPOINT ["dotnet", "Swarm.Cluster.dll"]
