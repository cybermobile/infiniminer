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

        bool CanChangeClass
        {
            get { return _P.playerPosition.Y > _P.SurfaceHeight; }
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
                    Label = "RESPAWN",
                    Value = () => "COMMITS PIXELCIDE",
                    Enabled = () => !_P.playerDead,
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
