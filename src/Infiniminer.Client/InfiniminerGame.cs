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
        Type deferredState = null;
        ServerMessageHandler messageHandler;
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
                propertyBag.sounds.Volume = Settings.Volume;
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
        // Team colours. Colour-blind mode uses vermilion and sky blue (from the Okabe-Ito palette), which stay
        // distinct for all common kinds of colour blindness, and adds [R]/[B] tags to team chat.
        public static bool ColorBlindMode = false;

        public static Color IM_BLUE
        {
            get { return ColorBlindMode ? new Color(86, 180, 233) : new Color(80, 150, 255); }
        }

        public static Color IM_RED
        {
            get { return ColorBlindMode ? new Color(213, 94, 0) : new Color(222, 24, 24); }
        }

        public InfiniminerGame(string[] args)
        {
        }

        public static string Sanitize(string input)
        {
            return TextUtil.Sanitize(input);
        }

        // The server started with HOST GAME, if any. It keeps running while we are in the menus (so the
        // game survives a restart after a match) until we end it or quit.
        public HostedServer HostedServer;

        public bool IsHosting
        {
            get { return HostedServer != null && HostedServer.Running; }
        }

        public void StopHostedServer()
        {
            HostedServer?.Stop();
            HostedServer = null;
        }

        // Leaves the current server; if it is the one we host, that ends the game for everyone.
        public void LeaveServer()
        {
            Rejoin.Cancel();
            propertyBag.serverPeer?.Disconnect();
            if (IsHosting && HostedServer.Ready && HostedServer.EndPoint.Equals(CurrentServer))
                StopHostedServer();
        }

        // The server we joined last.
        public IPEndPoint CurrentServer { get; private set; }
        (string address, string name)? joiningRecent;

        // Rejoins the server when it restarts after a match.
        public readonly AutoRejoin Rejoin = new AutoRejoin();

        double Now
        {
            get { return currentGameTime.TotalGameTime.TotalSeconds; }
        }

        // The map has arrived: we're in.
        public void OnJoined()
        {
            Rejoin.Cancel();
            bool ownGame = IsHosting && CurrentServer != null && CurrentServer.Port == HostedServer.Port;
            if (joiningRecent != null && !ownGame)
            {
                Settings.AddRecentServer(joiningRecent.Value.address, joiningRecent.Value.name);
                SaveSettings();
            }
            joiningRecent = null;
        }

        // `address` and `name` are remembered in the recent servers list once we're in.
        public void JoinGame(IPEndPoint serverEndPoint, string address = null, string name = null)
        {
            joiningRecent = address != null ? (address, name ?? address) : null;
            // Clear out the map load progress indicator.
            propertyBag.mapLoadProgress = new bool[64, 64];
            propertyBag.downloadComplete = false;
            StatusMessage = "";

            // Create our connect message.
            NetDataWriter connectBuffer = ClientMessages.ConnectRequest(propertyBag.playerHandle);

            // Connect to the server.
            CurrentServer = serverEndPoint;
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

        public static async Task<List<ServerInformation>> FetchPublicServerList(string url)
        {
            return ParsePublicServerList(await HttpRequest.GetAsync(ServerListUrl.Endpoint(url, "/plain")));
        }

        // One server per line: name;ip;INFINIMINER;players;capacity;extra[;port]
        public static List<ServerInformation> ParsePublicServerList(string publicList)
        {
            List<ServerInformation> serverList = new List<ServerInformation>();
            foreach (string s in publicList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] args = s.Split(';');
                if (args.Length != 6 && args.Length != 7)
                    continue;
                int port = GameConstants.DefaultPort;
                if (args.Length == 7 && !int.TryParse(args[6], out port))
                    continue;
                if (IPAddress.TryParse(args[1], out IPAddress serverIp) && args[2] == "INFINIMINER")
                    serverList.Add(new ServerInformation(new IPEndPoint(serverIp, port), args[0], args[5], args[3], args[4]));
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
                if (CurrentStateType == typeof(States.MainGameState))
                    propertyBag.SendPlayerUpdate();
            }

            // Recieve messages from the server. State changes are applied afterwards, because entering
            // the server browser shuts down the network connection we're reading from.
            propertyBag.netClient.PollEvents();

            if (deferredState != null)
            {
                Type newState = deferredState;
                deferredState = null;
                ChangeState(newState);
            }
        }

        // Changes state once the network events being handled are done (see UpdateNetwork).
        public void ChangeStateAfterNetworkUpdate(Type newState)
        {
            deferredState = newState;
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
            {
                // While rejoining a restarting server, failing to connect just means it isn't back yet.
                if (Rejoin.Active)
                    StatusMessage = Rejoin.AttemptFailed(Now) ? "REJOINING THE SERVER..." : "ERROR: COULD NOT REJOIN THE SERVER.";
                else
                    StatusMessage = "ERROR: COULD NOT CONNECT TO THE SERVER.";
            }
            else if (disconnectInfo.Reason != DisconnectReason.DisconnectPeerCalled)
            {
                StatusMessage = reason != "" ? "DISCONNECTED: " + Sanitize(reason).ToUpperInvariant() : "DISCONNECTED FROM THE SERVER.";
                if (reason == AutoRejoin.RestartReason && CurrentServer != null)
                {
                    Rejoin.Begin(CurrentServer, Now);
                    StatusMessage = "THE SERVER IS STARTING A NEW MAP. REJOINING...";
                }
            }
            if (disconnectInfo.Reason == DisconnectReason.ConnectionRejected)
                Rejoin.Cancel();

            deferredState = typeof(States.ServerBrowserState);
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
                messageHandler.Handle(msgBuffer, currentGameTime);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Ignoring malformed packet: " + e.Message);
            }
        }

        protected override void Initialize()
        {
            graphicsDeviceManager.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;

            Settings = ClientSettings.Load(ConfigPath);
            NoSound = Settings.NoSound;
            ColorBlindMode = Settings.ColorBlind;
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
            StopHostedServer();

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
            messageHandler ??= new ServerMessageHandler(this);
            propertyBag.playerHandle = Settings.Handle;
            propertyBag.sounds.Volume = Settings.Volume;
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
            ChangeState(typeof(States.TitleState));

            // Play the title music.
            if (!NoSound)
                TitleMusic.Start(Assets.MusicPath("song_title"), propertyBag.sounds.Volume);
        }
    }
}
