using LiteNetLib;
using LiteNetLib.Utils;
using Xunit;
using Bot = Infiniminer.Tests.ServerIntegrationTests.Bot;
using TestServer = Infiniminer.Tests.MatchTests.TestServer;

namespace Infiniminer.Tests
{
    [Collection("World generation")]
    public class AdminTests
    {
        static void Say(Bot bot, string text)
        {
            bot.Send(ClientMessages.Chat(ChatMessageType.SayAll, text));
        }

        static bool Heard(Bot bot, string fragment)
        {
            return bot.MessagesOfType(InfiniminerMessage.ChatMessage).Any(r => { r.GetByte(); return r.GetString().Contains(fragment); });
        }

        [Fact]
        public void AdminsCanLogInAndRunCommandsFromChat()
        {
            using TestServer server = new TestServer(new ServerConfig { AdminPassword = "hunter2" });
            using Bot admin = server.Join("Admin", PlayerTeam.Red, PlayerClass.Miner);
            using Bot other = server.Join("Other", PlayerTeam.Blue, PlayerClass.Miner);

            Say(admin, "/kick Other");
            server.WaitFor(admin, () => Heard(admin, "log in first"), "refusal");

            Say(admin, "/login wrong");
            server.WaitFor(admin, () => Heard(admin, "wrong password"), "wrong password");

            Say(admin, "/login hunter2");
            server.WaitFor(admin, () => Heard(admin, "Admin logged in"), "login");

            Say(admin, "/players");
            server.WaitFor(admin, () => Heard(admin, "Other (B) - 127.0.0.1"), "player list");

            Say(admin, "/kick other");
            server.WaitFor(other, () => other.Disconnect.HasValue, "kick");

            // Commands (and the password) are never shown to other players.
            Assert.False(Heard(other, "hunter2"));
            Assert.False(Heard(other, "/players"));
        }

        [Fact]
        public void BanningByNameBlocksTheirAddress()
        {
            using TestServer server = new TestServer(new ServerConfig());
            using Bot bot = server.Join("Griefer", PlayerTeam.Red, PlayerClass.Miner);
            server.Server.EnqueueCommand("ban Griefer");
            server.WaitFor(bot, () => bot.Disconnect.HasValue, "kick");

            using Bot again = new Bot();
            again.Connect(server.Server.Port, handle: "Griefer");
            server.WaitFor(again, () => again.Disconnect.HasValue, "rejection");
            Assert.Equal(DisconnectReason.ConnectionRejected, again.Disconnect.Value.Reason);
            Assert.Equal("BAN;", again.RejectReason);

            server.Server.EnqueueCommand("unban 127.0.0.1");
            using Bot forgiven = new Bot();
            forgiven.Connect(server.Server.Port, handle: "Griefer");
            server.WaitFor(forgiven, () => forgiven.MapComplete, "rejoin after unban");
        }

        [Fact]
        public void LoadingAMapResendsTheWorld()
        {
            using TestServer server = new TestServer(new ServerConfig());
            using Bot bot = server.Join("Bot", PlayerTeam.Red, PlayerClass.Miner);
            int chunks = bot.ChunksReceived;

            server.Server.EnqueueCommand("save test.lvl");
            server.Server.EnqueueCommand("load test.lvl");
            server.WaitFor(bot, () => bot.ChunksReceived == 2 * chunks, "map resent");
            server.WaitFor(bot, () => bot.MessagesOfType(InfiniminerMessage.PlayerDead).Any(r => r.GetUInt() == bot.MyId), "respawn needed");
        }

        [Fact]
        public void MapFilesMustBePlainNames()
        {
            using TestServer server = new TestServer(new ServerConfig());
            using Bot bot = server.Join("Bot", PlayerTeam.Red, PlayerClass.Miner);
            server.Server.EnqueueCommand("save ../escape.lvl");
            server.Server.EnqueueCommand("load /etc/passwd");
            server.WaitFor(bot, () => server.Log.Count(l => l.Contains("give a plain file name")) == 2, "refusals");
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escape.lvl")));
        }
    }
}
