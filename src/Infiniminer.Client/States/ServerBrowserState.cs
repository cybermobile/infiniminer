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
        string nextState = null;
        List<ServerInformation> serverList = new List<ServerInformation>();
        List<int> descWidths = new List<int>();
        SpriteFontBase uiFont;
        bool directConnectIPEnter = false;
        string directConnectIP = "";

        ClickRegion[] clkMenuServer = new ClickRegion[2] {
            new ClickRegion(new Rectangle(763,713,243,42), "refresh"),
            new ClickRegion(new Rectangle(0,713,425,42), "direct")
        };

        public override void OnEnter(string oldState)
        {
            _SM.IsMouseVisible = true;
            Game.ResetPropertyBag();
            _P = _SM.propertyBag;

            texMenu = Game.Assets.Texture("menus/tex_menu_server");

            drawRect = new Rectangle(_SM.GraphicsDevice.Viewport.Width / 2 - 1024 / 2,
                                     _SM.GraphicsDevice.Viewport.Height / 2 - 768 / 2,
                                     1024,
                                     1024);

            uiFont = Game.Assets.UiFont;

            Game.StartServerDiscovery();
        }

        public override void OnLeave(string newState)
        {

        }

        public override string OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
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
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp);
            spriteBatch.Draw(texMenu, drawRect, Color.White);

            int drawY = 80;
            foreach (ServerInformation server in serverList)
            {
                if (drawY < 660)
                {
                    int textWidth = (int)(uiFont.MeasureString(server.GetServerDesc()).X);
                    descWidths.Add(textWidth + 30);
                    spriteBatch.DrawString(uiFont, server.GetServerDesc(), new Vector2(_SM.GraphicsDevice.Viewport.Width / 2 - textWidth / 2, drawRect.Y + drawY), Color.White);
                    drawY += 25;
                }
            }

            spriteBatch.DrawString(uiFont, InfiniminerGame.INFINIMINER_VERSION, new Vector2(10, _SM.GraphicsDevice.Viewport.Height - 20), Color.White);

            if (Game.StatusMessage != "")
            {
                int textWidth = (int)uiFont.MeasureString(Game.StatusMessage).X;
                spriteBatch.DrawString(uiFont, Game.StatusMessage, new Vector2(_SM.GraphicsDevice.Viewport.Width / 2 - textWidth / 2, drawRect.Y + 665), InfiniminerGame.IM_RED);
            }

            if (directConnectIPEnter)
                spriteBatch.DrawString(uiFont, "ENTER IP: " + directConnectIP, new Vector2(drawRect.X + 30, drawRect.Y + 690), Color.White);

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
                        nextState = "Infiniminer.States.LoadingState";
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
                    nextState = "Infiniminer.States.TitleState";
                }
            }
        }

        public override void OnKeyUp(Keys key)
        {

        }

        public override void OnMouseDown(MouseButton button, int x, int y)
        {
            if (directConnectIPEnter == false)
            {
                int serverIndex = (y - drawRect.Y - 75) / 25;
                if (serverIndex >= 0 && serverIndex < serverList.Count && serverIndex < descWidths.Count)
                {
                    int distanceFromCenter = Math.Abs(_SM.GraphicsDevice.Viewport.Width / 2 - x);
                    if (distanceFromCenter < descWidths[serverIndex] / 2)
                    {
                        Game.JoinGame(serverList[serverIndex].ipEndPoint);
                        nextState = "Infiniminer.States.LoadingState";
                        _P.PlaySound(InfiniminerSound.ClickHigh);
                    }
                }

                x -= drawRect.X;
                y -= drawRect.Y;
                switch (ClickRegion.HitTest(clkMenuServer, new Point(x, y)))
                {
                    case "refresh":
                        _P.PlaySound(InfiniminerSound.ClickHigh);
                        Game.StatusMessage = "";
                        Game.StartServerDiscovery();
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
