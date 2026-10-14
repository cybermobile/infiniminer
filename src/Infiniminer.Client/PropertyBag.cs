using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

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

    public class ChatMessage
    {
        public string message;
        public ChatMessageType type;
        public float timestamp;

        public ChatMessage(string message, ChatMessageType type, float timestamp)
        {
            this.message = message;
            this.type = type;
            this.timestamp = timestamp;
        }
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
        public Vector3 playerPosition = Vector3.Zero;
        public Vector3 playerVelocity = Vector3.Zero;
        public PlayerClass playerClass;
        public PlayerTools[] playerTools = new PlayerTools[1] { PlayerTools.Pickaxe };
        public int playerToolSelected = 0;
        public BlockType[] playerBlocks = new BlockType[1] {BlockType.None};
        public int playerBlockSelected = 0;
        public PlayerTeam playerTeam = PlayerTeam.Red;
        public bool playerDead = true;
        public bool respawnPending = false;
        public byte positionEpoch = 0;
        public uint playerOre = 0;
        public uint playerCash = 0;
        public uint playerWeight = 0;
        public uint playerOreMax = 0;
        public uint playerWeightMax = 0;
        public bool playerRadarMute = false;
        public float playerToolCooldown = 0;
        public string playerHandle = "Player";
        public float volumeLevel = 1.0f;
        public uint playerMyId = 0;
        public float radarCooldown = 0;
        public float radarDistance = 0;
        public float radarValue = 0;
        public float constructionGunAnimation = 0;

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

        // Sound stuff.
        public Dictionary<InfiniminerSound, SoundEffect> soundList = new Dictionary<InfiniminerSound, SoundEffect>();

        // Chat stuff.
        public ChatMessageType chatMode = ChatMessageType.None;
        public int chatMaxBuffer = 5;
        public List<ChatMessage> chatBuffer = new List<ChatMessage>(); // chatBuffer[0] is most recent
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
            if (!gameInstance.NoSound)
            {
                try
                {
                    Assets assets = gameInstance.Assets;
                    soundList[InfiniminerSound.DigDirt] = assets.Sound("sounds/dig-dirt");
                    soundList[InfiniminerSound.DigMetal] = assets.Sound("sounds/dig-metal");
                    soundList[InfiniminerSound.Ping] = assets.Sound("sounds/ping");
                    soundList[InfiniminerSound.ConstructionGun] = assets.Sound("sounds/build");
                    soundList[InfiniminerSound.Death] = assets.Sound("sounds/death");
                    soundList[InfiniminerSound.CashDeposit] = assets.Sound("sounds/cash");
                    soundList[InfiniminerSound.ClickHigh] = assets.Sound("sounds/click-loud");
                    soundList[InfiniminerSound.ClickLow] = assets.Sound("sounds/click-quiet");
                    soundList[InfiniminerSound.GroundHit] = assets.Sound("sounds/hitground");
                    soundList[InfiniminerSound.Teleporter] = assets.Sound("sounds/teleport");
                    soundList[InfiniminerSound.Jumpblock] = assets.Sound("sounds/jumpblock");
                    soundList[InfiniminerSound.Explosion] = assets.Sound("sounds/explosion");
                    soundList[InfiniminerSound.RadarHigh] = assets.Sound("sounds/radar-high");
                    soundList[InfiniminerSound.RadarLow] = assets.Sound("sounds/radar-low");
                    soundList[InfiniminerSound.RadarSwitch] = assets.Sound("sounds/switch");
                    soundList[InfiniminerSound.Drill] = assets.Sound("sounds/drill");
                }
                catch (Exception)
                {
                    // No usable audio device: play on without sound rather than crashing.
                    soundList.Clear();
                    gameInstance.NoSound = true;
                }
            }
        }

        public bool IsConnected
        {
            get { return serverPeer != null && serverPeer.ConnectionState == ConnectionState.Connected; }
        }

        void Send(NetDataWriter msgBuffer, NetChannel channel)
        {
            if (IsConnected)
                serverPeer.Send(msgBuffer, channel);
        }

        public void KillPlayer(string deathMessage)
        {
            if (!IsConnected)
                return;

            EnterDeathState();

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerDead);
            msgBuffer.Put(deathMessage);
            Send(msgBuffer, NetChannel.ReliableInOrder2);
        }

        // Shows the death screen. Called for deaths we notice ourselves and for deaths the server decides.
        public void EnterDeathState()
        {
            if (playerDead && screenEffect == ScreenEffect.Death)
                return;

            PlaySound(InfiniminerSound.Death);
            playerPosition = new Vector3(randGen.Next(2, mapSize - 2), mapSize + 2, randGen.Next(2, mapSize - 2));
            playerVelocity = Vector3.Zero;
            playerDead = true;
            respawnPending = false;
            screenEffect = ScreenEffect.Death;
            screenEffectCounter = 0;
        }

        // Asks the server to respawn us; it picks the spawn point and replies with our new position.
        public void RespawnPlayer()
        {
            if (!IsConnected || respawnPending)
                return;

            respawnPending = true;
            screenEffect = ScreenEffect.None;
            screenEffectCounter = 0;

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerAlive);
            Send(msgBuffer, NetChannel.ReliableInOrder2);
        }

        // The server has put us somewhere: a respawn, or a correction of a move it didn't accept.
        public void SetPositionFromServer(Vector3 position, byte epoch, PositionReason reason)
        {
            if (reason == PositionReason.Teleport)
            {
                screenEffect = ScreenEffect.Teleport;
                screenEffectCounter = 0;
            }
            playerPosition = position;
            playerVelocity = Vector3.Zero;
            positionEpoch = epoch;
            UpdateCamera();
        }

        // The server confirmed our respawn.
        public void OnRespawned()
        {
            playerDead = false;
            respawnPending = false;
            screenEffect = ScreenEffect.None;
            screenEffectCounter = 0;
            UpdateCamera();
        }

        public void PlaySound(InfiniminerSound sound)
        {
            if (soundList.TryGetValue(sound, out SoundEffect effect))
                effect.Play(Math.Clamp(volumeLevel, 0, 1), 0, 0);
        }

        public void PlaySound(InfiniminerSound sound, Vector3 position)
        {
            if (!soundList.TryGetValue(sound, out SoundEffect effect))
                return;

            Vector3 offset = position - playerPosition;
            float distance = offset.Length();
            float volume = Math.Max(0, 10 - distance) / 10.0f * volumeLevel;

            // Pan towards the side the sound came from (but not hard left/right, and less so up close).
            float pan = 0;
            if (distance > 0.5f)
                pan = Vector3.Dot(offset / distance, playerCamera.GetRightVector()) * 0.8f * Math.Min(1, distance / 2);
            effect.Play(Math.Clamp(volume, 0, 1), 0, Math.Clamp(pan, -1, 1));
        }

        public void PlaySoundForEveryone(InfiniminerSound sound, Vector3 position)
        {
            if (!IsConnected)
                return;

            // The PlaySound message can be used to instruct the server to have all clients play a directional sound.
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlaySound);
            msgBuffer.Put((byte)sound);
            msgBuffer.Put(position);
            Send(msgBuffer, NetChannel.ReliableUnordered);
        }

        //public void Teleport()
        //{
        //    float x = (float)randGen.NextDouble() * 74 - 5;
        //    float z = (float)randGen.NextDouble() * 74 - 5;
        //    //playerPosition = playerHomeBlock + new Vector3(0.5f, 3, 0.5f);
        //    playerPosition = new Vector3(x, 74, z);
        //    screenEffect = ScreenEffect.Teleport;
        //    screenEffectCounter = 0;
        //    UpdateCamera();
        //}

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
                        if (!blockEngine.SolidAtPointForPlayer(newPosition) && (newPosition-playerPosition).Length() < 0.7f)
                            playerCamera.Position = newPosition;
                    }
                    // For 2 to 3, move the camera back.
                    else if (screenEffectCounter < 3)
                    {
                        Vector3 lerpVector = playerPosition - playerCamera.Position;
                        playerCamera.Position += 0.5f * lerpVector;
                    }
                    else
                    {
                        screenEffect = ScreenEffect.None;
                        screenEffectCounter = 0;
                        playerCamera.Position = playerPosition;
                    }
                }
            }
            else
            {
                playerCamera.Position = playerPosition;
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

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.DepositCash);
            Send(msgBuffer, NetChannel.ReliableInOrder2);
        }

        public void DepositOre()
        {
            if (!IsConnected)
                return;

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.DepositOre);
            Send(msgBuffer, NetChannel.ReliableInOrder2);
        }

        public void WithdrawOre()
        {
            if (!IsConnected)
                return;

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.WithdrawOre);
            Send(msgBuffer, NetChannel.ReliableInOrder2);
        }

        public void SetPlayerTeam(PlayerTeam playerTeam)
        {
            if (!IsConnected)
                return;

            this.playerTeam = playerTeam;

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerSetTeam);
            msgBuffer.Put((byte)playerTeam);
            Send(msgBuffer, NetChannel.ReliableInOrder2);
        }

        public void SetPlayerClass(PlayerClass playerClass)
        {
            if (!IsConnected)
                return;

            this.playerClass = playerClass;

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.SelectClass);
            msgBuffer.Put((byte)playerClass);
            Send(msgBuffer, NetChannel.ReliableInOrder2);

            playerToolSelected = 0;
            playerBlockSelected = 0;

            playerTools = GameRules.GetTools(playerClass);
            playerBlocks = GameRules.GetBlocks(playerClass, playerTeam);
        }

        public void FireRadar()
        {
            if (!IsConnected)
                return;

            playerToolCooldown = GetToolCooldown(PlayerTools.ProspectingRadar);

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.UseTool);
            msgBuffer.Put(playerPosition);
            msgBuffer.Put(playerCamera.GetLookVector());
            msgBuffer.Put((byte)PlayerTools.ProspectingRadar);
            msgBuffer.Put((byte)BlockType.None);
            Send(msgBuffer, NetChannel.ReliableUnordered);
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
            if (!blockEngine.RayCollision(playerPosition, playerCamera.GetLookVector(), 2, 10, ref hitPoint, ref buildPoint))
                return;
            BlockType hit = blockEngine.BlockAtPoint(hitPoint);
            bool removed = hit == BlockType.Dirt || hit == BlockType.DirtSign || hit == BlockType.Ore
                || ((hit == BlockType.Gold || hit == BlockType.Diamond) && playerWeight < playerWeightMax)
                || (drill && hit == BlockType.Rock);
            var block = BlockOf(hitPoint);
            if (removed && InsideMap(block))
                blockPredictor.Predict(block.x, block.y, block.z, BlockType.None, currentTime);
        }

        // Places the block we're building straight away if the server is likely to accept it.
        void PredictBuild(BlockType blockType)
        {
            Vector3 hitPoint = Vector3.Zero, buildPoint = Vector3.Zero;
            if (!blockEngine.RayCollision(playerPosition, playerCamera.GetLookVector(), 6, 25, ref hitPoint, ref buildPoint))
                return;
            if (playerOre < BlockInformation.GetCost(blockType) || blockEngine.BlockAtPoint(hitPoint) == BlockType.Lava)
                return;
            var block = BlockOf(buildPoint);
            if (!InsideMap(block))
                return;
            // Nobody may be standing there (the server checks the same cells).
            foreach (Player p in playerList.Values)
            {
                Vector3 position = p.ID == playerMyId ? playerPosition : p.Position;
                if ((p.Alive || p.ID == playerMyId) && (int)position.X == block.x && (int)position.Z == block.z
                    && ((int)position.Y == block.y || (int)position.Y - 1 == block.y))
                    return;
            }
            blockPredictor.Predict(block.x, block.y, block.z, blockType, currentTime);
        }

        public void FireDrill()
        {
            if (!IsConnected)
                return;

            playerToolCooldown = GetToolCooldown(PlayerTools.Drill);

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.UseTool);
            msgBuffer.Put(playerPosition);
            msgBuffer.Put(playerCamera.GetLookVector());
            msgBuffer.Put((byte)PlayerTools.Drill);
            msgBuffer.Put((byte)BlockType.None);
            Send(msgBuffer, NetChannel.ReliableUnordered);
            PredictDig(true);
        }

        public void FirePickaxe()
        {
            if (!IsConnected)
                return;

            playerToolCooldown = GetToolCooldown(PlayerTools.Pickaxe);

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.UseTool);
            msgBuffer.Put(playerPosition);
            msgBuffer.Put(playerCamera.GetLookVector());
            msgBuffer.Put((byte)PlayerTools.Pickaxe);
            msgBuffer.Put((byte)BlockType.None);
            Send(msgBuffer, NetChannel.ReliableUnordered);
            PredictDig(false);
        }

        public void FireConstructionGun(BlockType blockType)
        {
            if (!IsConnected)
                return;

            playerToolCooldown = GetToolCooldown(PlayerTools.ConstructionGun);
            constructionGunAnimation = -5;

            // Send the message.
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.UseTool);
            msgBuffer.Put(playerPosition);
            msgBuffer.Put(playerCamera.GetLookVector());
            msgBuffer.Put((byte)PlayerTools.ConstructionGun);
            msgBuffer.Put((byte)blockType);
            Send(msgBuffer, NetChannel.ReliableUnordered);
            PredictBuild(blockType);
        }

        public void FireDeconstructionGun()
        {
            if (!IsConnected)
                return;

            playerToolCooldown = GetToolCooldown(PlayerTools.DeconstructionGun);
            constructionGunAnimation = -5;

            // Send the message.
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.UseTool);
            msgBuffer.Put(playerPosition);
            msgBuffer.Put(playerCamera.GetLookVector());
            msgBuffer.Put((byte)PlayerTools.DeconstructionGun);
            msgBuffer.Put((byte)BlockType.None);
            Send(msgBuffer, NetChannel.ReliableUnordered);
        }

        public void FireDetonator()
        {
            if (!IsConnected)
                return;

            playerToolCooldown = GetToolCooldown(PlayerTools.Detonator);

            // Send the message.
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.UseTool);
            msgBuffer.Put(playerPosition);
            msgBuffer.Put(playerCamera.GetLookVector());
            msgBuffer.Put((byte)PlayerTools.Detonator);
            msgBuffer.Put((byte)BlockType.None);
            Send(msgBuffer, NetChannel.ReliableUnordered); 
        }

        public void ToggleRadar()
        {
            playerRadarMute = !playerRadarMute;
            PlaySound(InfiniminerSound.RadarSwitch);
        }

        public void ReadRadar(ref float distanceReading, ref float valueReading)
        {
            valueReading = 0;
            distanceReading = 30;

            // Scan out along the camera axis for 30 meters.
            for (int i=-3;i<=3; i++)
                for (int j = -3; j <= 3; j++)
                {
                    Matrix rotation = Matrix.CreateRotationX((float)(i * Math.PI / 128)) * Matrix.CreateRotationY((float)(j * Math.PI / 128));
                    Vector3 scanPoint = playerPosition;
                    Vector3 lookVector = Vector3.Transform(playerCamera.GetLookVector(), rotation);
                    for (int k = 0; k < 60; k++)
                    {
                        BlockType blockType = blockEngine.BlockAtPoint(scanPoint);
                        if (blockType == BlockType.Gold)
                        {
                            distanceReading = Math.Min(distanceReading, 0.5f * k);
                            valueReading = Math.Max(valueReading, 200);
                        }
                        else if (blockType == BlockType.Diamond)
                        {
                            distanceReading = Math.Min(distanceReading, 0.5f * k);
                            valueReading = Math.Max(valueReading, 1000);
                        }
                        scanPoint += 0.5f * lookVector;
                    }
                }
        }

        // Returns true if the player is able to use a bank right now.
        public bool AtBankTerminal()
        {
            // Figure out what we're looking at.
            Vector3 hitPoint = Vector3.Zero;
            Vector3 buildPoint = Vector3.Zero;
            if (!blockEngine.RayCollision(playerPosition, playerCamera.GetLookVector(), 2.5f, 25, ref hitPoint, ref buildPoint))
                return false;

            // If it's a valid bank object, we're good!
            BlockType blockType = blockEngine.BlockAtPoint(hitPoint);
            if (blockType == BlockType.BankRed && playerTeam == PlayerTeam.Red)
                return true;
            if (blockType == BlockType.BankBlue && playerTeam == PlayerTeam.Blue)
                return true;
            return false;
        }

        public float GetToolCooldown(PlayerTools tool)
        {
            return GameRules.GetToolCooldown(tool, playerClass);
        }

        public void SendPlayerUpdate()
        {
            if (!IsConnected)
                return;

            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerUpdate);
            msgBuffer.Put(playerPosition);
            msgBuffer.Put(playerCamera.GetLookVector());
            msgBuffer.Put((byte)playerTools[playerToolSelected]);
            msgBuffer.Put(playerToolCooldown > 0.001f);
            msgBuffer.Put(positionEpoch);
            Send(msgBuffer, NetChannel.UnreliableInOrder1);
        }
    }
}
