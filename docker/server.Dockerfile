# The dedicated server. Build from the repository root:
#
#   docker build -f docker/server.Dockerfile -t infiniminer-server .
#   docker run -it -p 5565:5565/udp -v infiniminer:/data infiniminer-server
#
# /data holds server.config.txt, banlist.txt and saved maps; the defaults are copied in on first start.
# -it gives you the server console (detach with Ctrl+P Ctrl+Q, reattach with `docker attach`).
# LAN discovery doesn't cross Docker's network; use --network host if LAN players need to find it.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY bin/ bin/
COPY src/Directory.Build.props src/
COPY src/Infiniminer.Shared/ src/Infiniminer.Shared/
COPY src/Infiniminer.Server/ src/Infiniminer.Server/
RUN dotnet publish src/Infiniminer.Server -c Release -p:UseAppHost=false -p:DebugType=none -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
COPY --from=build /app /app
RUN mkdir /data && chown app:app /data
USER app
WORKDIR /data
VOLUME /data
EXPOSE 5565/udp
ENTRYPOINT ["/bin/sh", "-c", "for f in server.config.txt banlist.txt; do [ -e \"$f\" ] || cp \"/app/$f\" .; done; exec dotnet /app/InfiniminerServer.dll"]
