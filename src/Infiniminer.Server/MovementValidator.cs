using System.Numerics;

namespace Infiniminer
{
    // A token bucket: allows short bursts but caps the sustained rate.
    public sealed class RateLimiter
    {
        readonly float capacity;
        readonly float perSecond;
        float tokens;
        double lastTime = double.NaN;

        public RateLimiter(float capacity, float perSecond)
        {
            this.capacity = capacity;
            this.perSecond = perSecond;
            tokens = capacity;
        }

        public bool TryTake(double now)
        {
            if (!double.IsNaN(lastTime))
                tokens = Math.Min(capacity, tokens + (float)(now - lastTime) * perSecond);
            lastTime = now;
            if (tokens < 1)
                return false;
            tokens -= 1;
            return true;
        }
    }

    // What the server knows about a player's movement since their last accepted position.
    public sealed class MovementState
    {
        public Vector3 Position;
        public double LastTime;
        public float HorizontalBudget;
        public float UpwardBudget;
        public float GroundY;           // height of the last position where the player was supported
        public double LastSupportedTime;
        public float ApexY;             // highest point since the player was last supported (for fall damage)
        public bool Airborne;

        public void Reset(Vector3 position, double now)
        {
            Position = position;
            LastTime = now;
            HorizontalBudget = MovementValidator.MaxBurst / 2;
            UpwardBudget = MovementValidator.MaxBurst / 2;
            GroundY = position.Y;
            LastSupportedTime = now;
            ApexY = position.Y;
            Airborne = true;
        }
    }

    public struct MovementResult
    {
        public bool Accepted;
        public string Reason;
        public float FallHeight; // set when the player lands; 0 otherwise
        public Vector3? BlockedAt; // the point that collided, when rejected for being inside a block
    }

    // Checks player position updates against what the client's physics can actually do. Limits are
    // deliberately generous so that legitimate players on laggy connections are not corrected.
    public sealed class MovementValidator
    {
        public const float MaxHorizontalSpeed = 8f;        // walking on road is 7
        public const float MaxUpwardSpeed = 12f;           // jump blocks launch at 10
        public const float MaxBurst = 6f;                  // movement that can be banked during a lag spike
        public const float MaxHeightAboveSupport = 7.5f;   // a jump block reaches 6.25 blocks
        public const float MaxHoverTime = 3f;              // seconds airborne without actually falling
        public const float LethalFallHeight = 15.5f;       // the client dies at 15 m/s, i.e. ~14 blocks
        const float SweepThreshold = 0.9f;                 // longer moves are checked along their path
        const float SweepStep = 0.25f;

        readonly Func<int, int, int, BlockType> getBlock;

        public MovementValidator(Func<int, int, int, BlockType> getBlock)
        {
            this.getBlock = getBlock;
        }

        // The outermost layer of the map is never loaded by the client (players can pass through it),
        // so it counts as air here too.
        BlockType BlockAt(Vector3 point)
        {
            int x = GameConstants.ToBlock(point.X), y = GameConstants.ToBlock(point.Y), z = GameConstants.ToBlock(point.Z);
            if (x <= 0 || y <= 0 || z <= 0 || x >= GameConstants.MapSize - 1 || y >= GameConstants.MapSize - 1 || z >= GameConstants.MapSize - 1)
                return BlockType.None;
            return getBlock(x, y, z);
        }

        string Describe(Vector3 point)
        {
            return FormattableString.Invariant($"{BlockAt(point)} at {GameConstants.ToBlock(point.X)},{GameConstants.ToBlock(point.Y)},{GameConstants.ToBlock(point.Z)}");
        }

        bool SolidFor(Vector3 point, PlayerTeam team)
        {
            return !GameRules.IsPassable(BlockAt(point), team);
        }

        // Standing on something, or next to a ladder (climbing).
        public bool IsSupported(Vector3 position, PlayerTeam team)
        {
            if (SolidFor(position + new Vector3(0, GameRules.FootOffset, 0), team))
                return true;
            foreach (float offset in new[] { 0f, GameRules.MidBodyOffset, GameRules.LowerBodyOffset })
                foreach (Vector3 side in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                    if (BlockAt(position + new Vector3(0, offset, 0) + side * 0.6f) == BlockType.Ladder)
                        return true;
            return false;
        }

        public MovementResult Validate(MovementState state, PlayerTeam team, Vector3 newPosition, double now)
        {
            if (!float.IsFinite(newPosition.X) || !float.IsFinite(newPosition.Y) || !float.IsFinite(newPosition.Z))
                return new MovementResult { Reason = "invalid position" };

            // Refill the movement budgets for the time that has passed.
            float elapsed = (float)Math.Max(0, now - state.LastTime);
            state.LastTime = now;
            state.HorizontalBudget = Math.Min(MaxBurst, state.HorizontalBudget + MaxHorizontalSpeed * elapsed);
            state.UpwardBudget = Math.Min(MaxBurst, state.UpwardBudget + MaxUpwardSpeed * elapsed);

            Vector3 delta = newPosition - state.Position;
            float horizontal = new Vector2(delta.X, delta.Z).Length();
            float upward = Math.Max(0, delta.Y);
            if (horizontal > state.HorizontalBudget)
                return new MovementResult { Reason = "moving too fast" };
            if (upward > state.UpwardBudget)
                return new MovementResult { Reason = "rising too fast" };

            // The middle of the body never overlaps a block in normal play; checking it (and the path to it
            // for longer moves) stops walking or falling through walls.
            Vector3 midBody = new Vector3(0, GameRules.MidBodyOffset, 0);
            if (SolidFor(newPosition + midBody, team))
                return new MovementResult { Reason = "inside a block (" + Describe(newPosition + midBody) + ")", BlockedAt = newPosition + midBody };
            float distance = delta.Length();
            if (distance >= SweepThreshold)
            {
                for (float t = SweepStep; t < distance; t += SweepStep)
                {
                    Vector3 point = state.Position + delta * (t / distance) + midBody;
                    if (SolidFor(point, team))
                        return new MovementResult { Reason = "moved through a block (" + Describe(point) + ")", BlockedAt = point };
                }
            }

            bool supported = IsSupported(newPosition, team);
            if (!supported)
            {
                if (newPosition.Y > state.GroundY + MaxHeightAboveSupport)
                    return new MovementResult { Reason = "flying" };
                if (now - state.LastSupportedTime > MaxHoverTime && newPosition.Y > state.GroundY - 2)
                    return new MovementResult { Reason = "hovering" };
            }

            // Accepted: consume the budgets and track ground contact and falls.
            state.HorizontalBudget -= horizontal;
            state.UpwardBudget -= upward;
            state.Position = newPosition;

            MovementResult result = new MovementResult { Accepted = true };
            if (supported)
            {
                if (state.Airborne)
                    result.FallHeight = Math.Max(0, state.ApexY - newPosition.Y);
                state.Airborne = false;
                state.GroundY = newPosition.Y;
                state.ApexY = newPosition.Y;
                state.LastSupportedTime = now;
            }
            else
            {
                state.Airborne = true;
                state.ApexY = Math.Max(state.ApexY, newPosition.Y);
            }
            return result;
        }
    }
}
