using Xunit;

namespace Infiniminer.Tests
{
    public class BlockPredictorTests
    {
        readonly Dictionary<(int, int, int), BlockType> world = new Dictionary<(int, int, int), BlockType>();
        readonly BlockPredictor predictor;

        public BlockPredictorTests()
        {
            world[(1, 1, 1)] = BlockType.Dirt;
            predictor = new BlockPredictor((x, y, z) => world.GetValueOrDefault((x, y, z)), (x, y, z, type) => world[(x, y, z)] = type);
        }

        [Fact]
        public void DiggingShowsImmediatelyAndStaysWhenConfirmed()
        {
            predictor.Predict(1, 1, 1, BlockType.None, 0);
            Assert.Equal(BlockType.None, world[(1, 1, 1)]);

            predictor.OnServerBlockSet(1, 1, 1);    // the server agrees
            predictor.Update(10);
            Assert.Equal(BlockType.None, world[(1, 1, 1)]);
            Assert.Equal(0, predictor.PendingCount);
        }

        [Fact]
        public void UnconfirmedPredictionsAreRolledBack()
        {
            predictor.Predict(1, 1, 1, BlockType.None, 0);
            predictor.Update(BlockPredictor.Timeout / 2);
            Assert.Equal(BlockType.None, world[(1, 1, 1)]);
            predictor.Update(BlockPredictor.Timeout + 0.1);
            Assert.Equal(BlockType.Dirt, world[(1, 1, 1)]);
        }

        [Fact]
        public void RefusedBuildsAreRemoved()
        {
            predictor.Predict(2, 2, 2, BlockType.SolidRed, 0);
            Assert.Equal(BlockType.SolidRed, world[(2, 2, 2)]);
            predictor.OnBuildFailed();
            Assert.Equal(BlockType.None, world[(2, 2, 2)]);
        }

        [Fact]
        public void RollbacksNeverOverwriteWhatTheServerSaid()
        {
            predictor.Predict(1, 1, 1, BlockType.None, 0);
            world[(1, 1, 1)] = BlockType.Lava;      // lava flowed in, and the server said so...
            predictor.Update(BlockPredictor.Timeout + 0.1);
            Assert.Equal(BlockType.Lava, world[(1, 1, 1)]);
        }
    }
}
