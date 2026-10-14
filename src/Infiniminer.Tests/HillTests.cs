using System.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace Infiniminer.Tests
{
    // King of the hill (gamemode = hill).
    [Collection("World generation")]
    public class HillTests
    {
        readonly ITestOutputHelper output;

        public HillTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        static InfiniminerServer StartServer(ServerConfig config, out string dataDirectory)
        {
            dataDirectory = Path.Combine(Path.GetTempPath(), "infiniminer-hill-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            CaveGenerator.SetSeed(11);
            config.Port = 0;
            config.Mode = GameMode.Hill;
            InfiniminerServer server = new InfiniminerServer(config, dataDirectory);
            server.Log = _ => { };
            Assert.True(server.Initialize());
            return server;
        }

        static void StandOnHill(InfiniminerServer server, ServerPlayer player, int dx = 0)
        {
            var (x, y, z) = server.Hill.Value;
            player.Position = new Vector3(x + dx + 0.5f, y + 1 + 1.45f, z + 0.5f);
        }

        [Fact]
        public void TheTeamWithMorePlayersOnTheHillEarnsMoney()
        {
            InfiniminerServer server = StartServer(new ServerConfig { Bots = 2 }, out string dataDirectory);
            try
            {
                // A 7 x 7 metal platform in the middle of the map, with room to stand on it.
                Assert.NotNull(server.Hill);
                var (hx, hy, hz) = server.Hill.Value;
                Assert.Equal((32, 32), (hx, hz));
                for (int dx = -InfiniminerServer.HillRadius; dx <= InfiniminerServer.HillRadius; dx++)
                    for (int dz = -InfiniminerServer.HillRadius; dz <= InfiniminerServer.HillRadius; dz++)
                    {
                        Assert.Equal(BlockType.Metal, server.GetBlock(hx + dx, hy, hz + dz));
                        Assert.Equal(BlockType.None, server.GetBlock(hx + dx, hy + 1, hz + dz));
                        Assert.Equal(BlockType.None, server.GetBlock(hx + dx, hy + 2, hz + dz));
                    }

                ServerPlayer red = server.Bots.Single(b => b.Player.Team == PlayerTeam.Red).Player;
                ServerPlayer blue = server.Bots.Single(b => b.Player.Team == PlayerTeam.Blue).Player;
                red.Alive = blue.Alive = true;      // (bots come to life on their first update)
                red.Position = blue.Position = new Vector3(5.5f, 60, 5.5f);
                server.UpdateHill(1);
                Assert.Equal(0u, server.TeamCash(PlayerTeam.Red) + server.TeamCash(PlayerTeam.Blue));

                // Red alone on the hill: $25 a second.
                StandOnHill(server, red);
                Assert.True(server.OnHill(red));
                server.UpdateHill(1);
                server.UpdateHill(1);
                Assert.Equal(2 * InfiniminerServer.HillCashPerSecond, server.TeamCash(PlayerTeam.Red));

                // One each: contested, nobody earns.
                StandOnHill(server, blue, dx: 2);
                server.UpdateHill(1);
                Assert.Equal(2 * InfiniminerServer.HillCashPerSecond, server.TeamCash(PlayerTeam.Red));
                Assert.Equal(0u, server.TeamCash(PlayerTeam.Blue));

                // Dead players don't hold anything.
                red.Alive = false;
                server.UpdateHill(1);
                Assert.Equal(InfiniminerServer.HillCashPerSecond, server.TeamCash(PlayerTeam.Blue));

                // Off the edge of the platform doesn't count.
                StandOnHill(server, blue, dx: InfiniminerServer.HillRadius + 1);
                Assert.False(server.OnHill(blue));
            }
            finally
            {
                server.Shutdown("Test finished.");
                Directory.Delete(dataDirectory, true);
            }
        }

        [Fact]
        public void BotsGoAndHoldTheHill()
        {
            InfiniminerServer server = StartServer(new ServerConfig { Bots = 4, IncludeLava = false }, out string dataDirectory);
            try
            {
                // Let them spawn (and land on the surface), then send them all to the hill.
                for (int step = 0; step < 4 * 20; step++)
                    server.UpdateBots(0.05f);
                Assert.All(server.Bots, b => Assert.True(b.Player.Alive));
                foreach (ServerBot bot in server.Bots)
                    bot.GoToHill();

                // Six minutes of play: holding the hill, mining, and going back to it now and then. (A bot that can't
                // find a way up gives up after a while and tries again later, so not every trip succeeds.)
                for (int step = 0; step < 6 * 60 * 20; step++)
                {
                    server.UpdateBots(0.05f);
                    server.UpdateHill(0.05f);
                    server.DepositForPlayers();
                }
                foreach (ServerBot bot in server.Bots)
                    output.WriteLine($"{bot.Player.Handle} ({bot.Player.Team}): {bot.SecondsOnHill:0}s on the hill; {bot.Describe()}");
                // (Over 20 runs: 2 to 4 bots got there, 63 to 219 seconds on the hill between them.)
                Assert.True(server.Bots.Sum(b => b.SecondsOnHill) >= 40, "the bots should spend a good while on the hill");
                Assert.True(server.TeamCash(PlayerTeam.Red) + server.TeamCash(PlayerTeam.Blue) > 0, "holding the hill should have earned something");
            }
            finally
            {
                server.Shutdown("Test finished.");
                Directory.Delete(dataDirectory, true);
            }
        }

        [Fact]
        public void TheModeIsReadFromTheConfigFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "infiniminer-mode-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(path, "gamemode = hill\n");
                Assert.Equal(GameMode.Hill, ServerConfig.Load(path).Mode);
                File.WriteAllText(path, "gamemode = nonsense\n");
                Assert.Equal(GameMode.Cash, ServerConfig.Load(path).Mode);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
