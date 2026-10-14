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
            ServerConfig config = new ServerConfig { Port = 0, SandboxMode = true, ServerName = "Test Server" };
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

        sealed class Bot : IDisposable
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
            WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.PlayerAlive).Any(), "respawn");
            return bot;
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
            Vector3 standingAbove = new Vector3(x + 0.5f, y + 1.6f, z + 0.5f);
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
