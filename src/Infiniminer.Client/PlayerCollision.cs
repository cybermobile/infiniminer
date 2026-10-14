using Microsoft.Xna.Framework;

namespace Infiniminer
{
    // Horizontal collision for the player's body.
    //
    // The original game tested three points on a line through the player (head, middle, feet), a little
    // ahead of the move. A body with no width can slip through gaps where two blocks only touch at an
    // edge and press the camera against walls, so the corners of a small square footprint are tested too.
    public static class PlayerCollision
    {
        public const float Radius = 0.2f;
        static readonly float[] Heights = { 0, GameRules.MidBodyOffset, GameRules.LowerBodyOffset };
        static readonly Vector2[] Corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) };

        // The blocks the body would run into by moving from `from` to `to`; empty if the move is clear.
        public static List<BlockType> Obstacles(Func<Vector3, BlockType> blockAt, Func<BlockType, bool> passable, Vector3 from, Vector3 to)
        {
            List<BlockType> obstacles = new List<BlockType>();
            Vector3 move = to - from;
            Vector3 ahead = move.LengthSquared() > 0 ? Vector3.Normalize(move) * (move.Length() + 0.1f) : Vector3.Zero;

            foreach (float height in Heights)
            {
                Vector3 up = new Vector3(0, height, 0);

                // The centre line, looking a little ahead as the original did.
                BlockType centre = blockAt(from + ahead + up);
                if (!passable(centre))
                    obstacles.Add(centre);

                // The corners of the footprint. A corner that is already inside a block (someone built next
                // to us, or we fell past an edge) doesn't count, so the player can always move back out.
                foreach (Vector2 corner in Corners)
                {
                    Vector3 offset = up + new Vector3(corner.X * Radius, 0, corner.Y * Radius);
                    BlockType block = blockAt(to + offset);
                    if (!passable(block) && passable(blockAt(from + offset)))
                        obstacles.Add(block);
                }
            }
            return obstacles;
        }
    }
}
