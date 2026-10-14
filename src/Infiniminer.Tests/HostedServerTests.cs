using Xunit;

namespace Infiniminer.Tests
{
    [Collection("World generation")]
    public class HostedServerTests
    {
        static void WaitFor(Func<bool> condition, string what, Action poll = null)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Timed out waiting for " + what);
                poll?.Invoke();
                Thread.Sleep(5);
            }
        }

        [Fact]
        public void HostGameRunsAServerThatPlayersCanJoinUntilTheHostEndsIt()
        {
            string dataDirectory = Path.Combine(Path.GetTempPath(), "infiniminer-host-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDirectory);
            ClientSettings settings = new ClientSettings { Handle = "Zach", HostMapSize = 96, HostTimeLimit = 20, HostHomeBases = true };
            ServerConfig config = settings.HostConfig();
            Assert.Equal("ZACH'S GAME", config.ServerName);
            Assert.Equal(96, config.MapSize);
            Assert.Equal(20, config.TimeLimitMinutes);
            config.Port = 0;    // any free port, so the test doesn't need the standard one

            HostedServer hosted = HostedServer.Start(config, dataDirectory);
            try
            {
                WaitFor(() => hosted.Ready || hosted.Error != null, "the hosted server to start");
                Assert.Null(hosted.Error);
                Assert.True(hosted.Running);

                using ServerIntegrationTests.Bot bot = new ServerIntegrationTests.Bot();
                bot.Connect(hosted.Port);
                WaitFor(() => bot.MapComplete && bot.MyId != 0, "joining the hosted game", () => bot.Net.PollEvents());
                Assert.Equal(96, bot.MapSize);

                // Ending the game disconnects everyone and stops the server.
                hosted.Stop();
                Assert.False(hosted.Running);
                WaitFor(() => bot.Disconnect != null, "being disconnected", () => bot.Net.PollEvents());
                Assert.Equal("The host ended the game.", bot.RejectReason);
            }
            finally
            {
                hosted.Stop();
                Directory.Delete(dataDirectory, true);
            }
        }
    }
}
