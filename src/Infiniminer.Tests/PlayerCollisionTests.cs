using Microsoft.Xna.Framework;
using Xunit;

namespace Infiniminer.Tests
{
    public class PlayerCollisionTests
    {
        // Open floor at y = 10 with a wall along x = 5, and two blocks touching only at an edge
        // at (10, 11, 10) and (11, 11, 11).
        static BlockType World(Vector3 p)
        {
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y), z = (int)Math.Floor(p.Z);
            if (y <= 10)
                return BlockType.Dirt;
            if (y <= 13 && x == 5)
                return BlockType.Rock;
            if (y <= 13 && ((x == 10 && z == 10) || (x == 11 && z == 11)))
                return BlockType.Rock;
            return BlockType.None;
        }

        static bool Passable(BlockType block) => block == BlockType.None;
        const float Eye = 12.45f;

        static bool Clear(Vector3 from, Vector3 to) => PlayerCollision.Obstacles(World, Passable, from, to).Count == 0;

        [Fact]
        public void OpenGroundIsClear()
        {
            Assert.True(Clear(new Vector3(2.5f, Eye, 2.5f), new Vector3(2.6f, Eye, 2.5f)));
        }

        [Fact]
        public void TheBodyKeepsItsDistanceFromWalls()
        {
            // The centre is still 0.15 from the wall, but the body's edge would touch it.
            Assert.False(Clear(new Vector3(4.7f, Eye, 2.5f), new Vector3(4.85f, Eye, 2.5f)));
            // Sliding along the wall at a safe distance is fine.
            Assert.True(Clear(new Vector3(4.75f, Eye, 2.5f), new Vector3(4.75f, Eye, 2.6f)));
        }

        [Fact]
        public void DiagonalGapsAreClosed()
        {
            // Approaching the point where the two blocks meet: the old point-sized body would squeeze
            // through; a body with width is stopped.
            Assert.True(Clear(new Vector3(10.6f, Eye, 11.4f), new Vector3(10.75f, Eye, 11.25f)));
            Assert.False(Clear(new Vector3(10.75f, Eye, 11.25f), new Vector3(10.85f, Eye, 11.15f)));
        }

        [Fact]
        public void APlayerOverlappingABlockCanMoveAway()
        {
            // A corner is already inside the wall: moving away is allowed, moving further in is not.
            Assert.True(Clear(new Vector3(4.9f, Eye, 2.5f), new Vector3(4.8f, Eye, 2.5f)));
            Assert.False(Clear(new Vector3(4.85f, Eye, 2.5f), new Vector3(4.9f, Eye, 2.5f)));
        }

        [Fact]
        public void ObstaclesAreReported()
        {
            Assert.Contains(BlockType.Rock, PlayerCollision.Obstacles(World, Passable, new Vector3(4.7f, Eye, 2.5f), new Vector3(4.85f, Eye, 2.5f)));
        }
    }
}
