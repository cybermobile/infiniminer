using System.Numerics;
using Xunit;

namespace Infiniminer.Tests
{
    // Bot behaviours on small hand-made worlds.
    public class BotBehaviourTests
    {
        sealed class FakeWorld : IBotWorld
        {
            public readonly BlockType[,,] Blocks;
            public int MapSize { get; }
            public readonly List<(int x, int y, int z, BlockType type)> Built = new List<(int, int, int, BlockType)>();
            public int Detonations;
            public uint OreDeposited;

            public FakeWorld(int size, BlockType fill)
            {
                MapSize = size;
                Blocks = new BlockType[size, size, size];
                for (int x = 0; x < size; x++)
                    for (int y = 0; y < size; y++)
                        for (int z = 0; z < size; z++)
                            Blocks[x, y, z] = fill;
            }

            public BlockType BlockAt(int x, int y, int z)
            {
                if (x < 0 || y < 0 || z < 0 || x >= MapSize || y >= MapSize || z >= MapSize)
                    return y >= MapSize ? BlockType.None : BlockType.Metal;
                return Blocks[x, y, z];
            }

            public void Dig(ServerPlayer bot, int x, int y, int z)
            {
                if (GameRules.Dig(Blocks[x, y, z], false).Removes)
                    Blocks[x, y, z] = BlockType.None;
            }

            public bool Build(ServerPlayer bot, int x, int y, int z, BlockType type)
            {
                uint cost = BlockInformation.GetCost(type);
                if (Blocks[x, y, z] != BlockType.None || bot.Ore < cost)
                    return false;
                Blocks[x, y, z] = type;
                bot.Ore -= cost;
                Built.Add((x, y, z, type));
                if (type == BlockType.Explosive)
                    bot.ExplosiveList.Add(new Vector3(x, y, z));
                return true;
            }

            public void Detonate(ServerPlayer bot)
            {
                foreach (Vector3 explosive in bot.ExplosiveList)
                {
                    Detonations++;
                    for (int dx = -2; dx <= 2; dx++)
                        for (int dy = -2; dy <= 2; dy++)
                            for (int dz = -2; dz <= 2; dz++)
                            {
                                int x = (int)explosive.X + dx, y = (int)explosive.Y + dy, z = (int)explosive.Z + dz;
                                if (x > 0 && y > 0 && z > 0 && x < MapSize - 1 && y < MapSize - 1 && z < MapSize - 1)
                                    Blocks[x, y, z] = BlockType.None;
                            }
                    if (Vector3.Distance(explosive + new Vector3(0.5f), bot.Position) < 3)
                        bot.Alive = false;
                }
                bot.ExplosiveList.Clear();
            }

            public bool AtOwnBank(ServerPlayer bot)
            {
                return FindBank(bot.Team) is (int x, int y, int z)
                    && Vector3.Distance(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), bot.Position) <= 3.5f;
            }

            public void DepositOre(ServerPlayer bot)
            {
                uint amount = Math.Min(50, bot.Ore);
                bot.Ore -= amount;
                OreDeposited += amount;
            }

            public (int x, int y, int z)? FindBank(PlayerTeam team)
            {
                BlockType bank = team == PlayerTeam.Red ? BlockType.BankRed : BlockType.BankBlue;
                for (int x = 0; x < MapSize; x++)
                    for (int y = 0; y < MapSize; y++)
                        for (int z = 0; z < MapSize; z++)
                            if (Blocks[x, y, z] == bank)
                                return (x, y, z);
                return null;
            }

            public (int x, int y, int z)? Hill { get; set; }
            public void Moved(ServerPlayer bot) { }
            public void Respawn(ServerPlayer bot) { bot.Alive = true; }
            public void Kill(ServerPlayer bot, string message) { bot.Alive = false; }

            public void Carve(int x0, int x1, int y0, int y1, int z0, int z1, BlockType type = BlockType.None)
            {
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        for (int z = z0; z <= z1; z++)
                            Blocks[x, y, z] = type;
            }
        }

        static ServerBot MakeBot(FakeWorld world, PlayerClass playerClass, int x, int y, int z, uint ore, PlayerTeam team = PlayerTeam.Red)
        {
            ServerPlayer player = new ServerPlayer(null, "bot") { Team = team, Alive = true, OreMax = 350, WeightMax = 4, Ore = ore };
            player.SetClass(playerClass);
            player.Position = new Vector3(x + 0.5f, y + 1.45f, z + 0.5f);
            return new ServerBot(player, world, 1);
        }

        static void Run(ServerBot bot, float seconds)
        {
            for (float t = 0; t < seconds && bot.Player.Alive; t += 0.05f)
                bot.Update(0.05f);
        }

        // Runs while the bot is still at something (at most `seconds`).
        static void RunWhile(ServerBot bot, Func<bool> busy, float seconds)
        {
            for (float t = 0; t < seconds && bot.Player.Alive && busy(); t += 0.05f)
                bot.Update(0.05f);
        }

        [Fact]
        public void HardBotsGetMoreDoneThanEasyOnes()
        {
            int Dug(BotSkill skill)
            {
                // A big block of dirt to tunnel through.
                FakeWorld world = new FakeWorld(48, BlockType.Dirt);
                world.Carve(20, 20, 20, 21, 20, 20);
                ServerBot bot = MakeBot(world, PlayerClass.Miner, 20, 20, 20, ore: 0);
                bot.Skill = skill;
                bot.StartTunnel(1, 0);
                Run(bot, 60);
                return bot.BlocksDug;
            }

            int easy = Dug(BotSkill.Easy), normal = Dug(BotSkill.Normal), hard = Dug(BotSkill.Hard);
            Assert.True(easy < normal && normal <= hard, $"easy {easy}, normal {normal}, hard {hard}");
            Assert.True(hard > easy * 3 / 2, $"easy {easy}, hard {hard}");
        }

        [Fact]
        public void SappersBlastThroughRockFromASafeDistance()
        {
            // Solid rock, with a corridor from x = 3 to 15 at y = 10..11, z = 10.
            FakeWorld world = new FakeWorld(32, BlockType.Rock);
            world.Carve(3, 15, 10, 11, 10, 10);
            ServerBot sapper = MakeBot(world, PlayerClass.Sapper, 15, 10, 10, ore: 200);

            sapper.StartTunnel(1, 0);
            Run(sapper, 8);

            Assert.True(sapper.Player.Alive, "the sapper should have been out of the blast");
            Assert.Equal(1, world.Detonations);
            Assert.Contains(world.Built, b => b.type == BlockType.Explosive && b.x == 15);
            Assert.Equal(BlockType.None, world.Blocks[16, 10, 10]);
            // The blast leaves a crater (it clears two blocks all round); the sapper carries on into it.
            Assert.True(sapper.Player.Position.X > 12, "the sapper should carry on into the blasted rock: " + sapper.Describe());
        }

        [Fact]
        public void BotsGetAwayFromLavaThatReachesThem()
        {
            // Open dirt floor at y = 9; lava flows in next to the bot.
            FakeWorld world = new FakeWorld(32, BlockType.Dirt);
            world.Carve(2, 29, 10, 20, 2, 29);
            ServerBot miner = MakeBot(world, PlayerClass.Miner, 10, 10, 10, ore: 0);
            miner.StartTunnel(1, 0);
            world.Blocks[11, 10, 10] = BlockType.Lava;

            miner.Update(0.05f);
            Assert.Equal(1, miner.LavaEscapes);
            Assert.True(miner.Ascending);
            Run(miner, 1);
            Assert.True(miner.Player.Alive);
            Assert.False((int)MathF.Floor(miner.Player.Position.X) == 10 && (int)MathF.Floor(miner.Player.Position.Z) == 10,
                "the bot should have stepped away from the lava");
        }

        [Fact]
        public void EngineersBuildTheTeamABankAndFillItWithOre()
        {
            FakeWorld world = new FakeWorld(32, BlockType.Dirt);
            world.Carve(2, 29, 25, 30, 2, 29);      // open ground at y = 24, the surface
            ServerBot engineer = MakeBot(world, PlayerClass.Engineer, 15, 25, 15, ore: 200);
            engineer.HeadForBank();
            Run(engineer, 5);

            Assert.Equal(1, engineer.BanksBuilt);
            Assert.Contains(world.Built, b => b.type == BlockType.BankRed);
            // 200 less the bank's 50; half of the rest goes on three force fields round it, the rest in the bank.
            Assert.Equal(3, engineer.DefencesBuilt);
            Assert.Equal(75u, world.OreDeposited);
            Assert.Equal(75u, engineer.OreDeposited);
        }

        [Fact]
        public void EngineersFenceTheBankInWithForceFieldsAndShockBlocks()
        {
            FakeWorld world = new FakeWorld(32, BlockType.Dirt);
            world.Carve(2, 29, 25, 30, 2, 29);
            world.Blocks[15, 25, 15] = BlockType.BankRed;
            var ring = ServerBot.DefenceRing((15, 25, 15)).ToHashSet();
            Assert.Equal(32, ring.Count);

            // Each trip home spends half its ore on defences until they are finished; then it all goes in the bank.
            ServerBot engineer = MakeBot(world, PlayerClass.Engineer, 14, 25, 15, ore: 300);
            void Trip(uint ore)
            {
                engineer.Player.Ore = ore;
                engineer.Player.Position = new Vector3(14.5f, 26.45f, 15.5f);
                engineer.HeadForBank();
                RunWhile(engineer, () => engineer.HeadingForBank, 30);
                Assert.True(engineer.Player.Alive && !engineer.HeadingForBank, "the trip should have finished: " + engineer.Describe());
            }
            Trip(300);
            Assert.Equal(6, engineer.DefencesBuilt);
            Assert.Equal(150u, world.OreDeposited);
            Assert.All(world.Built, b => Assert.Contains((b.x, b.y, b.z), ring));
            Assert.All(world.Built, b => Assert.Equal(BlockType.TransRed, b.type));

            for (int trip = 0; trip < 6; trip++)
                Trip(350);
            Assert.Equal(32, ring.Count(c => world.Blocks[c.x, c.y, c.z] == BlockType.TransRed));

            // Then a rail of shock blocks a block above the top of the fence.
            var rail = ServerBot.ShockRail((15, 25, 15)).ToList();
            Assert.Equal(16, rail.Count);
            Assert.All(rail, c => Assert.Equal(28, c.y));
            for (int trip = 0; trip < 6; trip++)
                Trip(350);
            Assert.Equal(16, rail.Count(c => world.Blocks[c.x, c.y, c.z] == BlockType.Shock));
            Assert.Equal(48, engineer.DefencesBuilt);
            uint before = world.OreDeposited;
            Trip(200);
            Assert.Equal(before + 200, world.OreDeposited);

            // Easy engineers just deposit.
            FakeWorld easyWorld = new FakeWorld(32, BlockType.Dirt);
            easyWorld.Carve(2, 29, 25, 30, 2, 29);
            easyWorld.Blocks[15, 25, 15] = BlockType.BankRed;
            ServerBot easy = MakeBot(easyWorld, PlayerClass.Engineer, 14, 25, 15, ore: 300);
            easy.Skill = BotSkill.Easy;
            easy.HeadForBank();
            RunWhile(easy, () => easy.HeadingForBank, 30);
            Assert.Equal(0, easy.DefencesBuilt);
            Assert.Equal(300u, easyWorld.OreDeposited);
        }

        [Fact]
        public void SappersBlastThroughTheEnemyBanksDefences()
        {
            // Open ground at y = 24; a blue bank at (24, 25, 15), fenced in with blue force fields.
            FakeWorld world = new FakeWorld(32, BlockType.Dirt);
            world.Carve(2, 29, 25, 30, 2, 29);
            world.Blocks[24, 25, 15] = BlockType.BankBlue;
            var ring = ServerBot.DefenceRing((24, 25, 15)).ToList();
            foreach (var (x, y, z) in ring)
                world.Blocks[x, y, z] = BlockType.TransBlue;

            ServerBot sapper = MakeBot(world, PlayerClass.Sapper, 8, 25, 15, ore: 200);
            sapper.StartRaid();
            RunWhile(sapper, () => sapper.Raiding, 60);

            Assert.True(sapper.Player.Alive, "the sapper should have been out of the blast: " + sapper.Describe());
            Assert.True(world.Detonations >= 1);
            Assert.Contains(ring, c => world.Blocks[c.x, c.y, c.z] == BlockType.None);
            // Through the gap to the bank.
            Assert.Equal(1, sapper.Raids);
            Assert.Equal(1, sapper.RaidsThrough);

            // Blue bots walk through their own force fields (red ones can't dig them).
            FakeWorld fenced = new FakeWorld(32, BlockType.Dirt);
            fenced.Carve(2, 29, 25, 30, 2, 29);
            fenced.Blocks[24, 25, 15] = BlockType.BankBlue;
            foreach (var (x, y, z) in ring)
                fenced.Blocks[x, y, z] = BlockType.TransBlue;
            ServerBot blue = MakeBot(fenced, PlayerClass.Engineer, 8, 25, 15, ore: 100, team: PlayerTeam.Blue);
            blue.HeadForBank();
            RunWhile(blue, () => blue.HeadingForBank, 30);
            // Half its ore goes on a shock block above the (already finished) fence, the rest in the bank.
            Assert.Equal(1, blue.DefencesBuilt);
            Assert.Equal(50u, blue.OreDeposited);
        }

        [Fact]
        public void EngineersClimbOutOfShaftsOnLadders()
        {
            // A deep shaft in dirt: the engineer climbs it on ladders, leaving them behind.
            FakeWorld world = new FakeWorld(64, BlockType.Dirt);
            world.Carve(1, 62, 56, 62, 1, 62);       // open air above the surface (feet at y >= 55)
            world.Carve(20, 20, 30, 55, 20, 20);     // the shaft
            ServerBot engineer = MakeBot(world, PlayerClass.Engineer, 20, 30, 20, ore: 350);
            engineer.StartTunnel(1, 0);
            engineer.Player.Ore = 350;
            // Full of ore: it heads home straight away.
            Run(engineer, 30);

            Assert.True(engineer.LaddersBuilt >= 5, "built " + engineer.LaddersBuilt + " ladders");
            Assert.All(world.Built.Where(b => b.type == BlockType.Ladder), b => Assert.Equal((20, 20), (b.x, b.z)));
            Assert.Equal(1, engineer.TripsHome);
        }
    }
}
