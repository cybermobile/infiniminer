using System.Numerics;
using Xunit;

namespace Infiniminer.Tests
{
    public class MovementValidatorTests
    {
        // Flat dirt floor with its top at y = 10, a wall at x = 20 (heights 11-13) and a ladder at x = 30.
        const int Floor = 10;
        const float StandingY = Floor + 2.45f; // eye height when standing on the floor
        const double Tick = 0.05;              // clients send 20 updates a second

        static BlockType World(int x, int y, int z)
        {
            if (y <= Floor)
                return BlockType.Dirt;
            if (x == 20 && y <= Floor + 3)
                return BlockType.Rock;
            if (x == 30 && y <= Floor + 20)
                return BlockType.Ladder;
            return BlockType.None;
        }

        readonly MovementValidator validator = new MovementValidator(World);

        MovementState StartAt(Vector3 position, double now = 0)
        {
            MovementState state = new MovementState();
            state.Reset(position, now);
            return state;
        }

        [Fact]
        public void WalkingOnRoadIsAccepted()
        {
            MovementState state = StartAt(new Vector3(2.5f, StandingY, 5.5f));
            double now = 0;
            for (int i = 1; i <= 40; i++)
            {
                now += Tick;
                Vector3 next = new Vector3(2.5f + i * 7f * (float)Tick, StandingY, 5.5f);
                Assert.True(validator.Validate(state, PlayerTeam.Red, next, now).Accepted, "step " + i);
            }
        }

        [Fact]
        public void JumpBlockArcIsAcceptedAndHarmless()
        {
            MovementState state = StartAt(new Vector3(5.5f, StandingY, 5.5f));
            float y = StandingY, velocity = GameRules.JumpBlockVelocity;
            double now = 0;
            float maxFall = 0;
            do
            {
                now += Tick;
                velocity += GameRules.Gravity * (float)Tick;
                y = Math.Max(StandingY, y + velocity * (float)Tick);
                MovementResult result = validator.Validate(state, PlayerTeam.Red, new Vector3(5.5f, y, 5.5f), now);
                Assert.True(result.Accepted, result.Reason);
                maxFall = Math.Max(maxFall, result.FallHeight);
            }
            while (y > StandingY);
            Assert.InRange(maxFall, 5f, MovementValidator.LethalFallHeight - 1);
        }

        [Fact]
        public void BunchedUpdatesAfterALagSpikeAreAccepted()
        {
            MovementState state = StartAt(new Vector3(2.5f, StandingY, 5.5f));
            double now = 0.6; // nothing arrived for 0.6 seconds...
            for (int i = 1; i <= 12; i++)
            {
                // ...then twelve walking updates arrive at once.
                Vector3 next = new Vector3(2.5f + i * 0.35f, StandingY, 5.5f);
                Assert.True(validator.Validate(state, PlayerTeam.Red, next, now).Accepted, "update " + i);
            }
        }

        [Fact]
        public void TeleportingIsRejected()
        {
            MovementState state = StartAt(new Vector3(2.5f, StandingY, 5.5f));
            MovementResult result = validator.Validate(state, PlayerTeam.Red, new Vector3(15.5f, StandingY, 5.5f), Tick);
            Assert.False(result.Accepted);
            Assert.Equal("moving too fast", result.Reason);
            Assert.Equal(new Vector3(2.5f, StandingY, 5.5f), state.Position);
        }

        [Fact]
        public void WalkingIntoAWallIsRejected()
        {
            MovementState state = StartAt(new Vector3(19.8f, StandingY, 5.5f));
            MovementResult result = validator.Validate(state, PlayerTeam.Red, new Vector3(20.2f, StandingY, 5.5f), Tick);
            Assert.False(result.Accepted);
            Assert.StartsWith("inside a block", result.Reason);
        }

        [Fact]
        public void SkippingThroughAWallIsRejected()
        {
            MovementState state = StartAt(new Vector3(19.6f, StandingY, 5.5f));
            MovementResult result = validator.Validate(state, PlayerTeam.Red, new Vector3(21.2f, StandingY, 5.5f), 0.3);
            Assert.False(result.Accepted);
            Assert.StartsWith("moved through a block", result.Reason);
        }

        [Fact]
        public void OwnForceFieldsCanBeWalkedThrough()
        {
            BlockType WithForceField(int x, int y, int z) => x == 3 && y == Floor + 1 ? BlockType.TransRed : World(x, y, z);
            MovementValidator withField = new MovementValidator(WithForceField);

            MovementState red = StartAt(new Vector3(2.9f, StandingY, 5.5f));
            Assert.True(withField.Validate(red, PlayerTeam.Red, new Vector3(3.2f, StandingY, 5.5f), Tick).Accepted);

            MovementState blue = StartAt(new Vector3(2.9f, StandingY, 5.5f));
            Assert.False(withField.Validate(blue, PlayerTeam.Blue, new Vector3(3.2f, StandingY, 5.5f), Tick).Accepted);
        }

        [Fact]
        public void TheMapBorderIsPassableLikeOnTheClient()
        {
            // The client never loads the outermost layer of blocks, so players can be inside it.
            MovementValidator solidWorld = new MovementValidator((x, y, z) => BlockType.Dirt);
            MovementState state = StartAt(new Vector3(0.5f, 64.7f, 30.5f));
            Assert.True(solidWorld.Validate(state, PlayerTeam.Red, new Vector3(0.5f, 64.5f, 30.5f), Tick).Accepted);
        }

        [Fact]
        public void FlyingIsRejected()
        {
            MovementState state = StartAt(new Vector3(5.5f, StandingY, 5.5f));
            double now = 0;
            MovementResult result = default;
            for (int i = 1; i <= 40 && (i == 1 || result.Accepted); i++)
            {
                now += Tick;
                result = validator.Validate(state, PlayerTeam.Red, new Vector3(5.5f, StandingY + i * 0.5f, 5.5f), now);
            }
            Assert.False(result.Accepted);
            Assert.Equal("flying", result.Reason);
        }

        [Fact]
        public void ClimbingALadderIsAccepted()
        {
            MovementState state = StartAt(new Vector3(29.4f, StandingY, 5.5f));
            double now = 0;
            for (int i = 1; i <= 100; i++)
            {
                now += Tick;
                Vector3 next = new Vector3(29.4f, StandingY + i * GameRules.ClimbVelocity * (float)Tick, 5.5f);
                MovementResult result = validator.Validate(state, PlayerTeam.Red, next, now);
                Assert.True(result.Accepted, "step " + i + ": " + result.Reason);
            }
        }

        [Fact]
        public void HoveringIsRejected()
        {
            MovementState state = StartAt(new Vector3(5.5f, StandingY, 5.5f));
            Assert.True(validator.Validate(state, PlayerTeam.Red, new Vector3(5.5f, StandingY + 1, 5.5f), Tick).Accepted);
            double now = Tick;
            MovementResult result;
            do
            {
                now += Tick;
                result = validator.Validate(state, PlayerTeam.Red, new Vector3(5.5f, StandingY + 1, 5.5f), now);
            }
            while (result.Accepted && now < 10);
            Assert.False(result.Accepted);
            Assert.Equal("hovering", result.Reason);
        }

        [Fact]
        public void LongFallsAreReportedOnLanding()
        {
            MovementState state = StartAt(new Vector3(5.5f, 40, 5.5f));
            double now = 0;
            float y = 40, velocity = 0;
            MovementResult result;
            do
            {
                now += Tick;
                velocity += GameRules.Gravity * (float)Tick;
                y = Math.Max(StandingY, y + velocity * (float)Tick);
                result = validator.Validate(state, PlayerTeam.Red, new Vector3(5.5f, y, 5.5f), now);
                Assert.True(result.Accepted, result.Reason);
            }
            while (y > StandingY);
            Assert.True(result.FallHeight >= MovementValidator.LethalFallHeight);
        }

        [Fact]
        public void RateLimiterAllowsBurstsButCapsTheRate()
        {
            RateLimiter limiter = new RateLimiter(2, 1);
            Assert.True(limiter.TryTake(0));
            Assert.True(limiter.TryTake(0));
            Assert.False(limiter.TryTake(0.5));
            Assert.True(limiter.TryTake(1.0));
            Assert.False(limiter.TryTake(1.1));
        }
    }
}
