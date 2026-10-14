using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StateMasher;

namespace Infiniminer.States
{
    public class ClassSelectionState : State
    {
        Texture2D texMenuRed, texMenuBlue;
        Rectangle drawRect;
        Type nextState = null;

        ClickRegion[] clkClassMenu = new ClickRegion[4] {
            new ClickRegion(new Rectangle(54,168,142,190), "miner"), 
            new ClickRegion(new Rectangle(300,169,142,190), "prospector"), 
            new ClickRegion(new Rectangle(580,170,133,187), "engineer"), 
            new ClickRegion(new Rectangle(819,172,133,190), "sapper")
        };

        public override void OnEnter(Type oldState)
        {
            _SM.IsMouseVisible = true;

            texMenuRed = Game.Assets.Texture("menus/tex_menu_class_red");
            texMenuBlue = Game.Assets.Texture("menus/tex_menu_class_blue");

            // Menus are drawn on a 1024x768 canvas that is scaled to fit the window.
            drawRect = new Rectangle(0, 0, 1024, 1024);

            _P.KillPlayer("");
        }

        public override void OnLeave(Type newState)
        {
            _P.RespawnPlayer();
        }

        public override Type OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            // Do network stuff.
            Game.UpdateNetwork(gameTime);

            _P.skyplaneEngine.Update(gameTime);
            _P.playerEngine.Update(gameTime);
            _P.interfaceEngine.Update(gameTime);
            _P.particleEngine.Update(gameTime);

            return nextState;
        }

        public override void OnRenderAtEnter(GraphicsDevice graphicsDevice)
        {

        }

        public override void OnRenderAtUpdate(GraphicsDevice graphicsDevice, GameTime gameTime)
        {
            SpriteBatch spriteBatch = _SM.SpriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, Game.MenuSampler, null, null, null, Game.MenuTransform);
            spriteBatch.Draw((_P.playerTeam == PlayerTeam.Red)?texMenuRed:texMenuBlue, drawRect, Color.White);
            spriteBatch.End();
        }

        public override void OnKeyDown(Keys key)
        {

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
            switch (ClickRegion.HitTest(clkClassMenu, new Point(x, y)))
            {
                case "miner":
                    _P.SetPlayerClass(PlayerClass.Miner);
                    nextState = typeof(MainGameState);
                    _P.PlaySound(InfiniminerSound.ClickHigh);
                    break;
                case "engineer":
                    _P.SetPlayerClass(PlayerClass.Engineer);
                    nextState = typeof(MainGameState);
                    _P.PlaySound(InfiniminerSound.ClickHigh);
                    break;
                case "prospector":
                    _P.SetPlayerClass(PlayerClass.Prospector);
                    nextState = typeof(MainGameState);
                    _P.PlaySound(InfiniminerSound.ClickHigh);
                    break;
                case "sapper":
                    _P.SetPlayerClass(PlayerClass.Sapper);
                    nextState = typeof(MainGameState);
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
