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
