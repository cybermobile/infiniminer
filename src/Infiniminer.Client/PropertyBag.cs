using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Xna.Framework;

namespace Infiniminer
{
    public enum ScreenEffect
    {
        None,
        Death,
        Teleport,
        Fall,
        Explosion,
    }

    public class PropertyBag
    {
        // Game engines.
        public BlockEngine blockEngine = null;
        public InterfaceEngine interfaceEngine = null;
        public PlayerEngine playerEngine = null;
        public SkyplaneEngine skyplaneEngine = null;
        public ParticleEngine particleEngine = null;

        // Network stuff.
        public NetManager netClient = null;
        public EventBasedNetListener netListener = null;
        public NetPeer serverPeer = null;
        public bool downloadComplete = false;
        public BlockPredictor blockPredictor;
        public double currentTime = 0;
        public List<ServerInformation> discoveredServers = new List<ServerInformation>();
        public Dictionary<uint, Player> playerList = new Dictionary<uint, Player>();
        public bool[,] mapLoadProgress = null;
        public int mapSize = GameConstants.MapSize;

        // The y coordinate of the surface: above it is "altitude 0", where loot is banked and classes can change.
        public float SurfaceHeight
        {
            get { return mapSize - GameConstants.GroundLevel; }
        }

        // Player variables.
        public Camera playerCamera = null;

        // The player at this computer.
        public readonly LocalPlayer player = new LocalPlayer();

        // Team variables.
        public uint teamOre = 0;
        public uint teamRedCash = 0;
        public uint teamBlueCash = 0;
        public PlayerTeam teamWinners = PlayerTeam.None;
        public double matchEndTime = double.NaN;    // in game time; NaN: no time limit
        public bool overtime = false;
        public uint winningCash = 0;
        public Dictionary<Vector3, Beacon> beaconList = new Dictionary<Vector3, Beacon>();

        // Screen effect stuff.
        private Random randGen = new Random();
        public ScreenEffect screenEffect = ScreenEffect.None;
        public double screenEffectCounter = 0;

        public readonly SoundPlayer sounds = new SoundPlayer();

        // Chat stuff.
        public ChatMessageType chatMode = ChatMessageType.None;
        public readonly ChatLog chatLog = new ChatLog();
        public string chatEntryBuffer = "";

        public PropertyBag(InfiniminerGame gameInstance)
        {
            // Initialize our network device.
            netListener = new EventBasedNetListener();
            netClient = new NetManager(netListener);
            netClient.ChannelsCount = NetChannel.ChannelCount;
            netClient.AutoRecycle = true;
            netClient.UnconnectedMessagesEnabled = true; // for LAN discovery replies
            //netClient.SimulateLatency = true;
            //netClient.SimulationMinLatency = 100;
            //netClient.SimulationMaxLatency = 150;
            //netClient.SimulatePacketLoss = true;
            //netClient.SimulationPacketLossChance = 10;
            netClient.Start();

            // Initialize engines.
        // (The block predictor talks to whichever block engine is current; it is replaced when the map size changes.)
            blockEngine = new BlockEngine(gameInstance);
            interfaceEngine = new InterfaceEngine(gameInstance);
            playerEngine = new PlayerEngine(gameInstance);
            skyplaneEngine = new SkyplaneEngine(gameInstance);
            particleEngine = new ParticleEngine(gameInstance);

            blockPredictor = new BlockPredictor(
                (x, y, z) => blockEngine.BlockAtPoint(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f)),
                SetBlockLocally);

            // Create a camera.
            playerCamera = new Camera(gameInstance.GraphicsDevice);
            playerCamera.FieldOfView = gameInstance.Settings.FieldOfView;
            UpdateCamera();

            // Load sounds.
            if (!gameInstance.NoSound && !sounds.Load(gameInstance.Assets))
                gameInstance.NoSound = true;
        }

        public bool IsConnected
        {
            get { return serverPeer != null && serverPeer.ConnectionState == ConnectionState.Connected; }
        }

        public void Send(OutgoingMessage message)
        {
            if (IsConnected)
                serverPeer.Send(message.Data, message.Channel);
        }

        public void KillPlayer(string deathMessage)
        {
            if (!IsConnected)
                return;

            EnterDeathState();

            Send(ClientMessages.Died(deathMessage));
        }

        // Shows the death screen. Called for deaths we notice ourselves and for deaths the server decides.
        public void EnterDeathState()
        {
            if (player.Dead && screenEffect == ScreenEffect.Death)
                return;
            // A spectator has no body to lose (the server confirms the switch with a "death").
            if (player.Spectating)
            {
                player.Dead = true;
                return;
            }

            PlaySound(InfiniminerSound.Death);
            player.Position = new Vector3(randGen.Next(2, mapSize - 2), mapSize + 2, randGen.Next(2, mapSize - 2));
            player.Velocity = Vector3.Zero;
            player.Dead = true;
            player.RespawnPending = false;
            screenEffect = ScreenEffect.Death;
            screenEffectCounter = 0;
        }

        // Asks the server to respawn us; it picks the spawn point and replies with our new position.
        public void RespawnPlayer()
        {
            if (!IsConnected || player.RespawnPending)
                return;

            player.RespawnPending = true;
            screenEffect = ScreenEffect.None;
            screenEffectCounter = 0;

            Send(ClientMessages.Respawn());
        }

        // The server has put us somewhere: a respawn, or a correction of a move it didn't accept.
        public void SetPositionFromServer(Vector3 position, byte epoch, PositionReason reason)
        {
            if (reason == PositionReason.Teleport)
            {
                screenEffect = ScreenEffect.Teleport;
                screenEffectCounter = 0;
            }
            player.Position = position;
            player.Velocity = Vector3.Zero;
            player.PositionEpoch = epoch;
            UpdateCamera();
        }

        // The server confirmed our respawn.
        public void OnRespawned()
        {
            player.Dead = false;
            player.RespawnPending = false;
            screenEffect = ScreenEffect.None;
            screenEffectCounter = 0;
            UpdateCamera();
        }

        public void PlaySound(InfiniminerSound sound)
        {
            sounds.Play(sound);
        }

        public void PlaySound(InfiniminerSound sound, Vector3 position)
        {
            sounds.Play(sound, position - player.Position, playerCamera.GetRightVector());
        }

        public void PlaySoundForEveryone(InfiniminerSound sound, Vector3 position)
        {
            if (!IsConnected)
                return;

            // The PlaySound message can be used to instruct the server to have all clients play a directional sound.
            Send(ClientMessages.PlaySound(sound, position.ToNumerics()));
        }

        // Version used during updates.
        public void UpdateCamera(GameTime gameTime)
        {
            // If we have a gameTime object, apply screen jitter.
            if (screenEffect == ScreenEffect.Explosion)
            {
                if (gameTime != null)
                {
                    screenEffectCounter += gameTime.ElapsedGameTime.TotalSeconds;
                    // For 0 to 2, shake the camera.
                    if (screenEffectCounter < 2)
                    {
                        Vector3 newPosition = playerCamera.Position;
                        newPosition.X += (float)(2 - screenEffectCounter) * (float)(randGen.NextDouble() - 0.5) * 0.5f;
                        newPosition.Y += (float)(2 - screenEffectCounter) * (float)(randGen.NextDouble() - 0.5) * 0.5f;
                        newPosition.Z += (float)(2 - screenEffectCounter) * (float)(randGen.NextDouble() - 0.5) * 0.5f;
                        if (!blockEngine.SolidAtPointForPlayer(newPosition) && (newPosition-player.Position).Length() < 0.7f)
                            playerCamera.Position = newPosition;
                    }
                    // For 2 to 3, move the camera back.
                    else if (screenEffectCounter < 3)
                    {
                        Vector3 lerpVector = player.Position - playerCamera.Position;
                        playerCamera.Position += 0.5f * lerpVector;
                    }
                    else
                    {
                        screenEffect = ScreenEffect.None;
                        screenEffectCounter = 0;
                        playerCamera.Position = player.Position;
                    }
                }
            }
            else
            {
                playerCamera.Position = player.Position;
            }
            playerCamera.Update();
        }

        public void UpdateCamera()
        {
            UpdateCamera(null);
        }

        public void DepositLoot()
        {
            if (!IsConnected)
                return;

            Send(ClientMessages.DepositCash());
        }

        public void DepositOre()
        {
            if (!IsConnected)
                return;

            Send(ClientMessages.DepositOre());
        }

        public void WithdrawOre()
        {
            if (!IsConnected)
                return;

            Send(ClientMessages.WithdrawOre());
        }

        public void SetPlayerTeam(PlayerTeam team)
        {
            if (!IsConnected)
                return;

            player.SetTeam(team);
            Send(ClientMessages.SetTeam(team));
        }

        // Watch instead of play: no team, no body; the camera flies freely from above the middle of the map.
        public void Spectate()
        {
            if (!IsConnected)
                return;
            player.Spectating = true;
            player.Team = PlayerTeam.None;
            player.Dead = true;
            player.RespawnPending = false;
            player.Velocity = Vector3.Zero;
            player.Position = new Vector3(mapSize / 2f, mapSize + 4, mapSize / 2f);
            screenEffect = ScreenEffect.None;
            Send(ClientMessages.SetTeam(PlayerTeam.None));
        }

        public void SetPlayerClass(PlayerClass playerClass)
        {
            if (!IsConnected)
                return;

            player.SetClass(playerClass);
            Send(ClientMessages.SelectClass(playerClass));
        }

        public void FireRadar()
        {
            if (!IsConnected)
                return;

            player.ToolCooldown = GetToolCooldown(PlayerTools.ProspectingRadar);

            UseTool(PlayerTools.ProspectingRadar);
        }

        // Changes a block on our side only (used for predictions).
        public void SetBlockLocally(int x, int y, int z, BlockType blockType)
        {
            if (blockEngine.BlockAtPoint(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f)) != BlockType.None)
                blockEngine.RemoveBlock((ushort)x, (ushort)y, (ushort)z);
            if (blockType != BlockType.None)
                blockEngine.AddBlock((ushort)x, (ushort)y, (ushort)z, blockType);
        }

        static (int x, int y, int z) BlockOf(Vector3 point)
        {
            return (GameConstants.ToBlock(point.X), GameConstants.ToBlock(point.Y), GameConstants.ToBlock(point.Z));
        }

        bool InsideMap((int x, int y, int z) block)
        {
            return block.x > 0 && block.y > 0 && block.z > 0 && block.x < mapSize - 1 && block.y < mapSize - 1 && block.z < mapSize - 1;
        }

        // Removes the block we're digging straight away if the server will (same ray and rules as the server).
        void PredictDig(bool drill)
        {
            Vector3 hitPoint = Vector3.Zero, buildPoint = Vector3.Zero;
            if (!blockEngine.RayCollision(player.Position, playerCamera.GetLookVector(), 2, 10, ref hitPoint, ref buildPoint))
                return;
            bool removed = GameRules.DigRemovesBlock(blockEngine.BlockAtPoint(hitPoint), drill, player.Weight, player.WeightMax);
            var block = BlockOf(hitPoint);
            if (removed && InsideMap(block))
                blockPredictor.Predict(block.x, block.y, block.z, BlockType.None, currentTime);
        }

        // Places the block we're building straight away if the server is likely to accept it.
        void PredictBuild(BlockType blockType)
        {
            Vector3 hitPoint = Vector3.Zero, buildPoint = Vector3.Zero;
            if (!blockEngine.RayCollision(player.Position, playerCamera.GetLookVector(), 6, 25, ref hitPoint, ref buildPoint))
                return;
            if (player.Ore < BlockInformation.GetCost(blockType) || blockEngine.BlockAtPoint(hitPoint) == BlockType.Lava)
                return;
            var block = BlockOf(buildPoint);
            if (!InsideMap(block))
                return;
            // Nobody may be standing there (the server checks the same cells).
            foreach (Player p in playerList.Values)
            {
                Vector3 position = p.ID == player.Id ? player.Position : p.Position;
                if ((p.Alive || p.ID == player.Id) && (int)position.X == block.x && (int)position.Z == block.z
                    && ((int)position.Y == block.y || (int)position.Y - 1 == block.y))
                    return;
            }
            blockPredictor.Predict(block.x, block.y, block.z, blockType, currentTime);
        }

        void UseTool(PlayerTools tool, BlockType blockType = BlockType.None)
        {
            Send(ClientMessages.UseTool(player.Position.ToNumerics(), playerCamera.GetLookVector().ToNumerics(), tool, blockType));
        }

        public void FireDrill()
        {
            if (!IsConnected)
                return;

            player.ToolCooldown = GetToolCooldown(PlayerTools.Drill);

            UseTool(PlayerTools.Drill);
            PredictDig(true);
        }

        public void FirePickaxe()
        {
            if (!IsConnected)
                return;

            player.ToolCooldown = GetToolCooldown(PlayerTools.Pickaxe);

            UseTool(PlayerTools.Pickaxe);
            PredictDig(false);
        }

        public void FireConstructionGun(BlockType blockType)
        {
            if (!IsConnected)
                return;

            player.ToolCooldown = GetToolCooldown(PlayerTools.ConstructionGun);
            player.ConstructionGunAnimation = -5;

            UseTool(PlayerTools.ConstructionGun, blockType);
            PredictBuild(blockType);
        }

        public void FireDeconstructionGun()
        {
            if (!IsConnected)
                return;

            player.ToolCooldown = GetToolCooldown(PlayerTools.DeconstructionGun);
            player.ConstructionGunAnimation = -5;

            UseTool(PlayerTools.DeconstructionGun);
        }

        public void FireDetonator()
        {
            if (!IsConnected)
                return;

            player.ToolCooldown = GetToolCooldown(PlayerTools.Detonator);

            UseTool(PlayerTools.Detonator);
        }

        public void ToggleRadar()
        {
            player.RadarMute = !player.RadarMute;
            PlaySound(InfiniminerSound.RadarSwitch);
        }

        public void ReadRadar(ref float distanceReading, ref float valueReading)
        {
            (distanceReading, valueReading) = Radar.Scan(blockEngine.BlockAtPoint, player.Position, playerCamera.GetLookVector());
        }

        // Returns true if the player is able to use a bank right now.
        public bool AtBankTerminal()
        {
            // Figure out what we're looking at.
            Vector3 hitPoint = Vector3.Zero;
            Vector3 buildPoint = Vector3.Zero;
            if (!blockEngine.RayCollision(player.Position, playerCamera.GetLookVector(), 2.5f, 25, ref hitPoint, ref buildPoint))
                return false;

            // If it's a valid bank object, we're good!
            BlockType blockType = blockEngine.BlockAtPoint(hitPoint);
            if (blockType == BlockType.BankRed && player.Team == PlayerTeam.Red)
                return true;
            if (blockType == BlockType.BankBlue && player.Team == PlayerTeam.Blue)
                return true;
            return false;
        }

        public float GetToolCooldown(PlayerTools tool)
        {
            return GameRules.GetToolCooldown(tool, player.Class);
        }

        public void SendPlayerUpdate()
        {
            if (!IsConnected || player.Spectating)
                return;

            Send(ClientMessages.PlayerUpdate(player.Position.ToNumerics(), playerCamera.GetLookVector().ToNumerics(),
                player.Tools[player.ToolSelected], player.ToolCooldown > 0.001f, player.PositionEpoch));
        }
    }
}
