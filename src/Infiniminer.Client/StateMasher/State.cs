using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace StateMasher
{
    public enum MouseButton
    {
        LeftButton,
        MiddleButton,
        RightButton
    }

    public class State
    {
        public StateMachine _SM = null;
        public Infiniminer.PropertyBag _P = null;

        protected Infiniminer.InfiniminerGame Game
        {
            get { return (Infiniminer.InfiniminerGame)_SM; }
        }

        public virtual void OnEnter(Type oldState)
        {
        }

        public virtual void OnLeave(Type newState)
        {
        }

        public virtual Type OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            return null;
        }

        public virtual void OnRenderAtEnter(GraphicsDevice graphicsDevice)
        {
        }

        public virtual void OnRenderAtUpdate(GraphicsDevice graphicsDevice, GameTime gameTime)
        {
        }

        public virtual void OnKeyDown(Keys key)
        {
        }

        public virtual void OnKeyUp(Keys key)
        {
        }

        // Printable characters typed by the user, already translated for their keyboard layout.
        public virtual void OnTextInput(char character)
        {
        }

        // When true, the controller's D-pad, left stick, A and B arrive as arrow keys, Enter and Escape.
        public virtual bool UsesMenuNavigation
        {
            get { return true; }
        }

        // Every controller button press (after any menu navigation keys it caused).
        public virtual void OnGamePadButtonDown(Buttons button)
        {
        }

        public virtual void OnMouseDown(MouseButton button, int x, int y)
        {
        }

        public virtual void OnMouseUp(MouseButton button, int x, int y)
        {
        }

        public virtual void OnMouseScroll(int scrollWheelValue)
        {
        }
    }
}
