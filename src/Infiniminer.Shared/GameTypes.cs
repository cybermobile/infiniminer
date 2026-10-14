namespace Infiniminer
{
    // These enums are part of the wire protocol: their numeric values are sent over the network,
    // so new members must only ever be appended.

    public enum BlockType : byte
    {
        None,
        Dirt,
        Ore,
        Gold,
        Diamond,
        Rock,
        Ladder,
        Explosive,
        Jump,
        Shock,
        BankRed,
        BankBlue,
        BeaconRed,
        BeaconBlue,
        Road,
        SolidRed,
        SolidBlue,
        Metal,
        DirtSign,
        Lava,
        TransRed,
        TransBlue,
        Teleporter,     // v1.8: linked team teleporters
        HomeRed,        // v1.8: team home bases
        HomeBlue,
        MAXIMUM
    }

    public enum PlayerClass : byte
    {
        Prospector,
        Miner,
        Engineer,
        Sapper
    }

    public enum PlayerTools : byte
    {
        Pickaxe,
        ConstructionGun,
        DeconstructionGun,
        ProspectingRadar,
        Detonator,
        Drill,          // v1.8: digs rock
    }

    public enum PlayerTeam : byte
    {
        None,
        Red,
        Blue
    }

    public enum ChatMessageType : byte
    {
        None,
        SayAll,
        SayRedTeam,
        SayBlueTeam,
    }

    public enum InfiniminerSound : byte
    {
        DigDirt,
        DigMetal,
        Ping,
        ConstructionGun,
        Death,
        CashDeposit,
        ClickHigh,
        ClickLow,
        GroundHit,
        Teleporter,
        Jumpblock,
        Explosion,
        RadarLow,
        RadarHigh,
        RadarSwitch,
        Drill,
    }

    public enum InfiniminerMessage : byte
    {
        BlockBulkTransfer,      // x-value, y-value, then 16 rows of blocktypes (16 * map size bytes), deflate-compressed
        BlockSet,               // x, y, z, type
        UseTool,                // position, heading, tool, blocktype
        SelectClass,            // class
        ResourceUpdate,         // ore, cash, weight, max ore, max weight, team ore, red cash, blue cash
        DepositOre,
        DepositCash,
        WithdrawOre,
        TriggerExplosion,       // position

        PlayerUpdate,           // (uint id for server), position, heading, current tool, animate using (bool)
        PlayerJoined,           // uint id, player name, bool thisIsMe, bool alive
        PlayerLeft,             // uint id
        PlayerSetTeam,          // (uint id for server), byte team
        PlayerDead,             // (uint id for server) / death message from the client
        PlayerAlive,            // (uint id for server)
        PlayerPing,             // uint id

        ChatMessage,            // byte type, string message
        GameOver,               // byte team
        PlaySound,              // byte sound, bool isPositional, ?Vector3 location
        TriggerConstructionGunAnimation,
        SetBeacon,              // vector3 position, string text ("" means remove), byte team
        PlayerSetPosition,      // vector3 position, byte epoch, byte reason (PositionReason): the server moves the player
        MatchInfo,              // int seconds left (-1: no time limit, 0: overtime), uint cash needed to win
        WorldInfo,              // ushort map size; sent before the map
    }

    public enum PositionReason : byte
    {
        Respawn,
        Correction,
        Teleport,
    }

    public class Beacon
    {
        public string ID;
        public PlayerTeam Team;
    }

    public static class GameConstants
    {
        // v1.6: new network transport (LiteNetLib). v1.7: server-authoritative movement and respawns.
        // v1.8: teleporters, drill, home bases. v1.9: variable map size, compressed map transfer.
        public const string Version = "v1.9";
        public const string AppIdentifier = "InfiniminerPlus";
        public const int DefaultPort = 5565;
        public const int MapSize = 64;          // the default (and original) size of the world cube
        public static readonly int[] MapSizes = { 64, 96, 128 }; // the cave generator needs multiples of 32; coordinates are sent as bytes
        public const int GroundLevel = 8;

        public const string DiscoveryRequest = "INFINIMINER_DISCOVER";
        public const string DiscoveryResponse = "INFINIMINER_SERVER";

        // Converts a world-space coordinate into a block index. Using Floor keeps negative positions
        // out of range on every CPU architecture (a raw (ushort) cast of a negative float is not portable).
        public static int ToBlock(float coordinate)
        {
            return (int)MathF.Floor(coordinate);
        }
    }

    public static class BlockInformation
    {
        public static uint GetCost(BlockType blockType)
        {
            switch (blockType)
            {
                case BlockType.BankRed:
                case BlockType.BankBlue:
                case BlockType.BeaconRed:
                case BlockType.BeaconBlue:
                    return 50;

                case BlockType.SolidRed:
                case BlockType.SolidBlue:
                    return 10;

                case BlockType.TransRed:
                case BlockType.TransBlue:
                    return 25;

                case BlockType.Road:
                    return 10;
                case BlockType.Jump:
                    return 25;
                case BlockType.Ladder:
                    return 25;
                case BlockType.Shock:
                    return 50;
                case BlockType.Explosive:
                    return 100;
                case BlockType.Teleporter:
                    return 200; // the price on the original teleporter icon

            }

            return 1000;
        }

        // Block types that a construction gun is allowed to place. Every class's build list only contains
        // these, so the server rejects anything else (e.g. a modified client trying to place gold or lava).
        public static bool IsBuildable(BlockType blockType)
        {
            switch (blockType)
            {
                case BlockType.BankRed:
                case BlockType.BankBlue:
                case BlockType.BeaconRed:
                case BlockType.BeaconBlue:
                case BlockType.SolidRed:
                case BlockType.SolidBlue:
                case BlockType.TransRed:
                case BlockType.TransBlue:
                case BlockType.Road:
                case BlockType.Jump:
                case BlockType.Ladder:
                case BlockType.Shock:
                case BlockType.Explosive:
                case BlockType.Teleporter:
                    return true;
            }
            return false;
        }
    }
}
