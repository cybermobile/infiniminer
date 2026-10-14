using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StateMasher;

namespace Infiniminer.States
{
    public class TeamSelectionState : State
    {
        Texture2D texMenu;
        Rectangle drawRect;
        Type nextState = null;
        SpriteFontBase uiFont;
        bool canCancel = false;
        int highlighted = 0;            // 0 red, 1 blue, 2 spectate: the keyboard/controller selection
        bool showHighlight = false;     // shown once the keyboard or controller is used

        ClickRegion[] clkTeamMenu = new ClickRegion[3] {
            new ClickRegion(new Rectangle(229,156,572,190), "red"), 
            new ClickRegion(new Rectangle(135,424,761,181), "blue"),
            new ClickRegion(new Rectangle(392,664,240,36), "spectate"),
        };

        public override void OnEnter(Type oldState)
        {
            _SM.IsMouseVisible = true;

            texMenu = Game.Assets.Texture("menus/tex_menu_team");

            // Menus are drawn on a 1024x768 canvas that is scaled to fit the window.
            drawRect = new Rectangle(0, 0, 1024, 1024);

            uiFont = Game.Assets.UiFont;

            if (oldState == typeof(MainGameState) || oldState == typeof(GameMenuState))
                canCancel = true;
            highlighted = _P.player.Spectating ? 2 : _P.player.Team == PlayerTeam.Blue ? 1 : 0;
            showHighlight = _SM.UsingGamePad;
        }

        public override void OnLeave(Type newState)
        {

        }

        public override Type OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            // Do network stuff.
            Game.UpdateNetwork(gameTime);

            return nextState;
        }

        public override void OnRenderAtEnter(GraphicsDevice graphicsDevice)
        {

        }

        public void QuickDrawText(SpriteBatch spriteBatch, string text, int y, Color color)
        {
            spriteBatch.DrawString(uiFont, text, new Vector2(InfiniminerGame.MenuWidth / 2 - uiFont.MeasureString(text).X / 2, drawRect.Y + y), color);
        }

        public override void OnRenderAtUpdate(GraphicsDevice graphicsDevice, GameTime gameTime)
        {
            int redTeamCount = 0, blueTeamCount = 0;
            foreach (Player p in _P.playerList.Values)
            {
                if (p.Team == PlayerTeam.Red)
                    redTeamCount += 1;
                else if (p.Team == PlayerTeam.Blue)
                    blueTeamCount += 1;
            }

            SpriteBatch spriteBatch = _SM.SpriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, Game.MenuSampler, null, null, null, Game.MenuTransform);
            spriteBatch.Draw(texMenu, drawRect, Color.White);

            // The original picture reuses the server list's heading and buttons: cover them and name this screen.
            spriteBatch.Draw(Game.Assets.WhitePixel, new Rectangle(0, 12, 1024, 56), Color.Black);
            spriteBatch.Draw(Game.Assets.WhitePixel, new Rectangle(0, 708, 1024, 56), Color.Black);
            const string title = "SELECT A TEAM";
            Vector2 titleSize = uiFont.MeasureString(title) * 1.5f;
            uiFont.DrawText(spriteBatch, title, new Vector2((int)(InfiniminerGame.MenuWidth / 2 - titleSize.X / 2), 28), Color.White, scale: new Vector2(1.5f));
            if (canCancel)
                QuickDrawText(spriteBatch, _SM.UsingGamePad ? "B: BACK TO THE GAME" : "ESC: BACK TO THE GAME", 725, new Color(150, 150, 150));
            QuickDrawText(spriteBatch, "" + redTeamCount + " PLAYERS", 360, InfiniminerGame.IM_RED);
            QuickDrawText(spriteBatch, "" + blueTeamCount + " PLAYERS", 620, InfiniminerGame.IM_BLUE);
            QuickDrawText(spriteBatch, "[ SPECTATE ]", 672, Color.White);
            if (showHighlight)
                Game.Assets.DrawOutline(spriteBatch, clkTeamMenu[highlighted].Rectangle, Color.White);
            spriteBatch.End();
        }

        public override void OnKeyDown(Keys key)
        {
            if (key == Keys.Escape && canCancel)
                nextState = typeof(MainGameState);
            if (key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right)
            {
                if (showHighlight)
                    highlighted = (highlighted + (key == Keys.Up || key == Keys.Left ? 2 : 1)) % 3;
                showHighlight = true;
                _P.PlaySound(InfiniminerSound.ClickLow);
            }
            if ((key == Keys.Enter || key == Keys.Space) && showHighlight)
            {
                if (highlighted == 2)
                    Spectate();
                else
                    ChooseTeam(highlighted == 0 ? PlayerTeam.Red : PlayerTeam.Blue);
            }
        }

        void ChooseTeam(PlayerTeam team)
        {
            if (_P.player.Team == team && canCancel)
                nextState = typeof(MainGameState);
            else
            {
                _P.SetPlayerTeam(team);
                nextState = typeof(ClassSelectionState);
            }
            _P.PlaySound(InfiniminerSound.ClickHigh);
        }

        void Spectate()
        {
            _P.Spectate();
            nextState = typeof(MainGameState);
            _P.PlaySound(InfiniminerSound.ClickHigh);
        }

        public override void OnKeyUp(Keys key)
        {

        }

        public override void OnMouseDown(MouseButton button, int x, int y)
        {
            Point menuPoint = Game.ToMenu(x, y);
            x = menuPoint.X;
            y = menuPoint.Y;
            x -= drawRect.X;
            y -= drawRect.Y;
            switch (ClickRegion.HitTest(clkTeamMenu, new Point(x, y)))
            {
                case "red":
                    ChooseTeam(PlayerTeam.Red);
                    break;
                case "blue":
                    ChooseTeam(PlayerTeam.Blue);
                    break;
                case "spectate":
                    Spectate();
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
