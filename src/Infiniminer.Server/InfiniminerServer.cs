using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Numerics;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Infiniminer
{
    public class InfiniminerServer : IBotWorld
    {
        NetManager netServer = null;
        EventBasedNetListener listener = null;
        BlockType[, ,] blockList = null;    // In game coordinates, where Y points up.
        PlayerTeam[, ,] blockCreatorTeam = null;
        readonly int MAPSIZE;     // the world is a cube this many blocks on a side
        readonly Dictionary<int, ServerPlayer> playerList = new Dictionary<int, ServerPlayer>();
        int lavaBlockCount = 0;
        readonly ServerConfig config;
        readonly string dataDirectory;
        DateTime lastServerListUpdate = DateTime.Now;
        List<string> banList = null;

        readonly ConcurrentQueue<string> pendingCommands = new ConcurrentQueue<string>();
        bool keepRunning = true;

        uint teamCashRed = 0;
        uint teamCashBlue = 0;
        uint teamOreRed = 0;
        uint teamOreBlue = 0;

        PlayerTeam winningTeam = PlayerTeam.None;
        double matchEndTime = double.PositiveInfinity;   // when the time limit runs out
        bool overtime = false;                           // time ran out with the teams tied

        // The cash a team needs to win, which can grow with the number of players.
        public uint WinningCash
        {
            get { return config.WinningCashAmount + config.WinningCashPerPlayer * (uint)playerList.Count; }
        }

        int SecondsLeft
        {
            get
            {
                if (double.IsPositiveInfinity(matchEndTime))
                    return -1;
                return overtime ? 0 : (int)Math.Max(1, Math.Ceiling(matchEndTime - Now));
            }
        }

        // Server restarting variables.
        DateTime restartTime = DateTime.Now;
        bool restartTriggered = false;

        // Store the last time that we did a flow calculation.
        DateTime lastFlowCalc = DateTime.Now;
        DateTime lastBotUpdate = DateTime.Now;
        readonly List<ServerBot> bots = new List<ServerBot>();
        bool balancingBots = false;
        static readonly string[] BotNames = { "DIGBY", "NUGGET", "PEBBLES", "FLINT", "DUSTY", "GARNET", "COAL", "ROCKY",
                                              "QUARTZ", "SHALE", "COBALT", "JASPER", "TOPAZ", "GRAVEL", "OPAL", "SLATE" };

        uint duplicateNameCount = 0;

        // Identifies this server instance in LAN discovery replies, so a server reachable over
        // several interfaces (IPv4 and IPv6) is listed once.
        readonly string instanceId = Guid.NewGuid().ToString("N");

        public Action<string> Log = line => Console.WriteLine(line);

        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly MovementValidator movementValidator;

        double Now
        {
            get { return clock.Elapsed.TotalSeconds; }
        }

        public InfiniminerServer(ServerConfig config, string dataDirectory)
        {
            this.config = config;
            this.dataDirectory = dataDirectory;
            MAPSIZE = config.MapSize;
            movementValidator = new MovementValidator((x, y, z) => blockList[x, y, z], MAPSIZE);
        }

        public int Port
        {
            get { return netServer.LocalPort; }
        }

        public int PlayerCount
        {
            get { return playerList.Count; }
        }

        public BlockType GetBlock(int x, int y, int z)
        {
            return blockList[x, y, z];
        }

        public string GetExtraInfo()
        {
            string extraInfo = "";
            if (config.SandboxMode)
                extraInfo += "sandbox";
            else
                extraInfo += string.Format(CultureInfo.InvariantCulture, "{0:#.##k}", config.WinningCashAmount / 1000);
            if (!config.IncludeLava)
                extraInfo += ", !lava";
            if (bots.Count > 0)
                extraInfo += ", " + bots.Count + (bots.Count == 1 ? " bot" : " bots");
            return extraInfo;
        }

        public void PublicServerListUpdate()
        {
            if (!config.PublicServer)
                return;
            lastServerListUpdate = DateTime.Now;

            if (config.ServerListUrl == "")
            {
                ConsoleWrite("PUBLICLIST: NO 'serverlist' URL CONFIGURED, SKIPPING");
                return;
            }

            Dictionary<string, string> postDict = new Dictionary<string, string>();
            postDict["name"] = config.ServerName;
            postDict["game"] = "INFINIMINER";
            postDict["player_count"] = "" + playerList.Count;
            postDict["player_capacity"] = "" + config.MaxPlayers;
            postDict["extra"] = GetExtraInfo();
            postDict["port"] = "" + netServer.LocalPort;

            // Posted in the background so a slow or dead list server never stalls the game loop.
            string url = ServerListUrl.Endpoint(config.ServerListUrl, "/post");
            Task.Run(async () =>
            {
                try
                {
                    await HttpRequest.PostAsync(url, postDict);
                    ConsoleWrite("PUBLICLIST: UPDATING SERVER LISTING");
                }
                catch (Exception)
                {
                    ConsoleWrite("PUBLICLIST: ERROR CONTACTING SERVER");
                }
            });
        }

        // While running a command an admin typed in chat, its output is sent back to them as well.
        ServerPlayer commandReplyTo = null;

        public void ConsoleWrite(string text)
        {
            Log(text);
            if (commandReplyTo != null)
            {
                NetDataWriter reply = NetExtensions.CreateMessage(InfiniminerMessage.ChatMessage);
                reply.Put((byte)ChatMessageType.SayAll);
                reply.Put(TextUtil.Sanitize(text));
                commandReplyTo.Peer.Send(reply, NetChannel.ReliableInOrder3);
            }
        }

        // Chat messages starting with "/" are commands: /login <password>, then any console command.
        void HandleChatCommand(ServerPlayer player, string command)
        {
            string[] args = command.Substring(1).Split(' ', 2);
            if (args[0].ToLowerInvariant() == "login")
            {
                commandReplyTo = player;
                try
                {
                    if (config.AdminPassword == "")
                        ConsoleWrite("ADMIN: this server has no admin password.");
                    else if (!player.LoginLimiter.TryTake(Now))
                        ConsoleWrite("ADMIN: too many attempts, wait a little.");
                    else if (args.Length == 2 && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.UTF8.GetBytes(args[1]), System.Text.Encoding.UTF8.GetBytes(config.AdminPassword)))
                    {
                        player.IsAdmin = true;
                        ConsoleWrite("ADMIN: " + player.Handle + " logged in. Type /help for commands.");
                    }
                    else
                        ConsoleWrite("ADMIN: wrong password.");
                }
                finally
                {
                    commandReplyTo = null;
                }
                return;
            }

            if (!player.IsAdmin)
            {
                commandReplyTo = player;
                ConsoleWrite("ADMIN: log in first with /login <password>.");
                commandReplyTo = null;
                return;
            }

            ConsoleWrite("ADMIN: " + player.Handle + " ran: " + command.Substring(1));
            commandReplyTo = player;
            try
            {
                ConsoleProcessInput(command.Substring(1));
            }
            finally
            {
                commandReplyTo = null;
            }
        }

        // Map files are plain names in the server's folder, so commands can't touch other files.
        static bool IsSafeFileName(string filename)
        {
            return filename != "" && Path.GetFileName(filename) == filename && !filename.StartsWith('.')
                && filename.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        // Players matching an IP address or a name (names are matched ignoring case).
        List<ServerPlayer> FindPlayers(string target)
        {
            return playerList.Values.Where(p => p.IP == target || string.Equals(p.Handle, target, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        string DataPath(string filename)
        {
            return Path.Combine(dataDirectory, filename);
        }

        public List<string> LoadBanList()
        {
            List<string> retList = new List<string>();
            try
            {
                foreach (string line in File.ReadAllLines(DataPath("banlist.txt")))
                    if (line.Trim() != "")
                        retList.Add(line.Trim());
            }
            catch (Exception)
            {
            }
            return retList;
        }

        public void SaveBanList(List<string> banList)
        {
            try
            {
                File.WriteAllLines(DataPath("banlist.txt"), banList);
            }
            catch (Exception)
            {
            }
        }

        public void KickPlayer(string target)
        {
            List<ServerPlayer> playersToKick = FindPlayers(target);
            if (playersToKick.Count == 0)
                ConsoleWrite("ERROR: no player with that name or IP");
            foreach (ServerPlayer p in playersToKick)
            {
                ServerBot bot = bots.FirstOrDefault(b => b.Player == p);
                if (bot != null)
                {
                    RemoveBot(bot);
                    continue;
                }
                p.Kicked = true;
                p.Peer.Disconnect(NetExtensions.StringPayload("You were kicked from the server."));
            }
        }

        public void BanPlayer(string ip)
        {
            if (!banList.Contains(ip))
            {
                banList.Add(ip);
                SaveBanList(banList);
            }
        }

        // Commands can be queued from any thread (e.g. the stdin reader); they run on the game loop.
        public void EnqueueCommand(string command)
        {
            pendingCommands.Enqueue(command);
        }

        public void ConsoleProcessInput(string consoleInput)
        {
            string[] args = consoleInput.Split(' ');

            ConsoleWrite("> " + consoleInput);

            switch (args[0].ToLower().Trim())
            {
                case "help":
                    {
                        ConsoleWrite("SERVER CONSOLE COMMANDS:");
                        ConsoleWrite(" players");
                        ConsoleWrite(" kick <ip or name>");
                        ConsoleWrite(" ban <ip or name>");
                        ConsoleWrite(" unban <ip>");
                        ConsoleWrite(" say <message>");
                        ConsoleWrite(" bots <count>");
                        ConsoleWrite(" tp <player name> <x> <y> <z>");
                        ConsoleWrite(" save <mapfile>");
                        ConsoleWrite(" load <mapfile>");
                        ConsoleWrite(" restart");
                        ConsoleWrite(" quit");
                    }
                    break;

                case "players":
                    {
                        foreach (ServerPlayer p in playerList.Values)
                        {
                            string teamIdent = "";
                            if (p.Team == PlayerTeam.Red)
                                teamIdent = " (R)";
                            else if (p.Team == PlayerTeam.Blue)
                                teamIdent = " (B)";
                            ConsoleWrite(p.Handle + teamIdent + " - " + p.IP);
                        }
                    }
                    break;

                case "kick":
                    {
                        if (args.Length >= 2)
                            KickPlayer(string.Join(" ", args[1..]));
                    }
                    break;

                case "ban":
                    {
                        // Ban by IP, or by the name of a connected player (which bans their IP).
                        if (args.Length >= 2)
                        {
                            string target = string.Join(" ", args[1..]);
                            List<string> ips = FindPlayers(target).Select(p => p.IP).Distinct().ToList();
                            if (ips.Count == 0 && System.Net.IPAddress.TryParse(target, out _))
                                ips.Add(target);
                            if (ips.Count == 0)
                                ConsoleWrite("ERROR: no player with that name or IP");
                            foreach (string ip in ips)
                            {
                                BanPlayer(ip);
                                ConsoleWrite("BANNED: " + ip);
                            }
                            KickPlayer(target);
                        }
                    }
                    break;

                case "unban":
                    {
                        if (args.Length == 2 && banList.Remove(args[1]))
                        {
                            SaveBanList(banList);
                            ConsoleWrite("UNBANNED: " + args[1]);
                        }
                        else
                            ConsoleWrite("ERROR: that IP is not banned");
                    }
                    break;

                case "quit":
                    {
                        keepRunning = false;
                    }
                    break;

                case "bots":
                    {
                        if (args.Length == 2 && int.TryParse(args[1], out int botCount) && botCount >= 0 && botCount <= 16)
                            SetBotCount(botCount);
                        else
                            ConsoleWrite("bots <count from 0 to 16>; there are " + bots.Count + " now");
                    }
                    break;

                case "restart":
                    {
                        restartTriggered = true;
                        restartTime = DateTime.Now;
                    }
                    break;

                case "say":
                    {
                        string message = "SERVER: ";
                        for (int i = 1; i < args.Length; i++)
                            message += args[i] + " ";
                        SendServerMessage(TextUtil.Sanitize(message));
                    }
                    break;

                case "tp":
                    {
                        // The last three arguments are the coordinates; the name may contain spaces.
                        if (args.Length >= 5
                            && float.TryParse(args[^3], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                            && float.TryParse(args[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                            && float.TryParse(args[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                        {
                            string handle = string.Join(" ", args[1..^3]);
                            ServerPlayer target = playerList.Values.FirstOrDefault(p => p.Handle == handle);
                            if (target == null || !target.Alive)
                                ConsoleWrite("ERROR: no living player called " + handle);
                            else
                            {
                                target.Position = new Vector3(x, y, z);
                                target.OnTeleporter = null;
                                SendSetPosition(target, target.Position, PositionReason.Teleport);
                            }
                        }
                        else
                            ConsoleWrite("USAGE: tp <player name> <x> <y> <z>");
                    }
                    break;

                case "save":
                    {
                        if (args.Length >= 2 && IsSafeFileName(args[1]))
                            SaveLevel(args[1]);
                        else
                            ConsoleWrite("ERROR: give a plain file name, e.g. save mymap.lvl");
                    }
                    break;

                case "load":
                    {
                        if (args.Length >= 2 && IsSafeFileName(args[1]))
                        {
                            if (LoadLevel(args[1]))
                                ResendWorld();
                        }
                        else
                            ConsoleWrite("ERROR: give a plain file name, e.g. load mymap.lvl");
                    }
                    break;
            }
        }

        public void SaveLevel(string filename)
        {
            try
            {
                using StreamWriter sw = new StreamWriter(DataPath(filename));
                for (int x = 0; x < MAPSIZE; x++)
                    for (int y = 0; y < MAPSIZE; y++)
                        for (int z = 0; z < MAPSIZE; z++)
                            sw.WriteLine((byte)blockList[x, y, z] + "," + (byte)blockCreatorTeam[x, y, z]);
            }
            catch (Exception e)
            {
                ConsoleWrite("ERROR: could not save level: " + e.Message);
            }
        }

        public bool LoadLevel(string filename)
        {
            BlockType[,,] newBlocks = new BlockType[MAPSIZE, MAPSIZE, MAPSIZE];
            PlayerTeam[,,] newTeams = new PlayerTeam[MAPSIZE, MAPSIZE, MAPSIZE];
            try
            {
                using StreamReader sr = new StreamReader(DataPath(filename));
                for (int x = 0; x < MAPSIZE; x++)
                    for (int y = 0; y < MAPSIZE; y++)
                        for (int z = 0; z < MAPSIZE; z++)
                        {
                            string line = sr.ReadLine();
                            if (line == null)
                                throw new InvalidDataException("the file is for a smaller map");
                            string[] fileArgs = line.Split(',');
                            if (fileArgs.Length == 2)
                            {
                                BlockType blockType = (BlockType)int.Parse(fileArgs[0], CultureInfo.InvariantCulture);
                                newBlocks[x, y, z] = blockType < BlockType.MAXIMUM ? blockType : BlockType.None;
                                newTeams[x, y, z] = (PlayerTeam)int.Parse(fileArgs[1], CultureInfo.InvariantCulture);
                            }
                        }
            }
            catch (FileNotFoundException)
            {
                ConsoleWrite("ERROR: File not found!");
                return false;
            }
            catch (Exception e)
            {
                ConsoleWrite("ERROR: could not load level: " + e.Message);
                return false;
            }
            blockList = newBlocks;
            blockCreatorTeam = newTeams;
            ConsoleWrite("LOADED " + filename);
            return true;
        }

        // After a map is loaded: rebuild what we track about special blocks, and give everyone the new world.
        void ResendWorld()
        {
            foreach (List<(int x, int y, int z)> network in teleporters.Values)
                network.Clear();
            beaconList.Clear();
            beaconIDList.Clear();
            homeBases.Clear();
            for (int x = 0; x < MAPSIZE; x++)
                for (int y = 0; y < MAPSIZE; y++)
                    for (int z = 0; z < MAPSIZE; z++)
                        switch (blockList[x, y, z])
                        {
                            case BlockType.Teleporter:
                                teleporters[blockCreatorTeam[x, y, z]].Add((x, y, z));
                                break;
                            case BlockType.BeaconRed:
                            case BlockType.BeaconBlue:
                                beaconList[new Vector3(x, y, z)] = new Beacon { ID = GenerateBeaconID(), Team = blockList[x, y, z] == BlockType.BeaconRed ? PlayerTeam.Red : PlayerTeam.Blue };
                                break;
                            case BlockType.HomeRed:
                            case BlockType.HomeBlue:
                                PlayerTeam team = blockList[x, y, z] == BlockType.HomeRed ? PlayerTeam.Red : PlayerTeam.Blue;
                                homeBases[team] = (x, y, z);
                                beaconList[new Vector3(x, y, z)] = new Beacon { ID = "HOME", Team = team };
                                break;
                        }

            foreach (ServerPlayer player in playerList.Values.ToList())
            {
                // Nobody should end up inside the new map's blocks: everyone respawns.
                KillPlayer(player, "");
                SendCurrentMap(player.Peer);
                foreach (KeyValuePair<Vector3, Beacon> bPair in beaconList)
                {
                    NetDataWriter beacon = NetExtensions.CreateMessage(InfiniminerMessage.SetBeacon);
                    beacon.Put(bPair.Key + Vector3.UnitY);
                    beacon.Put(bPair.Value.ID);
                    beacon.Put((byte)bPair.Value.Team);
                    player.Peer.Send(beacon, NetChannel.World);
                }
            }
            SendServerMessage("SERVER: A NEW MAP WAS LOADED.");
        }

        readonly Dictionary<PlayerTeam, List<(int x, int y, int z)>> teleporters = new Dictionary<PlayerTeam, List<(int x, int y, int z)>>
        {
            [PlayerTeam.None] = new List<(int x, int y, int z)>(),
            [PlayerTeam.Red] = new List<(int x, int y, int z)>(),
            [PlayerTeam.Blue] = new List<(int x, int y, int z)>(),
        };
        readonly Dictionary<PlayerTeam, (int x, int y, int z)> homeBases = new Dictionary<PlayerTeam, (int x, int y, int z)>();

        List<string> beaconIDList = new List<string>();
        Dictionary<Vector3, Beacon> beaconList = new Dictionary<Vector3, Beacon>();
        Random randGen = new Random();
        public string _GenerateBeaconID()
        {
            string id = "K";
            for (int i = 0; i < 3; i++)
                id += (char)randGen.Next(48, 58);
            return id;
        }
        public string GenerateBeaconID()
        {
            string newId = _GenerateBeaconID();
            while (beaconIDList.Contains(newId))
                newId = _GenerateBeaconID();
            beaconIDList.Add(newId);
            return newId;
        }

        bool InBounds(int x, int y, int z)
        {
            return !(x <= 0 || y <= 0 || z <= 0 || x >= MAPSIZE - 1 || y >= MAPSIZE - 1 || z >= MAPSIZE - 1);
        }

        public void SetBlock(int x, int y, int z, BlockType blockType, PlayerTeam team)
        {
            if (!InBounds(x, y, z))
                return;

            if (blockType == BlockType.BeaconRed || blockType == BlockType.BeaconBlue)
            {
                Beacon newBeacon = new Beacon();
                newBeacon.ID = GenerateBeaconID();
                newBeacon.Team = blockType == BlockType.BeaconRed ? PlayerTeam.Red : PlayerTeam.Blue;
                beaconList[new Vector3(x, y, z)] = newBeacon;
                SendSetBeacon(new Vector3(x, y + 1, z), newBeacon.ID, newBeacon.Team);
            }

            if (blockType == BlockType.None && (blockList[x, y, z] == BlockType.BeaconRed || blockList[x, y, z] == BlockType.BeaconBlue))
            {
                if (beaconList.ContainsKey(new Vector3(x, y, z)))
                    beaconList.Remove(new Vector3(x, y, z));
                SendSetBeacon(new Vector3(x, y + 1, z), "", PlayerTeam.None);
            }

            // Each team's teleporters are linked in the order they were built.
            if (blockList[x, y, z] == BlockType.Teleporter)
                teleporters[blockCreatorTeam[x, y, z]].Remove((x, y, z));
            if (blockType == BlockType.Teleporter)
                teleporters[team].Add((x, y, z));

            blockList[x, y, z] = blockType;
            blockCreatorTeam[x, y, z] = team;

            // x, y, z, type, all bytes
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.BlockSet);
            msgBuffer.Put((byte)x);
            msgBuffer.Put((byte)y);
            msgBuffer.Put((byte)z);
            msgBuffer.Put((byte)blockType);
            SendToAll(msgBuffer, NetChannel.World);

            if (blockType == BlockType.Lava)
                lavaBlockCount += 1;
        }

        // Generates the world and starts listening. Returns false if the port could not be opened.
        public bool Initialize()
        {
            // Load the ban-list.
            banList = LoadBanList();

            // Create our block world, translating the coordinates out of the cave generator (where Z points down)
            BlockType[, ,] worldData = CaveGenerator.GenerateCaveSystem(MAPSIZE, config.IncludeLava, config.OreFactor);
            blockList = new BlockType[MAPSIZE, MAPSIZE, MAPSIZE];
            blockCreatorTeam = new PlayerTeam[MAPSIZE, MAPSIZE, MAPSIZE];
            for (int i = 0; i < MAPSIZE; i++)
                for (int j = 0; j < MAPSIZE; j++)
                    for (int k = 0; k < MAPSIZE; k++)
                    {
                        blockList[i, MAPSIZE - 1 - k, j] = worldData[i, j, k];
                        blockCreatorTeam[i, j, k] = PlayerTeam.None;
                    }

            // Team home bases in opposite corners, as the original generator once had.
            if (config.HomeBases)
            {
                AddHomeBase(MAPSIZE - 6, MAPSIZE - 6, PlayerTeam.Red);
                AddHomeBase(5, 5, PlayerTeam.Blue);
            }

            // Initialize the server.
            listener = new EventBasedNetListener();
            listener.ConnectionRequestEvent += OnConnectionRequest;
            listener.PeerConnectedEvent += OnPeerConnected;
            listener.PeerDisconnectedEvent += OnPeerDisconnected;
            listener.NetworkReceiveEvent += OnNetworkReceive;
            listener.NetworkReceiveUnconnectedEvent += OnNetworkReceiveUnconnected;
            netServer = new NetManager(listener);
            netServer.ChannelsCount = NetChannel.ChannelCount;
            netServer.AutoRecycle = true;
            netServer.UnconnectedMessagesEnabled = true;
            netServer.BroadcastReceiveEnabled = true;
            if (config.DebugLatency > 0)
            {
                // Testing aid: delay every packet the server sends and receives.
                netServer.SimulateLatency = true;
                netServer.SimulationMinLatency = config.DebugLatency / 2;
                netServer.SimulationMaxLatency = config.DebugLatency / 2 + config.DebugLatency / 10;
                ConsoleWrite("SIMULATING " + config.DebugLatency + "MS OF LATENCY");
            }
            if (!netServer.Start(config.Port))
            {
                ConsoleWrite("ERROR: COULD NOT LISTEN ON PORT " + config.Port);
                return false;
            }
            ConsoleWrite("LISTENING ON PORT " + netServer.LocalPort);

            // Calculate initial lava flows.
            ConsoleWrite("CALCULATING INITIAL LAVA FLOWS");
            for (int i = 0; i < MAPSIZE * 2; i++)
                DoLavaStuff();
            ConsoleWrite("TOTAL LAVA BLOCKS = " + lavaBlockCount);

            // Send the initial server list update.
            PublicServerListUpdate();

            lastFlowCalc = DateTime.Now;
            SetBotCount(config.Bots);
            if (config.TimeLimitMinutes > 0)
                matchEndTime = Now + config.TimeLimitMinutes * 60;
            ConsoleWrite("SERVER READY");
            return true;
        }

        // Runs the server until it quits. Returns true if the server should be restarted.
        public bool Run()
        {
            ConsoleWrite("INFINIMINER SERVER " + GameConstants.Version);
            if (!Initialize())
                return false;

            while (Tick())
            {
                // Pass control over to waiting threads.
                Thread.Sleep(1);
            }

            return restartTriggered;
        }

        // Advances the server by one step. Returns false once the server has shut down.
        public bool Tick()
        {
            // Handle console commands.
            while (pendingCommands.TryDequeue(out string command))
                ConsoleProcessInput(command);

            if (!keepRunning)
            {
                Shutdown("The server was terminated.");
                return false;
            }

            // Process any messages that are here.
            netServer.PollEvents();

            // Computer players.
            DateTime botTime = DateTime.Now;
            UpdateBots((float)Math.Min(0.25, (botTime - lastBotUpdate).TotalSeconds));
            lastBotUpdate = botTime;

            // Lava, shock blocks and the void are lethal whether or not the client admits it.
            foreach (ServerPlayer p in playerList.Values.ToList())
                CheckHazards(p);

            // Time to send a new server update?
            TimeSpan updateTimeSpan = DateTime.Now - lastServerListUpdate;
            if (updateTimeSpan.TotalMinutes > 5)
                PublicServerListUpdate();

            // Check for players who are in the zone to deposit.
            DepositForPlayers();

            // Is it time to do a lava calculation? If so, do it!
            TimeSpan timeSpan = DateTime.Now - lastFlowCalc;
            if (timeSpan.TotalMilliseconds > 500)
            {
                DoLavaStuff();
                lastFlowCalc = DateTime.Now;
            }

            // Is the game over?
            if (winningTeam != PlayerTeam.None && !restartTriggered)
            {
                BroadcastGameOver();
                restartTriggered = true;
                restartTime = DateTime.Now.AddSeconds(10);
            }

            // Restart the server?
            if (restartTriggered && DateTime.Now > restartTime)
            {
                SaveLevel("autosave_" + (UInt64)DateTime.Now.ToBinary() + ".lvl");
                Shutdown("The server is restarting.");
                return false;
            }

            return true;
        }

        public void Shutdown(string reason)
        {
            if (netServer == null || !netServer.IsRunning)
                return;
            byte[] reasonData = NetExtensions.StringPayload(reason).CopyData();
            netServer.DisconnectAll(reasonData, 0, reasonData.Length);
            netServer.Stop();
        }

        void OnConnectionRequest(ConnectionRequest request)
        {
            string appIdentifier, handle, clientVersion;
            try
            {
                appIdentifier = request.Data.GetString();
                handle = request.Data.GetString();
                clientVersion = request.Data.GetString();
            }
            catch (Exception)
            {
                request.Reject();
                return;
            }

            if (appIdentifier != GameConstants.AppIdentifier)
            {
                request.Reject();
                return;
            }

            handle = TextUtil.Sanitize(handle).Trim();
            if (handle.Length == 0 || handle.Length > 20)
                handle = "Player";

            foreach (ServerPlayer oldPlayer in playerList.Values)
            {
                if (handle == oldPlayer.Handle)
                {
                    duplicateNameCount++;
                    handle += "." + duplicateNameCount.ToString();
                    break;
                }
            }

            IPAddress address = request.RemoteEndPoint.Address;
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            string ip = address.ToString();

            if (clientVersion != GameConstants.Version)
                request.Reject(NetExtensions.StringPayload("VER;" + GameConstants.Version));
            else if (banList.Contains(ip))
                request.Reject(NetExtensions.StringPayload("BAN;"));
            else if (playerList.Count - bots.Count >= config.MaxPlayers)
                request.Reject(NetExtensions.StringPayload("FULL;"));
            else
            {
                // A bot makes way for a person.
                if (playerList.Count >= config.MaxPlayers && bots.Count > 0)
                    RemoveBot(bots[bots.Count - 1]);
                NetPeer peer = request.Accept();
                ServerPlayer newPlayer = new ServerPlayer(peer, ip);
                newPlayer.Handle = handle;
                playerList[peer.Id] = newPlayer;
            }
        }

        void OnPeerConnected(NetPeer peer)
        {
            if (!playerList.TryGetValue(peer.Id, out ServerPlayer player))
                return;

            ConsoleWrite("CONNECT: " + player.Handle);
            SendCurrentMap(peer);
            SendPlayerJoined(player);
            SendMatchInfo(null);
            PublicServerListUpdate();
        }

        void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (!playerList.TryGetValue(peer.Id, out ServerPlayer player) || player.Peer != peer)
                return;

            ConsoleWrite("DISCONNECT: " + player.Handle);
            playerList.Remove(peer.Id);
            SendPlayerLeft(player, player.Kicked ? "WAS KICKED FROM THE GAME!" : "HAS ABANDONED THEIR DUTIES!");
            BalanceBots();
            SendMatchInfo(null);
            PublicServerListUpdate();
        }

        void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            if (!reader.TryGetString(out string request) || request != GameConstants.DiscoveryRequest)
                return;

            DiscoveryInfo info = new DiscoveryInfo
            {
                InstanceId = instanceId,
                ServerName = config.ServerName,
                PlayerCount = playerList.Count,
                MaxPlayers = (int)config.MaxPlayers,
                Extra = GetExtraInfo(),
            };
            NetDataWriter response = new NetDataWriter();
            info.Write(response);
            netServer.SendUnconnectedMessage(response, remoteEndPoint);
        }

        void OnNetworkReceive(NetPeer peer, NetPacketReader msgBuffer, byte channel, DeliveryMethod deliveryMethod)
        {
            if (!playerList.TryGetValue(peer.Id, out ServerPlayer player))
                return;

            try
            {
                HandleData(player, msgBuffer);
            }
            catch (Exception e)
            {
                // A malformed packet from one client must never take the server down.
                ConsoleWrite("WARNING: bad packet from " + player.Handle + " (" + player.IP + "): " + e.Message);
            }
        }

        void HandleData(ServerPlayer player, NetDataReader msgBuffer)
        {
            InfiniminerMessage dataType = (InfiniminerMessage)msgBuffer.GetByte();
            switch (dataType)
            {
                case InfiniminerMessage.ChatMessage:
                    {
                        // Read the data from the packet.
                        ChatMessageType chatType = (ChatMessageType)msgBuffer.GetByte();
                        string chatString = TextUtil.Sanitize(msgBuffer.GetString()).Trim();
                        if (chatString.Length > MaxChatLength)
                            chatString = chatString.Substring(0, MaxChatLength);
                        if (chatString == "" || !player.ChatLimiter.TryTake(Now))
                            break;
                        if (chatString.StartsWith('/'))
                        {
                            HandleChatCommand(player, chatString);
                            break;
                        }
                        ConsoleWrite("CHAT: (" + player.Handle + ") " + chatString);

                        // Append identifier information.
                        if (chatType == ChatMessageType.SayAll)
                            chatString = player.Handle + " (ALL): " + chatString;
                        else
                            chatString = player.Handle + " (TEAM): " + chatString;

                        // Construct the message packet.
                        NetDataWriter chatPacket = NetExtensions.CreateMessage(InfiniminerMessage.ChatMessage);
                        chatPacket.Put((byte)((player.Team == PlayerTeam.Red) ? ChatMessageType.SayRedTeam : ChatMessageType.SayBlueTeam));
                        chatPacket.Put(chatString);

                        // Send the packet to people who should recieve it.
                        foreach (ServerPlayer p in playerList.Values)
                        {
                            if (chatType == ChatMessageType.SayAll ||
                                chatType == ChatMessageType.SayBlueTeam && p.Team == PlayerTeam.Blue ||
                                chatType == ChatMessageType.SayRedTeam && p.Team == PlayerTeam.Red)
                                p.Peer.Send(chatPacket, NetChannel.ReliableInOrder3);
                        }
                    }
                    break;

                case InfiniminerMessage.UseTool:
                    {
                        Vector3 playerPosition = msgBuffer.GetVector3();
                        Vector3 playerHeading = msgBuffer.GetVector3();
                        PlayerTools playerTool = (PlayerTools)msgBuffer.GetByte();
                        BlockType blockType = (BlockType)msgBuffer.GetByte();

                        // Only living players can use tools, and only the tools their class carries,
                        // no faster than the tool's cooldown allows.
                        if (!player.Alive || !player.HasClass)
                            break;
                        if (!player.ToolLimiters.TryGetValue(playerTool, out RateLimiter toolLimiter) || !toolLimiter.TryTake(Now))
                            break;
                        if (!float.IsFinite(playerHeading.X) || !float.IsFinite(playerHeading.Y) || !float.IsFinite(playerHeading.Z) || playerHeading == Vector3.Zero)
                            break;
                        playerHeading = Vector3.Normalize(playerHeading);

                        // The client's position at the moment it used the tool is a little more precise than the
                        // last update we have, so use it, unless it's further away than lag can explain.
                        if (!float.IsFinite(playerPosition.X) || !float.IsFinite(playerPosition.Y) || !float.IsFinite(playerPosition.Z)
                            || Vector3.Distance(playerPosition, player.Position) > MaxToolPositionError)
                            playerPosition = player.Position;

                        switch (playerTool)
                        {
                            case PlayerTools.Pickaxe:
                                UsePickaxe(player, playerPosition, playerHeading, false);
                                break;
                            case PlayerTools.Drill:
                                UsePickaxe(player, playerPosition, playerHeading, true);
                                break;
                            case PlayerTools.ConstructionGun:
                                if (Array.IndexOf(GameRules.GetBlocks(player.Class, player.Team), blockType) < 0)
                                    TriggerConstructionGunAnimation(player, -0.2f);
                                else
                                    UseConstructionGun(player, playerPosition, playerHeading, blockType);
                                break;
                            case PlayerTools.DeconstructionGun:
                                UseDeconstructionGun(player, playerPosition, playerHeading);
                                break;
                            case PlayerTools.ProspectingRadar:
                                UseSignPainter(player, playerPosition, playerHeading);
                                break;
                            case PlayerTools.Detonator:
                                UseDetonator(player);
                                break;
                        }
                    }
                    break;

                case InfiniminerMessage.SelectClass:
                    {
                        PlayerClass playerClass = (PlayerClass)msgBuffer.GetByte();
                        if (!Enum.IsDefined(playerClass))
                            break;

                        ChooseClass(player, playerClass);
                    }
                    break;

                case InfiniminerMessage.PlayerSetTeam:
                    {
                        PlayerTeam playerTeam = (PlayerTeam)msgBuffer.GetByte();
                        if (playerTeam != PlayerTeam.Red && playerTeam != PlayerTeam.Blue)
                            break;

                        ChooseTeam(player, playerTeam);
                    }
                    break;

                case InfiniminerMessage.PlayerDead:
                    {
                        string deathMessage = TextUtil.Sanitize(msgBuffer.GetString());
                        if (deathMessage.Length > MaxChatLength)
                            deathMessage = deathMessage.Substring(0, MaxChatLength);
                        KillPlayer(player, deathMessage);
                    }
                    break;

                case InfiniminerMessage.PlayerAlive:
                    {
                        if (!player.Alive)
                            RespawnPlayer(player);
                    }
                    break;

                case InfiniminerMessage.PlayerUpdate:
                    {
                        Vector3 position = msgBuffer.GetVector3();
                        Vector3 heading = msgBuffer.GetVector3();
                        PlayerTools tool = (PlayerTools)msgBuffer.GetByte();
                        bool usingTool = msgBuffer.GetBool();
                        byte epoch = msgBuffer.AvailableBytes > 0 ? msgBuffer.GetByte() : (byte)(player.PositionEpoch - 1);

                        if (float.IsFinite(heading.X) && float.IsFinite(heading.Y) && float.IsFinite(heading.Z))
                            player.Heading = heading;
                        player.Tool = player.HasClass && Array.IndexOf(GameRules.GetTools(player.Class), tool) >= 0 ? tool : PlayerTools.Pickaxe;
                        player.UsingTool = usingTool;

                        // Updates sent before the server last moved this player are stale; dead players don't move.
                        if (player.Alive && epoch == player.PositionEpoch)
                            UpdatePlayerPosition(player, position);

                        SendPlayerUpdate(player);
                    }
                    break;

                case InfiniminerMessage.DepositOre:
                    {
                        if (!player.Alive || !NearOwnBank(player))
                            break;
                        DepositOre(player);
                        foreach (ServerPlayer p in playerList.Values)
                            SendResourceUpdate(p);
                    }
                    break;

                case InfiniminerMessage.WithdrawOre:
                    {
                        if (!player.Alive || !NearOwnBank(player))
                            break;
                        WithdrawOre(player);
                        foreach (ServerPlayer p in playerList.Values)
                            SendResourceUpdate(p);
                    }
                    break;

                case InfiniminerMessage.PlayerPing:
                    {
                        // The id in the packet is ignored so nobody can ping as someone else.
                        if (player.PingLimiter.TryTake(Now))
                            SendPlayerPing(player.ID);
                    }
                    break;

                case InfiniminerMessage.PlaySound:
                    {
                        // Clients only ask for the sounds of their own landings and jump blocks.
                        InfiniminerSound sound = (InfiniminerSound)msgBuffer.GetByte();
                        msgBuffer.GetVector3();
                        if ((sound == InfiniminerSound.GroundHit || sound == InfiniminerSound.Jumpblock) && player.Alive && player.SoundLimiter.TryTake(Now))
                            PlaySound(sound, player.Position);
                    }
                    break;
            }
        }

        const int MaxChatLength = 160;
        const float MaxToolPositionError = 1.5f;

        void UpdatePlayerPosition(ServerPlayer player, Vector3 position)
        {
            MovementResult result = movementValidator.Validate(player.Movement, player.Team, position, Now);
            if (!result.Accepted)
            {
                // If the client walked into a block it doesn't know about, resend the blocks there so we agree.
                if (result.BlockedAt.HasValue)
                    ResendBlocksAround(player, result.BlockedAt.Value);
                CorrectPlayerPosition(player, result.Reason);
                return;
            }

            player.Position = position;
            if (result.FallHeight >= MovementValidator.LethalFallHeight)
            {
                KillPlayer(player, "WAS KILLED BY GRAVITY!");
                return;
            }
            CheckTeleporter(player);
        }

        // Stepping onto one of your team's teleporters takes you to the next one your team built.
        void CheckTeleporter(ServerPlayer player)
        {
            Vector3 foot = player.Position + new Vector3(0, GameRules.FootOffset, 0);
            (int x, int y, int z) block = (GameConstants.ToBlock(foot.X), GameConstants.ToBlock(foot.Y), GameConstants.ToBlock(foot.Z));
            bool onOwnTeleporter = InBounds(block.x, block.y, block.z)
                && blockList[block.x, block.y, block.z] == BlockType.Teleporter
                && blockCreatorTeam[block.x, block.y, block.z] == player.Team;
            if (!onOwnTeleporter)
            {
                player.OnTeleporter = null;
                return;
            }
            if (player.OnTeleporter == block)
                return; // still standing on the one we arrived on
            player.OnTeleporter = block;

            List<(int x, int y, int z)> network = teleporters[player.Team];
            int index = network.IndexOf(block);
            if (index < 0 || network.Count < 2)
                return;
            (int x, int y, int z) destination = network[(index + 1) % network.Count];
            Vector3 arrival = new Vector3(destination.x + 0.5f, destination.y + 2.45f, destination.z + 0.5f);
            if (!GameRules.IsPassable(BlockAtPoint(arrival), player.Team)
                || !GameRules.IsPassable(BlockAtPoint(arrival + new Vector3(0, GameRules.LowerBodyOffset, 0)), player.Team))
                return; // blocked exit

            PlaySound(InfiniminerSound.Teleporter, player.Position);
            player.Position = arrival;
            player.OnTeleporter = destination;
            SendSetPosition(player, arrival, PositionReason.Teleport);
            PlaySound(InfiniminerSound.Teleporter, arrival);
        }

        void ResendBlocksAround(ServerPlayer player, Vector3 point)
        {
            int cx = GameConstants.ToBlock(point.X), cy = GameConstants.ToBlock(point.Y), cz = GameConstants.ToBlock(point.Z);
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int y = cy - 1; y <= cy + 1; y++)
                    for (int z = cz - 1; z <= cz + 1; z++)
                        if (InBounds(x, y, z))
                        {
                            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.BlockSet);
                            msgBuffer.Put((byte)x);
                            msgBuffer.Put((byte)y);
                            msgBuffer.Put((byte)z);
                            msgBuffer.Put((byte)blockList[x, y, z]);
                            player.Peer.Send(msgBuffer, NetChannel.World);
                        }
        }

        // Moves the player back to their last valid position.
        void CorrectPlayerPosition(ServerPlayer player, string reason)
        {
            if (Now - player.LastCorrectionLog > 5)
            {
                ConsoleWrite("ANTICHEAT: corrected " + player.Handle + " (" + player.IP + "): " + reason
                    + FormattableString.Invariant($" [last valid {player.Position.X:0.00},{player.Position.Y:0.00},{player.Position.Z:0.00}]"));
                player.LastCorrectionLog = Now;
            }
            SendSetPosition(player, player.Position, PositionReason.Correction);
        }

        void SendSetPosition(ServerPlayer player, Vector3 position, PositionReason reason)
        {
            player.PositionEpoch++;
            player.Movement.Reset(position, Now);
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerSetPosition);
            msgBuffer.Put(position);
            msgBuffer.Put(player.PositionEpoch);
            msgBuffer.Put((byte)reason);
            player.Peer.Send(msgBuffer, NetChannel.ReliableInOrder2);
        }

        public void ChooseClass(ServerPlayer player, PlayerClass playerClass)
        {
            // Changing class is only possible while dead (the client kills you first).
            if (player.Alive)
                KillPlayer(player, "");

            ConsoleWrite("SELECT_CLASS: " + player.Handle + ", " + playerClass.ToString());
            player.SetClass(playerClass);
            switch (playerClass)
            {
                case PlayerClass.Engineer:
                    player.OreMax = 350;
                    player.WeightMax = 4;
                    break;
                case PlayerClass.Miner:
                    player.OreMax = 200;
                    player.WeightMax = 8;
                    break;
                case PlayerClass.Prospector:
                    player.OreMax = 200;
                    player.WeightMax = 4;
                    break;
                case PlayerClass.Sapper:
                    player.OreMax = 200;
                    player.WeightMax = 4;
                    break;
            }
            SendResourceUpdate(player);
        }

        public void ChooseTeam(ServerPlayer player, PlayerTeam playerTeam)
        {
            // With auto-balance on, nobody can join a team that already has more players.
            if (config.AutoBalance)
            {
                PlayerTeam otherTeam = playerTeam == PlayerTeam.Red ? PlayerTeam.Blue : PlayerTeam.Red;
                int requested = playerList.Values.Count(p => p != player && p.Team == playerTeam);
                int other = playerList.Values.Count(p => p != player && p.Team == otherTeam);
                if (requested > other)
                {
                    playerTeam = otherTeam;
                    NetDataWriter notice = NetExtensions.CreateMessage(InfiniminerMessage.ChatMessage);
                    notice.Put((byte)ChatMessageType.SayAll);
                    notice.Put("SERVER: THE TEAMS ARE UNEVEN, SO YOU HAVE JOINED THE " + GetTeamName(playerTeam) + " TEAM.");
                    player.Peer.Send(notice, NetChannel.ReliableInOrder3);
                }
            }

            // Switching teams while alive would let you carry loot to the other side.
            if (player.Alive)
                KillPlayer(player, "");

            ConsoleWrite("SELECT_TEAM: " + player.Handle + ", " + playerTeam.ToString());
            player.Team = playerTeam;
            SendResourceUpdate(player);
            SendPlayerSetTeam(player);
            BalanceBots();
        }

        public IReadOnlyList<ServerBot> Bots
        {
            get { return bots; }
        }

        // Adds or sends home computer players until there are `count` of them.
        public void SetBotCount(int count)
        {
            while (bots.Count < count)
                AddBot();
            while (bots.Count > count)
                RemoveBot(bots[bots.Count - 1]);
        }

        void AddBot()
        {
            string name = BotNames.FirstOrDefault(n => !playerList.Values.Any(p => p.Handle == "BOT " + n)) ?? "BOT " + bots.Count;
            ServerPlayer player = new ServerPlayer(null, "bot") { Handle = name.StartsWith("BOT ") ? name : "BOT " + name };
            playerList[-(int)player.ID] = player;
            ServerBot bot = new ServerBot(player, this, randGen.Next());
            bots.Add(bot);
            ConsoleWrite("BOT JOINED: " + player.Handle);
            SendPlayerJoined(player);

            int red = playerList.Values.Count(p => p != player && p.Team == PlayerTeam.Red);
            int blue = playerList.Values.Count(p => p != player && p.Team == PlayerTeam.Blue);
            ChooseTeam(player, red <= blue ? PlayerTeam.Red : PlayerTeam.Blue);
            ChooseClass(player, PlayerClass.Miner);
            SendMatchInfo(null);
        }

        void RemoveBot(ServerBot bot)
        {
            bots.Remove(bot);
            playerList.Remove(-(int)bot.Player.ID);
            ConsoleWrite("BOT LEFT: " + bot.Player.Handle);
            SendPlayerLeft(bot.Player, "WAS SENT HOME.");
            SendMatchInfo(null);
        }

        // Keeps the teams even by moving bots (never people) from the bigger team.
        void BalanceBots()
        {
            if (balancingBots)
                return;
            balancingBots = true;
            try
            {
                while (true)
                {
                    int red = playerList.Values.Count(p => p.Team == PlayerTeam.Red);
                    int blue = playerList.Values.Count(p => p.Team == PlayerTeam.Blue);
                    if (Math.Abs(red - blue) < 2)
                        break;
                    PlayerTeam bigger = red > blue ? PlayerTeam.Red : PlayerTeam.Blue;
                    ServerBot bot = bots.LastOrDefault(b => b.Player.Team == bigger);
                    if (bot == null)
                        break;
                    ChooseTeam(bot.Player, bigger == PlayerTeam.Red ? PlayerTeam.Blue : PlayerTeam.Red);
                }
            }
            finally
            {
                balancingBots = false;
            }
        }

        public void UpdateBots(float elapsedSeconds)
        {
            foreach (ServerBot bot in bots.ToList())
                bot.Update(elapsedSeconds);
        }

        int IBotWorld.MapSize
        {
            get { return MAPSIZE; }
        }

        BlockType IBotWorld.BlockAt(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= MAPSIZE || y >= MAPSIZE || z >= MAPSIZE)
                return y >= MAPSIZE ? BlockType.None : BlockType.Metal;
            return blockList[x, y, z];
        }

        void IBotWorld.Dig(ServerPlayer bot, int x, int y, int z)
        {
            DigBlock(bot, x, y, z, false);
        }

        void IBotWorld.Moved(ServerPlayer bot)
        {
            SendPlayerUpdate(bot);
        }

        void IBotWorld.Respawn(ServerPlayer bot)
        {
            RespawnPlayer(bot);
        }

        void IBotWorld.Kill(ServerPlayer bot, string deathMessage)
        {
            KillPlayer(bot, deathMessage);
        }

        public void KillPlayer(ServerPlayer player, string deathMessage)
        {
            if (!player.Alive)
                return;

            ConsoleWrite("PLAYER_DEAD: " + player.Handle + " " + deathMessage);

            player.Ore = 0;
            player.Cash = 0;
            player.Weight = 0;
            player.Alive = false;
            SendResourceUpdate(player);
            SendPlayerDead(player);

            if (deathMessage != "")
            {
                NetDataWriter chatPacket = NetExtensions.CreateMessage(InfiniminerMessage.ChatMessage);
                chatPacket.Put((byte)(player.Team == PlayerTeam.Red ? ChatMessageType.SayRedTeam : ChatMessageType.SayBlueTeam));
                chatPacket.Put(player.Handle + " " + deathMessage);
                SendToAll(chatPacket, NetChannel.ReliableInOrder3);
            }
        }

        public void RespawnPlayer(ServerPlayer player)
        {
            ConsoleWrite("PLAYER_ALIVE: " + player.Handle);
            player.Position = FindHomeSpawn(player.Team) ?? FindSpawnPosition();
            SendSetPosition(player, player.Position, PositionReason.Respawn);

            player.Ore = 0;
            player.Cash = 0;
            player.Weight = 0;
            player.Alive = true;
            SendResourceUpdate(player);
            SendPlayerAlive(player);
        }

        // A crater at ground level with an indestructible metal floor and the team's home block in the middle.
        void AddHomeBase(int cx, int cz, PlayerTeam team)
        {
            int cy = MAPSIZE - 1 - GameConstants.GroundLevel;
            for (int x = cx - 4; x <= cx + 4; x++)
                for (int y = cy - 4; y <= cy + 4; y++)
                    for (int z = cz - 4; z <= cz + 4; z++)
                    {
                        if (!InBounds(x, y, z) || (x - cx) * (x - cx) + (y - cy) * (y - cy) + (z - cz) * (z - cz) >= 16)
                            continue;
                        blockList[x, y, z] = y >= cy ? BlockType.None : y == cy - 1 ? BlockType.Metal : BlockType.Dirt;
                        blockCreatorTeam[x, y, z] = PlayerTeam.None;
                    }
            blockList[cx, cy, cz] = team == PlayerTeam.Red ? BlockType.HomeRed : BlockType.HomeBlue;
            blockCreatorTeam[cx, cy, cz] = team;
            homeBases[team] = (cx, cy, cz);

            // Shown on the team's radar like a beacon (beacons are reported one block up).
            beaconList[new Vector3(cx, cy, cz)] = new Beacon { ID = "HOME", Team = team };
        }

        // With home bases, players spawn on their team's base floor (unless something has made it unsafe).
        Vector3? FindHomeSpawn(PlayerTeam team)
        {
            if (!homeBases.TryGetValue(team, out (int x, int y, int z) home))
                return null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int x = home.x + randGen.Next(-2, 3), z = home.z + randGen.Next(-2, 3);
                if (x == home.x && z == home.z)
                    continue;
                Vector3 position = new Vector3(x + 0.5f, home.y + 1.45f, z + 0.5f);
                BlockType floor = BlockAtPoint(position + new Vector3(0, GameRules.FootOffset, 0));
                if (floor != BlockType.None && floor != BlockType.Lava
                    && BlockAtPoint(position) == BlockType.None
                    && BlockAtPoint(position + new Vector3(0, GameRules.LowerBodyOffset, 0)) == BlockType.None)
                    return position;
            }
            return null;
        }

        // Drop the player a few blocks above a random safe spot on the surface (the client used to choose this).
        public Vector3 FindSpawnPosition()
        {
            for (int i = 0; i < 20; i++)
            {
                Vector3 startPos = new Vector3(randGen.Next(2, MAPSIZE - 2), MAPSIZE - 1, randGen.Next(2, MAPSIZE - 2));
                for (startPos.Y = MAPSIZE - 1; startPos.Y >= MAPSIZE - 10; startPos.Y--)
                {
                    BlockType blockType = BlockAtPoint(startPos);
                    if (blockType == BlockType.Lava)
                        break;
                    else if (blockType != BlockType.None)
                        return startPos + Vector3.UnitY * 5 + new Vector3(0.5f, 0, 0.5f);
                }
            }
            return new Vector3(randGen.Next(2, MAPSIZE - 2) + 0.5f, MAPSIZE + 2, randGen.Next(2, MAPSIZE - 2) + 0.5f);
        }

        void CheckHazards(ServerPlayer player)
        {
            if (!player.Alive)
                return;

            Vector3 position = player.Position;
            BlockType standingOn = BlockAtPoint(position + new Vector3(0, GameRules.FootOffset, 0));
            if (standingOn == BlockType.Lava
                || BlockAtPoint(position) == BlockType.Lava
                || BlockAtPoint(position + new Vector3(0, GameRules.MidBodyOffset, 0)) == BlockType.Lava
                || BlockAtPoint(position + new Vector3(0, GameRules.LowerBodyOffset, 0)) == BlockType.Lava)
                KillPlayer(player, "WAS INCINERATED BY LAVA!");
            else if (BlockAtPoint(position + new Vector3(0, GameRules.HeadOffset, 0)) == BlockType.Shock
                || (config.ShockTouch && TouchingShock(position)))
                KillPlayer(player, "WAS ELECTROCUTED!");
            else if (position.Y < -30)
                KillPlayer(player, "WAS KILLED BY MISADVENTURE!");
        }

        // Standing on a shock block, or brushing against one with any part of the body.
        bool TouchingShock(Vector3 position)
        {
            if (BlockAtPoint(position + new Vector3(0, GameRules.FootOffset, 0)) == BlockType.Shock)
                return true;
            foreach (float offset in new[] { 0f, GameRules.MidBodyOffset, GameRules.LowerBodyOffset })
                foreach (Vector3 side in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
                    if (BlockAtPoint(position + new Vector3(0, offset, 0) + side * 0.6f) == BlockType.Shock)
                        return true;
            return false;
        }

        bool NearOwnBank(ServerPlayer player)
        {
            BlockType ownBank = player.Team == PlayerTeam.Red ? BlockType.BankRed : BlockType.BankBlue;
            int px = GameConstants.ToBlock(player.Position.X), py = GameConstants.ToBlock(player.Position.Y), pz = GameConstants.ToBlock(player.Position.Z);
            for (int x = px - 3; x <= px + 3; x++)
                for (int y = py - 3; y <= py + 3; y++)
                    for (int z = pz - 3; z <= pz + 3; z++)
                        if (InBounds(x, y, z) && blockList[x, y, z] == ownBank
                            && Vector3.Distance(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), player.Position) <= 3.5f)
                            return true;
            return false;
        }

        public void DepositForPlayers()
        {
            foreach (ServerPlayer p in playerList.Values)
            {
                if (p.Alive && p.Position.Y > MAPSIZE - GameConstants.GroundLevel)
                    DepositCash(p);
            }

            if (config.SandboxMode)
                return;
            if (teamCashBlue >= WinningCash && winningTeam == PlayerTeam.None)
                winningTeam = PlayerTeam.Blue;
            if (teamCashRed >= WinningCash && winningTeam == PlayerTeam.None)
                winningTeam = PlayerTeam.Red;

            // When time runs out, the richer team wins; a tie goes to overtime and the next deposit decides it.
            if (winningTeam == PlayerTeam.None && Now >= matchEndTime)
            {
                if (teamCashRed != teamCashBlue)
                    winningTeam = teamCashRed > teamCashBlue ? PlayerTeam.Red : PlayerTeam.Blue;
                else if (!overtime)
                {
                    overtime = true;
                    SendServerMessage("SERVER: TIME IS UP AND THE TEAMS ARE TIED. OVERTIME: THE NEXT DEPOSIT WINS!");
                    SendMatchInfo(null);
                }
            }
        }

        public void DoLavaStuff()
        {
            bool[, ,] flowSleep = new bool[MAPSIZE, MAPSIZE, MAPSIZE]; //if true, do not calculate this turn

            for (int i = 0; i < MAPSIZE; i++)
                for (int j = 0; j < MAPSIZE; j++)
                    for (int k = 0; k < MAPSIZE; k++)
                        if (blockList[i, j, k] == BlockType.Lava && !flowSleep[i, j, k])
                        {
                            // RULES FOR LAVA EXPANSION:
                            // if the block below is lava, do nothing
                            // if the block below is empty space, add lava there
                            // if the block below is something solid, add lava to the sides
                            BlockType typeBelow = (j == 0) ? BlockType.Lava : blockList[i, j - 1, k];
                            if (typeBelow == BlockType.None)
                            {
                                if (j > 0)
                                {
                                    SetBlock(i, j - 1, k, BlockType.Lava, PlayerTeam.None);
                                    flowSleep[i, j - 1, k] = true;
                                }
                            }
                            else if (typeBelow != BlockType.Lava)
                            {
                                if (i > 0 && blockList[i - 1, j, k] == BlockType.None)
                                {
                                    SetBlock(i - 1, j, k, BlockType.Lava, PlayerTeam.None);
                                    flowSleep[i - 1, j, k] = true;
                                }
                                if (k > 0 && blockList[i, j, k - 1] == BlockType.None)
                                {
                                    SetBlock(i, j, k - 1, BlockType.Lava, PlayerTeam.None);
                                    flowSleep[i, j, k - 1] = true;
                                }
                                if (i < MAPSIZE - 1 && blockList[i + 1, j, k] == BlockType.None)
                                {
                                    SetBlock(i + 1, j, k, BlockType.Lava, PlayerTeam.None);
                                    flowSleep[i + 1, j, k] = true;
                                }
                                if (k < MAPSIZE - 1 && blockList[i, j, k + 1] == BlockType.None)
                                {
                                    SetBlock(i, j, k + 1, BlockType.Lava, PlayerTeam.None);
                                    flowSleep[i, j, k + 1] = true;
                                }
                            }
                        }
        }

        public BlockType BlockAtPoint(Vector3 point)
        {
            int x = GameConstants.ToBlock(point.X);
            int y = GameConstants.ToBlock(point.Y);
            int z = GameConstants.ToBlock(point.Z);
            if (!InBounds(x, y, z))
                return BlockType.None;
            return blockList[x, y, z];
        }

        public bool RayCollision(Vector3 startPosition, Vector3 rayDirection, float distance, int searchGranularity, ref Vector3 hitPoint, ref Vector3 buildPoint)
        {
            Vector3 testPos = startPosition;
            Vector3 buildPos = startPosition;
            for (int i = 0; i < searchGranularity; i++)
            {
                testPos += rayDirection * distance / searchGranularity;
                BlockType testBlock = BlockAtPoint(testPos);
                if (testBlock != BlockType.None)
                {
                    hitPoint = testPos;
                    buildPoint = buildPos;
                    return true;
                }
                buildPos = testPos;
            }
            return false;
        }

        // The drill digs everything the pickaxe does, plus rock, but more slowly.
        public void UsePickaxe(ServerPlayer player, Vector3 playerPosition, Vector3 playerHeading, bool drill)
        {
            player.QueueAnimationBreak = true;

            // Figure out what we're hitting.
            Vector3 hitPoint = Vector3.Zero;
            Vector3 buildPoint = Vector3.Zero;
            if (!RayCollision(playerPosition, playerHeading, 2, 10, ref hitPoint, ref buildPoint))
                return;
            DigBlock(player, GameConstants.ToBlock(hitPoint.X), GameConstants.ToBlock(hitPoint.Y), GameConstants.ToBlock(hitPoint.Z), drill);
        }

        // Digs one block for a player: what it gives and whether it goes follow GameRules.Dig.
        public void DigBlock(ServerPlayer player, int x, int y, int z, bool drill)
        {
            if (!InBounds(x, y, z))
                return;
            // Figure out what the result is.
            DigResult dig = GameRules.Dig(blockList[x, y, z], drill);
            bool removeBlock = dig.Removes;
            uint giveOre = dig.Ore;
            uint giveCash = dig.Cash;
            uint giveWeight = dig.Weight;
            InfiniminerSound sound = dig.Sound;

            if (giveOre > 0)
            {
                if (player.Ore < player.OreMax)
                {
                    player.Ore = Math.Min(player.Ore + giveOre, player.OreMax);
                    SendResourceUpdate(player);
                }
            }

            if (giveWeight > 0)
            {
                if (player.Weight < player.WeightMax)
                {
                    player.Weight = Math.Min(player.Weight + giveWeight, player.WeightMax);
                    player.Cash += giveCash;
                    SendResourceUpdate(player);
                }
                else
                    removeBlock = false;
            }

            if (removeBlock)
            {
                SetBlock(x, y, z, BlockType.None, PlayerTeam.None);
                PlaySound(sound, player.Position);
            }
        }

        public void UseConstructionGun(ServerPlayer player, Vector3 playerPosition, Vector3 playerHeading, BlockType blockType)
        {
            bool actionFailed = false;

            // Only real construction blocks can be built (guards against modified clients).
            if (!BlockInformation.IsBuildable(blockType))
                actionFailed = true;

            // If there's no surface within range, bail.
            Vector3 hitPoint = Vector3.Zero;
            Vector3 buildPoint = Vector3.Zero;
            if (!RayCollision(playerPosition, playerHeading, 6, 25, ref hitPoint, ref buildPoint))
                actionFailed = true;

            // If the block is too expensive, bail.
            uint blockCost = BlockInformation.GetCost(blockType);
            if (config.SandboxMode && blockCost <= player.OreMax)
                blockCost = 0;
            if (blockCost > player.Ore)
                actionFailed = true;

            // If there's someone there currently, bail.
            int x = GameConstants.ToBlock(buildPoint.X);
            int y = GameConstants.ToBlock(buildPoint.Y);
            int z = GameConstants.ToBlock(buildPoint.Z);
            foreach (ServerPlayer p in playerList.Values)
            {
                if (p.Alive && (int)p.Position.X == x && (int)p.Position.Z == z && ((int)p.Position.Y == y || (int)p.Position.Y - 1 == y))
                    actionFailed = true;
            }

            // If it's out of bounds, bail.
            if (!InBounds(x, y, z))
                actionFailed = true;

            // If it's lava, don't let them build off of lava.
            if (BlockAtPoint(hitPoint) == BlockType.Lava)
                actionFailed = true;

            if (actionFailed)
            {
                // Decharge the player's gun.
                TriggerConstructionGunAnimation(player, -0.2f);
            }
            else
            {
                // Fire the player's gun.
                TriggerConstructionGunAnimation(player, 0.5f);

                // Build the block.
                SetBlock(x, y, z, blockType, player.Team);
                player.Ore -= blockCost;
                SendResourceUpdate(player);

                // Play the sound.
                PlaySound(InfiniminerSound.ConstructionGun, player.Position);

                // If it's an explosive block, add it to our list.
                if (blockType == BlockType.Explosive)
                    player.ExplosiveList.Add(new Vector3(x, y, z));
            }
        }

        public void UseDeconstructionGun(ServerPlayer player, Vector3 playerPosition, Vector3 playerHeading)
        {
            bool actionFailed = false;

            // If there's no surface within range, bail.
            Vector3 hitPoint = Vector3.Zero;
            Vector3 buildPoint = Vector3.Zero;
            if (!RayCollision(playerPosition, playerHeading, 6, 25, ref hitPoint, ref buildPoint))
                actionFailed = true;
            int x = GameConstants.ToBlock(hitPoint.X);
            int y = GameConstants.ToBlock(hitPoint.Y);
            int z = GameConstants.ToBlock(hitPoint.Z);

            // If this is another team's block, bail.
            if (blockCreatorTeam[x, y, z] != player.Team)
                actionFailed = true;

            BlockType blockType = blockList[x, y, z];
            if (!(blockType == BlockType.SolidBlue ||
                blockType == BlockType.SolidRed ||
                blockType == BlockType.BankBlue ||
                blockType == BlockType.BankRed ||
                blockType == BlockType.Jump ||
                blockType == BlockType.Ladder ||
                blockType == BlockType.Road ||
                blockType == BlockType.Shock ||
                blockType == BlockType.BeaconRed ||
                blockType == BlockType.BeaconBlue ||
                blockType == BlockType.TransBlue ||
                blockType == BlockType.TransRed ||
                blockType == BlockType.Teleporter))
                actionFailed = true;

            if (actionFailed)
            {
                // Decharge the player's gun.
                TriggerConstructionGunAnimation(player, -0.2f);
            }
            else
            {
                // Fire the player's gun.
                TriggerConstructionGunAnimation(player, 0.5f);

                // Remove the block.
                SetBlock(x, y, z, BlockType.None, PlayerTeam.None);
                PlaySound(InfiniminerSound.ConstructionGun, player.Position);
            }
        }

        public void TriggerConstructionGunAnimation(ServerPlayer player, float animationValue)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.TriggerConstructionGunAnimation);
            msgBuffer.Put(animationValue);
            player.Peer.Send(msgBuffer, NetChannel.ReliableInOrder1);
        }

        public void UseSignPainter(ServerPlayer player, Vector3 playerPosition, Vector3 playerHeading)
        {
            // If there's no surface within range, bail.
            Vector3 hitPoint = Vector3.Zero;
            Vector3 buildPoint = Vector3.Zero;
            if (!RayCollision(playerPosition, playerHeading, 4, 25, ref hitPoint, ref buildPoint))
                return;
            int x = GameConstants.ToBlock(hitPoint.X);
            int y = GameConstants.ToBlock(hitPoint.Y);
            int z = GameConstants.ToBlock(hitPoint.Z);

            if (blockList[x, y, z] == BlockType.Dirt)
            {
                SetBlock(x, y, z, BlockType.DirtSign, PlayerTeam.None);
                PlaySound(InfiniminerSound.ConstructionGun, player.Position);
            }
            else if (blockList[x, y, z] == BlockType.DirtSign)
            {
                SetBlock(x, y, z, BlockType.Dirt, PlayerTeam.None);
                PlaySound(InfiniminerSound.ConstructionGun, player.Position);
            }
        }

        public void DetonateAtPoint(int x, int y, int z)
        {
            // Remove the block that is detonating.
            SetBlock(x, y, z, BlockType.None, PlayerTeam.None);

            // Remove this from any explosive lists it may be in.
            foreach (ServerPlayer p in playerList.Values)
                p.ExplosiveList.Remove(new Vector3(x, y, z));

            // Detonate the block.
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        // Check that this is a sane block position.
                        if (!InBounds(x + dx, y + dy, z + dz))
                            continue;

                        // Chain reactions!
                        if (blockList[x + dx, y + dy, z + dz] == BlockType.Explosive)
                            DetonateAtPoint(x + dx, y + dy, z + dz);

                        // Detonation of normal blocks.
                        bool destroyBlock = false;
                        switch (blockList[x + dx, y + dy, z + dz])
                        {
                            case BlockType.Rock:
                            case BlockType.Dirt:
                            case BlockType.DirtSign:
                            case BlockType.Ore:
                            case BlockType.SolidRed:
                            case BlockType.SolidBlue:
                            case BlockType.TransRed:
                            case BlockType.TransBlue:
                            case BlockType.Ladder:
                            case BlockType.Shock:
                            case BlockType.Jump:
                            case BlockType.Explosive:
                            case BlockType.Lava:
                            case BlockType.Road:
                            case BlockType.Teleporter:
                                destroyBlock = true;
                                break;
                        }
                        if (destroyBlock)
                            SetBlock(x + dx, y + dy, z + dz, BlockType.None, PlayerTeam.None);
                    }

            // Anyone too close dies.
            foreach (ServerPlayer p in playerList.Values.ToList())
                if (p.Alive && Vector3.Distance(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), p.Position) < 3)
                    KillPlayer(p, "WAS KILLED IN AN EXPLOSION!");

            // Send off the explosion to clients.
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.TriggerExplosion);
            msgBuffer.Put(new Vector3(x, y, z));
            SendToAll(msgBuffer, NetChannel.ReliableUnordered);
        }

        public void UseDetonator(ServerPlayer player)
        {
            while (player.ExplosiveList.Count > 0)
            {
                Vector3 blockPos = player.ExplosiveList[0];
                int x = (int)blockPos.X;
                int y = (int)blockPos.Y;
                int z = (int)blockPos.Z;

                if (blockList[x, y, z] != BlockType.Explosive)
                    player.ExplosiveList.RemoveAt(0);
                else
                    DetonateAtPoint(x, y, z);
            }
        }

        public void DepositOre(ServerPlayer player)
        {
            uint depositAmount = Math.Min(50, player.Ore);
            player.Ore -= depositAmount;
            if (player.Team == PlayerTeam.Red)
                teamOreRed = Math.Min(teamOreRed + depositAmount, 9999);
            else
                teamOreBlue = Math.Min(teamOreBlue + depositAmount, 9999);
        }

        public void WithdrawOre(ServerPlayer player)
        {
            if (player.Team == PlayerTeam.Red)
            {
                uint withdrawAmount = Math.Min(player.OreMax - player.Ore, Math.Min(50, teamOreRed));
                player.Ore += withdrawAmount;
                teamOreRed -= withdrawAmount;
            }
            else
            {
                uint withdrawAmount = Math.Min(player.OreMax - player.Ore, Math.Min(50, teamOreBlue));
                player.Ore += withdrawAmount;
                teamOreBlue -= withdrawAmount;
            }
        }

        public void DepositCash(ServerPlayer player)
        {
            if (player.Cash <= 0)
                return;

            player.Score += player.Cash;

            if (!config.SandboxMode)
            {
                if (player.Team == PlayerTeam.Red)
                    teamCashRed += player.Cash;
                else
                    teamCashBlue += player.Cash;
                SendServerMessage("SERVER: " + player.Handle + " HAS EARNED $" + player.Cash + " FOR THE " + GetTeamName(player.Team) + " TEAM!");
            }

            PlaySound(InfiniminerSound.CashDeposit, player.Position);
            ConsoleWrite("DEPOSIT_CASH: " + player.Handle + ", " + player.Cash);

            player.Cash = 0;
            player.Weight = 0;

            foreach (ServerPlayer p in playerList.Values)
                SendResourceUpdate(p);
        }

        public string GetTeamName(PlayerTeam team)
        {
            switch (team)
            {
                case PlayerTeam.Red:
                    return "RED";
                case PlayerTeam.Blue:
                    return "BLUE";
            }
            return "";
        }

        void SendToAll(NetDataWriter msgBuffer, NetChannel channel, ServerPlayer except = null)
        {
            foreach (ServerPlayer p in playerList.Values)
                if (p != except)
                    p.Peer.Send(msgBuffer, channel);
        }

        public void SendServerMessage(string message)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.ChatMessage);
            msgBuffer.Put((byte)ChatMessageType.SayAll);
            msgBuffer.Put(message);
            SendToAll(msgBuffer, NetChannel.ReliableInOrder3);
        }

        // Lets a player know about their resources.
        public void SendResourceUpdate(ServerPlayer player)
        {
            // ore, cash, weight, max ore, max weight, team ore, red cash, blue cash, all uint
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.ResourceUpdate);
            msgBuffer.Put((uint)player.Ore);
            msgBuffer.Put((uint)player.Cash);
            msgBuffer.Put((uint)player.Weight);
            msgBuffer.Put((uint)player.OreMax);
            msgBuffer.Put((uint)player.WeightMax);
            msgBuffer.Put((uint)(player.Team == PlayerTeam.Red ? teamOreRed : teamOreBlue));
            msgBuffer.Put((uint)teamCashRed);
            msgBuffer.Put((uint)teamCashBlue);
            player.Peer.Send(msgBuffer, NetChannel.ReliableInOrder1);
        }

        public void SendCurrentMap(NetPeer client)
        {
            // The size comes first, so the client can set up its world.
            NetDataWriter worldInfo = NetExtensions.CreateMessage(InfiniminerMessage.WorldInfo);
            worldInfo.Put((ushort)MAPSIZE);
            client.Send(worldInfo, NetChannel.World);

            byte[] rows = new byte[16 * MAPSIZE];
            for (int x = 0; x < MAPSIZE; x++)
                for (int y = 0; y < MAPSIZE; y += 16)
                {
                    for (int dy = 0; dy < 16; dy++)
                        for (int z = 0; z < MAPSIZE; z++)
                            rows[dy * MAPSIZE + z] = (byte)blockList[x, y + dy, z];
                    NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.BlockBulkTransfer);
                    msgBuffer.Put((byte)x);
                    msgBuffer.Put((byte)y);
                    msgBuffer.Put(MapChunk.Compress(rows));
                    client.Send(msgBuffer, NetChannel.World);
                }
        }

        public void SendPlayerPing(uint playerId)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerPing);
            msgBuffer.Put(playerId);
            SendToAll(msgBuffer, NetChannel.ReliableUnordered);
        }

        public void SendPlayerUpdate(ServerPlayer player)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerUpdate);
            msgBuffer.Put((uint)player.ID);
            msgBuffer.Put(player.Position);
            msgBuffer.Put(player.Heading);
            msgBuffer.Put((byte)player.Tool);

            if (player.QueueAnimationBreak)
            {
                player.QueueAnimationBreak = false;
                msgBuffer.Put(false);
            }
            else
                msgBuffer.Put(player.UsingTool);

            // Score is sent in hundreds. (The original cast the score to ushort *before* dividing,
            // which wrapped scores above $65,535.)
            msgBuffer.Put((ushort)Math.Min(player.Score / 100, ushort.MaxValue));

            SendToAll(msgBuffer, NetChannel.UnreliableInOrder1);
        }

        public void SendSetBeacon(Vector3 position, string text, PlayerTeam team)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.SetBeacon);
            msgBuffer.Put(position);
            msgBuffer.Put(text);
            msgBuffer.Put((byte)team);
            SendToAll(msgBuffer, NetChannel.ReliableInOrder2);
        }

        public void SendPlayerJoined(ServerPlayer player)
        {
            NetDataWriter msgBuffer;

            // Let this player know about other players.
            foreach (ServerPlayer p in playerList.Values)
            {
                msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerJoined);
                msgBuffer.Put((uint)p.ID);
                msgBuffer.Put(p.Handle);
                msgBuffer.Put(p == player);
                msgBuffer.Put(p.Alive);
                player.Peer.Send(msgBuffer, NetChannel.ReliableInOrder2);

                msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerSetTeam);
                msgBuffer.Put((uint)p.ID);
                msgBuffer.Put((byte)p.Team);
                player.Peer.Send(msgBuffer, NetChannel.ReliableInOrder2);
            }

            // Let this player know about all placed beacons.
            foreach (KeyValuePair<Vector3, Beacon> bPair in beaconList)
            {
                Vector3 position = bPair.Key;
                position.Y += 1; // beacon is shown a block below its actually position to make altitude show up right
                msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.SetBeacon);
                msgBuffer.Put(position);
                msgBuffer.Put(bPair.Value.ID);
                msgBuffer.Put((byte)bPair.Value.Team);
                player.Peer.Send(msgBuffer, NetChannel.ReliableInOrder2);
            }

            // Let other players know about this player.
            msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerJoined);
            msgBuffer.Put((uint)player.ID);
            msgBuffer.Put(player.Handle);
            msgBuffer.Put(false);
            msgBuffer.Put(player.Alive);
            SendToAll(msgBuffer, NetChannel.ReliableInOrder2, player);

            // Send this out just incase someone is joining at the last minute.
            if (winningTeam != PlayerTeam.None)
                BroadcastGameOver();

            // Send out a chat message.
            msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.ChatMessage);
            msgBuffer.Put((byte)ChatMessageType.SayAll);
            msgBuffer.Put(player.Handle + " HAS JOINED THE ADVENTURE!");
            SendToAll(msgBuffer, NetChannel.ReliableInOrder3);
        }

        // Tells one player (or everyone) the time left and the winning cash.
        public void SendMatchInfo(ServerPlayer player)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.MatchInfo);
            msgBuffer.Put(SecondsLeft);
            msgBuffer.Put(WinningCash);
            if (player != null)
                player.Peer.Send(msgBuffer, NetChannel.ReliableInOrder1);
            else
                SendToAll(msgBuffer, NetChannel.ReliableInOrder1);
        }

        public void BroadcastGameOver()
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.GameOver);
            msgBuffer.Put((byte)winningTeam);
            SendToAll(msgBuffer, NetChannel.ReliableUnordered);
        }

        public void SendPlayerLeft(ServerPlayer player, string reason)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerLeft);
            msgBuffer.Put((uint)player.ID);
            SendToAll(msgBuffer, NetChannel.ReliableInOrder2, player);

            // Send out a chat message.
            msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.ChatMessage);
            msgBuffer.Put((byte)ChatMessageType.SayAll);
            msgBuffer.Put(player.Handle + " " + reason);
            SendToAll(msgBuffer, NetChannel.ReliableInOrder3);
        }

        public void SendPlayerSetTeam(ServerPlayer player)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerSetTeam);
            msgBuffer.Put((uint)player.ID);
            msgBuffer.Put((byte)player.Team);
            SendToAll(msgBuffer, NetChannel.ReliableInOrder2);
        }

        public void SendPlayerDead(ServerPlayer player)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerDead);
            msgBuffer.Put((uint)player.ID);
            SendToAll(msgBuffer, NetChannel.ReliableInOrder2);
        }

        public void SendPlayerAlive(ServerPlayer player)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlayerAlive);
            msgBuffer.Put((uint)player.ID);
            SendToAll(msgBuffer, NetChannel.ReliableInOrder2);
        }

        public void PlaySound(InfiniminerSound sound, Vector3 position)
        {
            NetDataWriter msgBuffer = NetExtensions.CreateMessage(InfiniminerMessage.PlaySound);
            msgBuffer.Put((byte)sound);
            msgBuffer.Put(true);
            msgBuffer.Put(position);
            SendToAll(msgBuffer, NetChannel.ReliableUnordered);
        }
    }
}
