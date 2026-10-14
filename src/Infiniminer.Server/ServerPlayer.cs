using System.Numerics;
using LiteNetLib;

namespace Infiniminer
{
    public class ServerPlayer
    {
        public bool Kicked = false; // set to true when a player is kicked to let other clients know they were kicked
        public string Handle = "";
        public uint OreMax = 0;
        public uint WeightMax = 0;
        public uint Ore = 0;
        public uint Weight = 0;
        public uint Cash = 0;
        public bool Alive = false;
        public List<Vector3> ExplosiveList = new List<Vector3>();
        public uint ID;
        public Vector3 Position = Vector3.Zero;
        public Vector3 Heading = Vector3.Zero;
        public NetPeer Peer;
        public uint Score = 0;
        public string IP = "";
        public PlayerTeam Team = PlayerTeam.None;
        public PlayerTools Tool = PlayerTools.Pickaxe;
        public bool UsingTool = false;

        // This is used to force an update that says the player is not using their tool, thus causing a break
        // in their tool usage animation.
        public bool QueueAnimationBreak = false;

        // Server-side bookkeeping used to enforce the rules.
        public PlayerClass Class = PlayerClass.Miner;
        public bool HasClass = false;
        public byte PositionEpoch = 0;     // bumped whenever the server moves the player; stale updates are ignored
        public readonly MovementState Movement = new MovementState();
        public readonly RateLimiter ChatLimiter = new RateLimiter(5, 1);
        public readonly RateLimiter PingLimiter = new RateLimiter(2, 1);
        public readonly RateLimiter SoundLimiter = new RateLimiter(4, 4);
        public Dictionary<PlayerTools, RateLimiter> ToolLimiters = new Dictionary<PlayerTools, RateLimiter>();
        public double LastCorrectionLog = double.MinValue;
        public (int x, int y, int z)? OnTeleporter;   // the teleporter the player is standing on, if any
        public bool IsAdmin = false;
        public readonly RateLimiter LoginLimiter = new RateLimiter(3, 1 / 20f);   // three tries, then one every 20 s

        public void SetClass(PlayerClass playerClass, bool sandbox = false)
        {
            Class = playerClass;
            HasClass = true;
            ToolLimiters = new Dictionary<PlayerTools, RateLimiter>();
            foreach (PlayerTools tool in GameRules.GetTools(playerClass, sandbox))
            {
                // Allow a small burst (packets can arrive bunched up) but not a faster sustained rate.
                float cooldown = Math.Max(0.01f, GameRules.GetToolCooldown(tool, playerClass));
                ToolLimiters[tool] = new RateLimiter(2, 1 / cooldown);
            }
        }

        public ServerPlayer(NetPeer peer, string ip)
        {
            Peer = peer;
            IP = ip;
            ID = GetUniqueId();
        }

        static uint uniqueId = 0;
        static uint GetUniqueId()
        {
            return Interlocked.Increment(ref uniqueId);
        }
    }
}
