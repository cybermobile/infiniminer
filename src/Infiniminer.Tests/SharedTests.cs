using System.Numerics;
using LiteNetLib.Utils;
using Xunit;

namespace Infiniminer.Tests
{
    public class SharedTests
    {
        [Fact]
        public void Vector3RoundTripsThroughNetData()
        {
            NetDataWriter writer = new NetDataWriter();
            writer.Put(new Vector3(1.5f, -2.25f, 63.75f));
            NetDataReader reader = new NetDataReader(writer.CopyData());
            Assert.Equal(new Vector3(1.5f, -2.25f, 63.75f), reader.GetVector3());
        }

        [Fact]
        public void DiscoveryInfoRoundTrips()
        {
            DiscoveryInfo info = new DiscoveryInfo { InstanceId = "abc", ServerName = "Test", PlayerCount = 3, MaxPlayers = 16, Extra = "10k" };
            NetDataWriter writer = new NetDataWriter();
            info.Write(writer);
            DiscoveryInfo read = DiscoveryInfo.TryRead(new NetDataReader(writer.CopyData()));
            Assert.NotNull(read);
            Assert.Equal("abc", read.InstanceId);
            Assert.Equal("Test", read.ServerName);
            Assert.Equal(3, read.PlayerCount);
            Assert.Equal(16, read.MaxPlayers);
            Assert.Equal("10k", read.Extra);
            Assert.True(read.HasDetails);
        }

        [Fact]
        public void DiscoveryDetailsRoundTripAndOldRepliesStillRead()
        {
            DiscoveryInfo info = new DiscoveryInfo
            {
                InstanceId = "abc", ServerName = "Test", PlayerCount = 5, MaxPlayers = 16, Extra = "hill, 4 bots",
                Version = "v1.9", MapSize = 96, Mode = GameMode.Hill, Lava = false, Bots = 4, WinningCash = 12000,
            };
            NetDataWriter writer = new NetDataWriter();
            info.Write(writer);
            DiscoveryInfo read = DiscoveryInfo.TryRead(new NetDataReader(writer.CopyData()));
            Assert.True(read.HasDetails);
            Assert.Equal("v1.9", read.Version);
            Assert.Equal(96, read.MapSize);
            Assert.Equal(GameMode.Hill, read.Mode);
            Assert.False(read.Sandbox);
            Assert.False(read.Lava);
            Assert.Equal(4, read.Bots);
            Assert.Equal(12000u, read.WinningCash);

            // A reply from an older server ends after the summary.
            NetDataWriter old = new NetDataWriter();
            old.Put(GameConstants.DiscoveryResponse);
            old.Put("id"); old.Put("Old"); old.Put(2); old.Put(8); old.Put("10k");
            DiscoveryInfo oldRead = DiscoveryInfo.TryRead(new NetDataReader(old.CopyData()));
            Assert.NotNull(oldRead);
            Assert.False(oldRead.HasDetails);
            Assert.Equal("10k", oldRead.Extra);
        }

        [Fact]
        public void DiscoveryInfoRejectsOtherPackets()
        {
            NetDataWriter writer = new NetDataWriter();
            writer.Put("something else");
            Assert.Null(DiscoveryInfo.TryRead(new NetDataReader(writer.CopyData())));
            Assert.Null(DiscoveryInfo.TryRead(new NetDataReader(new byte[] { 1, 2, 3 })));
        }

        [Fact]
        public void SanitizeKeepsPrintableAscii()
        {
            Assert.Equal("Hello World!", TextUtil.Sanitize("Hello \tWorld!é\n"));
            Assert.Equal("", TextUtil.Sanitize(null));
        }

        [Fact]
        public void ToBlockFloorsNegativeCoordinates()
        {
            Assert.Equal(0, GameConstants.ToBlock(0.9f));
            Assert.Equal(-1, GameConstants.ToBlock(-0.1f));
            Assert.Equal(63, GameConstants.ToBlock(63.99f));
        }

        [Fact]
        public void OnlyConstructionBlocksAreBuildable()
        {
            for (BlockType type = BlockType.None; type < BlockType.MAXIMUM; type++)
                Assert.Equal(BlockInformation.GetCost(type) != 1000, BlockInformation.IsBuildable(type));
            Assert.False(BlockInformation.IsBuildable(BlockType.Gold));
            Assert.False(BlockInformation.IsBuildable(BlockType.Lava));
        }

        [Fact]
        public void DatafileLoaderParsesKeyValuePairs()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "# comment = ignored",
                    "servername = My Server ",
                    "maxplayers=8",
                    "volume = 0.5",
                    "sandbox = true",
                    "broken = a = b",
                    "",
                });
                DatafileLoader loader = new DatafileLoader(path);
                Assert.Equal("My Server", loader.GetString("servername", ""));
                Assert.Equal(8u, loader.GetUInt("maxplayers", 0));
                Assert.Equal(0.5f, loader.GetFloat("volume", 1));
                Assert.True(loader.GetBool("sandbox", false));
                Assert.False(loader.Data.ContainsKey("# comment"));
                Assert.False(loader.Data.ContainsKey("broken"));
                Assert.Equal(42, loader.GetInt("missing", 42));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void MissingConfigFileGivesDefaults()
        {
            ServerConfig config = ServerConfig.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"));
            Assert.Equal("Unnamed Server", config.ServerName);
            Assert.Equal(GameConstants.DefaultPort, config.Port);
            Assert.Equal(16u, config.MaxPlayers);
        }
    }

    [Collection("World generation")]
    public class CaveGeneratorTests
    {
        [Fact]
        public void GeneratesAPlayableWorld()
        {
            CaveGenerator.SetSeed(1234);
            BlockType[,,] world = CaveGenerator.GenerateCaveSystem(GameConstants.MapSize, true, 10);

            Dictionary<BlockType, int> counts = new Dictionary<BlockType, int>();
            foreach (BlockType block in world)
                counts[block] = counts.GetValueOrDefault(block) + 1;

            Assert.Equal(64 * 64 * 64, counts.Values.Sum());
            Assert.True(counts.GetValueOrDefault(BlockType.Dirt) > 64 * 64 * 20, "mostly dirt");
            Assert.True(counts.GetValueOrDefault(BlockType.Ore) > 0, "has ore");
            Assert.True(counts.GetValueOrDefault(BlockType.Gold) > 0, "has gold");
            Assert.True(counts.GetValueOrDefault(BlockType.Diamond) > 0, "has diamonds");
            Assert.True(counts.GetValueOrDefault(BlockType.Rock) > 0, "has rock");
            Assert.True(counts.GetValueOrDefault(BlockType.Lava) > 0, "has lava");
            Assert.True(counts.GetValueOrDefault(BlockType.None) > 0, "has caves and sky");
        }

        [Fact]
        public void SameSeedGivesSameWorld()
        {
            CaveGenerator.SetSeed(99);
            BlockType[,,] a = CaveGenerator.GenerateCaveSystem(GameConstants.MapSize, false, 10);
            CaveGenerator.SetSeed(99);
            BlockType[,,] b = CaveGenerator.GenerateCaveSystem(GameConstants.MapSize, false, 10);
            Assert.True(a.Cast<BlockType>().SequenceEqual(b.Cast<BlockType>()));
        }
    }
}
