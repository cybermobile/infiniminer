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

        ClickRegion[] clkTeamMenu = new ClickRegion[2] {
            new ClickRegion(new Rectangle(229,156,572,190), "red"), 
            new ClickRegion(new Rectangle(135,424,761,181), "blue")
        };

        public override void OnEnter(Type oldState)
        {
            _SM.IsMouseVisible = true;

            texMenu = Game.Assets.Texture("menus/tex_menu_team");

            // Menus are drawn on a 1024x768 canvas that is scaled to fit the window.
            drawRect = new Rectangle(0, 0, 1024, 1024);

            uiFont = Game.Assets.UiFont;

            if (oldState == typeof(MainGameState))
                canCancel = true;
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
            QuickDrawText(spriteBatch, "" + redTeamCount + " PLAYERS", 360, InfiniminerGame.IM_RED);
            QuickDrawText(spriteBatch, "" + blueTeamCount + " PLAYERS", 620, InfiniminerGame.IM_BLUE);
            spriteBatch.End();
        }

        public override void OnKeyDown(Keys key)
        {
            if (key == Keys.Escape && canCancel)
                nextState = typeof(MainGameState);
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
                    if (_P.playerTeam == PlayerTeam.Red && canCancel)
                        nextState = typeof(MainGameState);
                    else
                    {
                        _P.SetPlayerTeam(PlayerTeam.Red);
                        nextState = typeof(ClassSelectionState);
                    }
                    _P.PlaySound(InfiniminerSound.ClickHigh);
                    break;
                case "blue":
                    if (_P.playerTeam == PlayerTeam.Blue && canCancel)
                        nextState = typeof(MainGameState);
                    else
                    {
                        _P.SetPlayerTeam(PlayerTeam.Blue);
                        nextState = typeof(ClassSelectionState);
                    }
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
