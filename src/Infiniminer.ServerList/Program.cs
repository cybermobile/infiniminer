using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Infiniminer.ServerList
{
    // A public server list for Infiniminer, replacing the one at apps.keithholman.net that the original
    // game used. Game servers POST to /post every few minutes; clients GET /plain. / shows a web page.
    //
    //   dotnet run --project src/Infiniminer.ServerList -- --urls http://0.0.0.0:8080
    //
    // Set TRUST_FORWARDED_HEADERS=true when running behind a reverse proxy, so the real address of each
    // game server is listed rather than the proxy's.
    public static class ServerListApp
    {
        public static WebApplication Build(string[] args, ServerRegistry registry)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            if (builder.Configuration["urls"] == null && Environment.GetEnvironmentVariable("ASPNETCORE_URLS") == null)
                builder.WebHost.UseUrls("http://0.0.0.0:8080");
            WebApplication app = builder.Build();

            if (string.Equals(Environment.GetEnvironmentVariable("TRUST_FORWARDED_HEADERS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                ForwardedHeadersOptions forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor };
                forwarded.KnownNetworks.Clear();
                forwarded.KnownProxies.Clear();
                app.UseForwardedHeaders(forwarded);
            }

            app.MapPost("/post", async (HttpContext context) =>
            {
                if (!context.Request.HasFormContentType)
                    return Results.BadRequest("expected a form post");
                IFormCollection form = await context.Request.ReadFormAsync();
                Dictionary<string, string> fields = form.ToDictionary(f => f.Key, f => f.Value.ToString());
                string error = registry.Announce(AddressOf(context), fields, DateTime.UtcNow);
                return error == null ? Results.Text("OK") : Results.BadRequest(error);
            });

            app.MapGet("/plain", () => Results.Text(ServerRegistry.ToPlainText(registry.List(DateTime.UtcNow)), "text/plain"));

            app.MapGet("/", () =>
            {
                List<ServerListing> servers = registry.List(DateTime.UtcNow);
                string rows = string.Concat(servers.Select(s =>
                    $"<tr><td>{WebUtility.HtmlEncode(s.Name)}</td><td>{s.PlayerCount} / {s.MaxPlayers}</td>" +
                    $"<td>{WebUtility.HtmlEncode(s.Extra)}</td><td><code>{WebUtility.HtmlEncode(s.Address)}:{s.Port}</code></td></tr>"));
                string body = servers.Count == 0 ? "<p>No servers are online right now.</p>" :
                    $"<table><tr><th>Server</th><th>Players</th><th>Settings</th><th>Address</th></tr>{rows}</table>";
                return Results.Content(
                    "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\">" +
                    "<title>Infiniminer servers</title><style>body{font-family:monospace;background:#111;color:#ddd;margin:2em}" +
                    "table{border-collapse:collapse}td,th{padding:.3em 1em;text-align:left;border-bottom:1px solid #333}" +
                    "th{color:#5096ff}</style></head><body><h1>Infiniminer servers</h1>" + body + "</body></html>", "text/html");
            });

            return app;
        }

        static string AddressOf(HttpContext context)
        {
            IPAddress address = context.Connection.RemoteIpAddress ?? IPAddress.None;
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            return address.ToString();
        }

        public static void Main(string[] args)
        {
            Build(args, new ServerRegistry()).Run();
        }
    }
}
