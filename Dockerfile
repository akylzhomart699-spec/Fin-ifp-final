# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/FinTrack.Web/FinTrack.Web.csproj src/FinTrack.Web/
RUN dotnet restore src/FinTrack.Web/FinTrack.Web.csproj
COPY src/FinTrack.Web/ src/FinTrack.Web/
RUN dotnet publish src/FinTrack.Web/FinTrack.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
USER root
# libgomp1 is required by ML.NET native libraries on Linux
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgomp1 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

# Render routes traffic to port 10000; the database lives in a temp folder (demo data returns after a restart)
ENV ASPNETCORE_URLS=http://+:10000 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Default="Data Source=/tmp/fintrack.db"
EXPOSE 10000

ENTRYPOINT ["dotnet", "FinTrack.Web.dll"]
