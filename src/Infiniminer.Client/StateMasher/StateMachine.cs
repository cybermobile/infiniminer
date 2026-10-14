using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace StateMasher
{
    public class StateMachine : Game
    {
        public GraphicsDeviceManager graphicsDeviceManager;
        public Infiniminer.PropertyBag propertyBag = null;
        public SpriteBatch SpriteBatch;

        // The type of the current state (null before the first one).
        public Type CurrentStateType
        {
            get { return currentState?.GetType(); }
        }

        private State currentState = null;
        private bool needToRenderOnEnter = false;

        private int framesThisSecond = 0;
        private double frameTimer = 0;
        private double frameRate = 0;
        public double FrameRate
        {
            get { return frameRate; }
        }

        private HashSet<Keys> keysDown = new HashSet<Keys>();
        private MouseState msOld;

        public StateMachine()
        {
            graphicsDeviceManager = new GraphicsDeviceManager(this);
            Window.TextInput += (sender, e) =>
            {
                if (currentState != null && propertyBag != null && WindowHasFocus() && e.Character >= 32 && e.Character <= 126)
                    currentState.OnTextInput(e.Character);
            };
        }

        protected void ChangeState(Type newState)
        {
            if (!typeof(State).IsAssignableFrom(newState))
                throw new ArgumentException(newState + " is not a State", nameof(newState));

            Type oldState = CurrentStateType;
            if (currentState != null)
                currentState.OnLeave(newState);

            currentState = (State)Activator.CreateInstance(newState);
            currentState._P = propertyBag;
            currentState._SM = this;
            currentState.OnEnter(oldState);
            needToRenderOnEnter = true;
        }

        public bool WindowHasFocus()
        {
            return IsActive;
        }

        protected override void LoadContent()
        {
            SpriteBatch = new SpriteBatch(GraphicsDevice);
        }

        protected override void Update(GameTime gameTime)
        {
            if (currentState != null && propertyBag != null)
            {
                // Call OnUpdate.
                Type newState = currentState.OnUpdate(gameTime, Keyboard.GetState(), Mouse.GetState());
                if (newState != null)
                    ChangeState(newState);

                // Check for keyboard events.
                KeyboardState keyState = Keyboard.GetState();
                HashSet<Keys> keysDownNow = new HashSet<Keys>(keyState.GetPressedKeys());
                if (WindowHasFocus())
                {
                    foreach (Keys k in keysDownNow)
                        if (!keysDown.Contains(k))
                            currentState.OnKeyDown(k);
                    foreach (Keys k in keysDown)
                        if (!keysDownNow.Contains(k))
                            currentState.OnKeyUp(k);
                }
                keysDown = keysDownNow;

                // Check for mouse events.
                MouseState msNew = Mouse.GetState();
                if (WindowHasFocus())
                {
                    if (msOld.LeftButton == ButtonState.Released && msNew.LeftButton == ButtonState.Pressed)
                        currentState.OnMouseDown(MouseButton.LeftButton, msNew.X, msNew.Y);
                    if (msOld.MiddleButton == ButtonState.Released && msNew.MiddleButton == ButtonState.Pressed)
                        currentState.OnMouseDown(MouseButton.MiddleButton, msNew.X, msNew.Y);
                    if (msOld.RightButton == ButtonState.Released && msNew.RightButton == ButtonState.Pressed)
                        currentState.OnMouseDown(MouseButton.RightButton, msNew.X, msNew.Y);
                    if (msOld.LeftButton == ButtonState.Pressed && msNew.LeftButton == ButtonState.Released)
                        currentState.OnMouseUp(MouseButton.LeftButton, msNew.X, msNew.Y);
                    if (msOld.MiddleButton == ButtonState.Pressed && msNew.MiddleButton == ButtonState.Released)
                        currentState.OnMouseUp(MouseButton.MiddleButton, msNew.X, msNew.Y);
                    if (msOld.RightButton == ButtonState.Pressed && msNew.RightButton == ButtonState.Released)
                        currentState.OnMouseUp(MouseButton.RightButton, msNew.X, msNew.Y);
                    if (msOld.ScrollWheelValue != msNew.ScrollWheelValue)
                        currentState.OnMouseScroll(msNew.ScrollWheelValue - msOld.ScrollWheelValue);
                }
                msOld = msNew;
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            // Frames per second, measured over the last second.
            framesThisSecond += 1;
            frameTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (frameTimer >= 1)
            {
                frameRate = framesThisSecond / frameTimer;
                framesThisSecond = 0;
                frameTimer = 0;
            }

            GraphicsDevice.Clear(Color.Black);

            // Call OnRenderAtUpdate.
            if (currentState != null && propertyBag != null)
                currentState.OnRenderAtUpdate(GraphicsDevice, gameTime);

            // If we have one queued, call OnRenderAtEnter.
            if (currentState != null && needToRenderOnEnter && propertyBag != null)
            {
                needToRenderOnEnter = false;
                currentState.OnRenderAtEnter(GraphicsDevice);
            }

            base.Draw(gameTime);
        }
    }
}
