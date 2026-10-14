using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework.Input;

namespace Infiniminer
{
    // Actions that can be bound to keys. Escape (menu, and Escape+Y / Escape+K) is fixed.
    public enum GameAction
    {
        MoveForward,
        MoveBack,
        MoveLeft,
        MoveRight,
        Jump,
        NextTool,
        NextBlock,
        Ping,
        SayAll,
        SayTeam,
        ChangeClass,
        ChangeTeam,
        Scoreboard,
        Help,
        DepositOre,
        WithdrawOre,
    }

    // Client settings, stored in client.config.txt.
    public class ClientSettings
    {
        public string Handle = "Player";
        public int Width = 1024;
        public int Height = 768;
        public bool Fullscreen = false;
        public bool Pretty = true;
        public bool ShowFps = true;
        public bool InvertY = false;
        public float Volume = 1.0f;
        public bool NoSound = false;
        public int MouseSensitivity = 5;    // 1-20; 5 matches the original game
        public int FieldOfView = 70;        // degrees
        public int UiScale = 0;             // 0 = automatic
        public bool ColorBlind = false;
        public int ControllerSensitivity = 10;  // 1-20: right stick look speed
        public bool Vibration = true;
        public bool DeckSetupDone = false;  // Steam Deck defaults applied once (1280x800)
        public string ServerListUrl = "";

        // Servers joined recently, newest first: the address as typed or listed ("host:port") and a name.
        public List<(string Address, string Name)> RecentServers = new List<(string, string)>();
        public const int MaxRecentServers = 5;

        // HOST GAME settings (the server name defaults to "<handle>'S GAME").
        public string HostName = "";
        public int HostMapSize = GameConstants.MapSize;
        public int HostMaxPlayers = 16;
        public int HostTimeLimit = 0;       // minutes; 0 for none
        public bool HostLava = true;
        public bool HostHomeBases = true;
        public bool HostSandbox = false;
        public int HostBots = 2;            // computer players
        public bool HostInternet = false;   // forward the port on the router (UPnP)
        public Dictionary<GameAction, Keys> KeyBindings = DefaultKeyBindings();

        public const int MinSensitivity = 1, MaxSensitivity = 20;
        public const int MinFieldOfView = 60, MaxFieldOfView = 110;
        public const int MaxUiScale = 3;

        public static Dictionary<GameAction, Keys> DefaultKeyBindings()
        {
            return new Dictionary<GameAction, Keys>
            {
                [GameAction.MoveForward] = Keys.W,
                [GameAction.MoveBack] = Keys.S,
                [GameAction.MoveLeft] = Keys.A,
                [GameAction.MoveRight] = Keys.D,
                [GameAction.Jump] = Keys.Space,
                [GameAction.NextTool] = Keys.E,
                [GameAction.NextBlock] = Keys.R,
                [GameAction.Ping] = Keys.Q,
                [GameAction.SayAll] = Keys.Y,
                [GameAction.SayTeam] = Keys.U,
                [GameAction.ChangeClass] = Keys.M,
                [GameAction.ChangeTeam] = Keys.N,
                [GameAction.Scoreboard] = Keys.Tab,
                [GameAction.Help] = Keys.F1,
                [GameAction.DepositOre] = Keys.D1,
                [GameAction.WithdrawOre] = Keys.D2,
            };
        }

        // On a Steam Deck (Steam sets SteamDeck=1), the first run opens at the Deck's 1280x800. In Game Mode the
        // window fills the screen anyway; in Desktop Mode OPTIONS > FULLSCREEN is there. Returns true if anything
        // changed. Later changes in OPTIONS are kept.
        public bool ApplySteamDeckDefaults(bool onSteamDeck)
        {
            if (!onSteamDeck || DeckSetupDone)
                return false;
            Fullscreen = false;
            Width = 1280;
            Height = 800;
            DeckSetupDone = true;
            return true;
        }

        // Remembers a server we joined; it moves to the top of the list.
        public void AddRecentServer(string address, string name)
        {
            address = TextUtil.Sanitize(address).Replace("|", "").Trim();
            name = TextUtil.Sanitize(name).Replace("|", "").Trim();
            if (address == "")
                return;
            RecentServers.RemoveAll(r => string.Equals(r.Address, address, StringComparison.OrdinalIgnoreCase));
            RecentServers.Insert(0, (address, name == "" ? address : name));
            if (RecentServers.Count > MaxRecentServers)
                RecentServers.RemoveRange(MaxRecentServers, RecentServers.Count - MaxRecentServers);
        }

        public string HostServerName
        {
            get { return HostName != "" ? HostName : (Handle + "'s game").ToUpperInvariant(); }
        }

        // The server settings for HOST GAME.
        public ServerConfig HostConfig()
        {
            return new ServerConfig
            {
                ServerName = HostServerName,
                MapSize = HostMapSize,
                MaxPlayers = (uint)HostMaxPlayers,
                TimeLimitMinutes = HostTimeLimit,
                IncludeLava = HostLava,
                HomeBases = HostHomeBases,
                SandboxMode = HostSandbox,
                AutoBalance = true,
                Bots = HostBots,
                Upnp = HostInternet,
                // Open to the internet, the game is also announced on the public server list, if one is set.
                PublicServer = HostInternet && ServerListUrl != "",
                ServerListUrl = ServerListUrl,
            };
        }

        public Keys Key(GameAction action)
        {
            return KeyBindings.TryGetValue(action, out Keys key) ? key : DefaultKeyBindings()[action];
        }

        public bool IsDown(KeyboardState keyState, GameAction action)
        {
            return keyState.IsKeyDown(Key(action));
        }

        // Binds a key to an action; an action that already had that key takes this action's old key.
        public void Bind(GameAction action, Keys key)
        {
            Keys oldKey = Key(action);
            foreach (GameAction other in KeyBindings.Keys.ToList())
                if (other != action && KeyBindings[other] == key)
                    KeyBindings[other] = oldKey;
            KeyBindings[action] = key;
        }

        static string ConfigKey(GameAction action)
        {
            return "key_" + action.ToString().ToLowerInvariant();
        }

        public static ClientSettings Load(string path)
        {
            DatafileLoader dataFile = new DatafileLoader(path);
            ClientSettings settings = new ClientSettings();
            settings.Handle = dataFile.GetString("handle", settings.Handle);
            settings.Width = Math.Max(320, dataFile.GetInt("width", settings.Width));
            settings.Height = Math.Max(240, dataFile.GetInt("height", settings.Height));
            settings.Fullscreen = dataFile.GetBool("fullscreen", settings.Fullscreen);
            settings.Pretty = dataFile.GetBool("pretty", settings.Pretty);
            settings.ShowFps = dataFile.GetBool("showfps", settings.ShowFps);
            settings.InvertY = dataFile.GetBool("yinvert", settings.InvertY);
            settings.Volume = Math.Clamp(dataFile.GetFloat("volume", settings.Volume), 0, 1);
            settings.NoSound = dataFile.GetBool("nosound", settings.NoSound);
            settings.MouseSensitivity = Math.Clamp(dataFile.GetInt("sensitivity", settings.MouseSensitivity), MinSensitivity, MaxSensitivity);
            settings.FieldOfView = Math.Clamp(dataFile.GetInt("fov", settings.FieldOfView), MinFieldOfView, MaxFieldOfView);
            settings.UiScale = Math.Clamp(dataFile.GetInt("uiscale", settings.UiScale), 0, MaxUiScale);
            settings.ColorBlind = dataFile.GetBool("colorblind", settings.ColorBlind);
            settings.ControllerSensitivity = Math.Clamp(dataFile.GetInt("controllersensitivity", settings.ControllerSensitivity), MinSensitivity, MaxSensitivity);
            settings.Vibration = dataFile.GetBool("vibration", settings.Vibration);
            settings.DeckSetupDone = dataFile.GetBool("deck_setup", settings.DeckSetupDone);
            settings.ServerListUrl = dataFile.GetString("serverlist", settings.ServerListUrl);
            for (int i = MaxRecentServers; i >= 1; i--)
            {
                string[] recent = dataFile.GetString("recent" + i, "").Split('|', 2);
                if (recent[0].Trim() != "")
                    settings.AddRecentServer(recent[0].Trim(), recent.Length > 1 ? recent[1].Trim() : recent[0].Trim());
            }
            settings.HostName = dataFile.GetString("host_name", settings.HostName);
            int mapSize = dataFile.GetInt("host_mapsize", settings.HostMapSize);
            settings.HostMapSize = GameConstants.MapSizes.Contains(mapSize) ? mapSize : GameConstants.MapSize;
            settings.HostMaxPlayers = Math.Clamp(dataFile.GetInt("host_maxplayers", settings.HostMaxPlayers), 2, 32);
            settings.HostTimeLimit = Math.Clamp(dataFile.GetInt("host_timelimit", settings.HostTimeLimit), 0, 120);
            settings.HostLava = dataFile.GetBool("host_lava", settings.HostLava);
            settings.HostHomeBases = dataFile.GetBool("host_homebases", settings.HostHomeBases);
            settings.HostSandbox = dataFile.GetBool("host_sandbox", settings.HostSandbox);
            settings.HostBots = Math.Clamp(dataFile.GetInt("host_bots", settings.HostBots), 0, 16);
            settings.HostInternet = dataFile.GetBool("host_internet", settings.HostInternet);
            foreach (GameAction action in Enum.GetValues<GameAction>())
                if (Enum.TryParse(dataFile.GetString(ConfigKey(action), ""), true, out Keys key) && key != Keys.None)
                    settings.Bind(action, key);
            return settings;
        }

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            void Line(string text = "") => sb.Append(text).Append('\n');
            string Bool(bool value) => value ? "true" : "false";

            Line("# This value specifies your name - please change it to something unique!");
            Line("handle = " + Handle);
            Line();
            Line("width = " + Width.ToString(CultureInfo.InvariantCulture));
            Line("height = " + Height.ToString(CultureInfo.InvariantCulture));
            Line("fullscreen = " + Bool(Fullscreen));
            Line();
            Line("# If this flag is set to true, pretty mode will be enabled, which turns on bloom lighting!");
            Line("# If your performance is bad, try turning this off.");
            Line("pretty = " + Bool(Pretty));
            Line();
            Line("showfps = " + Bool(ShowFps));
            Line("yinvert = " + Bool(InvertY));
            Line();
            Line("# Mouse sensitivity from 1 to 20 (5 is the original game's), field of view in degrees,");
            Line("# and interface scale (0 picks one automatically from the window size).");
            Line("sensitivity = " + MouseSensitivity.ToString(CultureInfo.InvariantCulture));
            Line("fov = " + FieldOfView.ToString(CultureInfo.InvariantCulture));
            Line("uiscale = " + UiScale.ToString(CultureInfo.InvariantCulture));
            Line();
            Line("# Colour-blind friendly team colours, and [R]/[B] tags on team chat.");
            Line("colorblind = " + Bool(ColorBlind));
            Line();
            Line("# Controller: right stick look speed from 1 to 20, and whether the controller vibrates.");
            Line("controllersensitivity = " + ControllerSensitivity.ToString(CultureInfo.InvariantCulture));
            Line("vibration = " + Bool(Vibration));
            Line("deck_setup = " + Bool(DeckSetupDone));
            Line();
            Line("# This value specifies the override volume multiplier, for people who find the sounds too loud.");
            Line("volume = " + Volume.ToString("0.0#", CultureInfo.InvariantCulture));
            Line();
            Line("# If this flag is true, all sound for the game will be disabled.");
            Line("nosound = " + Bool(NoSound));
            Line();
            Line("# Public server list to show in the server browser (LAN servers are always found automatically).");
            Line("# Example: serverlist = http://example.com/plain");
            Line("serverlist = " + ServerListUrl);
            Line();
            Line("# Servers joined recently (address | name), shown in the server list.");
            for (int i = 0; i < RecentServers.Count; i++)
                Line("recent" + (i + 1) + " = " + RecentServers[i].Address + " | " + RecentServers[i].Name);
            Line();
            Line("# The last settings used for HOST GAME.");
            Line("host_name = " + HostName);
            Line("host_mapsize = " + HostMapSize.ToString(CultureInfo.InvariantCulture));
            Line("host_maxplayers = " + HostMaxPlayers.ToString(CultureInfo.InvariantCulture));
            Line("host_timelimit = " + HostTimeLimit.ToString(CultureInfo.InvariantCulture));
            Line("host_lava = " + Bool(HostLava));
            Line("host_homebases = " + Bool(HostHomeBases));
            Line("host_sandbox = " + Bool(HostSandbox));
            Line("host_bots = " + HostBots.ToString(CultureInfo.InvariantCulture));
            Line("host_internet = " + Bool(HostInternet));
            Line();
            Line("# Key bindings (names from https://docs.monogame.net/api/Microsoft.Xna.Framework.Input.Keys.html).");
            foreach (GameAction action in Enum.GetValues<GameAction>())
                Line(ConfigKey(action) + " = " + Key(action));
            File.WriteAllText(path, sb.ToString());
        }

        // Display names for actions and keys in the controls menu.
        public static string ActionName(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveForward: return "MOVE FORWARD";
                case GameAction.MoveBack: return "MOVE BACK";
                case GameAction.MoveLeft: return "MOVE LEFT";
                case GameAction.MoveRight: return "MOVE RIGHT";
                case GameAction.Jump: return "JUMP";
                case GameAction.NextTool: return "NEXT TOOL";
                case GameAction.NextBlock: return "NEXT BLOCK";
                case GameAction.Ping: return "PING TEAM";
                case GameAction.SayAll: return "CHAT (ALL)";
                case GameAction.SayTeam: return "CHAT (TEAM)";
                case GameAction.ChangeClass: return "CHANGE CLASS";
                case GameAction.ChangeTeam: return "CHANGE TEAM";
                case GameAction.Scoreboard: return "SCOREBOARD";
                case GameAction.Help: return "HELP";
                case GameAction.DepositOre: return "DEPOSIT ORE";
                case GameAction.WithdrawOre: return "WITHDRAW ORE";
            }
            return action.ToString().ToUpperInvariant();
        }

        public static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int)(key - Keys.D0)).ToString();
            switch (key)
            {
                case Keys.OemTilde: return "~";
                case Keys.OemMinus: return "-";
                case Keys.OemPlus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemComma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemPipe: return "\\";
                case Keys.LeftShift: return "LEFT SHIFT";
                case Keys.RightShift: return "RIGHT SHIFT";
                case Keys.LeftControl: return "LEFT CTRL";
                case Keys.RightControl: return "RIGHT CTRL";
                case Keys.LeftAlt: return "LEFT ALT";
                case Keys.RightAlt: return "RIGHT ALT";
            }
            return key.ToString().ToUpperInvariant();
        }
    }
}
