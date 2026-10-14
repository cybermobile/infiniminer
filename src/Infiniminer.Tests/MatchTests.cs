using System.Collections.Concurrent;
using System.Numerics;
using LiteNetLib.Utils;
using Xunit;
using Bot = Infiniminer.Tests.ServerIntegrationTests.Bot;

namespace Infiniminer.Tests
{
    // Match rules that need their own server configuration: time limits, scaled targets and auto-balance.
    [Collection("World generation")]
    public class MatchTests
    {
        sealed class TestServer : IDisposable
        {
            public readonly InfiniminerServer Server;
            readonly Thread thread;
            volatile bool stop;
            readonly string dataDirectory;
            public readonly ConcurrentQueue<string> Log = new ConcurrentQueue<string>();

            public TestServer(ServerConfig config)
            {
                dataDirectory = Path.Combine(Path.GetTempPath(), "infiniminer-match-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dataDirectory);
                CaveGenerator.SetSeed(42);
                config.Port = 0;
                Server = new InfiniminerServer(config, dataDirectory);
                Server.Log = line => Log.Enqueue(line);
                Assert.True(Server.Initialize());
                thread = new Thread(() =>
                {
                    while (!stop && Server.Tick())
                        Thread.Sleep(1);
                    Server.Shutdown("done");
                });
                thread.Start();
            }

            public void WaitFor(Bot bot, Func<bool> condition, string what)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!condition())
                {
                    if (DateTime.UtcNow > deadline)
                        throw new TimeoutException("Timed out waiting for " + what + ". Server log:\n" + string.Join("\n", Log));
                    bot.Net.PollEvents();
                    Thread.Sleep(5);
                }
            }

            public Bot Join(string handle, PlayerTeam team, PlayerClass playerClass)
            {
                Bot bot = new Bot();
                bot.Connect(Server.Port, handle: handle);
                WaitFor(bot, () => bot.MapComplete && bot.MyId != 0, "join");
                NetDataWriter setTeam = NetExtensions.CreateMessage(InfiniminerMessage.PlayerSetTeam);
                setTeam.Put((byte)team);
                bot.Send(setTeam, NetChannel.ReliableInOrder2);
                NetDataWriter selectClass = NetExtensions.CreateMessage(InfiniminerMessage.SelectClass);
                selectClass.Put((byte)playerClass);
                bot.Send(selectClass, NetChannel.ReliableInOrder2);
                bot.Send(NetExtensions.CreateMessage(InfiniminerMessage.PlayerAlive), NetChannel.ReliableInOrder2);
                WaitFor(bot, () => bot.PositionsReceived > 0, "spawn");
                return bot;
            }

            public void Teleport(Bot bot, string handle, Vector3 position)
            {
                int before = bot.PositionsReceived;
                Server.EnqueueCommand(FormattableString.Invariant($"tp {handle} {position.X} {position.Y} {position.Z}"));
                WaitFor(bot, () => bot.PositionsReceived > before, "teleport");
            }

            public void Dispose()
            {
                stop = true;
                thread.Join();
                Directory.Delete(dataDirectory, true);
            }
        }

        static (int seconds, uint cash)? LatestMatchInfo(Bot bot)
        {
            NetDataReader reader = bot.MessagesOfType(InfiniminerMessage.MatchInfo).LastOrDefault();
            return reader == null ? null : (reader.GetInt(), reader.GetUInt());
        }

        [Fact]
        public void LargerWorldsCanBePlayed()
        {
            using TestServer server = new TestServer(new ServerConfig { MapSize = 128, HomeBases = true });
            using Bot bot = server.Join("Bot", PlayerTeam.Red, PlayerClass.Miner);
            Assert.Equal(128, bot.MapSize);
            Assert.Equal(1024, bot.ChunksReceived);

            Dictionary<BlockType, int> counts = new Dictionary<BlockType, int>();
            foreach (BlockType block in bot.Map)
                counts[block] = counts.GetValueOrDefault(block) + 1;
            Assert.True(counts.GetValueOrDefault(BlockType.Gold) > 100, "plenty of gold");
            Assert.True(counts.GetValueOrDefault(BlockType.Diamond) > 50, "plenty of diamonds");

            // Red's base is in the far corner of the bigger map, and players spawn there.
            Assert.Equal(BlockType.HomeRed, bot.Map[122, 119, 122]);
            Assert.InRange(bot.Position.X, 119, 125);
            Assert.InRange(bot.Position.Z, 119, 125);
        }

        [Fact]
        public void TheWinningTargetCanGrowWithThePlayerCount()
        {
            using TestServer server = new TestServer(new ServerConfig { WinningCashAmount = 10000, WinningCashPerPlayer = 500 });
            using Bot bot = server.Join("Bot", PlayerTeam.Red, PlayerClass.Miner);
            server.WaitFor(bot, () => LatestMatchInfo(bot)?.cash == 10500, "match info");
            Assert.Equal(-1, LatestMatchInfo(bot).Value.seconds);
        }

        [Fact]
        public void AutoBalancePutsPlayersOnTheSmallerTeam()
        {
            using TestServer server = new TestServer(new ServerConfig { AutoBalance = true });
            using Bot first = server.Join("First", PlayerTeam.Red, PlayerClass.Miner);
            using Bot second = server.Join("Second", PlayerTeam.Red, PlayerClass.Miner);
            server.WaitFor(second, () => second.MessagesOfType(InfiniminerMessage.PlayerSetTeam).Any(r => r.GetUInt() == second.MyId && (PlayerTeam)r.GetByte() == PlayerTeam.Blue), "balanced team");
            Assert.Contains(second.MessagesOfType(InfiniminerMessage.ChatMessage), r => { r.GetByte(); return r.GetString().Contains("JOINED THE BLUE TEAM"); });
        }

        [Fact]
        public void WhenTimeRunsOutATieGoesToOvertimeAndTheNextDepositWins()
        {
            using TestServer server = new TestServer(new ServerConfig { TimeLimitMinutes = 0.02f });
            using Bot bot = server.Join("Bot", PlayerTeam.Red, PlayerClass.Miner);
            server.WaitFor(bot, () => LatestMatchInfo(bot)?.seconds > 0, "match clock");

            // Time runs out with no money banked: overtime.
            server.WaitFor(bot, () => LatestMatchInfo(bot)?.seconds == 0, "overtime");

            // Dig some gold from an open face...
            (int x, int y, int z, Vector3 face) gold = default;
            Vector3[] faces = { Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ };
            for (int x = 2; x < 62 && gold.x == 0; x++)
                for (int y = 2; y < 61 && gold.x == 0; y++)
                    for (int z = 2; z < 62 && gold.x == 0; z++)
                        if (bot.Map[x, y, z] == BlockType.Gold)
                            foreach (Vector3 face in faces)
                                if (bot.Map[x + (int)face.X, y + (int)face.Y, z + (int)face.Z] == BlockType.None)
                                {
                                    gold = (x, y, z, face);
                                    break;
                                }
            Assert.NotEqual(0, gold.x);
            Vector3 eye = new Vector3(gold.x + 0.5f, gold.y + 0.5f, gold.z + 0.5f) + gold.face;
            server.Teleport(bot, "Bot", eye);
            NetDataWriter dig = NetExtensions.CreateMessage(InfiniminerMessage.UseTool);
            dig.Put(eye);
            dig.Put(-gold.face);
            dig.Put((byte)PlayerTools.Pickaxe);
            dig.Put((byte)BlockType.None);
            bot.Send(dig, NetChannel.ReliableUnordered);
            server.WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.ResourceUpdate).Any(r => { r.GetUInt(); return r.GetUInt() == 100; }), "gold dug");

            // ...and bring it to the surface.
            server.Teleport(bot, "Bot", new Vector3(30.5f, 62, 30.5f));
            server.WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.GameOver).Any(r => (PlayerTeam)r.GetByte() == PlayerTeam.Red), "red win");
        }
    }
}
