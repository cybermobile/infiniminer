using System.Net;
using System.Net.Sockets;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StateMasher;

namespace Infiniminer.States
{
    // The multiplayer screen: QUICK PLAY and HOST A GAME, then a table of the games found (on the LAN, on the public
    // list, and recent servers joined by address) with their mode, world, players, bots and ping, and a line about the
    // selected one. The list refreshes itself every few seconds.
    public class ServerBrowserState : State
    {
        Texture2D texMenu;
        Rectangle drawRect;
        Type nextState = null;
        List<ServerInformation> serverList = new List<ServerInformation>();
        SpriteFontBase uiFont;
        bool directConnectIPEnter = false;
        int highlighted = -1;           // the keyboard/controller/mouse selection (-1: none yet)
        Point lastMouse;

        const int ActionY = 78, HeaderY = 122, ListTop = 150, RowHeight = 24, ListBottom = 582, DetailY = 596;
        const int MaxServerRows = (ListBottom - ListTop) / RowHeight;
        const double AutoRefreshSeconds = 5, SearchSeconds = 1.5;

        // Columns of the table (x on the 1024-wide menu canvas).
        const int ColType = 34, ColName = 104, ColMode = 446, ColWorld = 612, ColPlayers = 702, ColBots = 800, ColPing = 880;

        enum RowKind { QuickPlay, Host, Server, Recent }

        sealed class BrowserRow
        {
            public RowKind Kind;
            public string Text;
            public ServerInformation Server;
            public (string address, string name) Recent;
            public Rectangle Bounds;
            public Action Choose;
        }
        List<BrowserRow> rows = new List<BrowserRow>();

        bool IsOwn(ServerInformation server)
        {
            return Game.IsHosting && server.instanceId != null && server.instanceId == Game.HostedServer.InstanceId;
        }

        void BuildRows()
        {
            rows = new List<BrowserRow>
            {
                new BrowserRow { Kind = RowKind.QuickPlay, Text = "[ QUICK PLAY ]", Choose = QuickPlay },
                new BrowserRow { Kind = RowKind.Host, Text = Game.IsHosting ? "[ END YOUR GAME ]" : "[ HOST A GAME ]", Choose = HostOrEnd },
            };
            int quickWidth = (int)uiFont.MeasureString(rows[0].Text).X + 30, hostWidth = (int)uiFont.MeasureString(rows[1].Text).X + 30;
            rows[0].Bounds = new Rectangle(InfiniminerGame.MenuWidth / 2 - 30 - quickWidth, ActionY - 5, quickWidth, 26);
            rows[1].Bounds = new Rectangle(InfiniminerGame.MenuWidth / 2 + 30, ActionY - 5, hostWidth, 26);

            int y = ListTop;
            // Your own game first, then games with people in them, nearest first.
            foreach (ServerInformation server in serverList.OrderByDescending(IsOwn).ThenByDescending(s => s.People > 0)
                         .ThenBy(s => s.PingMs < 0 ? int.MaxValue : s.PingMs))
            {
                if (rows.Count - 2 >= MaxServerRows)
                    break;
                ServerInformation listed = server;
                rows.Add(new BrowserRow
                {
                    Kind = RowKind.Server,
                    Server = server,
                    Bounds = new Rectangle(ColType - 10, y - 3, InfiniminerGame.MenuWidth - 2 * (ColType - 10), RowHeight),
                    Choose = () => Join(listed),
                });
                y += RowHeight;
            }
            HashSet<string> found = new HashSet<string>(serverList.Select(server => server.ipEndPoint.ToString()));
            foreach (var recent in Game.Settings.RecentServers)
            {
                if (rows.Count - 2 >= MaxServerRows || found.Contains(recent.Address))
                    continue;
                var (address, name) = recent;
                rows.Add(new BrowserRow
                {
                    Kind = RowKind.Recent,
                    Recent = (address, name),
                    Bounds = new Rectangle(ColType - 10, y - 3, InfiniminerGame.MenuWidth - 2 * (ColType - 10), RowHeight),
                    Choose = () => JoinAddress(address, name),
                });
                y += RowHeight;
            }
            if (highlighted >= rows.Count)
                highlighted = rows.Count - 1;
        }

        void HostOrEnd()
        {
            _P.PlaySound(InfiniminerSound.ClickHigh);
            Game.Rejoin.Cancel();
            quickPlay = QuickPlayStep.None;
            if (Game.IsHosting)
            {
                Game.StopHostedServer();
                Refresh();
            }
            else
                nextState = typeof(HostGameState);
        }

        // QUICK PLAY: join the best open game (people playing, then nearest), or start one with computer players (the
        // HOST GAME settings) if there isn't one. It waits a moment for LAN servers to answer first.
        enum QuickPlayStep { None, Searching, Starting }
        QuickPlayStep quickPlay = QuickPlayStep.None;

        void QuickPlay()
        {
            _P.PlaySound(InfiniminerSound.ClickHigh);
            Game.Rejoin.Cancel();
            Game.StatusMessage = "";
            quickPlay = QuickPlayStep.Searching;
        }

        void UpdateQuickPlay()
        {
            if (quickPlay == QuickPlayStep.Searching && Game.SecondsSinceDiscovery >= SearchSeconds && !Game.FetchingPublicList)
            {
                ServerInformation best = ServerInformation.PickForQuickPlay(serverList, IsOwn);
                if (best != null)
                {
                    quickPlay = QuickPlayStep.None;
                    Join(best);
                    return;
                }
                quickPlay = QuickPlayStep.Starting;
                Game.StopHostedServer();
                Game.HostedServer = HostedServer.Start(Game.Settings.HostConfig(), Game.DataDirectory);
            }
            if (quickPlay == QuickPlayStep.Starting && Game.HostedServer != null)
            {
                if (Game.HostedServer.Error != null)
                {
                    Game.StatusMessage = "ERROR: " + Game.HostedServer.Error;
                    Game.HostedServer = null;
                    quickPlay = QuickPlayStep.None;
                }
                else if (Game.HostedServer.Ready && nextState == null)
                {
                    quickPlay = QuickPlayStep.None;
                    Game.JoinGame(Game.HostedServer.EndPoint);
                    nextState = typeof(LoadingState);
                }
            }
        }

        void ChooseRow(int row)
        {
            if (quickPlay != QuickPlayStep.None)
                return;
            if (row >= 0 && row < rows.Count)
                rows[row].Choose();
        }

        string directConnectIP = "";

        ClickRegion[] clkMenuServer = new ClickRegion[3] {
            new ClickRegion(new Rectangle(763,713,243,42), "refresh"),
            new ClickRegion(new Rectangle(0,713,425,42), "direct"),
            new ClickRegion(new Rectangle(440,713,300,42), "options")
        };

        public override void OnEnter(Type oldState)
        {
            _SM.IsMouseVisible = true;
            highlighted = -1;
            quickPlay = QuickPlayStep.None;
            Game.ResetPropertyBag();
            _P = _SM.propertyBag;

            texMenu = Game.Assets.Texture("menus/tex_menu_server");

            // Menus are drawn on a 1024x768 canvas that is scaled to fit the window.
            drawRect = new Rectangle(0, 0, 1024, 1024);

            uiFont = Game.Assets.UiFont;

            Game.StartServerDiscovery();

            // Started with --connect: join that server once.
            if (Game.ConnectOnStart != null)
            {
                string address = Game.ConnectOnStart;
                Game.ConnectOnStart = null;
                JoinAddress(address, address);
            }
        }

        public override void OnLeave(Type newState)
        {
            quickPlay = QuickPlayStep.None;
        }

        public override Type OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            // Pick up replies to our discovery requests, and ask again every few seconds so the list stays current.
            Game.UpdateNetwork(gameTime);
            if (Game.SecondsSinceDiscovery > AutoRefreshSeconds && !directConnectIPEnter)
                Game.StartServerDiscovery(clear: false);
            serverList = Game.GetServerList();
            BuildRows();
            UpdateQuickPlay();

            // The mouse picks the row under it when it moves.
            Point mouse = Game.ToMenu(mouseState.X, mouseState.Y);
            if (mouse != lastMouse && _SM.WindowHasFocus())
            {
                lastMouse = mouse;
                int under = rows.FindIndex(r => r.Bounds.Contains(mouse));
                if (under >= 0)
                    highlighted = under;
            }

            // Rejoining a server that is restarting with a new map.
            if (Game.Rejoin.ShouldAttempt(gameTime.TotalGameTime.TotalSeconds) && nextState == null)
            {
                Game.JoinGame(Game.Rejoin.Target);
                nextState = typeof(LoadingState);
            }

            return nextState;
        }

        public override void OnRenderAtEnter(GraphicsDevice graphicsDevice)
        {

        }

        static readonly Color Dim = new Color(110, 110, 110), Header = new Color(150, 150, 150), Highlight = new Color(60, 60, 60);

        // Cuts text to fit a column.
        string Fit(string text, int width)
        {
            if (uiFont.MeasureString(text).X <= width)
                return text;
            while (text.Length > 1 && uiFont.MeasureString(text + "...").X > width)
                text = text.Substring(0, text.Length - 1);
            return text + "...";
        }

        void DrawCentered(SpriteBatch spriteBatch, string text, int y, Color color)
        {
            Vector2 size = uiFont.MeasureString(text);
            spriteBatch.DrawString(uiFont, text, new Vector2((int)(InfiniminerGame.MenuWidth / 2 - size.X / 2), y), color);
        }

        public override void OnRenderAtUpdate(GraphicsDevice graphicsDevice, GameTime gameTime)
        {
            SpriteBatch spriteBatch = _SM.SpriteBatch;
            Texture2D white = Game.Assets.WhitePixel;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, Game.MenuSampler, null, null, null, Game.MenuTransform);
            spriteBatch.Draw(texMenu, drawRect, Color.White);

            // QUICK PLAY and HOST A GAME.
            for (int i = 0; i < 2 && i < rows.Count; i++)
            {
                BrowserRow row = rows[i];
                spriteBatch.Draw(white, row.Bounds, i == highlighted ? new Color(70, 70, 70) : new Color(30, 30, 30));
                Vector2 size = uiFont.MeasureString(row.Text);
                Color color = i == 0 ? new Color(255, 200, 40) : InfiniminerGame.IM_BLUE;
                spriteBatch.DrawString(uiFont, row.Text, new Vector2((int)(row.Bounds.Center.X - size.X / 2), ActionY), color);
            }

            // The table.
            spriteBatch.DrawString(uiFont, "WHERE", new Vector2(ColType, HeaderY), Header);
            spriteBatch.DrawString(uiFont, "SERVER", new Vector2(ColName, HeaderY), Header);
            spriteBatch.DrawString(uiFont, "MODE", new Vector2(ColMode, HeaderY), Header);
            spriteBatch.DrawString(uiFont, "WORLD", new Vector2(ColWorld, HeaderY), Header);
            spriteBatch.DrawString(uiFont, "PLAYERS", new Vector2(ColPlayers, HeaderY), Header);
            spriteBatch.DrawString(uiFont, "BOTS", new Vector2(ColBots, HeaderY), Header);
            spriteBatch.DrawString(uiFont, "PING", new Vector2(ColPing, HeaderY), Header);
            spriteBatch.Draw(white, new Rectangle(ColType - 10, HeaderY + 20, InfiniminerGame.MenuWidth - 2 * (ColType - 10), 1), Dim);

            for (int i = 2; i < rows.Count; i++)
            {
                BrowserRow row = rows[i];
                int y = row.Bounds.Y + 3;
                if (i == highlighted)
                    spriteBatch.Draw(white, row.Bounds, Highlight);
                if (row.Kind == RowKind.Recent)
                {
                    spriteBatch.DrawString(uiFont, "RECENT", new Vector2(ColType, y), Dim);
                    string name = row.Recent.name.ToUpperInvariant() + (row.Recent.name != row.Recent.address ? " (" + row.Recent.address + ")" : "");
                    spriteBatch.DrawString(uiFont, Fit(name, ColMode - ColName - 12), new Vector2(ColName, y), new Color(170, 170, 170));
                    spriteBatch.DrawString(uiFont, "NOT FOUND RIGHT NOW: CHOOSE IT TO TRY", new Vector2(ColMode, y), Dim);
                    continue;
                }
                ServerInformation server = row.Server;
                bool joinable = !server.Full && !server.WrongVersion;
                Color text = joinable ? Color.White : Dim;
                string where = IsOwn(server) ? "YOURS" : server.WrongVersion ? server.Version.ToUpperInvariant() : server.lanServer ? "LAN" : "NET";
                Color whereColor = IsOwn(server) ? InfiniminerGame.IM_BLUE : server.WrongVersion ? InfiniminerGame.IM_RED : Header;
                spriteBatch.DrawString(uiFont, where, new Vector2(ColType, y), whereColor);
                spriteBatch.DrawString(uiFont, Fit(server.Name, ColMode - ColName - 12), new Vector2(ColName, y), text);
                spriteBatch.DrawString(uiFont, Fit(server.ModeLabel, ColWorld - ColMode - 10), new Vector2(ColMode, y), text);
                spriteBatch.DrawString(uiFont, server.WorldLabel, new Vector2(ColWorld, y), text);
                spriteBatch.DrawString(uiFont, server.Full ? "FULL" : server.PlayersLabel, new Vector2(ColPlayers, y), server.Full ? InfiniminerGame.IM_RED : text);
                spriteBatch.DrawString(uiFont, server.BotsLabel, new Vector2(ColBots, y), text);
                Color pingColor = server.PingMs < 0 ? Dim : server.PingMs < 80 ? new Color(120, 220, 120) : server.PingMs < 180 ? new Color(255, 200, 40) : InfiniminerGame.IM_RED;
                spriteBatch.DrawString(uiFont, server.PingLabel, new Vector2(ColPing, y), pingColor);
            }

            // Nothing to list yet.
            if (rows.Count == 2)
            {
                if (Game.SecondsSinceDiscovery < SearchSeconds || Game.FetchingPublicList)
                    DrawCentered(spriteBatch, "LOOKING FOR GAMES...", ListTop + 60, Header);
                else
                {
                    DrawCentered(spriteBatch, "NO GAMES FOUND ON YOUR NETWORK.", ListTop + 60, Color.White);
                    DrawCentered(spriteBatch, "QUICK PLAY STARTS ONE WITH COMPUTER PLAYERS; HOST A GAME TO CHOOSE THE SETTINGS.", ListTop + 90, Header);
                    DrawCentered(spriteBatch, "FRIENDS ON YOUR NETWORK WILL SEE YOUR GAME HERE.", ListTop + 114, Header);
                    if (Game.Settings.ServerListUrl == "")
                        DrawCentered(spriteBatch, "TO JOIN A GAME ON THE INTERNET, USE DIRECT CONNECT WITH THE HOST'S ADDRESS.", ListTop + 150, Dim);
                }
            }

            // About the selected row.
            spriteBatch.Draw(white, new Rectangle(ColType - 10, DetailY - 8, InfiniminerGame.MenuWidth - 2 * (ColType - 10), 1), Dim);
            if (highlighted >= 0 && highlighted < rows.Count)
            {
                BrowserRow row = rows[highlighted];
                string first = "", second = "";
                Color secondColor = Header;
                switch (row.Kind)
                {
                    case RowKind.QuickPlay:
                        first = "QUICK PLAY";
                        second = "JOINS THE BEST OPEN GAME, OR STARTS ONE WITH " + Game.Settings.HostBots + " COMPUTER PLAYERS IF THERE ISN'T ONE";
                        break;
                    case RowKind.Host:
                        first = Game.IsHosting ? "END YOUR GAME" : "HOST A GAME";
                        second = Game.IsHosting ? "STOPS THE GAME YOU ARE HOSTING, FOR EVERYONE IN IT"
                            : "CHOOSE THE WORLD, MODE, COMPUTER PLAYERS AND MORE; PLAYERS ON YOUR NETWORK CAN JOIN";
                        break;
                    case RowKind.Recent:
                        first = row.Recent.name.ToUpperInvariant() + "   " + row.Recent.address;
                        second = "A SERVER YOU JOINED BEFORE; IT ISN'T ANSWERING RIGHT NOW";
                        break;
                    default:
                        ServerInformation server = row.Server;
                        first = server.Name + "   " + server.ipEndPoint;
                        second = server.DetailLine;
                        if (server.WrongVersion)
                        {
                            second = "THIS SERVER RUNS INFINIMINER " + server.Version.ToUpperInvariant() + "; YOU HAVE " + GameConstants.Version.ToUpperInvariant();
                            secondColor = InfiniminerGame.IM_RED;
                        }
                        else if (server.Full)
                        {
                            second = "FULL: " + second;
                            secondColor = InfiniminerGame.IM_RED;
                        }
                        break;
                }
                int width = InfiniminerGame.MenuWidth - 2 * ColType;
                DrawCentered(spriteBatch, Fit(first, width), DetailY, Color.White);
                DrawCentered(spriteBatch, Fit(second, width), DetailY + 24, secondColor);
            }

            spriteBatch.DrawString(uiFont, InfiniminerGame.INFINIMINER_VERSION, new Vector2(10, InfiniminerGame.MenuHeight - 20), Color.White);

            string status = quickPlay == QuickPlayStep.Searching ? "QUICK PLAY: LOOKING FOR AN OPEN GAME..."
                : quickPlay == QuickPlayStep.Starting ? "QUICK PLAY: NO OPEN GAMES, SO STARTING ONE WITH " + Game.Settings.HostBots + " COMPUTER PLAYERS..."
                : Game.StatusMessage;
            if (status != "")
                DrawCentered(spriteBatch, status, drawRect.Y + 665, quickPlay != QuickPlayStep.None ? new Color(255, 200, 40) : InfiniminerGame.IM_RED);

            if (directConnectIPEnter)
                spriteBatch.DrawString(uiFont, "ENTER IP: " + directConnectIP, new Vector2(drawRect.X + 30, drawRect.Y + 690), Color.White);
            else
            {
                string hint = Game.Rejoin.Active ? (_SM.UsingGamePad ? "B: STOP REJOINING" : "ESC: STOP REJOINING")
                    : _SM.UsingGamePad ? "A: JOIN   X: REFRESH   Y: OPTIONS   BACK: ADDRESS   B: BACK" : "CLICK OR ARROWS AND ENTER: JOIN   F5: REFRESH";
                DrawCentered(spriteBatch, hint, drawRect.Y + 690, new Color(150, 150, 150));
            }

            // The options button sits between the two buttons painted into the menu image.
            SpriteFontBase titleFont = Game.Assets.TitleFont;
            Vector2 optionsSize = titleFont.MeasureString("[ OPTIONS ]");
            spriteBatch.DrawString(titleFont, "[ OPTIONS ]", new Vector2((int)(590 - optionsSize.X / 2), 711), Color.White);

            spriteBatch.End();
        }

        // Accepts "host", "ip", "host:port" and "[ipv6]:port".
        static IPEndPoint ResolveEndPoint(string address)
        {
            address = address.Trim();
            int port = GameConstants.DefaultPort;

            if (IPEndPoint.TryParse(address, out IPEndPoint endPoint))
            {
                if (endPoint.Port == 0)
                    endPoint.Port = port;
                return endPoint;
            }

            int colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address.Substring(colon + 1), out int parsedPort))
            {
                port = parsedPort;
                address = address.Substring(0, colon);
            }

            try
            {
                // Prefer IPv4 when a name resolves to both.
                IPAddress[] resolveResults = Dns.GetHostAddresses(address);
                IPAddress connectIp = resolveResults.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? resolveResults.FirstOrDefault();
                if (connectIp != null)
                    return new IPEndPoint(connectIp, port);
            }
            catch (Exception)
            {
                // So, GetHostAddresses() might fail, but we don't really care.
            }
            return null;
        }

        public override void OnTextInput(char character)
        {
            if (directConnectIPEnter)
                directConnectIP += character;
        }

        public override void OnKeyDown(Keys key)
        {
            if (directConnectIPEnter)
            {
                if (key == Keys.Escape)
                {
                    directConnectIPEnter = false;
                    directConnectIP = "";
                }

                if (key == Keys.Back && directConnectIP.Length > 0)
                {
                    directConnectIP = directConnectIP.Substring(0, directConnectIP.Length - 1);
                }

                if (key == Keys.Enter)
                {
                    // Try what was entered first as an IP, and then second as a host name.
                    directConnectIPEnter = false;
                    _P.PlaySound(InfiniminerSound.ClickHigh);
                    JoinAddress(directConnectIP.Trim(), directConnectIP.Trim());
                    directConnectIP = "";
                }

                if (key == Keys.V && (Keyboard.GetState().IsKeyDown(Keys.LeftControl) || Keyboard.GetState().IsKeyDown(Keys.RightControl)
                                      || Keyboard.GetState().IsKeyDown(Keys.LeftWindows) || Keyboard.GetState().IsKeyDown(Keys.RightWindows)))
                {
                    try
                    {
                        directConnectIP += TextUtil.Sanitize(TextCopy.ClipboardService.GetText()).Trim();
                    }
                    catch (Exception)
                    {
                        // No clipboard available (e.g. xsel/xclip missing on Linux).
                    }
                }
            }
            else
            {
                if (key == Keys.Escape)
                {
                    // The first Escape only stops a rejoin, or a quick play, in progress.
                    if (Game.Rejoin.Active || quickPlay == QuickPlayStep.Searching)
                    {
                        Game.Rejoin.Cancel();
                        quickPlay = QuickPlayStep.None;
                        Game.StatusMessage = "";
                        return;
                    }
                    nextState = typeof(TitleState);
                }

                // Choosing without the mouse: up and down through everything, left and right between the two buttons.
                int rowCount = rows.Count;
                if (key == Keys.Up || key == Keys.Down)
                {
                    if (highlighted < 0 || highlighted >= rowCount)
                        highlighted = key == Keys.Up ? rowCount - 1 : 0;
                    else
                        highlighted = (highlighted + (key == Keys.Up ? -1 : 1) + rowCount) % rowCount;
                    _P.PlaySound(InfiniminerSound.ClickLow);
                }
                if ((key == Keys.Left || key == Keys.Right) && highlighted <= 1)
                {
                    highlighted = highlighted == 0 && key == Keys.Right ? 1 : highlighted == 1 && key == Keys.Left ? 0 : Math.Max(0, highlighted);
                    _P.PlaySound(InfiniminerSound.ClickLow);
                }
                if (key == Keys.Enter)
                    ChooseRow(highlighted);
                if (key == Keys.F5)
                    Refresh();
            }
        }

        void Join(ServerInformation server)
        {
            if (server.WrongVersion)
            {
                Game.StatusMessage = "THAT SERVER RUNS INFINIMINER " + server.Version.ToUpperInvariant() + " (YOU HAVE " + GameConstants.Version.ToUpperInvariant() + ")";
                return;
            }
            if (server.Full)
            {
                Game.StatusMessage = "THAT SERVER IS FULL";
                return;
            }
            Game.Rejoin.Cancel();
            Game.JoinGame(server.ipEndPoint, server.ipEndPoint.ToString(), server.serverName);
            nextState = typeof(LoadingState);
            _P.PlaySound(InfiniminerSound.ClickHigh);
        }

        // Joins by address (typed, or from the recent servers).
        void JoinAddress(string address, string name)
        {
            Game.Rejoin.Cancel();
            _P.PlaySound(InfiniminerSound.ClickHigh);
            IPEndPoint endPoint = ResolveEndPoint(address);
            if (endPoint == null)
            {
                Game.StatusMessage = "ERROR: COULD NOT FIND " + TextUtil.Sanitize(address).ToUpperInvariant();
                return;
            }
            Game.JoinGame(endPoint, address, name);
            nextState = typeof(LoadingState);
        }

        void Refresh()
        {
            _P.PlaySound(InfiniminerSound.ClickHigh);
            Game.StatusMessage = "";
            highlighted = -1;
            Game.StartServerDiscovery();
        }

        public override string TextInputPrompt
        {
            get { return directConnectIPEnter ? "SERVER ADDRESS: " + directConnectIP + "_" : null; }
        }

        public override void OnGamePadButtonDown(Buttons button)
        {
            if (directConnectIPEnter)
                return;
            // Back: type an address with the on-screen keyboard.
            if (button == Buttons.Back)
            {
                directConnectIPEnter = true;
                directConnectIP = "";
                Game.StatusMessage = "";
                _P.PlaySound(InfiniminerSound.ClickHigh);
            }
            if (button == Buttons.X)
                Refresh();
            if (button == Buttons.Y || button == Buttons.Start)
            {
                _P.PlaySound(InfiniminerSound.ClickHigh);
                nextState = typeof(SettingsState);
            }
        }

        public override void OnKeyUp(Keys key)
        {

        }

        public override void OnMouseDown(MouseButton button, int x, int y)
        {
            Point menuPoint = Game.ToMenu(x, y);
            if (directConnectIPEnter)
                return;

            int row = rows.FindIndex(r => r.Bounds.Contains(menuPoint));
            if (row >= 0)
            {
                highlighted = row;
                ChooseRow(row);
                return;
            }

            switch (ClickRegion.HitTest(clkMenuServer, new Point(menuPoint.X - drawRect.X, menuPoint.Y - drawRect.Y)))
            {
                case "refresh":
                    Refresh();
                    break;

                case "options":
                    _P.PlaySound(InfiniminerSound.ClickHigh);
                    nextState = typeof(SettingsState);
                    break;

                case "direct":
                    directConnectIPEnter = true;
                    Game.StatusMessage = "";
                    _P.PlaySound(InfiniminerSound.ClickHigh);
                    break;
            }
        }

        public override void OnMouseUp(MouseButton button, int x, int y)
        {

        }

        public override void OnMouseScroll(int scrollDelta)
        {

        }
    }
}
