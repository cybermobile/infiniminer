using Microsoft.Xna.Framework;

namespace Infiniminer
{
    // The player at this computer: where they are, what they carry and what they have equipped.
    // (Other players are Player objects in PropertyBag.playerList.)
    public class LocalPlayer
    {
        public uint Id = 0;
        public string Handle = "Player";
        public Vector3 Position = Vector3.Zero;
        public Vector3 Velocity = Vector3.Zero;
        public PlayerTeam Team = PlayerTeam.Red;
        public PlayerClass Class;
        public bool HasClass;              // chosen a class on this server
        public bool Spectating;            // watching: no team, flying freely
        public bool Dead = true;
        public bool RespawnPending = false;
        public byte PositionEpoch = 0;     // the last position epoch the server gave us

        // Equipment.
        public PlayerTools[] Tools = { PlayerTools.Pickaxe };
        public int ToolSelected = 0;
        public BlockType[] Blocks = { BlockType.None };
        public int BlockSelected = 0;
        public float ToolCooldown = 0;
        public float ConstructionGunAnimation = 0;

        // Resources (the server's numbers).
        public uint Ore = 0;
        public uint OreMax = 0;
        public uint Cash = 0;
        public uint Weight = 0;
        public uint WeightMax = 0;

        // The prospector's radar.
        public bool RadarMute = false;
        public float RadarCooldown = 0;
        public float RadarDistance = 0;
        public float RadarValue = 0;

        public PlayerTools SelectedTool
        {
            get { return Tools[ToolSelected]; }
        }

        public BlockType SelectedBlock
        {
            get { return Blocks[BlockSelected]; }
        }

        public void CycleTool(int direction)
        {
            ToolSelected = ((ToolSelected + direction) % Tools.Length + Tools.Length) % Tools.Length;
        }

        // Only the construction gun has blocks to choose from; returns false otherwise.
        public bool CycleBlock(int direction)
        {
            if (SelectedTool != PlayerTools.ConstructionGun)
                return false;
            BlockSelected = ((BlockSelected + direction) % Blocks.Length + Blocks.Length) % Blocks.Length;
            return true;
        }

        public void SetClass(PlayerClass playerClass)
        {
            Class = playerClass;
            HasClass = true;
            ToolSelected = 0;
            BlockSelected = 0;
            Tools = GameRules.GetTools(playerClass, Sandbox);
            Blocks = GameRules.GetBlocks(playerClass, Team, Sandbox);
        }

        // Teams build in their own colours, so the blocks on offer change with the team.
        public void SetTeam(PlayerTeam team)
        {
            Spectating = false;
            Team = team;
            Blocks = GameRules.GetBlocks(Class, team, Sandbox);
            BlockSelected = Math.Min(BlockSelected, Blocks.Length - 1);
        }

        // A sandbox server (for building maps): building is free, with the world's own blocks too, and everyone
        // carries the deconstruction gun.
        public bool Sandbox { get; private set; }

        public void SetSandbox(bool sandbox)
        {
            if (sandbox == Sandbox)
                return;
            Sandbox = sandbox;
            if (HasClass)
            {
                Tools = GameRules.GetTools(Class, sandbox);
                ToolSelected = Math.Min(ToolSelected, Tools.Length - 1);
            }
            Blocks = GameRules.GetBlocks(Class, Team, sandbox);
            BlockSelected = Math.Min(BlockSelected, Blocks.Length - 1);
        }

        // What building a block costs this player.
        public uint BuildCost(BlockType blockType)
        {
            return Sandbox ? 0 : BlockInformation.GetCost(blockType);
        }
    }
}
