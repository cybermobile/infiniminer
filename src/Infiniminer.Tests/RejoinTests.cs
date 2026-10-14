using System.Net;
using Xunit;

namespace Infiniminer.Tests
{
    public class RejoinTests
    {
        static readonly IPEndPoint Server = new IPEndPoint(IPAddress.Loopback, 5565);

        [Fact]
        public void RejoiningWaitsForTheServerThenRetriesUntilItGivesUp()
        {
            AutoRejoin rejoin = new AutoRejoin();
            Assert.False(rejoin.ShouldAttempt(0));

            rejoin.Begin(Server, 100);
            Assert.True(rejoin.Active);
            Assert.False(rejoin.ShouldAttempt(101));
            Assert.True(rejoin.ShouldAttempt(100 + AutoRejoin.FirstAttemptDelay));
            Assert.False(rejoin.ShouldAttempt(110));        // one attempt at a time

            // The server isn't back yet: try again a little later.
            Assert.True(rejoin.AttemptFailed(104));
            Assert.False(rejoin.ShouldAttempt(105));
            Assert.True(rejoin.ShouldAttempt(104 + AutoRejoin.RetryInterval));
            Assert.Equal(Server, rejoin.Target);

            // Long enough: give up.
            Assert.False(rejoin.AttemptFailed(100 + AutoRejoin.GiveUpAfter + 1));
            Assert.False(rejoin.Active);
        }

        [Fact]
        public void RejoiningRemembersTheTeamAndClass()
        {
            AutoRejoin rejoin = new AutoRejoin();
            rejoin.Begin(Server, 0, (PlayerTeam.Blue, PlayerClass.Engineer));
            Assert.Equal((PlayerTeam.Blue, PlayerClass.Engineer), rejoin.Choice);
            rejoin.Cancel();
            Assert.Null(rejoin.Choice);

            rejoin.Begin(Server, 0);
            Assert.Null(rejoin.Choice);
        }

        [Fact]
        public void RejoiningCanBeCancelled()
        {
            AutoRejoin rejoin = new AutoRejoin();
            rejoin.Begin(Server, 0);
            rejoin.Cancel();
            Assert.False(rejoin.ShouldAttempt(100));
            Assert.False(rejoin.AttemptFailed(1));
        }

        [Fact]
        public void RecentServersKeepTheNewestFiveWithoutDuplicates()
        {
            ClientSettings settings = new ClientSettings();
            for (int i = 1; i <= 7; i++)
                settings.AddRecentServer("10.0.0." + i + ":5565", "Server " + i);
            settings.AddRecentServer("10.0.0.5:5565", "Renamed");

            Assert.Equal(ClientSettings.MaxRecentServers, settings.RecentServers.Count);
            Assert.Equal(("10.0.0.5:5565", "Renamed"), settings.RecentServers[0]);
            Assert.Equal(new[] { "10.0.0.5:5565", "10.0.0.7:5565", "10.0.0.6:5565", "10.0.0.4:5565", "10.0.0.3:5565" },
                settings.RecentServers.Select(r => r.Address));

            // Names can't break the config format.
            settings.AddRecentServer("example.com", "a|b");
            Assert.Equal(("example.com", "ab"), settings.RecentServers[0]);
        }

        [Fact]
        public void RecentServersAreSavedInOrder()
        {
            string path = Path.Combine(Path.GetTempPath(), "infiniminer-recent-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                ClientSettings settings = new ClientSettings();
                settings.AddRecentServer("old.example.com:5565", "Old");
                settings.AddRecentServer("192.168.1.20:5565", "Zach's game");
                settings.Save(path);

                ClientSettings loaded = ClientSettings.Load(path);
                Assert.Equal(settings.RecentServers, loaded.RecentServers);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
