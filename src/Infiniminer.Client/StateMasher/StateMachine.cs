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

        // The controller, and whether it (rather than the keyboard and mouse) was used last; on-screen
        // prompts follow whichever the player is using.
        public readonly Infiniminer.GamePadInput GamePadInput = CreateGamePadInput();
        public bool UsingGamePad { get; private set; }

        // The on-screen keyboard, shown while a state takes text and the controller is in use.
        protected readonly Infiniminer.OnScreenKeyboard ScreenKeyboard = new Infiniminer.OnScreenKeyboard();
        protected bool KeyboardOpen { get; private set; }

        static Infiniminer.GamePadInput CreateGamePadInput()
        {
            Infiniminer.ScriptedGamePad scripted = Infiniminer.ScriptedGamePad.FromEnvironment();
            return scripted != null ? new Infiniminer.GamePadInput(scripted.Read) : new Infiniminer.GamePadInput();
        }

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

            Infiniminer.GameTrace.Write("STATE " + newState.Name);
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
                        {
                            UsingGamePad = false;
                            currentState.OnKeyDown(k);
                        }
                    foreach (Keys k in keysDown)
                        if (!keysDownNow.Contains(k))
                            currentState.OnKeyUp(k);
                }
                keysDown = keysDownNow;

                // Check for mouse events.
                MouseState msNew = Mouse.GetState();
                if (WindowHasFocus())
                {
                    bool clicked = (msOld.LeftButton == ButtonState.Released && msNew.LeftButton == ButtonState.Pressed)
                        || (msOld.RightButton == ButtonState.Released && msNew.RightButton == ButtonState.Pressed);
                    bool movedPointer = IsMouseVisible && (Math.Abs(msNew.X - msOld.X) > 3 || Math.Abs(msNew.Y - msOld.Y) > 3);
                    if (clicked || movedPointer)
                        UsingGamePad = false;
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

                UpdateGamePad(gameTime);
            }

            base.Update(gameTime);
        }

        void UpdateGamePad(GameTime gameTime)
        {
            GamePadInput.Update(gameTime.ElapsedGameTime.TotalSeconds);
            if (!WindowHasFocus())
                return;
            if (GamePadInput.HadInput)
                UsingGamePad = true;

            // Typing with the controller: the keyboard takes all of its input.
            if (currentState.TextInputPrompt != null && UsingGamePad)
            {
                if (!KeyboardOpen)
                {
                    ScreenKeyboard.Reset();
                    KeyboardOpen = true;
                    return;     // the press that started typing isn't a keystroke
                }
                foreach (Infiniminer.MenuDirection direction in GamePadInput.Navigation)
                    ScreenKeyboard.Move(direction);
                foreach (Buttons button in GamePadInput.Pressed)
                {
                    switch (ScreenKeyboard.Press(button, out char typed))
                    {
                        case Infiniminer.OnScreenKeyboard.Action.Type: currentState.OnTextInput(typed); break;
                        case Infiniminer.OnScreenKeyboard.Action.Delete: currentState.OnKeyDown(Keys.Back); break;
                        case Infiniminer.OnScreenKeyboard.Action.Done: currentState.OnKeyDown(Keys.Enter); break;
                        case Infiniminer.OnScreenKeyboard.Action.Cancel: currentState.OnKeyDown(Keys.Escape); break;
                    }
                    if (currentState.TextInputPrompt == null)
                        break;
                }
                return;
            }
            KeyboardOpen = false;

            // In menus the controller drives the keyboard controls: the D-pad or left stick are the arrow
            // keys, A is Enter and B is Escape.
            if (currentState.UsesMenuNavigation)
            {
                foreach (Infiniminer.MenuDirection direction in GamePadInput.Navigation)
                    currentState.OnKeyDown(direction switch
                    {
                        Infiniminer.MenuDirection.Up => Keys.Up,
                        Infiniminer.MenuDirection.Down => Keys.Down,
                        Infiniminer.MenuDirection.Left => Keys.Left,
                        _ => Keys.Right,
                    });
                if (GamePadInput.WasPressed(Buttons.A))
                    currentState.OnKeyDown(Keys.Enter);
                if (GamePadInput.WasPressed(Buttons.B))
                    currentState.OnKeyDown(Keys.Escape);
            }
            foreach (Buttons button in GamePadInput.Pressed)
                currentState.OnGamePadButtonDown(button);
        }

        protected virtual void DrawOnScreenKeyboard(string prompt)
        {
        }

        protected override void Draw(GameTime gameTime)
        {
            // Frames per second, measured over the last second.
            framesThisSecond += 1;
            frameTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (frameTimer >= 1)
            {
                frameRate = framesThisSecond / frameTimer;
                if (Infiniminer.GameTrace.Enabled && propertyBag?.blockEngine != null && currentState is Infiniminer.States.MainGameState)
                    Infiniminer.GameTrace.Write($"PERF {frameRate:0.0} fps, {propertyBag.blockEngine.DrawCalls} draw calls, {propertyBag.blockEngine.Triangles} triangles");
                framesThisSecond = 0;
                frameTimer = 0;
            }

            GraphicsDevice.Clear(Color.Black);

            // Call OnRenderAtUpdate.
            if (currentState != null && propertyBag != null)
                currentState.OnRenderAtUpdate(GraphicsDevice, gameTime);

            // The on-screen keyboard goes over everything.
            if (currentState != null && KeyboardOpen && currentState.TextInputPrompt != null && propertyBag != null)
                DrawOnScreenKeyboard(currentState.TextInputPrompt);

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
