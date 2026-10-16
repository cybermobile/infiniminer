using System.Numerics;
using Xunit;

namespace Infiniminer.Tests
{
    // With home bases, loot is banked in your own base, and the other team can't build in or over it.
    [Collection("World generation")]
    public class HomeBaseTests
    {
        static InfiniminerServer StartServer(ServerConfig config, out string dataDirectory)
        {
            dataDirectory = Path.Combine(Path.GetTempPath(), "infiniminer-home-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            if (config.Seed == 0)
                config.Seed = 11;
            config.Port = 0;
            if (config.Bots == 0)
                config.Bots = 2;    // a red miner and a blue prospector, standing still (unless the test updates them)
            InfiniminerServer server = new InfiniminerServer(config, dataDirectory);
            server.Log = _ => { };
            Assert.True(server.Initialize());
            return server;
        }

        [Fact]
        public void LootIsBankedInYourOwnBase()
        {
            InfiniminerServer server = StartServer(new ServerConfig { HomeBases = true }, out string dataDirectory);
            try
            {
                ServerPlayer red = server.Bots.Single(b => b.Player.Team == PlayerTeam.Red).Player;
                red.Alive = true;
                var home = server.HomeBase(PlayerTeam.Red).Value;
                int surface = server.MapSize - GameConstants.GroundLevel;

                // On the surface, away from the base: kept.
                red.Cash = 300;
                red.Position = new Vector3(32.5f, surface + 2, 32.5f);
                server.DepositForPlayers();
                Assert.Equal(300u, red.Cash);

                // In the other team's base: kept.
                var blueHome = server.HomeBase(PlayerTeam.Blue).Value;
                red.Position = new Vector3(blueHome.x + 1.5f, blueHome.y + 1.45f, blueHome.z + 0.5f);
                server.DepositForPlayers();
                Assert.Equal(300u, red.Cash);

                // In its own base: banked.
                red.Position = new Vector3(home.x + 1.5f, home.y + 1.45f, home.z + 0.5f);
                server.DepositForPlayers();
                Assert.Equal(0u, red.Cash);
                Assert.Equal(300u, red.Score);
            }
            finally
            {
                server.Shutdown("Test finished.");
                Directory.Delete(dataDirectory, true);
            }
        }

        [Fact]
        public void TheOtherTeamCannotBuildInABase()
        {
            InfiniminerServer server = StartServer(new ServerConfig { HomeBases = true }, out string dataDirectory);
            try
            {
                ServerPlayer red = server.Bots.Single(b => b.Player.Team == PlayerTeam.Red).Player;
                red.Alive = true;
                ServerPlayer blue = server.Bots.Single(b => b.Player.Team == PlayerTeam.Blue).Player;
                red.Ore = blue.Ore = 200;
                var home = server.HomeBase(PlayerTeam.Red).Value;
                red.Position = blue.Position = new Vector3(2.5f, server.MapSize - 1.5f, 2.5f);   // out of the way

                // In the crater, and over it.
                Assert.False(server.PlaceBlock(blue, home.x + 2, home.y + 1, home.z, BlockType.Shock));
                Assert.False(server.PlaceBlock(blue, home.x, home.y + 9, home.z + 1, BlockType.SolidBlue));
                Assert.Equal(200u, blue.Ore);
                // The team itself can.
                Assert.True(server.PlaceBlock(red, home.x + 2, home.y + 1, home.z, BlockType.SolidRed));
                // Away from the base, anyone can.
                Assert.True(server.PlaceBlock(blue, home.x - 9, server.MapSize - 2, home.z - 9, BlockType.SolidBlue));
            }
            finally
            {
                server.Shutdown("Test finished.");
                Directory.Delete(dataDirectory, true);
            }
        }

        [Fact]
        public void BotsCarryTheirLootHome()
        {
            InfiniminerServer server = StartServer(new ServerConfig { HomeBases = true, Bots = 4, Seed = 7 }, out string dataDirectory);
            try
            {
                // Eight minutes of play, simulated.
                for (int step = 0; step < 8 * 60 * 20; step++)
                {
                    server.UpdateBots(0.05f);
                    server.DepositForPlayers();
                }
                Assert.True(server.Bots.Sum(b => (long)b.Player.Score) > 0, "bots should have banked loot at home");
            }
            finally
            {
                server.Shutdown("Test finished.");
                Directory.Delete(dataDirectory, true);
            }
        }

        [Fact]
        public void WithoutHomeBasesLootIsBankedAnywhereOnTheSurface()
        {
            InfiniminerServer server = StartServer(new ServerConfig { HomeBases = false }, out string dataDirectory);
            try
            {
                ServerPlayer red = server.Bots.Single(b => b.Player.Team == PlayerTeam.Red).Player;
                red.Alive = true;
                Assert.Null(server.HomeBase(PlayerTeam.Red));
                red.Cash = 100;
                red.Position = new Vector3(32.5f, server.MapSize - GameConstants.GroundLevel + 2, 32.5f);
                server.DepositForPlayers();
                Assert.Equal(0u, red.Cash);
            }
            finally
            {
                server.Shutdown("Test finished.");
                Directory.Delete(dataDirectory, true);
            }
        }
    }
}
