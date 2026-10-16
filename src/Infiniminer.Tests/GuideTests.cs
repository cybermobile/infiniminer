using Xunit;

namespace Infiniminer.Tests
{
    public class GuideTests
    {
        static readonly GuideControls Keys = new GuideControls("LEFT CLICK", "E", "R", "TAB");

        static GuideState Playing(PlayerClass playerClass = PlayerClass.Miner)
        {
            return new GuideState(true, -1, 0, 0, playerClass, PlayerTools.Pickaxe, BlockType.SolidRed, 0);
        }

        [Fact]
        public void ANewPlayerIsWalkedThroughTheGame()
        {
            HashSet<string> done = new HashSet<string>();
            Guide guide = new Guide(done);
            GuideState s = Playing(PlayerClass.Prospector);

            // On the surface: dig down.
            guide.Update(s, 0.1f);
            Assert.Equal("dig", guide.Current);
            Assert.Contains("HOLD LEFT CLICK TO DIG", guide.Text(s, Keys));
            Assert.True(guide.Update(s = s with { Depth = 3 }, 0.1f));
            Assert.Contains("dig", done);

            // Find ore.
            guide.Update(s, 0.1f);
            Assert.Equal("ore", guide.Current);
            guide.Update(s = s with { Ore = 20 }, 0.1f);
            Assert.Equal("loot", guide.Current);

            // Find treasure, then take it up.
            guide.Update(s = s with { Cash = 100, Depth = 6 }, 0.1f);
            guide.Update(s, 0.1f);
            Assert.Equal("bank", guide.Current);
            Assert.Contains("$100", guide.Text(s, Keys));
            guide.Update(s = s with { Depth = 0 }, 0.1f);
            guide.Update(s = s with { Cash = 0 }, 0.1f);
            Assert.Contains("bank", done);

            // The prospector's radar: held for a few seconds.
            guide.Update(s, 0.1f);
            Assert.Equal("class_prospector", guide.Current);
            Assert.Contains("PRESS E", guide.Text(s, Keys));
            s = s with { Tool = PlayerTools.ProspectingRadar };
            Assert.DoesNotContain("PRESS E", guide.Text(s, Keys));
            for (int i = 0; i < 70 && guide.Current == "class_prospector"; i++)
                guide.Update(s, 0.1f);
            Assert.Contains("class_prospector", done);

            // How to win, then nothing more.
            Assert.Equal("win", guide.Current);
            Assert.Contains("TEAM THAT BANKS THE MOST", guide.Text(s, Keys));
            Assert.Contains("$500", guide.Text(s with { WinningCash = 500 }, Keys));
            for (int i = 0; i < 130; i++)
                guide.Update(s, 0.1f);
            Assert.Null(guide.Current);
        }

        [Fact]
        public void DeepDownWithOreItTeachesLadders()
        {
            Guide guide = new Guide(new HashSet<string> { "dig", "ore" });
            GuideState s = Playing() with { Depth = 10, Ore = 60 };
            guide.Update(s, 0.1f);
            Assert.Equal("ladder", guide.Current);

            // One step at a time: the gun, the ladder, then building it.
            Assert.Contains("PRESS E", guide.Text(s, Keys));
            s = s with { Tool = PlayerTools.ConstructionGun };
            Assert.Contains("PRESS R", guide.Text(s, Keys));
            s = s with { Block = BlockType.Ladder };
            Assert.Contains("BUILD A LADDER", guide.Text(s, Keys));
            guide.Update(s, 0.1f);
            Assert.Equal("ladder", guide.Current);
            Assert.True(guide.Update(s with { Ore = 35 }, 0.1f));
            Assert.True(guide.IsDone("ladder"));

            // Without the ore for a ladder (or near the top), it's not the time.
            Guide poor = new Guide(new HashSet<string> { "dig", "ore" });
            poor.Update(Playing() with { Depth = 10, Ore = 20 }, 0.1f);
            Assert.Equal("loot", poor.Current);
            poor.Update(Playing() with { Depth = 1, Ore = 60 }, 0.1f);
            Assert.Equal("loot", poor.Current);
        }

        [Fact]
        public void DiggingUpOreAlsoCountsAsHavingDug()
        {
            Guide guide = new Guide(new HashSet<string>());
            guide.Update(Playing(), 0.1f);
            Assert.Equal("dig", guide.Current);
            Assert.True(guide.Update(Playing() with { Ore = 20 }, 0.1f));
            Assert.True(guide.IsDone("dig"));
        }

        [Fact]
        public void DyingWithLootDoesNotCountAsBankingIt()
        {
            Guide guide = new Guide(new HashSet<string> { "dig", "ore", "ladder", "loot" });
            GuideState s = Playing() with { Depth = 10, Cash = 1000 };
            guide.Update(s, 0.1f);
            Assert.Equal("bank", guide.Current);
            guide.Update(s with { Playing = false, Cash = 0 }, 0.1f);
            Assert.Null(guide.Current);
            guide.Update(s with { Cash = 0 }, 0.1f);
            Assert.False(guide.IsDone("bank"));
        }

        [Fact]
        public void WithAHomeBaseTheBankTipSaysToTakeLootThere()
        {
            Guide guide = new Guide(new HashSet<string> { "dig", "ore", "ladder", "loot" });
            GuideState s = Playing() with { Cash = 100, HomeBase = true };
            guide.Update(s, 0.1f);
            Assert.Equal("bank", guide.Current);
            Assert.Contains("HOME BASE", guide.Text(s, Keys));
            Assert.DoesNotContain("HOME BASE", guide.Text(s with { HomeBase = false }, Keys));
            Assert.Contains("YOUR TEAM'S BASE", BlockGuide.Description(BlockType.HomeRed, PlayerTeam.Red, PlayerClass.Miner));
            Assert.Contains("OTHER TEAM'S BASE", BlockGuide.Description(BlockType.HomeRed, PlayerTeam.Blue, PlayerClass.Miner));
        }

        [Fact]
        public void NoTipsWhenNotPlaying()
        {
            Guide guide = new Guide(new HashSet<string>());
            guide.Update(Playing() with { Playing = false }, 0.1f);
            Assert.Null(guide.Current);
            Assert.Null(guide.Text(Playing(), Keys));
        }

        [Fact]
        public void EveryClassHasItsOwnTip()
        {
            foreach (PlayerClass playerClass in Enum.GetValues<PlayerClass>())
            {
                Guide guide = new Guide(new HashSet<string> { "dig", "ore", "ladder", "loot", "bank" });
                GuideState s = Playing(playerClass);
                guide.Update(s, 0.1f);
                Assert.Equal("class_" + playerClass.ToString().ToLowerInvariant(), guide.Current);
                Assert.StartsWith("AS A", guide.Text(s, Keys));
                // Its own tool is one the class carries.
                Assert.Contains(Guide.ClassTool(playerClass), GameRules.GetTools(playerClass));
            }
        }

        [Fact]
        public void EveryBlockAndToolIsExplained()
        {
            foreach (BlockType blockType in Enum.GetValues<BlockType>())
            {
                if (blockType == BlockType.None || blockType == BlockType.MAXIMUM)
                    continue;
                Assert.NotEqual("", BlockGuide.Description(blockType, PlayerTeam.Red, PlayerClass.Miner));
            }
            foreach (PlayerTools tool in Enum.GetValues<PlayerTools>())
                Assert.NotEqual("", BlockGuide.ToolDescription(tool, false));

            // Team blocks read differently for each side.
            Assert.Contains("YOUR TEAM", BlockGuide.Description(BlockType.TransBlue, PlayerTeam.Blue, PlayerClass.Miner));
            Assert.Contains("ONLY THE BLUE TEAM", BlockGuide.Description(BlockType.TransBlue, PlayerTeam.Red, PlayerClass.Miner));
            Assert.Contains("YOUR TEAM", BlockGuide.Description(BlockType.BankRed, PlayerTeam.Red, PlayerClass.Engineer));
            Assert.Contains("OTHER TEAM", BlockGuide.Description(BlockType.BankRed, PlayerTeam.Blue, PlayerClass.Engineer));
            Assert.Contains("DRILL", BlockGuide.Description(BlockType.Rock, PlayerTeam.Red, PlayerClass.Miner));
        }

        [Fact]
        public void TipsSettingsSurviveASaveAndLoad()
        {
            string path = Path.GetTempFileName();
            try
            {
                ClientSettings settings = new ClientSettings { ShowTips = false };
                settings.TipsDone.Add("dig");
                settings.TipsDone.Add("class_miner");
                settings.Save(path);

                ClientSettings loaded = ClientSettings.Load(path);
                Assert.False(loaded.ShowTips);
                Assert.Equal(new HashSet<string> { "dig", "class_miner" }, loaded.TipsDone);

                // A new player's settings: tips on, none done.
                ClientSettings fresh = new ClientSettings();
                Assert.True(fresh.ShowTips);
                Assert.Empty(fresh.TipsDone);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
