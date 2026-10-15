using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Infiniminer.Tests
{
    public class ClientSettingsTests
    {
        [Fact]
        public void SettingsSurviveASaveAndLoad()
        {
            string path = Path.GetTempFileName();
            try
            {
                ClientSettings settings = new ClientSettings
                {
                    Handle = "Digger",
                    Width = 1920,
                    Height = 1080,
                    Fullscreen = true,
                    MouseSensitivity = 12,
                    FieldOfView = 90,
                    UiScale = 2,
                    Volume = 0.3f,
                };
                settings.Bind(GameAction.MoveForward, Keys.Up);
                settings.Save(path);

                ClientSettings loaded = ClientSettings.Load(path);
                Assert.Equal("Digger", loaded.Handle);
                Assert.Equal(1920, loaded.Width);
                Assert.Equal(1080, loaded.Height);
                Assert.True(loaded.Fullscreen);
                Assert.Equal(12, loaded.MouseSensitivity);
                Assert.Equal(90, loaded.FieldOfView);
                Assert.Equal(2, loaded.UiScale);
                Assert.Equal(0.3f, loaded.Volume, 3);
                Assert.Equal(Keys.Up, loaded.Key(GameAction.MoveForward));
                Assert.Equal(Keys.S, loaded.Key(GameAction.MoveBack));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void OldConfigFilesStillLoad()
        {
            string path = Path.GetTempFileName();
            try
            {
                // The original v1.5 client.config.txt format.
                File.WriteAllLines(path, new[] { "handle = Old", "width = 800", "height = 600", "pretty = false", "volume = 2.0" });
                ClientSettings loaded = ClientSettings.Load(path);
                Assert.Equal("Old", loaded.Handle);
                Assert.Equal(800, loaded.Width);
                Assert.False(loaded.Pretty);
                Assert.Equal(1.0f, loaded.Volume);
                Assert.Equal(5, loaded.MouseSensitivity);
                Assert.Equal(Keys.W, loaded.Key(GameAction.MoveForward));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void HostedGamesHaveThreeASideByDefault()
        {
            string path = Path.GetTempFileName();
            try
            {
                Assert.Equal(5, new ClientSettings().HostBots);

                // Settings saved when the default was 2 (one bot a side) get the new default.
                File.WriteAllLines(path, new[] { "handle = Host", "host_bots = 2" });
                Assert.Equal(5, ClientSettings.Load(path).HostBots);

                // A number chosen since then is kept.
                ClientSettings settings = new ClientSettings { HostBots = 9 };
                settings.Save(path);
                Assert.Equal(9, ClientSettings.Load(path).HostBots);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void BindingATakenKeySwapsTheBindings()
        {
            ClientSettings settings = new ClientSettings();
            settings.Bind(GameAction.Jump, Keys.W);
            Assert.Equal(Keys.W, settings.Key(GameAction.Jump));
            Assert.Equal(Keys.Space, settings.Key(GameAction.MoveForward));
        }

        [Fact]
        public void EveryActionHasADistinctDefaultKey()
        {
            Dictionary<GameAction, Keys> defaults = ClientSettings.DefaultKeyBindings();
            Assert.Equal(Enum.GetValues<GameAction>().Length, defaults.Count);
            Assert.Equal(defaults.Count, defaults.Values.Distinct().Count());
        }
    }
}
