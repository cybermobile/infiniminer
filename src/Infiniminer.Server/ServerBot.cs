using System.Numerics;

namespace Infiniminer
{
    // What a bot needs from the server. The server implements it with the same rules real players get.
    public interface IBotWorld
    {
        int MapSize { get; }
        BlockType BlockAt(int x, int y, int z);         // outside the map counts as indestructible
        void Dig(ServerPlayer bot, int x, int y, int z);
        bool Build(ServerPlayer bot, int x, int y, int z, BlockType blockType);    // the construction gun's rules
        void Detonate(ServerPlayer bot);                // sets off the bot's explosives
        bool AtOwnBank(ServerPlayer bot);
        void DepositOre(ServerPlayer bot);              // 50 ore into the team's bank, as the deposit key does
        (int x, int y, int z)? FindBank(PlayerTeam team);      // the team's bank nearest the surface
        void Moved(ServerPlayer bot);                   // tell everyone where the bot is
        void Respawn(ServerPlayer bot);
        void Kill(ServerPlayer bot, string deathMessage);
    }

    // How well computer players play. Even hard bots stay within what a person can do: they dig no faster than
    // the tools allow and walk no faster than players; they notice more and waste less time.
    public enum BotSkill { Easy, Normal, Hard }

    // A computer player. Every class digs down, tunnels and climbs back to the surface, where the server banks
    // its loot as it does for everyone; what it looks for and how it gets around depends on its class:
    //   Miner       gold and diamonds, close by
    //   Prospector  gold and diamonds, sensed from further away (the radar)
    //   Engineer    ore for the team: climbs out on ladders it builds, builds the team a bank if it has none,
    //               fences it in with the team's force fields (which only the team can walk through) and deposits
    //               the rest of its ore there
    //   Sapper      blasts through rock that a pickaxe can't dig, and raids the enemy bank to blow up its defences
    // It moves one block at a time, digging what is in the way, keeps away from lava (and flees lava that flows
    // towards it), and heads home early when carrying diamonds.
    public sealed class ServerBot
    {
        public const float RespawnDelay = 3;
        const float FallSpeed = 12;     // blocks per second
        const int LethalFall = 15;      // blocks, as for players (GameRules' 15.5 m)
        const float StuckLimit = 30;    // seconds without progress before giving up and respawning
        const float BankTripLimit = 60; // seconds to spend walking to the bank before giving up on it
        const int BlastDistance = 4;    // explosions kill within 3 blocks
        const float RaidLimit = 90;     // seconds a sapper spends on a raid
        const int BuildReach = 5;       // blocks; as far as the construction gun reaches

        public readonly ServerPlayer Player;
        readonly IBotWorld world;
        readonly Random random;
        public BotSkill Skill = BotSkill.Normal;
        float idleTime;                 // easy bots stop for a breather now and then

        enum Plan { Wander, Descend, Tunnel, Ascend, ToBank, Blast, Raid }
        Plan plan = Plan.Wander;
        int x, fy, z;                   // the block the bot's feet are in
        int dirX = 1, dirZ = 0;
        int stepsLeft, targetDepth, tunnelsLeft;
        float digCooldown, deadTime, sendTimer, toolTimer, stuckTime, planTime, actionCooldown;
        bool moving, falling;
        Vector3 moveFrom, moveTo;
        float moveProgress, moveDuration;
        int fallStartY;
        (int x, int y, int z)? pendingLadder;   // engineers climbing: the ladder goes under their feet
        int blastStage, retreatLeft;
        Plan afterBlast = Plan.Tunnel;
        int defenceBudget = -1;             // ore an engineer at the bank may spend on defences (-1: not decided yet)
        int ascendBestY, relocateSteps;     // climbing: highest point so far, and steps left to walk elsewhere
        float ascendStall;

        // For tests and the console.
        public int BlocksDug { get; private set; }
        public int TripsHome { get; private set; }
        public int DeepestY { get; private set; } = int.MaxValue;
        public int LaddersBuilt { get; private set; }
        public int BanksBuilt { get; private set; }
        public int Blasts { get; private set; }
        public uint OreDeposited { get; private set; }
        public int LavaEscapes { get; private set; }
        public int DefencesBuilt { get; private set; }
        public int Raids { get; private set; }
        public int RaidsThrough { get; private set; }     // raids that got through to the enemy bank

        // Tests: start tunnelling in a direction from where the bot stands.
        internal void StartTunnel(int dx, int dz)
        {
            SyncFromPosition();
            SetPlan(Plan.Tunnel);
            (dirX, dirZ) = (dx, dz);
            stepsLeft = 20;
            tunnelsLeft = 0;
        }

        internal bool Ascending
        {
            get { return plan == Plan.Ascend; }
        }

        internal void HeadForBank()
        {
            SyncFromPosition();
            SetPlan(Plan.ToBank);
        }

        internal bool HeadingForBank
        {
            get { return plan == Plan.ToBank; }
        }

        internal bool Raiding
        {
            get { return plan == Plan.Raid || (plan == Plan.Blast && afterBlast == Plan.Raid); }
        }

        internal void StartRaid()
        {
            SyncFromPosition();
            SetPlan(Plan.Raid);
            Raids++;
        }

        public string Describe()
        {
            return $"{plan} at ({x},{fy},{z}) ore {Player.Ore} weight {Player.Weight} cash {Player.Cash} alive {Player.Alive} stuck {stuckTime:0.0}"
                + (DefencesBuilt > 0 ? $" defences {DefencesBuilt}" : "") + (Raids > 0 ? $" raids {RaidsThrough}/{Raids}" : "");
        }

        public ServerBot(ServerPlayer player, IBotWorld world, int seed)
        {
            Player = player;
            this.world = world;
            random = new Random(seed);
            player.Tool = PlayerTools.Pickaxe;
        }

        PlayerClass Class
        {
            get { return Player.Class; }
        }

        int SurfaceFeetY
        {
            // Above this the server counts the bot as on the surface (and banks its loot).
            get { return world.MapSize - GameConstants.GroundLevel - 1; }
        }

        // How far a bot notices what it is after: a prospector's radar reaches further than anyone's eyes, and
        // better players notice more.
        int SenseRange
        {
            get
            {
                int range = Class == PlayerClass.Prospector ? 12 : 6;
                return Skill == BotSkill.Easy ? range / 2 : Skill == BotSkill.Hard ? range * 3 / 2 : range;
            }
        }

        float WalkSpeed
        {
            get { return GameRules.MoveSpeed * (Skill == BotSkill.Easy ? 0.7f : 1f); }
        }

        // Seconds between pickaxe swings: the tool's cooldown, plus some hesitation unless hard.
        float DigDelay
        {
            get
            {
                float cooldown = GameRules.GetToolCooldown(PlayerTools.Pickaxe, Class);
                return Skill == BotSkill.Easy ? cooldown * 1.8f + 0.1f : Skill == BotSkill.Hard ? cooldown : cooldown + 0.05f;
            }
        }

        BlockType OwnBank
        {
            get { return Player.Team == PlayerTeam.Red ? BlockType.BankRed : BlockType.BankBlue; }
        }

        BlockType OwnForceField
        {
            get { return Player.Team == PlayerTeam.Red ? BlockType.TransRed : BlockType.TransBlue; }
        }

        PlayerTeam Enemy
        {
            get { return Player.Team == PlayerTeam.Red ? PlayerTeam.Blue : PlayerTeam.Red; }
        }

        // The ring a bank's defences go in: two blocks out from it all round, two blocks high (too high to jump).
        internal static IEnumerable<(int x, int y, int z)> DefenceRing((int x, int y, int z) bank)
        {
            for (int dy = 0; dy <= 1; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    for (int dz = -2; dz <= 2; dz++)
                        if (Math.Abs(dx) == 2 || Math.Abs(dz) == 2)
                            yield return (bank.x + dx, bank.y + dy, bank.z + dz);
        }

        // A team's walls and force fields: only explosives get through them.
        static bool IsDefence(BlockType block, PlayerTeam team)
        {
            return team == PlayerTeam.Red ? block == BlockType.SolidRed || block == BlockType.TransRed
                                          : block == BlockType.SolidBlue || block == BlockType.TransBlue;
        }

        int DefencesAround((int x, int y, int z) bank, PlayerTeam team)
        {
            return DefenceRing(bank).Count(c => IsDefence(world.BlockAt(c.x, c.y, c.z), team));
        }

        // What a sapper's explosives are for: rock, and the enemy's defences.
        bool Blastable(int bx, int by, int bz)
        {
            BlockType block = world.BlockAt(bx, by, bz);
            return block == BlockType.Rock || IsDefence(block, Enemy);
        }

        bool Solid(int bx, int by, int bz)
        {
            return !GameRules.IsPassable(world.BlockAt(bx, by, bz), Player.Team);
        }

        bool NearLava(int bx, int by, int bz)
        {
            return world.BlockAt(bx, by, bz) == BlockType.Lava
                || world.BlockAt(bx + 1, by, bz) == BlockType.Lava || world.BlockAt(bx - 1, by, bz) == BlockType.Lava
                || world.BlockAt(bx, by + 1, bz) == BlockType.Lava || world.BlockAt(bx, by - 1, bz) == BlockType.Lava
                || world.BlockAt(bx, by, bz + 1) == BlockType.Lava || world.BlockAt(bx, by, bz - 1) == BlockType.Lava;
        }

        bool InsideMap(int bx, int by, int bz)
        {
            int edge = world.MapSize - 1;
            return bx > 0 && by > 0 && bz > 0 && bx < edge && by < edge && bz < edge;
        }

        // Diggable with a pickaxe, inside the map, and not about to let lava in.
        bool Diggable(int bx, int by, int bz)
        {
            return InsideMap(bx, by, bz) && GameRules.Dig(world.BlockAt(bx, by, bz), false).Removes && !NearLava(bx, by, bz);
        }

        // What this bot is looking for.
        bool Wanted(int bx, int by, int bz)
        {
            BlockType block = world.BlockAt(bx, by, bz);
            bool treasure = (block == BlockType.Gold || block == BlockType.Diamond) && Player.Weight < Player.WeightMax;
            // Engineers gather ore for the team; sappers need it for explosives.
            if (Class == PlayerClass.Engineer || (Class == PlayerClass.Sapper && Player.Ore < 2 * BlockInformation.GetCost(BlockType.Explosive)))
                return treasure || (block == BlockType.Ore && Player.Ore < Player.OreMax);
            return treasure;
        }

        bool InsideMargin(int bx, int bz)
        {
            return bx >= 2 && bz >= 2 && bx <= world.MapSize - 3 && bz <= world.MapSize - 3;
        }

        void SyncFromPosition()
        {
            x = (int)MathF.Floor(Player.Position.X);
            z = (int)MathF.Floor(Player.Position.Z);
            fy = (int)MathF.Floor(Player.Position.Y + GameRules.FootOffset + 0.05f);
            moving = false;
            falling = false;
            pendingLadder = null;
        }

        Vector3 CellPosition(int cx, int cy, int cz)
        {
            return new Vector3(cx + 0.5f, cy - GameRules.FootOffset - 0.05f, cz + 0.5f);
        }

        static readonly (int dx, int dz)[] Directions = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        void TurnRandomly()
        {
            (dirX, dirZ) = Directions[random.Next(4)];
        }

        // Heads for what it is looking for within sensing range, otherwise anywhere.
        void ChooseDirection()
        {
            int best = int.MaxValue;
            foreach (var (dx, dz) in Directions)
                for (int distance = 1; distance <= SenseRange && distance < best; distance++)
                {
                    int cx = x + dx * distance, cz = z + dz * distance;
                    if (!InsideMargin(cx, cz))
                        break;
                    if (Wanted(cx, fy, cz) || Wanted(cx, fy + 1, cz) || Wanted(cx, fy - 1, cz))
                    {
                        best = distance;
                        dirX = dx;
                        dirZ = dz;
                    }
                }
            if (best == int.MaxValue)
                TurnRandomly();
        }

        void StartMove(int nx, int ny, int nz, float speed)
        {
            moveFrom = Player.Position;
            moveTo = CellPosition(nx, ny, nz);
            moveProgress = 0;
            moveDuration = 1 / speed;
            moving = true;
            Vector3 direction = new Vector3(nx - x, 0, nz - z);
            if (direction != Vector3.Zero)
                Player.Heading = Vector3.Normalize(direction);
            x = nx;
            fy = ny;
            z = nz;
            stuckTime = 0;
        }

        void Aim(int bx, int by, int bz)
        {
            Vector3 target = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);
            if (target != Player.Position)
                Player.Heading = Vector3.Normalize(target - Player.Position);
        }

        // One swing of the pickaxe, when it is ready. Returns true if the bot spent its turn digging.
        bool TryDig(int bx, int by, int bz)
        {
            if (digCooldown > 0)
                return true;
            Player.Tool = PlayerTools.Pickaxe;
            Aim(bx, by, bz);
            world.Dig(Player, bx, by, bz);
            BlocksDug++;
            digCooldown = DigDelay;
            Player.UsingTool = true;
            toolTimer = 0.3f;
            return true;
        }

        bool TryBuild(int bx, int by, int bz, BlockType blockType)
        {
            Player.Tool = PlayerTools.ConstructionGun;
            Aim(bx, by, bz);
            return world.Build(Player, bx, by, bz, blockType);
        }

        public void Update(float elapsed)
        {
            if (!Player.Alive)
            {
                deadTime += elapsed;
                if (deadTime >= RespawnDelay && Player.Team != PlayerTeam.None)
                {
                    deadTime = 0;
                    world.Respawn(Player);
                    SyncFromPosition();
                    SetPlan(Plan.Wander);
                    stepsLeft = 3 + random.Next(6);
                    TurnRandomly();
                    world.Moved(Player);
                }
                return;
            }

            // Something else moved us (a teleporter, the tp command, a new map): carry on from there.
            if (!moving && Vector3.DistanceSquared(Player.Position, CellPosition(x, fy, z)) > 1)
                SyncFromPosition();

            digCooldown -= elapsed;
            actionCooldown -= elapsed;
            sendTimer -= elapsed;
            planTime += elapsed;
            if (toolTimer > 0)
            {
                toolTimer -= elapsed;
                if (toolTimer <= 0)
                    Player.UsingTool = false;
            }

            if (moving)
            {
                moveProgress += elapsed / moveDuration;
                Player.Position = Vector3.Lerp(moveFrom, moveTo, Math.Min(1, moveProgress));
                if (moveProgress >= 1)
                {
                    moving = false;
                    Player.Position = moveTo;
                    // An engineer that just climbed puts a ladder where its feet were.
                    if (pendingLadder is (int lx, int ly, int lz))
                    {
                        pendingLadder = null;
                        if (TryBuild(lx, ly, lz, BlockType.Ladder))
                            LaddersBuilt++;
                    }
                }
            }
            else
            {
                Think(elapsed);
                DeepestY = Math.Min(DeepestY, fy);
            }

            if (sendTimer <= 0)
            {
                world.Moved(Player);
                sendTimer = 0.05f;
            }
        }

        void SetPlan(Plan newPlan)
        {
            plan = newPlan;
            planTime = 0;
            ascendBestY = fy;
            ascendStall = 0;
            relocateSteps = 0;
            defenceBudget = -1;
        }

        // Turns towards the middle of the map, along whichever axis is further off.
        void FaceCentre()
        {
            int centre = world.MapSize / 2, dx = centre - x, dz = centre - z;
            (dirX, dirZ) = Math.Abs(dx) >= Math.Abs(dz) ? (Math.Sign(dx), 0) : (0, Math.Sign(dz));
            if (dirX == 0 && dirZ == 0)
                TurnRandomly();
        }

        void Think(float elapsed)
        {
            // Nothing underfoot: fall.
            if (!Solid(x, fy - 1, z))
            {
                if (!falling)
                {
                    falling = true;
                    fallStartY = fy;
                }
                StartMove(x, fy - 1, z, FallSpeed);
                return;
            }
            if (falling)
            {
                falling = false;
                if (fallStartY - fy > LethalFall)
                {
                    world.Kill(Player, "WAS KILLED BY GRAVITY!");
                    return;
                }
            }

            if (idleTime > 0)
            {
                idleTime -= elapsed;
                return;
            }
            if (Skill == BotSkill.Easy && plan != Plan.Blast && random.NextDouble() < elapsed * 0.1)
            {
                idleTime = 1 + (float)random.NextDouble() * 2;
                return;
            }

            stuckTime += elapsed;
            if (stuckTime > StuckLimit)
            {
                world.Kill(Player, "HAS COMMMITTED PIXELCIDE!");
                return;
            }

            // Lava has flowed up to us: get away from it, and head for the surface.
            if (plan != Plan.Blast && (NearLava(x, fy, z) || NearLava(x, fy + 1, z)) && FleeLava())
                return;

            // Diamonds are worth too much to risk: take them home straight away.
            if ((plan == Plan.Tunnel || plan == Plan.Descend) && Player.Cash >= 1000)
            {
                SetPlan(Plan.Ascend);
                return;
            }

            // Anything wanted right next to us is always worth a swing.
            if (plan != Plan.Blast)
                foreach (var (tx, ty, tz) in new[] { (x + 1, fy, z), (x - 1, fy, z), (x, fy, z + 1), (x, fy, z - 1),
                                                     (x + 1, fy + 1, z), (x - 1, fy + 1, z), (x, fy + 1, z + 1), (x, fy + 1, z - 1),
                                                     (x, fy + 2, z), (x, fy - 1, z) })
                    if (Wanted(tx, ty, tz) && Diggable(tx, ty, tz))
                    {
                        TryDig(tx, ty, tz);
                        return;
                    }

            switch (plan)
            {
                case Plan.Wander:
                    if (stepsLeft <= 0 || fy < SurfaceFeetY)
                    {
                        SetPlan(Plan.Descend);
                        targetDepth = Math.Max(3, SurfaceFeetY - 6 - random.Next(20));
                        return;
                    }
                    if (!Step(climb: true, dig: false))
                        TurnRandomly();
                    return;

                case Plan.Descend:
                    if (fy <= targetDepth || !Diggable(x, fy - 1, z))
                    {
                        SetPlan(Plan.Tunnel);
                        stepsLeft = 10 + random.Next(20);
                        tunnelsLeft = Skill == BotSkill.Hard ? 4 : Skill == BotSkill.Easy ? 2 : 3;
                        ChooseDirection();
                        return;
                    }
                    TryDig(x, fy - 1, z);
                    return;

                case Plan.Tunnel:
                    // Home when full, or after a few tunnels; an empty-handed bot tries another direction first.
                    bool full = Player.Weight >= Player.WeightMax || (Class == PlayerClass.Engineer && Player.Ore >= Player.OreMax);
                    bool carrying = Player.Weight > 0 || (Class == PlayerClass.Engineer && Player.Ore >= 100);
                    if (full || (stepsLeft <= 0 && (carrying || tunnelsLeft <= 0)))
                    {
                        SetPlan(Plan.Ascend);
                        return;
                    }
                    if (stepsLeft <= 0)
                    {
                        tunnelsLeft--;
                        stepsLeft = 10 + random.Next(20);
                        ChooseDirection();
                    }
                    if (!Step(climb: false, dig: true))
                    {
                        if (Class == PlayerClass.Sapper && TryStartBlast())
                            return;
                        ChooseDirection();
                    }
                    return;

                case Plan.Ascend:
                    if (fy >= SurfaceFeetY)
                    {
                        // On the surface: the server banks the loot.
                        TripsHome++;
                        if (Class == PlayerClass.Engineer && Player.Ore >= 50)
                            SetPlan(Plan.ToBank);
                        else if (Class == PlayerClass.Sapper && WantsToRaid())
                        {
                            SetPlan(Plan.Raid);
                            Raids++;
                        }
                        else
                        {
                            SetPlan(Plan.Wander);
                            stepsLeft = 3 + random.Next(8);
                            TurnRandomly();
                        }
                        return;
                    }
                    // No higher for a while (under rock, or against the edge of the map): walk somewhere else and try
                    // again from there; give up eventually.
                    if (fy > ascendBestY)
                    {
                        ascendBestY = fy;
                        ascendStall = 0;
                    }
                    else
                        ascendStall += elapsed;
                    if (ascendStall > 45)
                    {
                        world.Kill(Player, "HAS COMMMITTED PIXELCIDE!");
                        return;
                    }
                    if (ascendStall > 15 && relocateSteps == 0)
                    {
                        relocateSteps = 6;
                        ascendStall = 0;
                        FaceCentre();
                    }
                    if (relocateSteps > 0)
                    {
                        if (!Step(climb: true, dig: true))
                        {
                            relocateSteps = 0;
                            TurnRandomly();
                        }
                        else if (moving)
                            relocateSteps--;
                        return;
                    }

                    // Engineers climb straight up on ladders while they have ore to spare (keeping 100 for the bank);
                    // everyone else cuts stairs.
                    if (Class == PlayerClass.Engineer && Player.Ore >= 100 + BlockInformation.GetCost(BlockType.Ladder) && ClimbLadder())
                        return;
                    if (!Climb())
                        TurnRandomly();
                    return;

                case Plan.ToBank:
                    GoToBank();
                    return;

                case Plan.Blast:
                    ContinueBlast();
                    return;

                case Plan.Raid:
                    Raid();
                    return;
            }
        }

        // Moves to a neighbouring block away from lava (or up, if that's clear). Returns false if trapped.
        bool FleeLava()
        {
            foreach (var (dx, dz) in Directions.OrderBy(_ => random.Next()))
            {
                int nx = x + dx, nz = z + dz;
                if (InsideMargin(nx, nz) && !Solid(nx, fy, nz) && !Solid(nx, fy + 1, nz) && !NearLava(nx, fy, nz) && !NearLava(nx, fy + 1, nz))
                {
                    LavaEscapes++;
                    SetPlan(Plan.Ascend);
                    (dirX, dirZ) = (dx, dz);
                    StartMove(nx, fy, nz, WalkSpeed);
                    return true;
                }
            }
            if (plan != Plan.Ascend)
            {
                LavaEscapes++;
                SetPlan(Plan.Ascend);
            }
            return false;
        }

        // Moves one block in the current direction, digging through what's in the way when `dig` is set, and
        // stepping up one block when `climb` is set. Returns false when the way is blocked.
        bool Step(bool climb, bool dig)
        {
            int nx = x + dirX, nz = z + dirZ;
            if (!InsideMargin(nx, nz))
                return false;

            if (!Solid(nx, fy, nz) && !Solid(nx, fy + 1, nz))
            {
                // Don't walk off a drop that would hurt, or into lava.
                int drop = 0;
                while (drop < 5 && !Solid(nx, fy - 1 - drop, nz))
                    drop++;
                if (drop >= 5 || NearLava(nx, fy, nz) || NearLava(nx, fy - drop, nz))
                    return false;
                StartMove(nx, fy, nz, WalkSpeed);
                stepsLeft--;
                return true;
            }

            if (climb && Solid(nx, fy, nz) && !Solid(nx, fy + 1, nz) && !Solid(nx, fy + 2, nz) && !Solid(x, fy + 2, z)
                && !NearLava(nx, fy + 1, nz))
            {
                StartMove(nx, fy + 1, nz, WalkSpeed);
                stepsLeft--;
                return true;
            }

            if (!dig)
                return false;
            foreach (int by in new[] { fy + 1, fy })
                if (Solid(nx, by, nz))
                {
                    if (!Diggable(nx, by, nz))
                        return false;
                    return TryDig(nx, by, nz);
                }
            return false;
        }

        // Cuts a staircase up: clears headroom above us and the two blocks above the next step, then steps up. Any side
        // with something to step onto will do (the current direction first); on open cave floor, walks on until
        // there's a wall to cut steps into.
        bool Climb()
        {
            foreach (var (dx, dz) in new[] { (dirX, dirZ) }.Concat(Directions.Where(d => d != (dirX, dirZ))))
            {
                int nx = x + dx, nz = z + dz;
                if (!InsideMargin(nx, nz) || !Solid(nx, fy, nz) || NearLava(nx, fy + 1, nz) || NearLava(nx, fy + 2, nz))
                    continue;
                (int, int, int)? toDig = null;
                bool possible = true;
                foreach (var (bx, by, bz) in new[] { (x, fy + 2, z), (nx, fy + 2, nz), (nx, fy + 1, nz) })
                    if (Solid(bx, by, bz))
                    {
                        if (!Diggable(bx, by, bz))
                        {
                            possible = false;
                            break;
                        }
                        toDig ??= (bx, by, bz);
                    }
                if (!possible)
                    continue;
                (dirX, dirZ) = (dx, dz);
                if (toDig is (int tx, int ty, int tz))
                    return TryDig(tx, ty, tz);
                StartMove(nx, fy + 1, nz, WalkSpeed);
                return true;
            }
            return Step(climb: false, dig: true);
        }

        // Climbs straight up a shaft by building a ladder under its feet (as players pillar up), leaving a ladder
        // anyone can use. Returns false if the shaft can't go higher here.
        bool ClimbLadder()
        {
            if (Solid(x, fy + 2, z))
            {
                if (!Diggable(x, fy + 2, z))
                    return false;
                return TryDig(x, fy + 2, z);
            }
            if (NearLava(x, fy + 2, z) || actionCooldown > 0)
                return actionCooldown > 0;
            actionCooldown = GameRules.GetToolCooldown(PlayerTools.ConstructionGun, Class);
            pendingLadder = (x, fy, z);
            StartMove(x, fy + 1, z, WalkSpeed);
            return true;
        }

        // Engineers on the surface: build the team a bank if it has none, otherwise walk to it and deposit ore.
        void GoToBank()
        {
            if (Player.Ore == 0 || planTime > BankTripLimit)
            {
                SetPlan(Plan.Wander);
                stepsLeft = 3 + random.Next(8);
                return;
            }
            if (world.AtOwnBank(Player))
            {
                if (actionCooldown > 0)
                    return;
                // Up to half of what it brought goes on defences while the bank still needs them (not easy bots).
                if (defenceBudget < 0)
                    defenceBudget = Skill == BotSkill.Easy ? 0 : (int)Player.Ore / 2;
                // Building and depositing are progress, even standing still.
                stuckTime = 0;
                if (defenceBudget >= BlockInformation.GetCost(OwnForceField) && Fortify())
                {
                    actionCooldown = GameRules.GetToolCooldown(PlayerTools.ConstructionGun, Class);
                    return;
                }
                defenceBudget = 0;
                uint before = Player.Ore;
                world.DepositOre(Player);
                OreDeposited += before - Player.Ore;
                actionCooldown = 0.5f;
                return;
            }

            (int x, int y, int z)? bank = world.FindBank(Player.Team);
            if (bank == null)
            {
                // No bank yet: build one next to us, on solid ground.
                uint cost = BlockInformation.GetCost(OwnBank);
                if (Player.Ore < cost)
                {
                    SetPlan(Plan.Wander);
                    stepsLeft = 3 + random.Next(8);
                    return;
                }
                foreach (var (dx, dz) in Directions)
                    if (!Solid(x + dx, fy, z + dz) && Solid(x + dx, fy - 1, z + dz) && TryBuild(x + dx, fy, z + dz, OwnBank))
                    {
                        BanksBuilt++;
                        return;
                    }
                // Nowhere to put it here: walk on a little.
                if (!Step(climb: true, dig: false))
                    TurnRandomly();
                return;
            }

            if (!WalkTowards(bank.Value.x, bank.Value.z))
            {
                TurnRandomly();
                Step(climb: true, dig: true);
            }
        }

        // The two directions that lead towards a block, the more direct first.
        (int dx, int dz)[] DirectionsTowards(int tx, int tz)
        {
            int ddx = tx - x, ddz = tz - z;
            (int, int) preferred = Math.Abs(ddx) >= Math.Abs(ddz) ? (Math.Sign(ddx), 0) : (0, Math.Sign(ddz));
            (int, int) other = Math.Abs(ddx) >= Math.Abs(ddz) ? (0, Math.Sign(ddz)) : (Math.Sign(ddx), 0);
            return new[] { preferred, other }.Where(d => d != (0, 0)).ToArray();
        }

        // One step towards a block, climbing or digging as needed. Returns false if both ways are blocked.
        bool WalkTowards(int tx, int tz)
        {
            foreach (var (dx, dz) in DirectionsTowards(tx, tz))
            {
                (dirX, dirZ) = (dx, dz);
                if (Step(climb: true, dig: true))
                    return true;
            }
            return false;
        }

        // Engineers at the bank: puts one of the team's force fields in the next empty spot of the ring round it,
        // within reach and not where it is standing. Returns false when there is nothing more to build here.
        bool Fortify()
        {
            if (world.FindBank(Player.Team) is not (int, int, int) bank)
                return false;
            foreach (var (cx, cy, cz) in DefenceRing(bank))
            {
                if (world.BlockAt(cx, cy, cz) != BlockType.None || (cx == x && cz == z && (cy == fy || cy == fy + 1)))
                    continue;
                if (Vector3.Distance(Player.Position, new Vector3(cx + 0.5f, cy + 0.5f, cz + 0.5f)) > BuildReach)
                    continue;
                if (TryBuild(cx, cy, cz, OwnForceField))
                {
                    DefencesBuilt++;
                    defenceBudget -= (int)BlockInformation.GetCost(OwnForceField);
                    return true;
                }
            }
            return false;
        }

        // Sappers back on the surface with an explosive to spare sometimes go after the enemy's defences.
        bool WantsToRaid()
        {
            if (Skill == BotSkill.Easy || Player.Ore < BlockInformation.GetCost(BlockType.Explosive))
                return false;
            return world.FindBank(Enemy) is (int, int, int) bank && DefencesAround(bank, Enemy) > 0 && random.Next(2) == 0;
        }

        // Walks to the enemy bank and blasts a way through the defences round it.
        void Raid()
        {
            (int x, int y, int z)? bank = world.FindBank(Enemy);
            bool through = bank != null && Math.Max(Math.Abs(bank.Value.x - x), Math.Abs(bank.Value.z - z)) <= 1;
            if (through)
                RaidsThrough++;
            if (bank == null || through || planTime > RaidLimit || Player.Ore < BlockInformation.GetCost(BlockType.Explosive))
            {
                SetPlan(Plan.Wander);
                stepsLeft = 3 + random.Next(8);
                TurnRandomly();
                return;
            }
            if (WalkTowards(bank.Value.x, bank.Value.z))
                return;
            foreach (var (dx, dz) in DirectionsTowards(bank.Value.x, bank.Value.z))
            {
                (dirX, dirZ) = (dx, dz);
                if (TryStartBlast(then: Plan.Raid))
                    return;
            }
            TurnRandomly();
            Step(climb: true, dig: true);
        }

        // Sappers facing rock (or enemy defences): back off a block, put an explosive where they stood, retreat out of
        // range and set it off, then carry on with `then`. Needs a straight, open way behind to retreat along.
        // Returns false if it can't be done here.
        bool TryStartBlast(Plan then = Plan.Tunnel)
        {
            int nx = x + dirX, nz = z + dirZ;
            if (!Blastable(nx, fy, nz) && !Blastable(nx, fy + 1, nz))
                return false;
            if (Player.Ore < BlockInformation.GetCost(BlockType.Explosive))
                return false;
            for (int back = 1; back <= BlastDistance + 1; back++)
            {
                int bx = x - dirX * back, bz = z - dirZ * back;
                if (!InsideMargin(bx, bz) || Solid(bx, fy, bz) || Solid(bx, fy + 1, bz) || !Solid(bx, fy - 1, bz))
                    return false;
            }
            SetPlan(Plan.Blast);
            blastStage = 0;
            afterBlast = then;
            return true;
        }

        void ContinueBlast()
        {
            int backX = -dirX, backZ = -dirZ;
            switch (blastStage)
            {
                case 0:
                    // Step back; the explosive goes where we were standing, against the rock.
                    blastStage = 1;
                    StartMove(x + backX, fy, z + backZ, WalkSpeed);
                    return;
                case 1:
                    if (!TryBuild(x - backX, fy, z - backZ, BlockType.Explosive))
                    {
                        SetPlan(afterBlast);
                        ChooseDirection();
                        return;
                    }
                    blastStage = 2;
                    retreatLeft = BlastDistance - 1;
                    return;
                case 2:
                    if (retreatLeft-- > 0)
                    {
                        StartMove(x + backX, fy, z + backZ, WalkSpeed);
                        return;
                    }
                    Player.Tool = PlayerTools.Detonator;
                    world.Detonate(Player);
                    Blasts++;
                    blastStage = 3;
                    actionCooldown = 0.5f;
                    return;
                default:
                    if (actionCooldown > 0)
                        return;
                    // Back to the blasted rock face (or on with the raid).
                    SetPlan(afterBlast);
                    stepsLeft = Math.Max(stepsLeft, 8);
                    return;
            }
        }
    }
}
