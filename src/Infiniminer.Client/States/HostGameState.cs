using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Infiniminer.States
{
    // HOST GAME: pick the settings and start a server inside the game, then join it.
    public class HostGameState : MenuListState
    {
        static readonly int[] TimeLimits = { 0, 10, 20, 30, 60 };
        bool editingName = false;
        string status = "";

        ClientSettings Settings
        {
            get { return Game.Settings; }
        }

        protected override string Title
        {
            get { return "HOST GAME"; }
        }

        protected override bool Busy
        {
            get { return editingName || Starting; }
        }

        bool Starting
        {
            get { return Game.HostedServer != null && !Game.HostedServer.Ready && Game.HostedServer.Error == null; }
        }

        protected override string Hint
        {
            get
            {
                if (editingName)
                    return "TYPE A NAME FOR THE GAME, THEN PRESS ENTER";
                if (status != "")
                    return status;
                return "PLAYERS ON YOUR NETWORK CAN JOIN FROM THEIR SERVER LIST";
            }
        }

        public override void OnEnter(Type oldState)
        {
            base.OnEnter(oldState);
            Rebuild();
            selected = rows.Count - 2;  // START GAME
        }

        static string OnOff(bool value)
        {
            return value ? "ON" : "OFF";
        }

        void Changed()
        {
            Game.SaveSettings();
            _P.PlaySound(InfiniminerSound.ClickLow);
        }

        protected override void BuildRows()
        {
            rows = new List<Row>
            {
                new Row
                {
                    Label = "NAME",
                    Value = () => editingName ? Settings.HostName + "_" : Settings.HostServerName,
                    Activate = () =>
                    {
                        editingName = !editingName;
                        if (editingName)
                            Settings.HostName = Settings.HostServerName;
                        else
                            FinishNameEdit();
                    },
                },
                new Row
                {
                    Label = "WORLD SIZE",
                    Value = () => Settings.HostMapSize + " X " + Settings.HostMapSize,
                    Change = direction =>
                    {
                        int index = Array.IndexOf(GameConstants.MapSizes, Settings.HostMapSize);
                        Settings.HostMapSize = GameConstants.MapSizes[Cycle(Math.Max(0, index), direction, GameConstants.MapSizes.Length)];
                        Changed();
                    },
                },
                new Row
                {
                    Label = "MAX PLAYERS",
                    Value = () => Settings.HostMaxPlayers.ToString(),
                    Change = direction => { Settings.HostMaxPlayers = Math.Clamp(Settings.HostMaxPlayers + 2 * direction, 2, 32); Changed(); },
                },
                new Row
                {
                    Label = "COMPUTER PLAYERS",
                    Value = () => Settings.HostBots == 0 ? "NONE" : Settings.HostBots.ToString(),
                    Change = direction => { Settings.HostBots = Math.Clamp(Settings.HostBots + direction, 0, 8); Changed(); },
                },
                new Row
                {
                    Label = "TIME LIMIT",
                    Value = () => Settings.HostTimeLimit == 0 ? "NONE" : Settings.HostTimeLimit + " MINUTES",
                    Change = direction =>
                    {
                        int index = Math.Max(0, Array.IndexOf(TimeLimits, Settings.HostTimeLimit));
                        Settings.HostTimeLimit = TimeLimits[Cycle(index, direction, TimeLimits.Length)];
                        Changed();
                    },
                },
                new Row
                {
                    Label = "LAVA",
                    Value = () => OnOff(Settings.HostLava),
                    Change = direction => { Settings.HostLava = !Settings.HostLava; Changed(); },
                },
                new Row
                {
                    Label = "HOME BASES",
                    Value = () => OnOff(Settings.HostHomeBases),
                    Change = direction => { Settings.HostHomeBases = !Settings.HostHomeBases; Changed(); },
                },
                new Row
                {
                    Label = "INTERNET PLAYERS",
                    Value = () => Settings.HostInternet ? "OPEN ROUTER (UPNP)" : "LAN ONLY",
                    Change = direction => { Settings.HostInternet = !Settings.HostInternet; Changed(); },
                },
                new Row
                {
                    Label = "SANDBOX (FREE BUILDING)",
                    Value = () => OnOff(Settings.HostSandbox),
                    Change = direction => { Settings.HostSandbox = !Settings.HostSandbox; Changed(); },
                },
                new Row { Label = "START GAME", Activate = StartGame },
                new Row { Label = "BACK", Activate = Back },
            };
        }

        void FinishNameEdit()
        {
            editingName = false;
            Settings.HostName = TextUtil.Sanitize(Settings.HostName).Trim();
            // Left as the default, the name keeps following the player's name.
            if (Settings.HostName == (Settings.Handle + "'s game").ToUpperInvariant())
                Settings.HostName = "";
            Changed();
        }

        void StartGame()
        {
            if (Starting)
                return;
            _P.PlaySound(InfiniminerSound.ClickHigh);
            Game.StopHostedServer();
            status = "CREATING THE WORLD...";
            Game.HostedServer = HostedServer.Start(Settings.HostConfig(), Game.DataDirectory);
        }

        protected override void Back()
        {
            if (Starting)
                return;
            if (editingName)
                FinishNameEdit();
            _P.PlaySound(InfiniminerSound.ClickHigh);
            nextState = typeof(ServerBrowserState);
        }

        public override Type OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            HostedServer hosted = Game.HostedServer;
            if (status != "" && hosted != null)
            {
                if (hosted.Error != null)
                {
                    status = "ERROR: " + hosted.Error;
                    Game.HostedServer = null;
                }
                else if (hosted.Ready && nextState == null)
                {
                    Game.JoinGame(hosted.EndPoint);
                    nextState = typeof(LoadingState);
                }
            }
            return base.OnUpdate(gameTime, keyState, mouseState);
        }

        public override void OnTextInput(char character)
        {
            if (editingName && Settings.HostName.Length < 24)
                Settings.HostName += character;
        }

        public override void OnKeyDown(Keys key)
        {
            if (editingName)
            {
                if (key == Keys.Back && Settings.HostName.Length > 0)
                    Settings.HostName = Settings.HostName.Substring(0, Settings.HostName.Length - 1);
                else if (key == Keys.Enter || key == Keys.Escape)
                    FinishNameEdit();
                return;
            }
            if (Starting)
                return;
            base.OnKeyDown(key);
        }
    }
}
