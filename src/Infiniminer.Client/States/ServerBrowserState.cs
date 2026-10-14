using System.Net;
using System.Net.Sockets;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StateMasher;

namespace Infiniminer.States
{
    public class ServerBrowserState : State
    {
        Texture2D texMenu;
        Rectangle drawRect;
        Type nextState = null;
        List<ServerInformation> serverList = new List<ServerInformation>();
        List<int> descWidths = new List<int>();
        SpriteFontBase uiFont;
        bool directConnectIPEnter = false;
        int highlighted = -1;           // the keyboard/controller selection in the server list (-1: none yet)
        string directConnectIP = "";

        ClickRegion[] clkMenuServer = new ClickRegion[3] {
            new ClickRegion(new Rectangle(763,713,243,42), "refresh"),
            new ClickRegion(new Rectangle(0,713,425,42), "direct"),
            new ClickRegion(new Rectangle(440,713,300,42), "options")
        };

        public override void OnEnter(Type oldState)
        {
            _SM.IsMouseVisible = true;
            Game.ResetPropertyBag();
            _P = _SM.propertyBag;

            texMenu = Game.Assets.Texture("menus/tex_menu_server");

            // Menus are drawn on a 1024x768 canvas that is scaled to fit the window.
            drawRect = new Rectangle(0, 0, 1024, 1024);

            uiFont = Game.Assets.UiFont;

            Game.StartServerDiscovery();
        }

        public override void OnLeave(Type newState)
        {

        }

        public override Type OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            // Pick up replies to our LAN discovery broadcast.
            Game.UpdateNetwork(gameTime);
            serverList = Game.GetServerList();

            return nextState;
        }

        public override void OnRenderAtEnter(GraphicsDevice graphicsDevice)
        {

        }

        public override void OnRenderAtUpdate(GraphicsDevice graphicsDevice, GameTime gameTime)
        {
            descWidths = new List<int>();
            SpriteBatch spriteBatch = _SM.SpriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, Game.MenuSampler, null, null, null, Game.MenuTransform);
            spriteBatch.Draw(texMenu, drawRect, Color.White);

            int drawY = 80;
            for (int i = 0; i < serverList.Count && drawY < 660; i++, drawY += 25)
            {
                string description = serverList[i].GetServerDesc();
                int textWidth = (int)(uiFont.MeasureString(description).X);
                descWidths.Add(textWidth + 30);
                if (i == highlighted)
                    spriteBatch.Draw(Game.Assets.WhitePixel, new Rectangle(InfiniminerGame.MenuWidth / 2 - textWidth / 2 - 15, drawRect.Y + drawY - 4, textWidth + 30, 24), new Color(60, 60, 60));
                spriteBatch.DrawString(uiFont, description, new Vector2(InfiniminerGame.MenuWidth / 2 - textWidth / 2, drawRect.Y + drawY), Color.White);
            }

            spriteBatch.DrawString(uiFont, InfiniminerGame.INFINIMINER_VERSION, new Vector2(10, InfiniminerGame.MenuHeight - 20), Color.White);

            if (Game.StatusMessage != "")
            {
                int textWidth = (int)uiFont.MeasureString(Game.StatusMessage).X;
                spriteBatch.DrawString(uiFont, Game.StatusMessage, new Vector2(InfiniminerGame.MenuWidth / 2 - textWidth / 2, drawRect.Y + 665), InfiniminerGame.IM_RED);
            }

            if (directConnectIPEnter)
                spriteBatch.DrawString(uiFont, "ENTER IP: " + directConnectIP, new Vector2(drawRect.X + 30, drawRect.Y + 690), Color.White);
            else
            {
                string hint = _SM.UsingGamePad ? "A: JOIN   X: REFRESH   Y: OPTIONS   B: BACK" : "UP/DOWN AND ENTER: JOIN   F5: REFRESH";
                Vector2 hintSize = uiFont.MeasureString(hint);
                spriteBatch.DrawString(uiFont, hint, new Vector2((int)(InfiniminerGame.MenuWidth / 2 - hintSize.X / 2), drawRect.Y + 690), new Color(150, 150, 150));
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
                    IPEndPoint connectEndPoint = ResolveEndPoint(directConnectIP);
                    if (connectEndPoint != null)
                    {
                        Game.JoinGame(connectEndPoint);
                        nextState = typeof(LoadingState);
                    }
                    else
                        Game.StatusMessage = "ERROR: COULD NOT FIND " + TextUtil.Sanitize(directConnectIP).ToUpperInvariant();
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
                    nextState = typeof(TitleState);
                }

                // Choosing a server without the mouse.
                int visible = Math.Min(serverList.Count, 24);
                if ((key == Keys.Up || key == Keys.Down) && visible > 0)
                {
                    if (highlighted < 0 || highlighted >= visible)
                        highlighted = key == Keys.Up ? visible - 1 : 0;
                    else
                        highlighted = (highlighted + (key == Keys.Up ? -1 : 1) + visible) % visible;
                    _P.PlaySound(InfiniminerSound.ClickLow);
                }
                if (key == Keys.Enter && highlighted >= 0 && highlighted < serverList.Count)
                    Join(serverList[highlighted]);
                if (key == Keys.F5)
                    Refresh();
            }
        }

        void Join(ServerInformation server)
        {
            Game.JoinGame(server.ipEndPoint);
            nextState = typeof(LoadingState);
            _P.PlaySound(InfiniminerSound.ClickHigh);
        }

        void Refresh()
        {
            _P.PlaySound(InfiniminerSound.ClickHigh);
            Game.StatusMessage = "";
            highlighted = -1;
            Game.StartServerDiscovery();
        }

        public override void OnGamePadButtonDown(Buttons button)
        {
            if (directConnectIPEnter)
                return;
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
            x = menuPoint.X;
            y = menuPoint.Y;
            if (directConnectIPEnter == false)
            {
                int serverIndex = (y - drawRect.Y - 75) / 25;
                if (serverIndex >= 0 && serverIndex < serverList.Count && serverIndex < descWidths.Count)
                {
                    int distanceFromCenter = Math.Abs(InfiniminerGame.MenuWidth / 2 - x);
                    if (distanceFromCenter < descWidths[serverIndex] / 2)
                        Join(serverList[serverIndex]);
                }

                x -= drawRect.X;
                y -= drawRect.Y;
                switch (ClickRegion.HitTest(clkMenuServer, new Point(x, y)))
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
        }

        public override void OnMouseUp(MouseButton button, int x, int y)
        {

        }

        public override void OnMouseScroll(int scrollDelta)
        {

        }
    }
}
