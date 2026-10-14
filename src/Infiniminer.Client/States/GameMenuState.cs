using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Infiniminer.States
{
    // The in-game menu (Escape, or Start on a controller): everything that otherwise needs a key combination.
    public class GameMenuState : MenuListState
    {
        protected override string Title
        {
            get { return "MENU"; }
        }

        protected override int RowTop
        {
            get { return 220; }
        }

        protected override int RowHeight
        {
            get { return 44; }
        }

        // SAVE MAP: typing the name.
        bool namingMap;
        string mapName = "";

        protected override bool Busy
        {
            get { return namingMap; }
        }

        public override string TextInputPrompt
        {
            get { return namingMap ? "SAVE THE MAP AS: " + mapName + "_" : null; }
        }

        protected override string Hint
        {
            get { return namingMap ? "TYPE A NAME FOR THE MAP, THEN PRESS ENTER (ESCAPE CANCELS)" : base.Hint; }
        }

        bool CanChangeClass
        {
            get { return !_P.player.Spectating && _P.player.Position.Y > _P.SurfaceHeight; }
        }

        public override void OnEnter(Type oldState)
        {
            base.OnEnter(oldState);
            Rebuild();
        }

        protected override void BuildRows()
        {
            rows = new List<Row>
            {
                new Row { Label = "RESUME", Activate = Back },
                new Row { Label = "CHANGE TEAM", Activate = () => Go(typeof(TeamSelectionState)) },
                new Row
                {
                    Label = "CHANGE CLASS",
                    Value = () => CanChangeClass ? "" : "ON THE SURFACE ONLY",
                    Enabled = () => CanChangeClass,
                    Activate = () => Go(typeof(ClassSelectionState)),
                },
                new Row { Label = "OPTIONS", Activate = () => Go(typeof(SettingsState)) },
                new Row
                {
                    Label = "INTERNET",
                    Value = () => Game.HostedServer?.PortForward?.ShortStatus ?? (Game.IsHosting ? "LAN ONLY" : "-"),
                    Enabled = () => Game.HostedServer?.PortForward?.ExternalEndPoint != null,
                    Activate = CopyInternetAddress,
                },
                new Row
                {
                    Label = "SAVE MAP",
                    Value = () => namingMap ? mapName + "_" : Game.IsHosting ? "TO START GAMES ON" : "HOST ONLY",
                    Enabled = () => Game.HostedServer != null,
                    Activate = () => { namingMap = true; mapName = ""; _P.PlaySound(InfiniminerSound.ClickHigh); },
                },
                new Row
                {
                    Label = "RESPAWN",
                    Value = () => "COMMITS PIXELCIDE",
                    Enabled = () => !_P.player.Dead,
                    Activate = () => { _P.KillPlayer("HAS COMMMITTED PIXELCIDE!"); Go(typeof(MainGameState)); },
                },
                new Row
                {
                    Label = Game.IsHosting ? "END GAME" : "LEAVE SERVER",
                    Value = () => Game.IsHosting ? "FOR EVERYONE" : "",
                    Activate = () => { Game.LeaveServer(); Go(typeof(ServerBrowserState)); },
                },
            };
        }

        public override void OnTextInput(char character)
        {
            if (namingMap && mapName.Length < 24 && (char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == ' '))
                mapName += char.ToLowerInvariant(character);
        }

        public override void OnKeyDown(Keys key)
        {
            if (!namingMap)
            {
                base.OnKeyDown(key);
                return;
            }
            if (key == Keys.Back && mapName.Length > 0)
                mapName = mapName.Substring(0, mapName.Length - 1);
            else if (key == Keys.Escape)
                namingMap = false;
            else if (key == Keys.Enter)
                SaveMap();
        }

        // Saves the hosted world as a map (on the server's thread); the server says in chat when it's done.
        void SaveMap()
        {
            namingMap = false;
            string name = mapName.Trim().Replace(' ', '-');
            if (name == "" || Game.HostedServer == null)
                return;
            Game.HostedServer.Do(server => server.SaveMap(name));
            _P.PlaySound(InfiniminerSound.ClickHigh);
            Go(typeof(MainGameState));
        }

        void CopyInternetAddress()
        {
            string address = Game.HostedServer?.PortForward?.ExternalEndPoint;
            if (address == null)
                return;
            try
            {
                TextCopy.ClipboardService.SetText(address);
                _P.chatLog.Add("COPIED " + address + " - SEND IT TO YOUR FRIENDS TO JOIN WITH DIRECT CONNECT.", ChatMessageType.SayAll);
            }
            catch (Exception)
            {
                _P.chatLog.Add("YOUR GAME'S INTERNET ADDRESS IS " + address, ChatMessageType.SayAll);
            }
            _P.PlaySound(InfiniminerSound.ClickHigh);
        }

        void Go(Type state)
        {
            _P.PlaySound(InfiniminerSound.ClickHigh);
            nextState = state;
        }

        protected override void Back()
        {
            Go(typeof(MainGameState));
        }

        public override Type OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            // The game carries on underneath.
            Game.UpdateNetwork(gameTime);
            return base.OnUpdate(gameTime, keyState, mouseState);
        }

        // The game stays on screen behind the menu, dimmed.
        public override void OnRenderAtUpdate(GraphicsDevice graphicsDevice, GameTime gameTime)
        {
            Game.Bloom?.BeginScene();
            _P.skyplaneEngine.Render(graphicsDevice);
            _P.particleEngine.Render(graphicsDevice);
            _P.playerEngine.Render(graphicsDevice);
            _P.blockEngine.Render(graphicsDevice, gameTime);
            Game.Bloom?.EndScene();

            SpriteBatch spriteBatch = _SM.SpriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
            spriteBatch.Draw(texBlank, graphicsDevice.Viewport.Bounds, new Color(0, 0, 0, 190));
            spriteBatch.End();

            base.OnRenderAtUpdate(graphicsDevice, gameTime);
        }

        // Start closes the menu again, as it opened it.
        public override void OnGamePadButtonDown(Buttons button)
        {
            if (button == Buttons.Start)
                Back();
        }
    }
}
