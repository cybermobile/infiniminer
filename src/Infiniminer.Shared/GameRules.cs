namespace Infiniminer
{
    // Rules shared by the client (which plays by them) and the server (which enforces them).
    public static class GameRules
    {
        // Player physics, as simulated by the client.
        public const float MoveSpeed = 3.5f;
        public const float RoadSpeedMultiplier = 2;
        public const float Gravity = -8.0f;
        public const float JumpVelocity = 4.0f;
        public const float JumpBlockVelocity = 2.5f * JumpVelocity;
        public const float ClimbVelocity = 2.5f;
        public const float DieVelocity = 15.0f;

        // Offsets from the player's eye position to the points used for collision.
        public const float HeadOffset = 0.1f;
        public const float MidBodyOffset = -0.7f;
        public const float LowerBodyOffset = -1.4f;
        public const float FootOffset = -1.5f;

        public static PlayerTools[] GetTools(PlayerClass playerClass)
        {
            switch (playerClass)
            {
                case PlayerClass.Prospector:
                    return new[] { PlayerTools.Pickaxe, PlayerTools.ConstructionGun, PlayerTools.ProspectingRadar };
                case PlayerClass.Miner:
                    return new[] { PlayerTools.Pickaxe, PlayerTools.ConstructionGun };
                case PlayerClass.Engineer:
                    return new[] { PlayerTools.Pickaxe, PlayerTools.ConstructionGun, PlayerTools.DeconstructionGun };
                case PlayerClass.Sapper:
                    return new[] { PlayerTools.Pickaxe, PlayerTools.ConstructionGun, PlayerTools.Detonator };
            }
            return new[] { PlayerTools.Pickaxe };
        }

        public static BlockType[] GetBlocks(PlayerClass playerClass, PlayerTeam team)
        {
            bool red = team == PlayerTeam.Red;
            switch (playerClass)
            {
                case PlayerClass.Prospector:
                    return new[] { red ? BlockType.SolidRed : BlockType.SolidBlue,
                                   red ? BlockType.TransRed : BlockType.TransBlue,
                                   red ? BlockType.BeaconRed : BlockType.BeaconBlue,
                                   BlockType.Ladder };
                case PlayerClass.Miner:
                    return new[] { red ? BlockType.SolidRed : BlockType.SolidBlue,
                                   red ? BlockType.TransRed : BlockType.TransBlue,
                                   BlockType.Ladder };
                case PlayerClass.Engineer:
                    return new[] { red ? BlockType.SolidRed : BlockType.SolidBlue,
                                   BlockType.TransRed,
                                   BlockType.TransBlue,
                                   BlockType.Road,
                                   BlockType.Ladder,
                                   BlockType.Jump,
                                   BlockType.Shock,
                                   red ? BlockType.BeaconRed : BlockType.BeaconBlue,
                                   red ? BlockType.BankRed : BlockType.BankBlue };
                case PlayerClass.Sapper:
                    return new[] { red ? BlockType.SolidRed : BlockType.SolidBlue,
                                   red ? BlockType.TransRed : BlockType.TransBlue,
                                   BlockType.Ladder,
                                   BlockType.Explosive };
            }
            return new[] { BlockType.None };
        }

        public static float GetToolCooldown(PlayerTools tool, PlayerClass playerClass)
        {
            switch (tool)
            {
                // Miners dig much faster than the other classes.
                case PlayerTools.Pickaxe: return playerClass == PlayerClass.Miner ? 0.55f * 0.4f : 0.55f;
                case PlayerTools.Detonator: return 0.01f;
                case PlayerTools.ConstructionGun: return 0.5f;
                case PlayerTools.DeconstructionGun: return 0.5f;
                case PlayerTools.ProspectingRadar: return 0.5f;
                default: return 0;
            }
        }

        // Blocks the player can walk through: air, and force fields of their own team.
        public static bool IsPassable(BlockType blockType, PlayerTeam team)
        {
            return blockType == BlockType.None
                || (team == PlayerTeam.Red && blockType == BlockType.TransRed)
                || (team == PlayerTeam.Blue && blockType == BlockType.TransBlue);
        }
    }
}
