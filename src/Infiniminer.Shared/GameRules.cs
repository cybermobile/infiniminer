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

        // In a sandbox everyone also carries the deconstruction gun, to take away any block.
        public static PlayerTools[] GetTools(PlayerClass playerClass, bool sandbox)
        {
            PlayerTools[] tools = GetTools(playerClass);
            return sandbox && !tools.Contains(PlayerTools.DeconstructionGun) ? tools.Append(PlayerTools.DeconstructionGun).ToArray() : tools;
        }

        public static PlayerTools[] GetTools(PlayerClass playerClass)
        {
            switch (playerClass)
            {
                case PlayerClass.Prospector:
                    return new[] { PlayerTools.Pickaxe, PlayerTools.ConstructionGun, PlayerTools.ProspectingRadar };
                case PlayerClass.Miner:
                    return new[] { PlayerTools.Pickaxe, PlayerTools.Drill, PlayerTools.ConstructionGun };
                case PlayerClass.Engineer:
                    return new[] { PlayerTools.Pickaxe, PlayerTools.ConstructionGun, PlayerTools.DeconstructionGun };
                case PlayerClass.Sapper:
                    return new[] { PlayerTools.Pickaxe, PlayerTools.ConstructionGun, PlayerTools.Detonator };
            }
            return new[] { PlayerTools.Pickaxe };
        }

        // Sandbox games are for building maps: everyone can also place the world's own blocks, for free.
        public static readonly BlockType[] EditorBlocks =
        {
            BlockType.Dirt, BlockType.Rock, BlockType.Ore, BlockType.Gold, BlockType.Diamond, BlockType.Lava, BlockType.Metal
        };

        public static BlockType[] GetBlocks(PlayerClass playerClass, PlayerTeam team, bool sandbox)
        {
            BlockType[] blocks = GetBlocks(playerClass, team);
            return sandbox ? blocks.Concat(EditorBlocks).ToArray() : blocks;
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
                                   red ? BlockType.BankRed : BlockType.BankBlue,
                                   BlockType.Teleporter };
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
                case PlayerTools.Drill: return 0.9f;
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

        // What digging a block with the pickaxe (or the drill) gives. Gold and diamonds add weight and are
        // left in place when the digger can't carry any more.
        public static DigResult Dig(BlockType blockType, bool drill)
        {
            DigResult result;
            switch (blockType)
            {
                case BlockType.Dirt:
                case BlockType.DirtSign:
                    result = new DigResult(true, 0, 0, 0, InfiniminerSound.DigDirt);
                    break;
                case BlockType.Ore:
                    result = new DigResult(true, 20, 0, 0, InfiniminerSound.DigMetal);
                    break;
                case BlockType.Gold:
                    result = new DigResult(true, 0, 100, 1, InfiniminerSound.DigMetal);
                    break;
                case BlockType.Diamond:
                    result = new DigResult(true, 0, 1000, 1, InfiniminerSound.DigMetal);
                    break;
                case BlockType.Rock:
                    result = new DigResult(drill, 0, 0, 0, InfiniminerSound.DigDirt);
                    break;
                default:
                    result = new DigResult(false, 0, 0, 0, InfiniminerSound.DigDirt);
                    break;
            }
            return drill ? result with { Sound = InfiniminerSound.Drill } : result;
        }

        // Whether a dig removes the block for a player carrying `weight` of at most `maxWeight`.
        public static bool DigRemovesBlock(BlockType blockType, bool drill, uint weight, uint maxWeight)
        {
            DigResult result = Dig(blockType, drill);
            return result.Removes && (result.Weight == 0 || weight < maxWeight);
        }
    }

    public readonly record struct DigResult(bool Removes, uint Ore, uint Cash, uint Weight, InfiniminerSound Sound);
}
