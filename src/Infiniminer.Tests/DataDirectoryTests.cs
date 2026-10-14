using Xunit;

namespace Infiniminer.Tests
{
    public class DataDirectoryTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "infiniminer-test-" + Guid.NewGuid().ToString("N"));
        readonly string current, install, user;

        public DataDirectoryTests()
        {
            current = Path.Combine(root, "cwd");
            install = Path.Combine(root, "install");
            user = Path.Combine(root, "user");
            Directory.CreateDirectory(current);
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "server.config.txt"), "port = 5565");
            File.WriteAllText(Path.Combine(install, "banlist.txt"), "");
        }

        public void Dispose()
        {
            Directory.Delete(root, true);
        }

        string Resolve(bool installWritable, string baseDirectory = null)
        {
            return DatafileLoader.ResolveDataDirectory(current, baseDirectory ?? install, user, _ => installWritable, "server.config.txt", "banlist.txt");
        }

        [Fact]
        public void AConfigInTheWorkingDirectoryWins()
        {
            File.WriteAllText(Path.Combine(current, "server.config.txt"), "");
            Assert.Equal(current, Resolve(installWritable: true));
            Assert.False(Directory.Exists(user));
        }

        [Fact]
        public void AWritableInstallFolderIsUsedInPlace()
        {
            Assert.Equal(install, Resolve(installWritable: true));
            Assert.False(Directory.Exists(user));
        }

        [Fact]
        public void AReadOnlyInstallUsesAPerUserFolderSeededWithTheDefaults()
        {
            Assert.Equal(user, Resolve(installWritable: false));
            Assert.Equal("port = 5565", File.ReadAllText(Path.Combine(user, "server.config.txt")));
            Assert.True(File.Exists(Path.Combine(user, "banlist.txt")));

            // The player's own changes are never overwritten by the defaults.
            File.WriteAllText(Path.Combine(user, "server.config.txt"), "port = 6000");
            Assert.Equal(user, Resolve(installWritable: false));
            Assert.Equal("port = 6000", File.ReadAllText(Path.Combine(user, "server.config.txt")));
        }

        [Fact]
        public void AppBundlesAreNeverWrittenTo()
        {
            string bundle = Path.Combine(root, "Infiniminer.app", "Contents", "MacOS");
            Directory.CreateDirectory(bundle);
            File.Copy(Path.Combine(install, "server.config.txt"), Path.Combine(bundle, "server.config.txt"));
            Assert.Equal(user, Resolve(installWritable: true, baseDirectory: bundle));
            Assert.True(File.Exists(Path.Combine(user, "server.config.txt")));
        }
    }
}
