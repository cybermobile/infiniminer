using Xunit;

namespace Infiniminer.Tests
{
    [Collection("World generation")]
    public class WorldSaveTests
    {
        [Fact]
        public void AWorldSurvivesBeingSavedAndLoaded()
        {
            string path = Path.Combine(Path.GetTempPath(), "infiniminer-world-" + Guid.NewGuid().ToString("N") + ".sav");
            try
            {
                WorldState world = new WorldState
                {
                    MapSize = 64,
                    Blocks = new BlockType[64, 64, 64],
                    Builders = new PlayerTeam[64, 64, 64],
                    CashRed = 4200, CashBlue = 1300, OreRed = 150, OreBlue = 75, SecondsLeft = 321, Overtime = false,
                };
                Random random = new Random(3);
                for (int i = 0; i < 5000; i++)
                    world.Blocks[random.Next(64), random.Next(64), random.Next(64)] = (BlockType)random.Next((int)BlockType.MAXIMUM);
                world.Builders[10, 20, 30] = PlayerTeam.Blue;

                WorldSave.Save(path, world);
                Assert.True(new FileInfo(path).Length < 64 * 64 * 64, "the save should be compressed");

                WorldState summary = WorldSave.Peek(path);
                Assert.Equal((64, 4200u, 1300u, 321), (summary.MapSize, summary.CashRed, summary.CashBlue, summary.SecondsLeft));
                Assert.Null(summary.Blocks);

                WorldState loaded = WorldSave.Load(path);
                Assert.Equal(world.Blocks, loaded.Blocks);
                Assert.Equal(world.Builders, loaded.Builders);
                Assert.Equal((150u, 75u, false), (loaded.OreRed, loaded.OreBlue, loaded.Overtime));

                Assert.Null(WorldSave.Peek(path + ".missing"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void APersistentServerCarriesOnUntilTheMatchEnds()
        {
            string dataDirectory = Path.Combine(Path.GetTempPath(), "infiniminer-persist-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            InfiniminerServer Start()
            {
                InfiniminerServer server = new InfiniminerServer(new ServerConfig { Port = 0, Persist = true, TimeLimitMinutes = 30 }, dataDirectory);
                server.Log = _ => { };
                Assert.True(server.Initialize());
                return server;
            }
            string save = Path.Combine(dataDirectory, InfiniminerServer.WorldFile);
            try
            {
                // Someone builds something; the server is stopped.
                CaveGenerator.SetSeed(5);
                InfiniminerServer first = Start();
                first.SetBlock(30, 50, 30, BlockType.SolidRed, PlayerTeam.Red);
                first.Shutdown("Stopping.");
                Assert.True(File.Exists(save));

                // Started again, it is the same world, with the clock where it was.
                CaveGenerator.SetSeed(6);
                InfiniminerServer second = Start();
                Assert.Equal(BlockType.SolidRed, second.GetBlock(30, 50, 30));
                Assert.InRange(second.CaptureWorld().SecondsLeft, 29 * 60, 30 * 60);

                // When the match is over (here, a restart), the next start makes a new world.
                second.EnqueueCommand("restart");
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while (second.Tick() && DateTime.UtcNow < deadline)
                    Thread.Sleep(5);
                Assert.False(File.Exists(save));
            }
            finally
            {
                Directory.Delete(dataDirectory, true);
            }
        }
    }
}
