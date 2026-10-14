using System.Numerics;
using Xunit;

namespace Infiniminer.Tests
{
    // Building maps in a sandbox, saving them and starting games on them.
    [Collection("World generation")]
    public class MapEditorTests
    {
        [Fact]
        public void SandboxPlayersCanBuildWithEverything()
        {
            Assert.DoesNotContain(BlockType.Gold, GameRules.GetBlocks(PlayerClass.Miner, PlayerTeam.Red, sandbox: false));
            BlockType[] blocks = GameRules.GetBlocks(PlayerClass.Miner, PlayerTeam.Red, sandbox: true);
            Assert.Contains(BlockType.Gold, blocks);
            Assert.Contains(BlockType.Lava, blocks);
            Assert.Contains(BlockType.SolidRed, blocks);

            // Everyone gets the deconstruction gun in a sandbox (engineers only once).
            Assert.DoesNotContain(PlayerTools.DeconstructionGun, GameRules.GetTools(PlayerClass.Miner, sandbox: false));
            Assert.Contains(PlayerTools.DeconstructionGun, GameRules.GetTools(PlayerClass.Miner, sandbox: true));
            Assert.Single(GameRules.GetTools(PlayerClass.Engineer, sandbox: true), t => t == PlayerTools.DeconstructionGun);

            // The server only lets players use the tools they carry.
            Assert.False(Builder(PlayerClass.Miner, false).ToolLimiters.ContainsKey(PlayerTools.DeconstructionGun));
            Assert.True(Builder(PlayerClass.Miner, true).ToolLimiters.ContainsKey(PlayerTools.DeconstructionGun));
        }

        static InfiniminerServer StartServer(ServerConfig config, string dataDirectory)
        {
            CaveGenerator.SetSeed(5);
            config.Port = 0;
            InfiniminerServer server = new InfiniminerServer(config, dataDirectory);
            server.Log = _ => { };
            Assert.True(server.Initialize());
            return server;
        }

        static ServerPlayer Builder(PlayerClass playerClass, bool sandbox)
        {
            ServerPlayer player = new ServerPlayer(null, "test") { Team = PlayerTeam.Red, Alive = true, Ore = 0, OreMax = 200 };
            player.SetClass(playerClass, sandbox);
            player.Position = new Vector3(1.5f, 1.5f, 1.5f);
            return player;
        }

        static (int x, int y, int z) EmptyBlock(InfiniminerServer server)
        {
            for (int y = server.MapSize - 2; y > 0; y--)
                if (server.GetBlock(20, y, 20) == BlockType.None)
                    return (20, y, 20);
            throw new InvalidOperationException("no empty block");
        }

        [Fact]
        public void MapsAreSavedAndGamesStartOnThem()
        {
            string dataDirectory = Path.Combine(Path.GetTempPath(), "infiniminer-maps-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            try
            {
                // A sandbox: building with the world's own blocks is free.
                InfiniminerServer sandbox = StartServer(new ServerConfig { SandboxMode = true, MapSize = 64 }, dataDirectory);
                var (x, y, z) = EmptyBlock(sandbox);
                Assert.True(sandbox.PlaceBlock(Builder(PlayerClass.Miner, true), x, y, z, BlockType.Diamond));
                Assert.Equal(BlockType.Diamond, sandbox.GetBlock(x, y, z));
                Assert.True(sandbox.SaveMap("castle"));
                BlockType[] column = Enumerable.Range(0, 64).Select(h => sandbox.GetBlock(10, h, 10)).ToArray();
                sandbox.Shutdown("Test finished.");

                Assert.Equal(new[] { ("castle", 64) }, InfiniminerServer.ListMaps(dataDirectory));

                // Outside a sandbox, nobody builds diamonds.
                InfiniminerServer normal = StartServer(new ServerConfig { MapSize = 64 }, dataDirectory);
                var empty = EmptyBlock(normal);
                ServerPlayer rich = Builder(PlayerClass.Engineer, false);
                rich.Ore = 200;
                Assert.False(normal.PlaceBlock(rich, empty.x, empty.y, empty.z, BlockType.Diamond));
                normal.Shutdown("Test finished.");

                // A game on the map: the map's blocks, at the map's size whatever the setting says.
                InfiniminerServer onMap = StartServer(new ServerConfig { MapFile = "castle", MapSize = 128 }, dataDirectory);
                Assert.Equal(64, onMap.MapSize);
                Assert.Equal(BlockType.Diamond, onMap.GetBlock(x, y, z));
                Assert.Equal(column, Enumerable.Range(0, 64).Select(h => onMap.GetBlock(10, h, 10)).ToArray());
                onMap.Shutdown("Test finished.");

                // A missing map: a new world instead.
                InfiniminerServer missing = StartServer(new ServerConfig { MapFile = "nowhere", MapSize = 64 }, dataDirectory);
                Assert.Equal(64, missing.MapSize);
                missing.Shutdown("Test finished.");
            }
            finally
            {
                Directory.Delete(dataDirectory, true);
            }
        }

        [Fact]
        public void TheChosenMapIsSavedWithTheSettings()
        {
            string path = Path.Combine(Path.GetTempPath(), "infiniminer-hostmap-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                ClientSettings settings = new ClientSettings { HostMap = "castle", HostGameMode = GameMode.Hill, HostKeepWorld = true };
                settings.Save(path);
                ClientSettings loaded = ClientSettings.Load(path);
                Assert.Equal("castle", loaded.HostMap);
                Assert.Equal(GameMode.Hill, loaded.HostGameMode);
                // A game on a saved map always starts from the map.
                ServerConfig config = loaded.HostConfig();
                Assert.Equal("castle", config.MapFile);
                Assert.False(config.Persist);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
