namespace Infiniminer
{
    // What the guide needs to know about the player each frame.
    public readonly record struct GuideState(
        bool Playing,           // alive, on a team, not in a sandbox
        float Depth,            // blocks below the surface (negative above it)
        uint Ore,
        uint Cash,              // loot carried
        PlayerClass Class,
        PlayerTools Tool,
        BlockType Block,        // the construction gun's block
        uint WinningCash);      // 0 when the server hasn't said

    // The controls to name in tips: keys, or controller buttons.
    public readonly record struct GuideControls(string Use, string NextTool, string NextBlock, string Scores);

    // Tips for new players, one at a time, each finished by doing what it says. Finished tips are remembered
    // (tips_done in client.config.txt), so each is taught once; turning TIPS back on in OPTIONS starts again.
    public class Guide
    {
        public const float DigDepth = 3;            // blocks below the surface that count as having dug in
        public const float LadderDepth = 4;         // deep enough to need a way back up
        public const float ClassToolSeconds = 6;    // holding the class's own tool this long finishes its tip
        public const float WinSeconds = 12;         // the last tip stays this long

        readonly HashSet<string> done;
        uint lastOre, lastCash;
        float shownFor;     // seconds the current tip has been on screen

        public Guide(HashSet<string> done)
        {
            this.done = done;
        }

        // The tip to show now, or null.
        public string Current { get; private set; }

        public bool IsDone(string step)
        {
            return done.Contains(step);
        }

        static string ClassStep(PlayerClass playerClass)
        {
            return "class_" + playerClass.ToString().ToLowerInvariant();
        }

        // The class's own tool, which its tip is about.
        public static PlayerTools ClassTool(PlayerClass playerClass)
        {
            switch (playerClass)
            {
                case PlayerClass.Prospector: return PlayerTools.ProspectingRadar;
                case PlayerClass.Miner: return PlayerTools.Drill;
                case PlayerClass.Sapper: return PlayerTools.Detonator;
                default: return PlayerTools.ConstructionGun;
            }
        }

        // The tips in order, and when each is worth showing; the first unfinished one that applies is shown.
        IEnumerable<(string step, bool applies)> Steps(GuideState s)
        {
            yield return ("dig", true);
            yield return ("ore", true);
            yield return ("ladder", s.Depth >= LadderDepth && s.Ore >= BlockInformation.GetCost(BlockType.Ladder));
            yield return ("loot", true);
            yield return ("bank", s.Cash > 0);
            yield return (ClassStep(s.Class), true);
            yield return ("win", true);
        }

        // Moves the guide on; returns true when a tip has just been finished (so the caller can save).
        public bool Update(GuideState s, float elapsed)
        {
            bool finished = false;
            if (s.Playing && Current != null)
            {
                shownFor += elapsed;
                if (IsFinished(Current, s))
                {
                    done.Add(Current);
                    finished = true;
                }
            }
            lastOre = s.Ore;
            lastCash = s.Cash;

            string next = s.Playing ? Steps(s).Where(x => x.applies && !done.Contains(x.step)).Select(x => x.step).FirstOrDefault() : null;
            if (next != Current)
                shownFor = 0;
            Current = next;
            return finished;
        }

        bool IsFinished(string step, GuideState s)
        {
            switch (step)
            {
                // Dug in, or already digging things up.
                case "dig": return s.Depth >= DigDepth || s.Ore > 0 || s.Cash > 0;
                case "ore": return s.Ore > 0;
                // Ore spent while holding the construction gun on a ladder: one was built.
                case "ladder": return s.Tool == PlayerTools.ConstructionGun && s.Block == BlockType.Ladder && s.Ore < lastOre;
                case "loot": return s.Cash > 0;
                // Loot is banked as soon as its carrier reaches the surface.
                case "bank": return s.Cash == 0 && lastCash > 0 && s.Depth < 1;
                case "win": return shownFor >= WinSeconds;
            }
            if (step == ClassStep(s.Class))
                return s.Tool == ClassTool(s.Class) && shownFor >= ClassToolSeconds;
            return false;
        }

        // The current tip's text (it can change as the player gets part of the way).
        public string Text(GuideState s, GuideControls c)
        {
            switch (Current)
            {
                case "dig":
                    return "WELCOME! HOLD " + c.Use + " TO DIG WITH YOUR PICKAXE. DIG DOWN: THE ORE AND TREASURE ARE UNDERGROUND.";
                case "ore":
                    return "LOOK FOR ORE (DIRT STREAKED WITH SILVER) AND DIG IT. ORE PAYS FOR BUILDING THINGS, LIKE LADDERS. "
                           + "GREY ROCK IS TOO HARD FOR A PICKAXE.";
                case "ladder":
                    if (s.Tool != PlayerTools.ConstructionGun)
                        return "TO CLIMB BACK UP, BUILD A LADDER. PRESS " + c.NextTool + " UNTIL YOU HOLD THE CONSTRUCTION GUN.";
                    if (s.Block != BlockType.Ladder)
                        return "NOW PRESS " + c.NextBlock + " UNTIL THE GUN SHOWS THE LADDER.";
                    return "AIM AT A WALL AND PRESS " + c.Use + " TO BUILD A LADDER (25 ORE), THEN WALK INTO IT TO CLIMB. "
                           + "STACK THEM TO GO HIGHER.";
                case "loot":
                    return "DIG DEEPER FOR GOLD (YELLOW NUGGETS) AND DIAMONDS: THEY'RE YOUR TEAM'S MONEY. "
                           + "THEY'RE HEAVY, SO YOU CAN ONLY CARRY A FEW (WEIGHT, AT THE TOP).";
                case "bank":
                    return "YOU'RE CARRYING $" + s.Cash + ". TAKE IT UP TO THE SURFACE: IT'S BANKED FOR YOUR TEAM AS SOON AS YOU GET THERE.";
                case "win":
                    return (s.WinningCash > 0 ? "THE FIRST TEAM TO BANK $" + s.WinningCash + " WINS. " : "THE TEAM THAT BANKS THE MOST LOOT WINS. ")
                           + "HOLD " + c.Scores + " FOR THE SCORES. GOOD LUCK!";
            }
            if (Current == ClassStep(s.Class))
            {
                string tool = s.Tool == ClassTool(s.Class) ? "" : " (PRESS " + c.NextTool + " FOR IT)";
                switch (s.Class)
                {
                    case PlayerClass.Prospector:
                        return "AS A PROSPECTOR YOU HAVE A RADAR" + tool + ". POINT IT AROUND: IT BEEPS AND LIGHTS UP AT GOLD OR DIAMONDS, "
                               + "AND THE NEEDLE SHOWS HOW CLOSE THEY ARE.";
                    case PlayerClass.Miner:
                        return "AS A MINER YOU DIG FASTER, CARRY MORE, AND HAVE A DRILL" + tool + " THAT BORES THROUGH ROCK.";
                    case PlayerClass.Sapper:
                        return "AS A SAPPER YOU BUILD EXPLOSIVES (CONSTRUCTION GUN, " + c.NextBlock + " TO PICK THEM) AND SET THEM OFF "
                               + "WITH YOUR DETONATOR" + tool + ". BLAST THROUGH ROCK, OR THE OTHER TEAM'S WALLS. STAND WELL BACK!";
                    default:
                        return "AS AN ENGINEER YOU BUILD FOR THE TEAM" + tool + ": BANKS TO STORE ORE, JUMP BLOCKS, ROADS, FORCE FIELDS "
                               + "ONLY YOUR TEAM CAN PASS, SHOCK BLOCKS AND TELEPORTERS. " + c.NextBlock + " CHANGES THE BLOCK.";
                }
            }
            return null;
        }
    }

    // Names and one-line explanations of blocks and tools, shown to players learning the game.
    public static class BlockGuide
    {
        static string TeamName(PlayerTeam team)
        {
            return team == PlayerTeam.Red ? "RED" : "BLUE";
        }

        public static string Name(BlockType blockType)
        {
            switch (blockType)
            {
                case BlockType.Dirt:
                case BlockType.DirtSign: return "DIRT";
                case BlockType.Ore: return "ORE";
                case BlockType.Gold: return "GOLD";
                case BlockType.Diamond: return "DIAMOND";
                case BlockType.Rock: return "ROCK";
                case BlockType.Ladder: return "LADDER";
                case BlockType.Explosive: return "EXPLOSIVES";
                case BlockType.Jump: return "JUMP BLOCK";
                case BlockType.Shock: return "SHOCK BLOCK";
                case BlockType.BankRed: return "RED BANK";
                case BlockType.BankBlue: return "BLUE BANK";
                case BlockType.BeaconRed: return "RED BEACON";
                case BlockType.BeaconBlue: return "BLUE BEACON";
                case BlockType.Road: return "ROAD";
                case BlockType.SolidRed: return "RED BLOCK";
                case BlockType.SolidBlue: return "BLUE BLOCK";
                case BlockType.Metal: return "METAL";
                case BlockType.Lava: return "LAVA";
                case BlockType.TransRed: return "RED FORCE FIELD";
                case BlockType.TransBlue: return "BLUE FORCE FIELD";
                case BlockType.Teleporter: return "TELEPORTER";
                case BlockType.HomeRed: return "RED HOME BASE";
                case BlockType.HomeBlue: return "BLUE HOME BASE";
            }
            return blockType.ToString().ToUpperInvariant();
        }

        // What a block is for, from the point of view of a player on `team` playing `playerClass`.
        public static string Description(BlockType blockType, PlayerTeam team, PlayerClass playerClass)
        {
            switch (blockType)
            {
                case BlockType.Dirt:
                case BlockType.DirtSign: return "DIG IT WITH YOUR PICKAXE";
                case BlockType.Ore: return "DIG IT FOR ORE, WHICH PAYS FOR BUILDING";
                case BlockType.Gold: return "WORTH $100: DIG IT AND CARRY IT TO THE SURFACE";
                case BlockType.Diamond: return "WORTH $1000: DIG IT AND CARRY IT TO THE SURFACE";
                case BlockType.Rock:
                    return playerClass == PlayerClass.Miner ? "TOO HARD FOR A PICKAXE: USE YOUR DRILL"
                         : playerClass == PlayerClass.Sapper ? "TOO HARD FOR A PICKAXE: BLAST IT WITH EXPLOSIVES"
                         : "TOO HARD FOR A PICKAXE (MINERS DRILL IT, SAPPERS BLAST IT)";
                case BlockType.Ladder: return "WALK INTO IT TO CLIMB";
                case BlockType.Explosive: return "A SAPPER'S DETONATOR SETS IT OFF";
                case BlockType.Jump: return "STEP ON IT TO BE THROWN HIGH IN THE AIR";
                case BlockType.Shock: return "KILLS ANYONE WHO BUMPS THEIR HEAD ON IT";
                case BlockType.BankRed:
                case BlockType.BankBlue:
                    return (blockType == BlockType.BankRed) == (team == PlayerTeam.Red)
                        ? "YOUR TEAM'S ORE STORE: STAND NEXT TO IT TO PUT ORE IN OR TAKE IT OUT"
                        : "THE OTHER TEAM'S ORE STORE";
                case BlockType.BeaconRed:
                case BlockType.BeaconBlue: return "MARKS A PLACE ON ITS TEAM'S RADAR";
                case BlockType.Road: return "YOU RUN FASTER ON IT";
                case BlockType.SolidRed:
                case BlockType.SolidBlue: return "A WALL: ITS TEAM'S ENGINEERS OR EXPLOSIVES TAKE IT DOWN";
                case BlockType.TransRed:
                case BlockType.TransBlue:
                    return (blockType == BlockType.TransRed) == (team == PlayerTeam.Red)
                        ? "YOUR TEAM WALKS THROUGH IT; THE OTHER TEAM CAN'T"
                        : "ONLY THE " + TeamName(blockType == BlockType.TransRed ? PlayerTeam.Red : PlayerTeam.Blue) + " TEAM CAN WALK THROUGH IT";
                case BlockType.Teleporter: return "STEP ON IT TO GO TO ITS TEAM'S NEXT TELEPORTER";
                case BlockType.Metal: return "NOTHING DIGS THROUGH IT";
                case BlockType.Lava: return "DEADLY, AND IT FLOWS INTO HOLES";
                case BlockType.HomeRed:
                case BlockType.HomeBlue: return "ITS TEAM STARTS HERE";
            }
            return "";
        }

        public static string ToolName(PlayerTools tool)
        {
            switch (tool)
            {
                case PlayerTools.Pickaxe: return "PICKAXE";
                case PlayerTools.ConstructionGun: return "CONSTRUCTION GUN";
                case PlayerTools.DeconstructionGun: return "DECONSTRUCTION GUN";
                case PlayerTools.ProspectingRadar: return "RADAR";
                case PlayerTools.Detonator: return "DETONATOR";
                case PlayerTools.Drill: return "DRILL";
            }
            return tool.ToString().ToUpperInvariant();
        }

        public static string ToolDescription(PlayerTools tool, bool sandbox)
        {
            switch (tool)
            {
                case PlayerTools.Pickaxe: return "DIGS DIRT, ORE AND TREASURE";
                case PlayerTools.ConstructionGun: return "BUILDS BLOCKS WITH YOUR ORE";
                case PlayerTools.DeconstructionGun: return sandbox ? "TAKES AWAY ANY BLOCK" : "TAKES AWAY BLOCKS YOUR TEAM BUILT";
                case PlayerTools.ProspectingRadar: return "BEEPS AT GOLD AND DIAMONDS YOU POINT IT AT";
                case PlayerTools.Detonator: return "SETS OFF YOUR EXPLOSIVES";
                case PlayerTools.Drill: return "SLOWER, BUT BORES THROUGH ROCK";
            }
            return "";
        }
    }
}
