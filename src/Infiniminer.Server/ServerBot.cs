using System.Numerics;

namespace Infiniminer
{
    // What a bot needs from the server. The server implements it with the same rules real players get.
    public interface IBotWorld
    {
        int MapSize { get; }
        BlockType BlockAt(int x, int y, int z);         // outside the map counts as indestructible
        void Dig(ServerPlayer bot, int x, int y, int z);
        void Moved(ServerPlayer bot);                   // tell everyone where the bot is
        void Respawn(ServerPlayer bot);
        void Kill(ServerPlayer bot, string deathMessage);
    }

    // A computer player: a miner that digs down, tunnels for gold and diamonds, and climbs back to the surface
    // (where the server banks its loot, as it does for everyone). It moves one block at a time, digging what is
    // in the way, and keeps away from lava.
    public sealed class ServerBot
    {
        public const float RespawnDelay = 3;
        const float FallSpeed = 12;     // blocks per second
        const int LethalFall = 15;      // blocks, as for players (GameRules' 15.5 m)
        const float StuckLimit = 30;    // seconds without progress before giving up and respawning

        public readonly ServerPlayer Player;
        readonly IBotWorld world;
        readonly Random random;

        enum Plan { Wander, Descend, Tunnel, Ascend }
        Plan plan = Plan.Wander;
        int x, fy, z;                   // the block the bot's feet are in
        int dirX = 1, dirZ = 0;
        int stepsLeft, targetDepth, tunnelsLeft;
        float digCooldown, deadTime, sendTimer, toolTimer, stuckTime;
        bool moving, falling;
        Vector3 moveFrom, moveTo;
        float moveProgress, moveDuration;
        int fallStartY;

        // For tests and the console.
        public int BlocksDug { get; private set; }
        public int TripsHome { get; private set; }
        public int DeepestY { get; private set; } = int.MaxValue;

        public ServerBot(ServerPlayer player, IBotWorld world, int seed)
        {
            Player = player;
            this.world = world;
            random = new Random(seed);
            player.Tool = PlayerTools.Pickaxe;
        }

        int SurfaceFeetY
        {
            // Above this the server counts the bot as on the surface (and banks its loot).
            get { return world.MapSize - GameConstants.GroundLevel - 1; }
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

        // Diggable with a pickaxe, inside the map, and not about to let lava in.
        bool Diggable(int bx, int by, int bz)
        {
            int edge = world.MapSize - 1;
            return bx > 0 && by > 0 && bz > 0 && bx < edge && by < edge && bz < edge
                && GameRules.Dig(world.BlockAt(bx, by, bz), false).Removes && !NearLava(bx, by, bz);
        }

        bool Treasure(int bx, int by, int bz)
        {
            BlockType block = world.BlockAt(bx, by, bz);
            return block == BlockType.Gold || block == BlockType.Diamond;
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
        }

        Vector3 CellPosition(int cx, int cy, int cz)
        {
            return new Vector3(cx + 0.5f, cy - GameRules.FootOffset - 0.05f, cz + 0.5f);
        }

        void TurnRandomly()
        {
            int[][] directions = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
            int[] chosen = directions[random.Next(4)];
            dirX = chosen[0];
            dirZ = chosen[1];
        }

        // Heads for gold or diamonds within a few blocks (the glint a player would notice), otherwise anywhere.
        void ChooseDirection()
        {
            int best = int.MaxValue;
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                for (int distance = 1; distance <= 6 && distance < best; distance++)
                {
                    int cx = x + dx * distance, cz = z + dz * distance;
                    if (!InsideMargin(cx, cz))
                        break;
                    if (Treasure(cx, fy, cz) || Treasure(cx, fy + 1, cz) || Treasure(cx, fy - 1, cz))
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

        // One swing of the pickaxe, when it is ready. Returns true if the bot spent its turn digging.
        bool TryDig(int bx, int by, int bz)
        {
            if (digCooldown > 0)
                return true;
            Vector3 eye = Player.Position;
            Vector3 target = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);
            if (target != eye)
                Player.Heading = Vector3.Normalize(target - eye);
            world.Dig(Player, bx, by, bz);
            BlocksDug++;
            digCooldown = GameRules.GetToolCooldown(PlayerTools.Pickaxe, Player.Class) + 0.05f;
            Player.UsingTool = true;
            toolTimer = 0.3f;
            return true;
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
                    plan = Plan.Wander;
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
            sendTimer -= elapsed;
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

            stuckTime += elapsed;
            if (stuckTime > StuckLimit)
            {
                world.Kill(Player, "HAS COMMMITTED PIXELCIDE!");
                return;
            }

            // Gold and diamonds right next to us are always worth a swing.
            if (Player.Weight < Player.WeightMax)
                foreach (var (tx, ty, tz) in new[] { (x + 1, fy, z), (x - 1, fy, z), (x, fy, z + 1), (x, fy, z - 1),
                                                     (x + 1, fy + 1, z), (x - 1, fy + 1, z), (x, fy + 1, z + 1), (x, fy + 1, z - 1),
                                                     (x, fy + 2, z), (x, fy - 1, z) })
                    if (Treasure(tx, ty, tz) && Diggable(tx, ty, tz))
                    {
                        TryDig(tx, ty, tz);
                        return;
                    }

            switch (plan)
            {
                case Plan.Wander:
                    if (stepsLeft <= 0 || fy < SurfaceFeetY)
                    {
                        plan = Plan.Descend;
                        targetDepth = Math.Max(3, SurfaceFeetY - 6 - random.Next(20));
                        return;
                    }
                    if (!Step(climb: true, dig: false))
                        TurnRandomly();
                    return;

                case Plan.Descend:
                    if (fy <= targetDepth || !Diggable(x, fy - 1, z))
                    {
                        plan = Plan.Tunnel;
                        stepsLeft = 10 + random.Next(20);
                        tunnelsLeft = 3;
                        ChooseDirection();
                        return;
                    }
                    TryDig(x, fy - 1, z);
                    return;

                case Plan.Tunnel:
                    // Home when full, or after a few tunnels; an empty-handed miner tries another direction first.
                    if (Player.Weight >= Player.WeightMax || (stepsLeft <= 0 && (Player.Weight > 0 || tunnelsLeft <= 0)))
                    {
                        plan = Plan.Ascend;
                        return;
                    }
                    if (stepsLeft <= 0)
                    {
                        tunnelsLeft--;
                        stepsLeft = 10 + random.Next(20);
                        ChooseDirection();
                    }
                    if (!Step(climb: false, dig: true))
                        ChooseDirection();
                    return;

                case Plan.Ascend:
                    if (fy >= SurfaceFeetY)
                    {
                        // On the surface: the server banks the loot. Head off somewhere new.
                        TripsHome++;
                        plan = Plan.Wander;
                        stepsLeft = 3 + random.Next(8);
                        TurnRandomly();
                        return;
                    }
                    if (!Climb())
                        TurnRandomly();
                    return;
            }
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
                StartMove(nx, fy, nz, GameRules.MoveSpeed);
                stepsLeft--;
                return true;
            }

            if (climb && Solid(nx, fy, nz) && !Solid(nx, fy + 1, nz) && !Solid(nx, fy + 2, nz) && !Solid(x, fy + 2, z)
                && !NearLava(nx, fy + 1, nz))
            {
                StartMove(nx, fy + 1, nz, GameRules.MoveSpeed);
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

        // Cuts a staircase up: clears headroom above us and the two blocks above the next step, then steps up.
        bool Climb()
        {
            int nx = x + dirX, nz = z + dirZ;
            if (!InsideMargin(nx, nz))
                return false;

            // The next step needs something to stand on; over a gap, walk across at this level instead.
            if (!Solid(nx, fy, nz))
                return Step(climb: false, dig: true);

            foreach (var (bx, by, bz) in new[] { (x, fy + 2, z), (nx, fy + 2, nz), (nx, fy + 1, nz) })
                if (Solid(bx, by, bz))
                {
                    if (!Diggable(bx, by, bz))
                        return false;
                    return TryDig(bx, by, bz);
                }
            if (NearLava(nx, fy + 1, nz) || NearLava(nx, fy + 2, nz))
                return false;
            StartMove(nx, fy + 1, nz, GameRules.MoveSpeed);
            return true;
        }
    }
}
