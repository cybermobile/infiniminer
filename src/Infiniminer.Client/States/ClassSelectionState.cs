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
        int highlighted = 0;            // the keyboard/controller selection, in menu order
        bool showHighlight = false;     // shown once the keyboard or controller is used
        static readonly PlayerClass[] MenuOrder = { PlayerClass.Miner, PlayerClass.Prospector, PlayerClass.Engineer, PlayerClass.Sapper };

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
            highlighted = Math.Max(0, Array.IndexOf(MenuOrder, _P.playerClass));
            showHighlight = _SM.UsingGamePad;
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
            if (showHighlight)
            {
                Rectangle area = clkClassMenu[highlighted].Rectangle;
                area.Inflate(8, 8);
                Game.Assets.DrawOutline(spriteBatch, area, Color.White);
            }
            spriteBatch.End();
        }

        public override void OnKeyDown(Keys key)
        {
            if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
            {
                int direction = key == Keys.Left || key == Keys.Up ? -1 : 1;
                if (showHighlight)
                    highlighted = (highlighted + direction + MenuOrder.Length) % MenuOrder.Length;
                showHighlight = true;
                _P.PlaySound(InfiniminerSound.ClickLow);
            }
            if ((key == Keys.Enter || key == Keys.Space) && showHighlight)
                ChooseClass(MenuOrder[highlighted]);
        }

        void ChooseClass(PlayerClass playerClass)
        {
            _P.SetPlayerClass(playerClass);
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
            switch (ClickRegion.HitTest(clkClassMenu, new Point(x, y)))
            {
                case "miner":
                    ChooseClass(PlayerClass.Miner);
                    break;
                case "engineer":
                    ChooseClass(PlayerClass.Engineer);
                    break;
                case "prospector":
                    ChooseClass(PlayerClass.Prospector);
                    break;
                case "sapper":
                    ChooseClass(PlayerClass.Sapper);
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
