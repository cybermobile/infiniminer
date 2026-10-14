using System.Collections.Concurrent;
using System.Net;
using System.Numerics;
using LiteNetLib;
using LiteNetLib.Utils;
using Xunit;

namespace Infiniminer.Tests
{
    // Runs a real server on a random local port and talks to it the way the game client does.
    [Collection("World generation")]
    public class ServerIntegrationTests : IDisposable
    {
        readonly InfiniminerServer server;
        readonly Thread serverThread;
        volatile bool stopServer;
        readonly string dataDirectory;
        readonly ConcurrentQueue<string> serverLog = new ConcurrentQueue<string>();

        public ServerIntegrationTests()
        {
            dataDirectory = Path.Combine(Path.GetTempPath(), "infiniminer-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);

            CaveGenerator.SetSeed(42);
            ServerConfig config = new ServerConfig { Port = 0, SandboxMode = true, ServerName = "Test Server", HomeBases = true, ShockTouch = true };
            server = new InfiniminerServer(config, dataDirectory);
            server.Log = line => serverLog.Enqueue(line);
            Assert.True(server.Initialize());

            serverThread = new Thread(() =>
            {
                while (!stopServer && server.Tick())
                    Thread.Sleep(1);
                server.Shutdown("Test finished.");
            });
            serverThread.Start();
        }

        public void Dispose()
        {
            stopServer = true;
            serverThread.Join();
            Directory.Delete(dataDirectory, true);
        }

        internal sealed class Bot : IDisposable
        {
            public readonly EventBasedNetListener Listener = new EventBasedNetListener();
            public readonly NetManager Net;
            public NetPeer Peer;
            public readonly List<byte[]> Messages = new List<byte[]>();
            public DisconnectInfo? Disconnect;
            public readonly BlockType[,,] Map = new BlockType[64, 64, 64];
            public int ChunksReceived;
            public uint MyId;
            public DiscoveryInfo Discovery;
            public Vector3 Position;
            public byte Epoch;
            public int PositionsReceived;
            public PositionReason LastPositionReason;

            public Bot()
            {
                Net = new NetManager(Listener) { ChannelsCount = NetChannel.ChannelCount, AutoRecycle = true, UnconnectedMessagesEnabled = true };
                Listener.NetworkReceiveEvent += (peer, reader, channel, method) => Handle(reader.GetRemainingBytes());
                Listener.PeerDisconnectedEvent += (peer, info) =>
                {
                    // AdditionalData is recycled after the event, so copy what we need now.
                    Disconnect = info;
                    RejectReason = info.AdditionalData != null && info.AdditionalData.TryGetString(out string reason) ? reason : null;
                };
                Listener.NetworkReceiveUnconnectedEvent += (endPoint, reader, type) => Discovery = DiscoveryInfo.TryRead(reader);
                Net.Start();
            }

            public string RejectReason;

            void Handle(byte[] data)
            {
                Messages.Add(data);
                NetDataReader reader = new NetDataReader(data);
                InfiniminerMessage type = (InfiniminerMessage)reader.GetByte();
                if (type == InfiniminerMessage.BlockBulkTransfer)
                {
                    byte x = reader.GetByte(), y = reader.GetByte();
                    for (int dy = 0; dy < 16; dy++)
                        for (int z = 0; z < 64; z++)
                            Map[x, y + dy, z] = (BlockType)reader.GetByte();
                    ChunksReceived++;
                }
                else if (type == InfiniminerMessage.PlayerSetPosition)
                {
                    Position = reader.GetVector3();
                    Epoch = reader.GetByte();
                    LastPositionReason = (PositionReason)reader.GetByte();
                    PositionsReceived++;
                }
                else if (type == InfiniminerMessage.PlayerJoined)
                {
                    uint id = reader.GetUInt();
                    reader.GetString();
                    if (reader.GetBool())
                        MyId = id;
                }
            }

            public void Connect(int port, string version = GameConstants.Version, string handle = "Bot")
            {
                NetDataWriter connect = new NetDataWriter();
                connect.Put(GameConstants.AppIdentifier);
                connect.Put(handle);
                connect.Put(version);
                Peer = Net.Connect(new IPEndPoint(IPAddress.Loopback, port), connect);
            }

            public void Send(NetDataWriter writer, NetChannel channel)
            {
                Peer.Send(writer, channel);
            }

            public void SendPosition(Vector3 position)
            {
                NetDataWriter update = NetExtensions.CreateMessage(InfiniminerMessage.PlayerUpdate);
                update.Put(position);
                update.Put(-Vector3.UnitZ);
                update.Put((byte)PlayerTools.Pickaxe);
                update.Put(false);
                update.Put(Epoch);
                Send(update, NetChannel.UnreliableInOrder1);
            }

            public bool WasKilled(string message)
            {
                return MessagesOfType(InfiniminerMessage.PlayerDead).Any(r => r.GetUInt() == MyId)
                    && MessagesOfType(InfiniminerMessage.ChatMessage).Any(r => { r.GetByte(); return r.GetString() == "Bot " + message; });
            }

            public IEnumerable<NetDataReader> MessagesOfType(InfiniminerMessage type)
            {
                foreach (byte[] data in Messages.ToArray())
                    if (data.Length > 0 && data[0] == (byte)type)
                    {
                        NetDataReader reader = new NetDataReader(data);
                        reader.GetByte();
                        yield return reader;
                    }
            }

            public void Dispose()
            {
                Net.Stop();
            }
        }

        void WaitFor(Bot bot, Func<bool> condition, string what)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Timed out waiting for " + what + ". Server log:\n" + string.Join("\n", serverLog));
                bot.Net.PollEvents();
                Thread.Sleep(5);
            }
        }

        Bot JoinGame(PlayerTeam team, PlayerClass playerClass)
        {
            Bot bot = new Bot();
            bot.Connect(server.Port);
            WaitFor(bot, () => bot.ChunksReceived == 256 && bot.MyId != 0, "map download and join");

            NetDataWriter setTeam = NetExtensions.CreateMessage(InfiniminerMessage.PlayerSetTeam);
            setTeam.Put((byte)team);
            bot.Send(setTeam, NetChannel.ReliableUnordered);

            NetDataWriter selectClass = NetExtensions.CreateMessage(InfiniminerMessage.SelectClass);
            selectClass.Put((byte)playerClass);
            bot.Send(selectClass, NetChannel.ReliableUnordered);

            bot.Send(NetExtensions.CreateMessage(InfiniminerMessage.PlayerAlive), NetChannel.ReliableUnordered);
            WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.PlayerAlive).Any() && bot.PositionsReceived > 0, "respawn");
            return bot;
        }

        // Moves the bot with the server's admin "tp" command and waits for the server to tell it.
        void Teleport(Bot bot, Vector3 position)
        {
            int before = bot.PositionsReceived;
            server.EnqueueCommand(FormattableString.Invariant($"tp Bot {position.X} {position.Y} {position.Z}"));
            WaitFor(bot, () => bot.PositionsReceived > before, "teleport");
            Assert.Equal(position, bot.Position);
        }

        static Vector3 StandingOn(int x, int y, int z)
        {
            return new Vector3(x + 0.5f, y + 2.45f, z + 0.5f);
        }

        static void UseTool(Bot bot, Vector3 position, Vector3 heading, PlayerTools tool, BlockType blockType = BlockType.None)
        {
            NetDataWriter msg = NetExtensions.CreateMessage(InfiniminerMessage.UseTool);
            msg.Put(position);
            msg.Put(heading);
            msg.Put((byte)tool);
            msg.Put((byte)blockType);
            bot.Send(msg, NetChannel.ReliableUnordered);
        }

        static bool HasBlockSet(Bot bot, int x, int y, int z, BlockType type)
        {
            foreach (NetDataReader reader in bot.MessagesOfType(InfiniminerMessage.BlockSet))
                if (reader.GetByte() == x && reader.GetByte() == y && reader.GetByte() == z && (BlockType)reader.GetByte() == type)
                    return true;
            return false;
        }

        static (int x, int y, int z) FindBlock(Bot bot, BlockType type)
        {
            for (int x = 1; x < 63; x++)
                for (int y = 1; y < 63; y++)
                    for (int z = 1; z < 63; z++)
                        if (bot.Map[x, y, z] == type)
                            return (x, y, z);
            throw new InvalidOperationException("No " + type + " found");
        }

        // Finds a column whose topmost block is dirt, with clear air above it.
        static (int x, int y, int z) FindSurfaceDirt(Bot bot)
        {
            for (int x = 2; x < 62; x++)
                for (int z = 2; z < 62; z++)
                    for (int y = 61; y > 1; y--)
                    {
                        if (bot.Map[x, y, z] == BlockType.None)
                            continue;
                        if (bot.Map[x, y, z] == BlockType.Dirt && bot.Map[x, y + 1, z] == BlockType.None && bot.Map[x, y + 2, z] == BlockType.None)
                            return (x, y, z);
                        break;
                    }
            throw new InvalidOperationException("No surface dirt found");
        }

        [Fact]
        public void PlayerCanJoinDigAndBuild()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);

            // Class selection sets the miner's carrying capacity.
            WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.ResourceUpdate).Any(r =>
            {
                r.GetUInt(); r.GetUInt(); r.GetUInt();
                return r.GetUInt() == 200 && r.GetUInt() == 8;
            }), "miner resource update");

            Assert.Contains(bot.MessagesOfType(InfiniminerMessage.ChatMessage), r => { r.GetByte(); return r.GetString() == "Bot HAS JOINED THE ADVENTURE!"; });

            // Dig the dirt block below us.
            (int x, int y, int z) = FindSurfaceDirt(bot);
            Vector3 standingAbove = StandingOn(x, y, z);
            Teleport(bot, standingAbove);
            UseTool(bot, standingAbove, -Vector3.UnitY, PlayerTools.Pickaxe);
            WaitFor(bot, () => HasBlockSet(bot, x, y, z, BlockType.None), "dug block to be removed");

            // Fill the hole back in (sandbox mode makes blocks free).
            UseTool(bot, standingAbove, -Vector3.UnitY, PlayerTools.ConstructionGun, BlockType.SolidRed);
            WaitFor(bot, () => HasBlockSet(bot, x, y, z, BlockType.SolidRed), "built block");
        }

        [Fact]
        public void ServerRejectsNonConstructionBlocks()
        {
            using Bot bot = JoinGame(PlayerTeam.Blue, PlayerClass.Engineer);
            (int x, int y, int z) = FindSurfaceDirt(bot);
            Vector3 above = new Vector3(x + 0.5f, y + 2.6f, z + 0.5f);

            // A modified client asking for gold gets the "failed" gun animation and nothing is built.
            UseTool(bot, above, -Vector3.UnitY, PlayerTools.ConstructionGun, BlockType.Gold);
            WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.TriggerConstructionGunAnimation).Any(r => r.GetFloat() < 0), "failed build animation");
            Assert.False(HasBlockSet(bot, x, y + 1, z, BlockType.Gold));
        }

        [Fact]
        public void MalformedPacketsDoNotStopTheServer()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Sapper);

            bot.Peer.Send(new byte[] { (byte)InfiniminerMessage.UseTool, 1, 2 }, DeliveryMethod.ReliableOrdered);
            bot.Peer.Send(new byte[] { (byte)InfiniminerMessage.PlaySound, 250, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, DeliveryMethod.ReliableOrdered);

            NetDataWriter chat = NetExtensions.CreateMessage(InfiniminerMessage.ChatMessage);
            chat.Put((byte)ChatMessageType.SayAll);
            chat.Put("still alive");
            bot.Send(chat, NetChannel.ReliableInOrder3);
            WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.ChatMessage).Any(r => { r.GetByte(); return r.GetString() == "Bot (ALL): still alive"; }), "chat echo");
            Assert.DoesNotContain(bot.MessagesOfType(InfiniminerMessage.PlaySound), r => r.GetByte() == 250);
        }

        [Fact]
        public void ServerChoosesTheSpawnPoint()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);
            Assert.InRange(bot.Position.X, 2, 63);
            Assert.InRange(bot.Position.Z, 2, 63);
            Assert.InRange(bot.Position.Y, 50, 70);
        }

        [Fact]
        public void TeleportHacksAreCorrected()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);
            (int x, int y, int z) = FindSurfaceDirt(bot);
            Vector3 start = StandingOn(x, y, z);
            Teleport(bot, start);
            byte epoch = bot.Epoch;

            bot.SendPosition(start + new Vector3(20, 0, 0));
            WaitFor(bot, () => bot.Epoch != epoch, "position correction");
            Assert.Equal(start, bot.Position);
            Assert.Contains(serverLog, line => line.Contains("ANTICHEAT") && line.Contains("moving too fast"));
        }

        [Fact]
        public void WalkingIntoTheGroundIsCorrected()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);
            (int x, int y, int z) = FindSurfaceDirt(bot);
            Vector3 start = StandingOn(x, y, z);
            Teleport(bot, start);
            byte epoch = bot.Epoch;

            bot.SendPosition(start - new Vector3(0, 1.5f, 0));
            WaitFor(bot, () => bot.Epoch != epoch, "position correction");
            Assert.Equal(start, bot.Position);
        }

        [Fact]
        public void ServerKillsPlayersInLava()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);
            (int x, int y, int z) = FindBlock(bot, BlockType.Lava);
            Teleport(bot, new Vector3(x + 0.5f, y + 0.5f - GameRules.MidBodyOffset, z + 0.5f));
            WaitFor(bot, () => bot.WasKilled("WAS INCINERATED BY LAVA!"), "lava death");
        }

        [Fact]
        public void ServerAppliesFallDamage()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);
            (int x, int y, int z) = FindSurfaceDirt(bot);
            Vector3 ground = StandingOn(x, y, z);
            Vector3 start = ground + new Vector3(0, 20, 0);
            Teleport(bot, start);

            for (float height = start.Y - 0.8f; height > ground.Y; height -= 0.8f)
            {
                bot.SendPosition(new Vector3(ground.X, height, ground.Z));
                Thread.Sleep(50);
                bot.Net.PollEvents();
            }
            bot.SendPosition(ground);
            WaitFor(bot, () => bot.WasKilled("WAS KILLED BY GRAVITY!"), "fall death");
        }

        [Fact]
        public void ClassesCanOnlyUseTheirOwnToolsAndBlocks()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);
            (int x, int y, int z) = FindSurfaceDirt(bot);
            Vector3 start = StandingOn(x, y, z);
            Teleport(bot, start);

            // Miners don't carry the engineer's deconstruction gun: the dirt below survives.
            UseTool(bot, start, -Vector3.UnitY, PlayerTools.DeconstructionGun);
            // Miners can't build banks, only their own blocks.
            UseTool(bot, start, -Vector3.UnitY, PlayerTools.ConstructionGun, BlockType.BankRed);
            WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.TriggerConstructionGunAnimation).Any(r => r.GetFloat() < 0), "failed build");
            Assert.False(HasBlockSet(bot, x, y + 1, z, BlockType.BankRed));
            Assert.False(HasBlockSet(bot, x, y, z, BlockType.None));
        }

        [Fact]
        public void PingsCannotImpersonateOtherPlayers()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);
            NetDataWriter ping = NetExtensions.CreateMessage(InfiniminerMessage.PlayerPing);
            ping.Put(12345u);
            bot.Send(ping, NetChannel.ReliableUnordered);
            WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.PlayerPing).Any(), "ping");
            Assert.All(bot.MessagesOfType(InfiniminerMessage.PlayerPing), r => Assert.Equal(bot.MyId, r.GetUInt()));
        }

        [Fact]
        public void ChangingTeamWhileAliveKillsYou()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);
            NetDataWriter setTeam = NetExtensions.CreateMessage(InfiniminerMessage.PlayerSetTeam);
            setTeam.Put((byte)PlayerTeam.Blue);
            bot.Send(setTeam, NetChannel.ReliableInOrder2);
            WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.PlayerDead).Any(r => r.GetUInt() == bot.MyId), "death on team change");
        }

        [Fact]
        public void ChatIsRateLimited()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);
            for (int i = 0; i < 20; i++)
            {
                NetDataWriter chat = NetExtensions.CreateMessage(InfiniminerMessage.ChatMessage);
                chat.Put((byte)ChatMessageType.SayAll);
                chat.Put("spam " + i);
                bot.Send(chat, NetChannel.ReliableInOrder3);
            }
            WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.ChatMessage).Any(r => { r.GetByte(); return r.GetString().StartsWith("Bot (ALL): spam 4"); }), "chat");
            Thread.Sleep(300);
            bot.Net.PollEvents();
            int delivered = bot.MessagesOfType(InfiniminerMessage.ChatMessage).Count(r => { r.GetByte(); return r.GetString().StartsWith("Bot (ALL): spam"); });
            Assert.InRange(delivered, 5, 6);
        }

        static IEnumerable<(int x, int y, int z)> SurfaceDirtColumns(Bot bot)
        {
            for (int x = 2; x < 62; x++)
                for (int z = 2; z < 62; z++)
                    for (int y = 61; y > 1; y--)
                    {
                        if (bot.Map[x, y, z] == BlockType.None)
                            continue;
                        if (bot.Map[x, y, z] == BlockType.Dirt && Enumerable.Range(1, 5).All(dy => y + dy >= 63 || bot.Map[x, y + dy, z] == BlockType.None))
                            yield return (x, y, z);
                        break;
                    }
        }

        // Builds a block on top of a column by standing a little above it and aiming down.
        void BuildOnTop(Bot bot, (int x, int y, int z) column, BlockType blockType)
        {
            Vector3 above = new Vector3(column.x + 0.5f, column.y + 4.45f, column.z + 0.5f);
            Teleport(bot, above);
            UseTool(bot, above, -Vector3.UnitY, PlayerTools.ConstructionGun, blockType);
            WaitFor(bot, () => HasBlockSet(bot, column.x, column.y + 1, column.z, blockType), "building " + blockType);
        }

        [Fact]
        public void TeleportersLinkInBuildOrder()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Engineer);
            List<(int x, int y, int z)> columns = SurfaceDirtColumns(bot).ToList();
            (int x, int y, int z) a = columns[0];
            (int x, int y, int z) b = columns.First(c => Math.Abs(c.x - a.x) + Math.Abs(c.z - a.z) > 10);
            BuildOnTop(bot, a, BlockType.Teleporter);
            BuildOnTop(bot, b, BlockType.Teleporter);

            // Step onto the first teleporter...
            Vector3 onA = StandingOn(a.x, a.y + 1, a.z);
            Teleport(bot, onA);
            int before = bot.PositionsReceived;
            bot.SendPosition(onA);

            // ...and arrive on top of the second.
            WaitFor(bot, () => bot.PositionsReceived > before, "teleporter");
            Assert.Equal(PositionReason.Teleport, bot.LastPositionReason);
            Assert.Equal(StandingOn(b.x, b.y + 1, b.z), bot.Position);
        }

        [Fact]
        public void TheDrillDigsRockButThePickaxeDoesNot()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Miner);
            (int x, int y, int z) rock = (0, 0, 0);
            for (int x = 2; x < 62 && rock.x == 0; x++)
                for (int y = 2; y < 59 && rock.x == 0; y++)
                    for (int z = 2; z < 62 && rock.x == 0; z++)
                        if (bot.Map[x, y, z] == BlockType.Rock && bot.Map[x, y + 1, z] == BlockType.None && bot.Map[x, y + 2, z] == BlockType.None && bot.Map[x, y + 3, z] == BlockType.None)
                            rock = (x, y, z);
            Assert.NotEqual(0, rock.x);

            Vector3 standing = StandingOn(rock.x, rock.y, rock.z);
            Teleport(bot, standing);
            UseTool(bot, standing, -Vector3.UnitY, PlayerTools.Pickaxe);
            Thread.Sleep(300);
            bot.Net.PollEvents();
            Assert.False(HasBlockSet(bot, rock.x, rock.y, rock.z, BlockType.None));

            UseTool(bot, standing, -Vector3.UnitY, PlayerTools.Drill);
            WaitFor(bot, () => HasBlockSet(bot, rock.x, rock.y, rock.z, BlockType.None), "drilled rock");
        }

        [Fact]
        public void PlayersSpawnAtTheirHomeBase()
        {
            using Bot bot = JoinGame(PlayerTeam.Blue, PlayerClass.Miner);
            Assert.Equal(BlockType.HomeBlue, bot.Map[5, 55, 5]);
            Assert.Equal(BlockType.HomeRed, bot.Map[58, 55, 58]);
            Assert.Equal(BlockType.Metal, bot.Map[5, 54, 5]);
            Assert.InRange(bot.Position.X, 3, 8);
            Assert.InRange(bot.Position.Z, 3, 8);
            Assert.Equal(56.45f, bot.Position.Y, 2);
            Assert.Contains(bot.MessagesOfType(InfiniminerMessage.SetBeacon), r => { r.GetVector3(); return r.GetString() == "HOME"; });
        }

        [Fact]
        public void ShockBlocksKillOnTouchWhenEnabled()
        {
            using Bot bot = JoinGame(PlayerTeam.Red, PlayerClass.Engineer);
            (int x, int y, int z) column = SurfaceDirtColumns(bot).First(c => c.x > 15 && c.x < 50);
            BuildOnTop(bot, column, BlockType.Shock);

            // Stand right next to it, brushing against its side.
            Teleport(bot, new Vector3(column.x + 1.3f, column.y + 1.5f - GameRules.MidBodyOffset, column.z + 0.5f));
            WaitFor(bot, () => bot.WasKilled("WAS ELECTROCUTED!"), "electrocution");
        }

        [Fact]
        public void MismatchedVersionIsRejected()
        {
            using Bot bot = new Bot();
            bot.Connect(server.Port, version: "v1.5");
            WaitFor(bot, () => bot.Disconnect.HasValue, "rejection");
            Assert.Equal(DisconnectReason.ConnectionRejected, bot.Disconnect.Value.Reason);
            Assert.Equal("VER;" + GameConstants.Version, bot.RejectReason);
        }

        [Fact]
        public void ServerAnswersLanDiscovery()
        {
            using Bot bot = new Bot();
            NetDataWriter request = new NetDataWriter();
            request.Put(GameConstants.DiscoveryRequest);
            bot.Net.SendUnconnectedMessage(request, new IPEndPoint(IPAddress.Loopback, server.Port));
            WaitFor(bot, () => bot.Discovery != null, "discovery reply");
            Assert.Equal("Test Server", bot.Discovery.ServerName);
            Assert.Equal(16, bot.Discovery.MaxPlayers);
            Assert.Equal("sandbox", bot.Discovery.Extra);
        }
    }
}
