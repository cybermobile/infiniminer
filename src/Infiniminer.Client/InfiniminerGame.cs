using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Infiniminer
{
    public class InfiniminerGame : StateMasher.StateMachine
    {
        double timeSinceLastUpdate = 0;
        GameTime currentGameTime = new GameTime();
        string deferredState = null;
        Task<List<ServerInformation>> publicServerListTask = null;

        public ClientSettings Settings { get; private set; } = new ClientSettings();
        public bool NoSound = false;    // set from the config, or when no audio device is available
        bool resizing = false;

        public bool DrawFrameRate
        {
            get { return Settings.ShowFps; }
        }

        public bool InvertMouseYAxis
        {
            get { return Settings.InvertY; }
        }

        // The original menus were drawn for a 1024x768 window; they are scaled to fit whatever the window is.
        public const int MenuWidth = 1024, MenuHeight = 768;

        public float MenuScale
        {
            get { return Math.Min(GraphicsDevice.Viewport.Width / (float)MenuWidth, GraphicsDevice.Viewport.Height / (float)MenuHeight); }
        }

        public Matrix MenuTransform
        {
            get
            {
                float scale = MenuScale;
                Vector2 offset = new Vector2((GraphicsDevice.Viewport.Width - MenuWidth * scale) / 2, (GraphicsDevice.Viewport.Height - MenuHeight * scale) / 2);
                return Matrix.CreateScale(scale) * Matrix.CreateTranslation((int)offset.X, (int)offset.Y, 0);
            }
        }

        // Converts a mouse position on screen into menu coordinates.
        public Point ToMenu(int x, int y)
        {
            Vector2 menu = Vector2.Transform(new Vector2(x, y), Matrix.Invert(MenuTransform));
            return new Point((int)menu.X, (int)menu.Y);
        }

        // Menus are point-sampled at whole-number scales (crisp pixels), smoothed otherwise.
        public SamplerState MenuSampler
        {
            get { return Math.Abs(MenuScale - MathF.Round(MenuScale)) < 0.01f ? SamplerState.PointClamp : SamplerState.LinearClamp; }
        }

        // The in-game interface was also laid out for 1024x768; on big screens it is drawn at 2x or 3x.
        public int HudScale
        {
            get
            {
                if (Settings.UiScale > 0)
                    return Settings.UiScale;
                int automatic = (int)Math.Floor(Math.Min(GraphicsDevice.Viewport.Width / (float)MenuWidth, GraphicsDevice.Viewport.Height / (float)MenuHeight));
                return Math.Clamp(automatic, 1, ClientSettings.MaxUiScale);
            }
        }

        public string ConfigPath
        {
            get { return Path.Combine(DataDirectory, "client.config.txt"); }
        }

        public void SaveSettings()
        {
            try
            {
                Settings.Save(ConfigPath);
            }
            catch (Exception e)
            {
                StatusMessage = "ERROR: COULD NOT SAVE SETTINGS: " + Sanitize(e.Message).ToUpperInvariant();
            }
        }

        // Applies the resolution and fullscreen settings to the window.
        public void ApplyDisplaySettings()
        {
            resizing = true;
            graphicsDeviceManager.IsFullScreen = Settings.Fullscreen;
            graphicsDeviceManager.PreferredBackBufferWidth = Settings.Width;
            graphicsDeviceManager.PreferredBackBufferHeight = Settings.Height;
            graphicsDeviceManager.ApplyChanges();
            resizing = false;
            OnDisplayChanged();
        }

        // Turns the bloom post-process on or off.
        public void SetPretty(bool pretty)
        {
            Settings.Pretty = pretty;
            Bloom?.Dispose();
            Bloom = null;
            if (pretty)
            {
                Bloom = new BloomComponent();
                Bloom.Load(GraphicsDevice, SpriteBatch);
            }
        }

        public void SetVolume(float volume)
        {
            Settings.Volume = Math.Clamp(volume, 0, 1);
            if (propertyBag != null)
                propertyBag.volumeLevel = Settings.Volume;
            TitleMusic.SetVolume(Settings.Volume);
        }

        void OnDisplayChanged()
        {
            // Render targets are sized to the window, so rebuild them.
            if (Bloom != null)
                SetPretty(true);
        }

        void OnClientSizeChanged(object sender, EventArgs e)
        {
            if (resizing || graphicsDeviceManager.IsFullScreen)
                return;
            Rectangle bounds = Window.ClientBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            if (bounds.Width == GraphicsDevice.PresentationParameters.BackBufferWidth && bounds.Height == GraphicsDevice.PresentationParameters.BackBufferHeight)
                return;
            Settings.Width = bounds.Width;
            Settings.Height = bounds.Height;
            ApplyDisplaySettings();
        }

        public Assets Assets { get; private set; }
        public AlphaTestEffect SpriteEffect { get; private set; }
        public BloomComponent Bloom { get; private set; }
        public TitleMusic TitleMusic { get; } = new TitleMusic();
        public readonly string DataDirectory = DatafileLoader.ResolveDataDirectory("client.config.txt");

        // Shown in the server browser; replaces the message boxes the Windows-only original popped up.
        public string StatusMessage = "";

        public const string INFINIMINER_VERSION = GameConstants.Version;
        public const int GROUND_LEVEL = GameConstants.GroundLevel;
        public static Color IM_BLUE = new Color(80, 150, 255);
        public static Color IM_RED = new Color(222, 24, 24);

        public InfiniminerGame(string[] args)
        {
        }

        public static string Sanitize(string input)
        {
            return TextUtil.Sanitize(input);
        }

        public void JoinGame(IPEndPoint serverEndPoint)
        {
            // Clear out the map load progress indicator.
            propertyBag.mapLoadProgress = new bool[64, 64];
            propertyBag.downloadComplete = false;
            StatusMessage = "";

            // Create our connect message.
            NetDataWriter connectBuffer = new NetDataWriter();
            connectBuffer.Put(GameConstants.AppIdentifier);
            connectBuffer.Put(propertyBag.playerHandle);
            connectBuffer.Put(INFINIMINER_VERSION);

            // Connect to the server.
            propertyBag.serverPeer = propertyBag.netClient.Connect(serverEndPoint, connectBuffer);
        }

        // Starts looking for servers: a LAN broadcast (replies arrive through UpdateNetwork) and,
        // if a list URL is configured, a download of the public server list.
        public void StartServerDiscovery()
        {
            propertyBag.discoveredServers.Clear();

            NetDataWriter request = new NetDataWriter();
            request.Put(GameConstants.DiscoveryRequest);
            propertyBag.netClient.SendBroadcast(request, GameConstants.DefaultPort);

            string serverListUrl = Settings.ServerListUrl;
            publicServerListTask = serverListUrl == "" ? null : Task.Run(() => FetchPublicServerList(serverListUrl));
        }

        static async Task<List<ServerInformation>> FetchPublicServerList(string url)
        {
            List<ServerInformation> serverList = new List<ServerInformation>();
            string publicList = await HttpRequest.GetAsync(url);
            foreach (string s in publicList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] args = s.Split(';');
                if (args.Length == 6)
                {
                    if (IPAddress.TryParse(args[1], out IPAddress serverIp) && args[2] == "INFINIMINER")
                        serverList.Add(new ServerInformation(serverIp, args[0], args[5], args[3], args[4]));
                }
            }
            return serverList;
        }

        // LAN servers that answered, followed by any public servers.
        public List<ServerInformation> GetServerList()
        {
            List<ServerInformation> serverList = new List<ServerInformation>(propertyBag.discoveredServers);
            if (publicServerListTask != null && publicServerListTask.IsCompletedSuccessfully)
                foreach (ServerInformation server in publicServerListTask.Result)
                    if (!serverList.Contains(server))
                        serverList.Add(server);
            return serverList;
        }

        public void UpdateNetwork(GameTime gameTime)
        {
            currentGameTime = gameTime;

            // Update the server with our status.
            timeSinceLastUpdate += gameTime.ElapsedGameTime.TotalSeconds;
            if (timeSinceLastUpdate > 0.05)
            {
                timeSinceLastUpdate = 0;
                if (CurrentStateType == "Infiniminer.States.MainGameState")
                    propertyBag.SendPlayerUpdate();
            }

            // Recieve messages from the server. State changes are applied afterwards, because entering
            // the server browser shuts down the network connection we're reading from.
            propertyBag.netClient.PollEvents();

            if (deferredState != null)
            {
                string newState = deferredState;
                deferredState = null;
                ChangeState(newState);
            }
        }

        void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            string reason = "";
            if (disconnectInfo.AdditionalData != null && disconnectInfo.AdditionalData.AvailableBytes > 0)
                disconnectInfo.AdditionalData.TryGetString(out reason);
            reason ??= "";

            if (disconnectInfo.Reason == DisconnectReason.ConnectionRejected)
            {
                string[] rejection = reason.Split(';');
                if (rejection[0] == "VER")
                    StatusMessage = "ERROR: CLIENT/SERVER VERSION INCOMPATIBILITY! SERVER: " + (rejection.Length > 1 ? rejection[1] : "?") + " CLIENT: " + INFINIMINER_VERSION;
                else if (rejection[0] == "BAN")
                    StatusMessage = "ERROR: YOU ARE BANNED FROM THIS SERVER!";
                else if (rejection[0] == "FULL")
                    StatusMessage = "ERROR: THE SERVER IS FULL!";
                else
                    StatusMessage = "ERROR: THE SERVER REJECTED THE CONNECTION.";
            }
            else if (disconnectInfo.Reason == DisconnectReason.ConnectionFailed || disconnectInfo.Reason == DisconnectReason.HostUnreachable || disconnectInfo.Reason == DisconnectReason.UnknownHost)
                StatusMessage = "ERROR: COULD NOT CONNECT TO THE SERVER.";
            else if (disconnectInfo.Reason != DisconnectReason.DisconnectPeerCalled)
                StatusMessage = reason != "" ? "DISCONNECTED: " + Sanitize(reason).ToUpperInvariant() : "DISCONNECTED FROM THE SERVER.";

            deferredState = "Infiniminer.States.ServerBrowserState";
        }

        void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            DiscoveryInfo info = DiscoveryInfo.TryRead(reader);
            if (info == null)
                return;
            ServerInformation serverInfo = new ServerInformation(remoteEndPoint, info);
            if (!propertyBag.discoveredServers.Contains(serverInfo))
                propertyBag.discoveredServers.Add(serverInfo);
        }

        void OnNetworkReceive(NetPeer peer, NetPacketReader msgBuffer, byte channel, DeliveryMethod deliveryMethod)
        {
            try
            {
                HandleData(msgBuffer);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Ignoring malformed packet: " + e.Message);
            }
        }

        void HandleData(NetDataReader msgBuffer)
        {
            GameTime gameTime = currentGameTime;
            InfiniminerMessage dataType = (InfiniminerMessage)msgBuffer.GetByte();
            switch (dataType)
            {
                case InfiniminerMessage.BlockBulkTransfer:
                    {
                        byte x = msgBuffer.GetByte();
                        byte y = msgBuffer.GetByte();
                        propertyBag.mapLoadProgress[x, y] = true;
                        for (byte dy = 0; dy < 16; dy++)
                            for (byte z = 0; z < 64; z++)
                            {
                                BlockType blockType = (BlockType)msgBuffer.GetByte();
                                if (blockType != BlockType.None)
                                    propertyBag.blockEngine.downloadList[x, y + dy, z] = blockType;
                            }
                        bool downloadComplete = true;
                        for (x = 0; x < 64; x++)
                            for (y = 0; y < 64; y += 16)
                                if (propertyBag.mapLoadProgress[x, y] == false)
                                {
                                    downloadComplete = false;
                                    break;
                                }
                        if (downloadComplete && !propertyBag.downloadComplete)
                        {
                            propertyBag.downloadComplete = true;
                            deferredState = "Infiniminer.States.TeamSelectionState";
                            TitleMusic.Stop();
                            propertyBag.blockEngine.DownloadComplete();
                        }
                    }
                    break;

                case InfiniminerMessage.SetBeacon:
                    {
                        Vector3 position = msgBuffer.GetXnaVector3();
                        string text = msgBuffer.GetString();
                        PlayerTeam team = (PlayerTeam)msgBuffer.GetByte();

                        if (text == "")
                        {
                            if (propertyBag.beaconList.ContainsKey(position))
                                propertyBag.beaconList.Remove(position);
                        }
                        else
                        {
                            Beacon newBeacon = new Beacon();
                            newBeacon.ID = text;
                            newBeacon.Team = team;
                            propertyBag.beaconList[position] = newBeacon;
                        }
                    }
                    break;

                case InfiniminerMessage.TriggerConstructionGunAnimation:
                    {
                        propertyBag.constructionGunAnimation = msgBuffer.GetFloat();
                        if (propertyBag.constructionGunAnimation <= -0.1)
                            propertyBag.PlaySound(InfiniminerSound.RadarSwitch);
                    }
                    break;

                case InfiniminerMessage.ResourceUpdate:
                    {
                        // ore, cash, weight, max ore, max weight, team ore, red cash, blue cash, all uint
                        propertyBag.playerOre = msgBuffer.GetUInt();
                        propertyBag.playerCash = msgBuffer.GetUInt();
                        propertyBag.playerWeight = msgBuffer.GetUInt();
                        propertyBag.playerOreMax = msgBuffer.GetUInt();
                        propertyBag.playerWeightMax = msgBuffer.GetUInt();
                        propertyBag.teamOre = msgBuffer.GetUInt();
                        propertyBag.teamRedCash = msgBuffer.GetUInt();
                        propertyBag.teamBlueCash = msgBuffer.GetUInt();
                    }
                    break;

                case InfiniminerMessage.BlockSet:
                    {
                        // x, y, z, type, all bytes
                        byte x = msgBuffer.GetByte();
                        byte y = msgBuffer.GetByte();
                        byte z = msgBuffer.GetByte();
                        BlockType blockType = (BlockType)msgBuffer.GetByte();
                        if (blockType >= BlockType.MAXIMUM)
                            break;
                        if (blockType == BlockType.None)
                        {
                            if (propertyBag.blockEngine.BlockAtPoint(new Vector3(x, y, z)) != BlockType.None)
                                propertyBag.blockEngine.RemoveBlock(x, y, z);
                        }
                        else
                        {
                            if (propertyBag.blockEngine.BlockAtPoint(new Vector3(x, y, z)) != BlockType.None)
                                propertyBag.blockEngine.RemoveBlock(x, y, z);
                            propertyBag.blockEngine.AddBlock(x, y, z, blockType);
                            CheckForStandingInLava();
                        }
                    }
                    break;

                case InfiniminerMessage.TriggerExplosion:
                    {
                        Vector3 blockPos = msgBuffer.GetXnaVector3();

                        // Play the explosion sound.
                        propertyBag.PlaySound(InfiniminerSound.Explosion, blockPos);

                        // Create some particles.
                        propertyBag.particleEngine.CreateExplosionDebris(blockPos);

                        // Figure out what the effect is.
                        float distFromExplosive = (blockPos + 0.5f * Vector3.One - propertyBag.playerPosition).Length();
                        if (distFromExplosive < 3)
                            propertyBag.KillPlayer("WAS KILLED IN AN EXPLOSION!");
                        else if (distFromExplosive < 8)
                        {
                            // If we're not in explosion mode, turn it on with the minimum ammount of shakiness.
                            if (propertyBag.screenEffect != ScreenEffect.Explosion)
                            {
                                propertyBag.screenEffect = ScreenEffect.Explosion;
                                propertyBag.screenEffectCounter = 2;
                            }
                            // If this bomb would result in a bigger shake, use its value.
                            propertyBag.screenEffectCounter = Math.Min(propertyBag.screenEffectCounter, (distFromExplosive - 2) / 5);
                        }
                    }
                    break;

                case InfiniminerMessage.PlayerSetTeam:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (propertyBag.playerList.ContainsKey(playerId))
                        {
                            Player player = propertyBag.playerList[playerId];
                            player.Team = (PlayerTeam)msgBuffer.GetByte();
                        }
                    }
                    break;

                case InfiniminerMessage.PlayerJoined:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        string playerName = msgBuffer.GetString();
                        bool thisIsMe = msgBuffer.GetBool();
                        bool playerAlive = msgBuffer.GetBool();
                        propertyBag.playerList[playerId] = new Player(this);
                        propertyBag.playerList[playerId].Handle = Sanitize(playerName);
                        propertyBag.playerList[playerId].ID = playerId;
                        propertyBag.playerList[playerId].Alive = playerAlive;
                        if (thisIsMe)
                            propertyBag.playerMyId = playerId;
                    }
                    break;

                case InfiniminerMessage.PlayerLeft:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (propertyBag.playerList.ContainsKey(playerId))
                            propertyBag.playerList.Remove(playerId);
                    }
                    break;

                case InfiniminerMessage.PlayerDead:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (propertyBag.playerList.ContainsKey(playerId))
                        {
                            Player player = propertyBag.playerList[playerId];
                            player.Alive = false;
                            if (playerId == propertyBag.playerMyId)
                                propertyBag.EnterDeathState();
                            propertyBag.particleEngine.CreateBloodSplatter(player.Position, player.Team == PlayerTeam.Red ? Color.Red : Color.Blue);
                            if (playerId != propertyBag.playerMyId)
                                propertyBag.PlaySound(InfiniminerSound.Death, player.Position);
                        }
                    }
                    break;

                case InfiniminerMessage.PlayerAlive:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (propertyBag.playerList.ContainsKey(playerId))
                        {
                            Player player = propertyBag.playerList[playerId];
                            player.Alive = true;
                            if (playerId == propertyBag.playerMyId)
                                propertyBag.OnRespawned();
                        }
                    }
                    break;

                case InfiniminerMessage.PlayerUpdate:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (propertyBag.playerList.ContainsKey(playerId))
                        {
                            Player player = propertyBag.playerList[playerId];
                            player.UpdatePosition(msgBuffer.GetXnaVector3(), gameTime.TotalGameTime.TotalSeconds);
                            player.Heading = msgBuffer.GetXnaVector3();
                            player.Tool = (PlayerTools)msgBuffer.GetByte();
                            player.UsingTool = msgBuffer.GetBool();
                            player.Score = (uint)(msgBuffer.GetUShort() * 100);
                        }
                    }
                    break;

                case InfiniminerMessage.GameOver:
                    {
                        propertyBag.teamWinners = (PlayerTeam)msgBuffer.GetByte();
                    }
                    break;

                case InfiniminerMessage.ChatMessage:
                    {
                        ChatMessageType chatType = (ChatMessageType)msgBuffer.GetByte();
                        string chatString = Sanitize(msgBuffer.GetString());
                        ChatMessage chatMsg = new ChatMessage(chatString, chatType, 10);
                        propertyBag.chatBuffer.Insert(0, chatMsg);
                        propertyBag.PlaySound(InfiniminerSound.ClickLow);
                    }
                    break;

                case InfiniminerMessage.PlayerPing:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (propertyBag.playerList.ContainsKey(playerId))
                        {
                            if (propertyBag.playerList[playerId].Team == propertyBag.playerTeam)
                            {
                                propertyBag.playerList[playerId].Ping = 1;
                                propertyBag.PlaySound(InfiniminerSound.Ping);
                            }
                        }
                    }
                    break;

                case InfiniminerMessage.PlayerSetPosition:
                    {
                        Vector3 position = msgBuffer.GetXnaVector3();
                        byte epoch = msgBuffer.GetByte();
                        PositionReason reason = (PositionReason)msgBuffer.GetByte();
                        propertyBag.SetPositionFromServer(position, epoch, reason);
                    }
                    break;

                case InfiniminerMessage.PlaySound:
                    {
                        InfiniminerSound sound = (InfiniminerSound)msgBuffer.GetByte();
                        bool hasPosition = msgBuffer.GetBool();
                        if (hasPosition)
                        {
                            Vector3 soundPosition = msgBuffer.GetXnaVector3();
                            propertyBag.PlaySound(sound, soundPosition);
                        }
                        else
                            propertyBag.PlaySound(sound);
                    }
                    break;
            }
        }

        private void CheckForStandingInLava()
        {
            // Copied from TryToMoveTo; responsible for checking if lava has flowed over us.

            Vector3 movePosition = propertyBag.playerPosition;
            Vector3 midBodyPoint = movePosition + new Vector3(0, -0.7f, 0);
            Vector3 lowerBodyPoint = movePosition + new Vector3(0, -1.4f, 0);
            BlockType lowerBlock = propertyBag.blockEngine.BlockAtPoint(lowerBodyPoint);
            BlockType midBlock = propertyBag.blockEngine.BlockAtPoint(midBodyPoint);
            BlockType upperBlock = propertyBag.blockEngine.BlockAtPoint(movePosition);
            if (upperBlock == BlockType.Lava || lowerBlock == BlockType.Lava || midBlock == BlockType.Lava)
            {
                propertyBag.KillPlayer("WAS INCINERATED BY LAVA!");
            }
        }

        protected override void Initialize()
        {
            graphicsDeviceManager.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;

            Settings = ClientSettings.Load(ConfigPath);
            NoSound = Settings.NoSound;
            graphicsDeviceManager.IsFullScreen = Settings.Fullscreen;
            graphicsDeviceManager.PreferredBackBufferWidth = Settings.Width;
            graphicsDeviceManager.PreferredBackBufferHeight = Settings.Height;
            Window.AllowUserResizing = true;
            Window.ClientSizeChanged += OnClientSizeChanged;

            graphicsDeviceManager.ApplyChanges();
            Window.Title = "Infiniminer";
            base.Initialize();
        }

        protected override void Update(GameTime gameTime)
        {
            if (!NoSound)
                TitleMusic.Update();
            base.Update(gameTime);
        }

        protected override void OnExiting(object sender, ExitingEventArgs args)
        {
            propertyBag?.netClient.Stop();

            base.OnExiting(sender, args);
        }

        public void ResetPropertyBag()
        {
            if (propertyBag != null)
            {
                propertyBag.netClient.Stop();
                propertyBag.blockEngine.Dispose();
            }

            propertyBag = new Infiniminer.PropertyBag(this);
            propertyBag.playerHandle = Settings.Handle;
            propertyBag.volumeLevel = Settings.Volume;
            propertyBag.netListener.PeerDisconnectedEvent += OnPeerDisconnected;
            propertyBag.netListener.NetworkReceiveEvent += OnNetworkReceive;
            propertyBag.netListener.NetworkReceiveUnconnectedEvent += OnNetworkReceiveUnconnected;
            publicServerListTask = null;
        }

        protected override void LoadContent()
        {
            base.LoadContent();
            Assets = new Assets(GraphicsDevice);
            SpriteEffect = new AlphaTestEffect(GraphicsDevice);
            SetPretty(Settings.Pretty);

            // Initialize the property bag.
            ResetPropertyBag();

            // Set the initial state to team selection
            ChangeState("Infiniminer.States.TitleState");

            // Play the title music.
            if (!NoSound)
                TitleMusic.Start(Assets.MusicPath("song_title"), propertyBag.volumeLevel);
        }
    }
}
