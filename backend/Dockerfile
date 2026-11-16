# Build once, publish two images: the API (two replicas) and the one-shot migrator.
FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props TrackOps.sln ./
COPY src/TrackOps.Domain/TrackOps.Domain.csproj src/TrackOps.Domain/
COPY src/TrackOps.Application/TrackOps.Application.csproj src/TrackOps.Application/
COPY src/TrackOps.Infrastructure/TrackOps.Infrastructure.csproj src/TrackOps.Infrastructure/
COPY src/TrackOps.Api/TrackOps.Api.csproj src/TrackOps.Api/
COPY src/TrackOps.Migrator/TrackOps.Migrator.csproj src/TrackOps.Migrator/
RUN dotnet restore TrackOps.sln
COPY src/ src/
RUN dotnet publish src/TrackOps.Api -c Release -o /out/api --no-restore \
 && dotnet publish src/TrackOps.Migrator -c Release -o /out/migrator --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15 AS migrator
WORKDIR /app
COPY --from=build /out/migrator .
USER $APP_UID
ENTRYPOINT ["dotnet", "TrackOps.Migrator.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15 AS api
WORKDIR /app
COPY --from=build /out/api .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TrackOps.Api.dll"]
