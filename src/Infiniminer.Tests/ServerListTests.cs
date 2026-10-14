using System.Net;
using Infiniminer.ServerList;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Infiniminer.Tests
{
    public class ServerRegistryTests
    {
        static readonly DateTime T0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        static Dictionary<string, string> Announcement(string name = "My Server", string port = null)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>
            {
                ["name"] = name,
                ["game"] = "INFINIMINER",
                ["player_count"] = "3",
                ["player_capacity"] = "16",
                ["extra"] = "10k, !lava",
            };
            if (port != null)
                fields["port"] = port;
            return fields;
        }

        [Fact]
        public void ListsAnnouncedServersInTheOriginalFormat()
        {
            ServerRegistry registry = new ServerRegistry();
            Assert.Null(registry.Announce("203.0.113.5", Announcement(), T0));
            Assert.Null(registry.Announce("203.0.113.6", Announcement("Other", "7000"), T0));

            string text = ServerRegistry.ToPlainText(registry.List(T0));
            Assert.Contains("My Server;203.0.113.5;INFINIMINER;3;16;10k, !lava;5565\r\n", text);
            Assert.Contains("Other;203.0.113.6;INFINIMINER;3;16;10k, !lava;7000\r\n", text);
        }

        [Fact]
        public void TheClientParsesTheListIncludingPorts()
        {
            string text = "My Server;203.0.113.5;INFINIMINER;3;16;10k;7000\r\nOld Style;203.0.113.6;INFINIMINER;0;8;\r\nnot a server\r\n";
            List<ServerInformation> servers = InfiniminerGame.ParsePublicServerList(text);
            Assert.Equal(2, servers.Count);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.5"), 7000), servers[0].ipEndPoint);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.6"), 5565), servers[1].ipEndPoint);
            Assert.Equal("My Server ( 3 / 16 ) - 10k", servers[0].GetServerDesc());
        }

        [Fact]
        public void ReannouncingUpdatesTheSameEntry()
        {
            ServerRegistry registry = new ServerRegistry();
            registry.Announce("203.0.113.5", Announcement(), T0);
            Dictionary<string, string> update = Announcement();
            update["player_count"] = "9";
            registry.Announce("203.0.113.5", update, T0.AddMinutes(5));
            ServerListing listing = Assert.Single(registry.List(T0.AddMinutes(5)));
            Assert.Equal(9, listing.PlayerCount);
        }

        [Fact]
        public void ServersThatStopAnnouncingExpire()
        {
            ServerRegistry registry = new ServerRegistry();
            registry.Announce("203.0.113.5", Announcement(), T0);
            Assert.Single(registry.List(T0.AddMinutes(11)));
            Assert.Empty(registry.List(T0.AddMinutes(13)));
        }

        [Fact]
        public void BadAnnouncementsAreRejected()
        {
            ServerRegistry registry = new ServerRegistry();
            Dictionary<string, string> otherGame = Announcement();
            otherGame["game"] = "MINECRAFT";
            Assert.NotNull(registry.Announce("203.0.113.5", otherGame, T0));
            Assert.NotNull(registry.Announce("203.0.113.5", Announcement(name: ";;"), T0));
            Assert.NotNull(registry.Announce("203.0.113.5", Announcement(port: "99999"), T0));
            Assert.Empty(registry.List(T0));
        }

        [Fact]
        public void SeparatorsAreStrippedFromNames()
        {
            ServerRegistry registry = new ServerRegistry();
            registry.Announce("203.0.113.5", Announcement(name: "Evil;1.2.3.4;INFINIMINER\r\nFake"), T0);
            Assert.Equal("Evil1.2.3.4INFINIMINERFake", Assert.Single(registry.List(T0)).Name);
        }

        [Fact]
        public void EachAddressCanOnlyListAFewServers()
        {
            ServerRegistry registry = new ServerRegistry { MaxPerAddress = 2 };
            Assert.Null(registry.Announce("203.0.113.5", Announcement(port: "1"), T0));
            Assert.Null(registry.Announce("203.0.113.5", Announcement(port: "2"), T0));
            Assert.NotNull(registry.Announce("203.0.113.5", Announcement(port: "3"), T0));
            Assert.Null(registry.Announce("203.0.113.6", Announcement(port: "3"), T0));
        }
    }

    // A real game server announcing itself to a real list service, and the client's code finding it.
    [Collection("World generation")]
    public class ServerListEndToEndTests
    {
        [Fact]
        public async Task GameServersAppearInTheList()
        {
            ServerRegistry registry = new ServerRegistry();
            await using WebApplication listApp = ServerListApp.Build(new[] { "--urls", "http://127.0.0.1:0" }, registry);
            await listApp.StartAsync();
            string listUrl = listApp.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();

            string dataDirectory = Path.Combine(Path.GetTempPath(), "infiniminer-list-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            ServerConfig config = new ServerConfig { Port = 0, PublicServer = true, ServerListUrl = listUrl, ServerName = "Listed Server" };
            InfiniminerServer gameServer = new InfiniminerServer(config, dataDirectory);
            gameServer.Log = _ => { };
            try
            {
                Assert.True(gameServer.Initialize()); // announces itself
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (registry.List(DateTime.UtcNow).Count == 0 && DateTime.UtcNow < deadline)
                    await Task.Delay(50);

                List<ServerInformation> servers = await InfiniminerGame.FetchPublicServerList(listUrl);
                ServerInformation listed = Assert.Single(servers);
                Assert.Equal("Listed Server", listed.serverName);
                Assert.Equal(gameServer.Port, listed.ipEndPoint.Port);
                Assert.Equal(IPAddress.Loopback, listed.ipEndPoint.Address);

                using HttpClient http = new HttpClient();
                string page = await http.GetStringAsync(listUrl);
                Assert.Contains("Listed Server", page);
            }
            finally
            {
                gameServer.Shutdown("done");
                await listApp.StopAsync();
                Directory.Delete(dataDirectory, true);
            }
        }
    }
}
