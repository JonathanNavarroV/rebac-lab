# Contexto de build: ./backend (ver docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY shared/ shared/
# El negocio necesita access-control únicamente para poder compilar el adaptador
# InProcess. Ver el comentario de cabecera de Playground.Business.Infrastructure.csproj.
COPY access-control/ access-control/
COPY business/ business/

RUN dotnet publish business/presentation/Playground.Business.Api.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Playground.Business.Api.dll"]
