using System.Net;
using Xunit;

namespace Infiniminer.Tests
{
    public class ServerBrowserTests
    {
        static ServerInformation Answered(string name, int players, int max, int bots = 0, int ping = 10, bool lan = true,
            string version = GameConstants.Version, bool sandbox = false, GameMode mode = GameMode.Cash, int port = 5565)
        {
            DiscoveryInfo info = new DiscoveryInfo
            {
                InstanceId = name, ServerName = name, PlayerCount = players, MaxPlayers = max, Extra = "",
                HasDetails = true, Version = version, MapSize = 64, Mode = mode, Sandbox = sandbox, Bots = bots, WinningCash = 10000,
            };
            return new ServerInformation(new IPEndPoint(IPAddress.Loopback, port), info) { PingMs = ping, lanServer = lan };
        }

        [Fact]
        public void ServersAreDescribedForTheTable()
        {
            ServerInformation server = Answered("Diggers", 6, 16, bots: 4, ping: 42, mode: GameMode.Hill);
            Assert.Equal("DIGGERS", server.Name);
            Assert.Equal("KING OF THE HILL", server.ModeLabel);
            Assert.Equal("64X64", server.WorldLabel);
            Assert.Equal("2/16", server.PlayersLabel);     // people; the bots make way for them
            Assert.Equal("4", server.BotsLabel);
            Assert.Equal("42", server.PingLabel);
            Assert.Equal("KING OF THE HILL TO $10,000  -  LAVA  -  64X64 WORLD  -  2 PEOPLE  -  4 COMPUTER PLAYERS  -  "
                + GameConstants.Version.ToUpperInvariant(), server.DetailLine);
            Assert.False(server.Full);
            Assert.False(server.WrongVersion);
        }

        [Fact]
        public void PublicListEntriesAreDescribedFromTheirSummary()
        {
            // Not (yet) answered: only the public list's summary is known.
            ServerInformation listed = new ServerInformation(new IPEndPoint(IPAddress.Loopback, 5565), "Old", "10k, hill, !lava, 3 bots", "5", "8");
            Assert.Equal("KING OF THE HILL", listed.ModeLabel);
            Assert.Equal(3, listed.Bots);
            Assert.Equal("2/8", listed.PlayersLabel);
            Assert.Equal("-", listed.WorldLabel);
            Assert.Equal("-", listed.PingLabel);
            Assert.Contains("NO LAVA", listed.DetailLine);
            Assert.Equal("SANDBOX", new ServerInformation(new IPEndPoint(IPAddress.Loopback, 5565), "S", "sandbox", "0", "8").ModeLabel);
        }

        [Fact]
        public void FullAndOtherVersionServersAreMarked()
        {
            Assert.True(Answered("Full", 16, 16).Full);
            Assert.False(Answered("Bots only", 16, 16, bots: 10).Full);   // bots go home for a person
            ServerInformation old = Answered("Old", 1, 16, version: "v1.8");
            Assert.True(old.WrongVersion);
            Assert.False(Answered("Unknown", 1, 16, version: "").WrongVersion);
        }

        [Fact]
        public void QuickPlayPicksTheBestOpenGame()
        {
            ServerInformation empty = Answered("Empty", 0, 16, ping: 1);
            ServerInformation busyFar = Answered("Busy far", 5, 16, ping: 150, lan: false);
            ServerInformation busyNear = Answered("Busy near", 3, 16, ping: 20, port: 5566);
            ServerInformation full = Answered("Full", 16, 16, ping: 1, port: 5567);
            ServerInformation old = Answered("Old", 4, 16, ping: 1, version: "v1.8", port: 5568);
            ServerInformation sandbox = Answered("Sandbox", 4, 16, ping: 1, sandbox: true, port: 5569);
            List<ServerInformation> all = new List<ServerInformation> { empty, busyFar, busyNear, full, old, sandbox };

            // Games with people first, LAN before internet.
            Assert.Same(busyNear, ServerInformation.PickForQuickPlay(all, _ => false));
            // Your own hosted game comes first.
            Assert.Same(empty, ServerInformation.PickForQuickPlay(all, s => s == empty));
            // Nothing joinable: none (QUICK PLAY then starts a game).
            Assert.Null(ServerInformation.PickForQuickPlay(new[] { full, old, sandbox }, _ => false));
        }
    }
}
