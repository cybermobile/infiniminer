using LiteNetLib.Utils;
using Microsoft.Xna.Framework;
using Xunit;

namespace Infiniminer.Tests
{
    // Client logic that doesn't need a window or a graphics device.
    public class ClientLogicTests
    {
        [Fact]
        public void RadarFindsTheNearestTreasureAndTheBestValue()
        {
            // Looking along -Z from z = 0: gold 10 m ahead, a diamond 20 m ahead.
            BlockType World(Vector3 p)
            {
                int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y), z = (int)Math.Floor(p.Z);
                if (x == 0 && y == 0 && z == -11) return BlockType.Gold;
                if (x == 0 && y == 0 && z == -21) return BlockType.Diamond;
                return BlockType.None;
            }
            Vector3 eye = new Vector3(0.5f, 0.5f, 0);

            var (distance, value) = Radar.Scan(World, eye, -Vector3.UnitZ);
            Assert.InRange(distance, 10, 10.5f);
            Assert.Equal(1000, value);

            // Facing away, there is nothing to find.
            Assert.Equal((Radar.Range, 0f), Radar.Scan(World, eye, Vector3.UnitZ));
        }

        [Fact]
        public void ChatShowsNewestFirstAndFadesOut()
        {
            ChatLog log = new ChatLog();
            log.Add("first", ChatMessageType.SayAll);
            log.Update(4);
            log.Add("second", ChatMessageType.SayRedTeam);
            Assert.Equal(new[] { "second", "first" }, log.Messages.Select(m => m.message));

            log.Update(ChatLog.Lifetime - 4);
            Assert.Equal(new[] { "second" }, log.Messages.Select(m => m.message));
            log.Update(4);
            Assert.Empty(log.Messages);
        }

        [Fact]
        public void PositionalSoundsArePannedAndFadeWithDistance()
        {
            Vector3 right = Vector3.UnitX;

            var (rightVolume, rightPan) = SoundPlayer.Positional(new Vector3(3, 0, 0), right, 1);
            var (leftVolume, leftPan) = SoundPlayer.Positional(new Vector3(-3, 0, 0), right, 1);
            Assert.True(rightPan > 0.5f && leftPan < -0.5f);
            Assert.Equal(rightVolume, leftVolume, 3);
            Assert.Equal(0.7f, rightVolume, 3);

            // Straight ahead is centred; far away is silent; the master volume scales everything.
            Assert.Equal(0, SoundPlayer.Positional(new Vector3(0, 0, -3), right, 1).pan, 3);
            Assert.Equal(0, SoundPlayer.Positional(new Vector3(0, 0, -SoundPlayer.HearingDistance - 1), right, 1).volume);
            Assert.Equal(0.35f, SoundPlayer.Positional(new Vector3(3, 0, 0), right, 0.5f).volume, 3);
        }

        [Fact]
        public void DigRulesMatchTheOriginalGame()
        {
            Assert.Equal(new DigResult(true, 20, 0, 0, InfiniminerSound.DigMetal), GameRules.Dig(BlockType.Ore, false));
            Assert.Equal(new DigResult(true, 0, 1000, 1, InfiniminerSound.DigMetal), GameRules.Dig(BlockType.Diamond, false));
            Assert.True(GameRules.Dig(BlockType.Dirt, false).Removes);
            Assert.False(GameRules.Dig(BlockType.Rock, false).Removes);
            Assert.True(GameRules.Dig(BlockType.Rock, true).Removes);
            Assert.Equal(InfiniminerSound.Drill, GameRules.Dig(BlockType.Dirt, true).Sound);
            Assert.False(GameRules.Dig(BlockType.Metal, true).Removes);

            // Treasure stays put when the digger can't carry more; dirt doesn't care.
            Assert.True(GameRules.DigRemovesBlock(BlockType.Gold, false, 3, 4));
            Assert.False(GameRules.DigRemovesBlock(BlockType.Gold, false, 4, 4));
            Assert.True(GameRules.DigRemovesBlock(BlockType.Dirt, false, 4, 4));
        }

        [Fact]
        public void ClientMessagesUseTheWireFormatTheServerReads()
        {
            OutgoingMessage update = ClientMessages.PlayerUpdate(new System.Numerics.Vector3(1, 2, 3), -System.Numerics.Vector3.UnitZ,
                PlayerTools.Drill, true, 7);
            Assert.Equal(InfiniminerMessage.PlayerUpdate, update.Type);
            Assert.Equal(NetChannel.UnreliableInOrder1.Method, update.Channel.Method);
            NetDataReader reader = new NetDataReader(update.Data.CopyData());
            reader.GetByte();
            Assert.Equal(new System.Numerics.Vector3(1, 2, 3), reader.GetVector3());
            Assert.Equal(-System.Numerics.Vector3.UnitZ, reader.GetVector3());
            Assert.Equal(PlayerTools.Drill, (PlayerTools)reader.GetByte());
            Assert.True(reader.GetBool());
            Assert.Equal(7, reader.GetByte());
            Assert.True(reader.EndOfData);

            reader = new NetDataReader(ClientMessages.Chat(ChatMessageType.SayBlueTeam, "hi").Data.CopyData());
            Assert.Equal(InfiniminerMessage.ChatMessage, (InfiniminerMessage)reader.GetByte());
            Assert.Equal(ChatMessageType.SayBlueTeam, (ChatMessageType)reader.GetByte());
            Assert.Equal("hi", reader.GetString());

            // Team, class and respawn requests share one ordered channel, so they can't overtake each other.
            Assert.Equal(ClientMessages.SetTeam(PlayerTeam.Red).Channel.Number, ClientMessages.Respawn().Channel.Number);
            Assert.Equal(ClientMessages.SelectClass(PlayerClass.Miner).Channel.Number, ClientMessages.Respawn().Channel.Number);
        }

        [Fact]
        public void FacesPointingAwayFromTheCameraAreSkipped()
        {
            // A region from (16,16,16) to (32,32,32).
            Vector3 min = new Vector3(16);
            bool Visible(BlockFaceDirection direction, float x, float y, float z) => BlockEngine.FacesVisibleFrom(direction, min, 16, new Vector3(x, y, z));

            // West of it, only its west-facing (-X) sides can be seen; east of it, only the east-facing ones.
            Assert.False(Visible(BlockFaceDirection.XIncreasing, 10, 20, 20));
            Assert.True(Visible(BlockFaceDirection.XDecreasing, 10, 20, 20));
            Assert.True(Visible(BlockFaceDirection.XIncreasing, 40, 20, 20));
            Assert.False(Visible(BlockFaceDirection.XDecreasing, 40, 20, 20));

            // Inside it, every direction may be visible.
            foreach (BlockFaceDirection direction in Enum.GetValues<BlockFaceDirection>().Where(d => d != BlockFaceDirection.MAXIMUM))
                Assert.True(Visible(direction, 24, 24, 24), direction.ToString());

            // Above it, the tops (+Y) but not the undersides.
            Assert.True(Visible(BlockFaceDirection.YIncreasing, 20, 50, 20));
            Assert.False(Visible(BlockFaceDirection.YDecreasing, 20, 50, 20));
        }

        [Fact]
        public void EveryGameStateCanBeCreatedByTheStateMachine()
        {
            Type[] states = typeof(InfiniminerGame).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(StateMasher.State)) && !t.IsAbstract).ToArray();
            Assert.Contains(typeof(States.MainGameState), states);
            foreach (Type state in states)
                Assert.NotNull(state.GetConstructor(Type.EmptyTypes));
        }
    }
}
