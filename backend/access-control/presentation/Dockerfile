# Contexto de build: ./backend (ver docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY shared/ shared/
COPY access-control/ access-control/

RUN dotnet publish access-control/presentation/Playground.AccessControl.Api.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Playground.AccessControl.Api.dll"]
