using Xunit;

namespace Infiniminer.Tests
{
    public class LocalPlayerTests
    {
        [Fact]
        public void ChoosingAClassGivesItsToolsAndTeamBlocks()
        {
            LocalPlayer player = new LocalPlayer { Team = PlayerTeam.Blue, ToolSelected = 1, BlockSelected = 2 };
            player.SetClass(PlayerClass.Engineer);
            Assert.Equal(GameRules.GetTools(PlayerClass.Engineer), player.Tools);
            Assert.Equal(GameRules.GetBlocks(PlayerClass.Engineer, PlayerTeam.Blue), player.Blocks);
            Assert.Equal(0, player.ToolSelected);
            Assert.Equal(0, player.BlockSelected);
            Assert.Contains(BlockType.SolidBlue, player.Blocks);

            // Moved to the other team (auto-balance): same class, the other team's blocks.
            player.SetTeam(PlayerTeam.Red);
            Assert.Contains(BlockType.SolidRed, player.Blocks);
            Assert.DoesNotContain(BlockType.SolidBlue, player.Blocks);
        }

        [Fact]
        public void ToolsAndBlocksCycleBothWays()
        {
            LocalPlayer player = new LocalPlayer();
            player.SetClass(PlayerClass.Engineer);
            int tools = player.Tools.Length, blocks = player.Blocks.Length;

            player.CycleTool(-1);
            Assert.Equal(tools - 1, player.ToolSelected);
            player.CycleTool(1);
            Assert.Equal(0, player.ToolSelected);

            // Blocks only change while holding the construction gun.
            Assert.NotEqual(PlayerTools.ConstructionGun, player.SelectedTool);
            Assert.False(player.CycleBlock(1));
            while (player.SelectedTool != PlayerTools.ConstructionGun)
                player.CycleTool(1);
            Assert.True(player.CycleBlock(-1));
            Assert.Equal(blocks - 1, player.BlockSelected);
            Assert.Equal(player.Blocks[blocks - 1], player.SelectedBlock);
        }
    }
}
