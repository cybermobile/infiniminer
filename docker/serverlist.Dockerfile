# The public server list. Build from the repository root:
#
#   docker build -f docker/serverlist.Dockerfile -t infiniminer-serverlist .
#   docker run -p 8080:8080 infiniminer-serverlist
#
# Behind a reverse proxy, add -e TRUST_FORWARDED_HEADERS=true so game servers are listed by their
# real address rather than the proxy's.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY bin/ bin/
COPY src/Directory.Build.props src/
COPY src/Infiniminer.Shared/ src/Infiniminer.Shared/
COPY src/Infiniminer.ServerList/ src/Infiniminer.ServerList/
RUN dotnet publish src/Infiniminer.ServerList -c Release -p:UseAppHost=false -p:DebugType=none -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
COPY --from=build /app /app
USER app
WORKDIR /app
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/InfiniminerServerList.dll"]
