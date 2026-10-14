using Xunit;
using Xunit.Abstractions;

namespace Infiniminer.Tests
{
    [Collection("World generation")]
    public class BotTests
    {
        readonly ITestOutputHelper output;

        public BotTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        static InfiniminerServer StartServer(ServerConfig config, out string dataDirectory)
        {
            dataDirectory = Path.Combine(Path.GetTempPath(), "infiniminer-bots-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            CaveGenerator.SetSeed(7);
            config.Port = 0;
            InfiniminerServer server = new InfiniminerServer(config, dataDirectory);
            server.Log = _ => { };
            Assert.True(server.Initialize());
            return server;
        }

        [Fact]
        public void BotsMineAndBringLootHome()
        {
            InfiniminerServer server = StartServer(new ServerConfig { Bots = 4 }, out string dataDirectory);
            try
            {
                Assert.Equal(4, server.Bots.Count);
                Assert.Equal(2, server.Bots.Count(b => b.Player.Team == PlayerTeam.Red));
                Assert.Equal(new[] { PlayerClass.Miner, PlayerClass.Prospector, PlayerClass.Engineer, PlayerClass.Sapper },
                    server.Bots.Select(b => b.Player.Class));

                // Eight minutes of play, simulated.
                int surface = GameConstants.MapSize - GameConstants.GroundLevel - 1;
                for (int step = 0; step < 8 * 60 * 20; step++)
                {
                    server.UpdateBots(0.05f);
                    server.DepositForPlayers();
                }

                foreach (ServerBot bot in server.Bots)
                    output.WriteLine($"{bot.Player.Handle} ({bot.Player.Class}): dug {bot.BlocksDug}, deepest {surface - bot.DeepestY} below, "
                        + $"{bot.TripsHome} trips home, score {bot.Player.Score}, ladders {bot.LaddersBuilt}, banks {bot.BanksBuilt}, "
                        + $"ore deposited {bot.OreDeposited}, blasts {bot.Blasts}, lava escapes {bot.LavaEscapes}");
                Assert.All(server.Bots, b => Assert.True(b.BlocksDug > 30, b.Player.Handle + " barely dug"));
                Assert.All(server.Bots, b => Assert.True(b.DeepestY < surface - 4, b.Player.Handle + " stayed near the surface"));
                Assert.True(server.Bots.Sum(b => b.TripsHome) >= 4, "bots should come back up to bank their loot");
                Assert.True(server.Bots.Sum(b => (long)b.Player.Score) > 0, "some loot should have been banked");
                // (What each class does is checked on small worlds in BotBehaviourTests; play here varies run to run.)
            }
            finally
            {
                server.Shutdown("Test finished.");
                Directory.Delete(dataDirectory, true);
            }
        }

        [Fact]
        public void BotsMakeWayForPeopleAndKeepTeamsEven()
        {
            InfiniminerServer server = StartServer(new ServerConfig { Bots = 2, MaxPlayers = 2 }, out string dataDirectory);
            try
            {
                // The server is "full" of bots, but a person can still join: a bot goes home.
                using ServerIntegrationTests.Bot person = new ServerIntegrationTests.Bot();
                person.Connect(server.Port);
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (person.MyId == 0 && DateTime.UtcNow < deadline)
                {
                    server.Tick();
                    person.Net.PollEvents();
                    Thread.Sleep(2);
                }
                Assert.NotEqual(0u, person.MyId);
                Assert.Single(server.Bots);

                // The person joins the bot's team: the bot switches sides.
                PlayerTeam botTeam = server.Bots[0].Player.Team;
                person.Send(ClientMessages.SetTeam(botTeam));
                deadline = DateTime.UtcNow.AddSeconds(15);
                while (server.Bots[0].Player.Team == botTeam && DateTime.UtcNow < deadline)
                {
                    server.Tick();
                    person.Net.PollEvents();
                    Thread.Sleep(2);
                }
                Assert.NotEqual(botTeam, server.Bots[0].Player.Team);

                server.SetBotCount(0);
                Assert.Empty(server.Bots);
            }
            finally
            {
                server.Shutdown("Test finished.");
                Directory.Delete(dataDirectory, true);
            }
        }
    }
}
