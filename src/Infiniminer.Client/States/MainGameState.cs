using LiteNetLib.Utils;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StateMasher;

namespace Infiniminer.States
{
    public class MainGameState : State
    {
        const float MOVESPEED = GameRules.MoveSpeed;
        const float GRAVITY = GameRules.Gravity;
        const float JUMPVELOCITY = GameRules.JumpVelocity;
        const float CLIMBVELOCITY = GameRules.ClimbVelocity;
        const float DIEVELOCITY = GameRules.DieVelocity;

        Type nextState = null;
        bool mouseInitialized = false;
        bool escapePressedHere = false; // the menu opens when Escape is released, if it was pressed in this state
        bool escapeComboUsed = false;   // Escape+Y/O/K, or Escape to cancel chat: releasing it doesn't open the menu
        bool wasDead = true;
        ScreenEffect lastEffect = ScreenEffect.None;

        GamePadInput Pad
        {
            get { return _SM.GamePadInput; }
        }

        // The controller's buttons are game actions here, not menu navigation.
        public override bool UsesMenuNavigation
        {
            get { return false; }
        }

        public override void OnEnter(Type oldState)
        {
            _SM.IsMouseVisible = false;
            wasDead = _P.player.Dead;
            lastEffect = _P.screenEffect;
        }

        public override void OnLeave(Type newState)
        {
            _P.chatEntryBuffer = "";
            _P.chatMode = ChatMessageType.None;
        }

        public override Type OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            // Update network stuff.
            Game.UpdateNetwork(gameTime);

            // Undo predicted block changes the server never confirmed.
            _P.currentTime = gameTime.TotalGameTime.TotalSeconds;
            _P.blockPredictor.Update(_P.currentTime);

            // Update the current screen effect.
            _P.screenEffectCounter += gameTime.ElapsedGameTime.TotalSeconds;
            _P.playerCamera.FieldOfView = Game.Settings.FieldOfView;

            // Update engines.
            _P.skyplaneEngine.Update(gameTime);
            _P.playerEngine.Update(gameTime);
            _P.interfaceEngine.Update(gameTime);
            _P.particleEngine.Update(gameTime);

            // Count down the tool cooldown.
            if (_P.player.ToolCooldown > 0)
            {
                _P.player.ToolCooldown -= (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (_P.player.ToolCooldown <= 0)
                    _P.player.ToolCooldown = 0;
            }

            // Moving the mouse changes where we look.
            if (_SM.WindowHasFocus())
            {
                if (mouseInitialized)
                {
                    int dx = mouseState.X - _SM.GraphicsDevice.Viewport.Width / 2;
                    int dy = mouseState.Y - _SM.GraphicsDevice.Viewport.Height / 2;

                    if (Game.InvertMouseYAxis)
                        dy = -dy;

                    // Sensitivity 5 is the original game's 0.005 radians per pixel.
                    float sensitivity = Game.Settings.MouseSensitivity * 0.001f;
                    _P.playerCamera.Yaw -= dx * sensitivity;
                    _P.playerCamera.Pitch = (float)Math.Min(Math.PI * 0.49, Math.Max(-Math.PI * 0.49, _P.playerCamera.Pitch - dy * sensitivity));
                }
                else
                {
                    mouseInitialized = true;
                }
                Mouse.SetPosition(_SM.GraphicsDevice.Viewport.Width / 2, _SM.GraphicsDevice.Viewport.Height / 2);
            }
            else
                mouseInitialized = false;

            // So does the right stick.
            if (_SM.WindowHasFocus() && Pad.RightStick != Vector2.Zero)
            {
                var (yaw, pitch) = ControllerLook.Turn(Pad.RightStick, Game.Settings.ControllerSensitivity, Game.InvertMouseYAxis,
                    (float)gameTime.ElapsedGameTime.TotalSeconds);
                _P.playerCamera.Yaw += yaw;
                _P.playerCamera.Pitch = (float)Math.Clamp(_P.playerCamera.Pitch + pitch, -Math.PI * 0.49, Math.PI * 0.49);
            }

            // Digging like a freaking terrier! Now for everyone!
            bool digging = (mouseInitialized && mouseState.LeftButton == ButtonState.Pressed)
                || (_SM.WindowHasFocus() && Pad.IsDown(Buttons.RightTrigger));
            if (digging && !_P.player.Dead && _P.player.ToolCooldown == 0)
            {
                if (_P.player.SelectedTool == PlayerTools.Pickaxe)
                    _P.FirePickaxe();
                else if (_P.player.SelectedTool == PlayerTools.Drill)
                    _P.FireDrill();
            }

            // Prospector radar stuff.
            if (!_P.player.Dead && _P.player.ToolCooldown == 0 && _P.player.SelectedTool == PlayerTools.ProspectingRadar)
            {
                float oldValue = _P.player.RadarValue;
                _P.ReadRadar(ref _P.player.RadarDistance, ref _P.player.RadarValue);
                if (_P.player.RadarValue != oldValue)
                {
                    if (_P.player.RadarValue == 200)
                        _P.PlaySound(InfiniminerSound.RadarLow);
                    if (_P.player.RadarValue == 1000)
                        _P.PlaySound(InfiniminerSound.RadarHigh);   
                }
            }

            // Update the player"s position.
            if (!_P.player.Dead)
                UpdatePlayerPosition(gameTime, keyState);
            
            // Update the camera regardless of if we"re alive or not.
            _P.UpdateCamera(gameTime);

            // The controller rumbles for deaths, hard landings and nearby explosions.
            if (_P.player.Dead && !wasDead)
                Rumble(0.8f, 0.5);
            else if (_P.screenEffect != lastEffect && _P.screenEffect == ScreenEffect.Explosion)
                Rumble(0.6f, 0.6);
            else if (_P.screenEffect != lastEffect && _P.screenEffect == ScreenEffect.Fall)
                Rumble(0.5f, 0.25);
            wasDead = _P.player.Dead;
            lastEffect = _P.screenEffect;

            return nextState;
        }

        private void UpdatePlayerPosition(GameTime gameTime, KeyboardState keyState)
        {
            // Double-speed move flag, set if we're on road.
            bool movingOnRoad = false;

            // Apply "gravity".
            _P.player.Velocity.Y += GRAVITY * (float)gameTime.ElapsedGameTime.TotalSeconds;
            Vector3 footPosition = _P.player.Position + new Vector3(0f, -1.5f, 0f);
            Vector3 headPosition = _P.player.Position + new Vector3(0f, 0.1f, 0f);
            if (_P.blockEngine.SolidAtPointForPlayer(footPosition) || _P.blockEngine.SolidAtPointForPlayer(headPosition))
            {
                BlockType standingOnBlock = _P.blockEngine.BlockAtPoint(footPosition);
                BlockType hittingHeadOnBlock = _P.blockEngine.BlockAtPoint(headPosition);
                
                // If we"re hitting the ground with a high velocity, die!
                if (standingOnBlock != BlockType.None && _P.player.Velocity.Y < 0)
                {
                    float fallDamage = Math.Abs(_P.player.Velocity.Y) / DIEVELOCITY;
                    if (fallDamage >= 1)
                    {
                        _P.PlaySoundForEveryone(InfiniminerSound.GroundHit, _P.player.Position);
                        _P.KillPlayer("WAS KILLED BY GRAVITY!");
                        return;
                    }
                    else if (fallDamage > 0.5)
                    {
                        // Fall damage of 0.5 maps to a screenEffectCounter value of 2, meaning that the effect doesn"t appear.
                        // Fall damage of 1.0 maps to a screenEffectCounter value of 0, making the effect very strong.
                        _P.screenEffect = ScreenEffect.Fall;
                        _P.screenEffectCounter = 2 - (fallDamage - 0.5) * 4;
                        _P.PlaySoundForEveryone(InfiniminerSound.GroundHit, _P.player.Position);
                    }
                }

                // If the player has their head stuck in a block, push them down.
                if (_P.blockEngine.SolidAtPointForPlayer(headPosition))
                {
                    int blockIn = (int)(headPosition.Y);
                    _P.player.Position.Y = (float)(blockIn - 0.15f);
                }
                
                // If the player is stuck in the ground, bring them out.
                // This happens because we"re standing on a block at -1.5, but stuck in it at -1.4, so -1.45 is the sweet spot.
                if (_P.blockEngine.SolidAtPointForPlayer(footPosition))
                {
                    int blockOn = (int)(footPosition.Y);
                    _P.player.Position.Y = (float)(blockOn + 1 + 1.45);
                }
                
                _P.player.Velocity.Y = 0;

                // Logic for standing on a block.
                switch (standingOnBlock)
                {
                    case BlockType.Jump:
                        _P.player.Velocity.Y = 2.5f * JUMPVELOCITY;
                        _P.PlaySoundForEveryone(InfiniminerSound.Jumpblock, _P.player.Position);
                        break;

                    case BlockType.Road:
                        movingOnRoad = true;
                        break;

                    //case BlockType.Teleporter:
                    //    if (!_P.player.Dead)
                    //    {
                    //        _P.Teleport();
                    //        _P.PlaySoundForEveryone(InfiniminerSound.Teleporter, _P.player.Position);
                    //    }
                    //    break;

                    //case BlockType.Shock:
                    //    _P.KillPlayer("WAS ELECTROCUTED!");
                    //    return;

                    case BlockType.Lava:
                        _P.KillPlayer("WAS INCINERATED BY LAVA!");
                        return;
                }

                // Logic for bumping your head on a block.
                switch (hittingHeadOnBlock)
                {
                    case BlockType.Shock:
                        _P.KillPlayer("WAS ELECTROCUTED!");
                        return;

                    case BlockType.Lava:
                        _P.KillPlayer("WAS INCINERATED BY LAVA!");
                        return;
                }
            }                
            _P.player.Position += _P.player.Velocity * (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Death by falling off the map.
            if (_P.player.Position.Y < -30)
            {
                _P.KillPlayer("WAS KILLED BY MISADVENTURE!");
                return;
            }

            // Pressing forward moves us in the direction we"re looking.
            Vector3 moveVector = Vector3.Zero;
            float moveSpeed = 1;

            if (_P.chatMode == ChatMessageType.None)
            {
                if (Game.Settings.IsDown(keyState, GameAction.MoveForward))
                    moveVector += _P.playerCamera.GetLookVector();
                if (Game.Settings.IsDown(keyState, GameAction.MoveBack))
                    moveVector -= _P.playerCamera.GetLookVector();
                if (Game.Settings.IsDown(keyState, GameAction.MoveRight))
                    moveVector += _P.playerCamera.GetRightVector();
                if (Game.Settings.IsDown(keyState, GameAction.MoveLeft))
                    moveVector -= _P.playerCamera.GetRightVector();

                // The left stick walks at any speed up to a run.
                if (moveVector.X == 0 && moveVector.Z == 0 && _SM.WindowHasFocus())
                    (moveVector, moveSpeed) = ControllerLook.Walk(Pad.LeftStick, _P.playerCamera.GetLookVector(), _P.playerCamera.GetRightVector());
            }
            
            if (moveVector.X != 0 || moveVector.Z != 0)
            {
                // "Flatten" the movement vector so that we don"t move up/down.
                moveVector.Y = 0;
                moveVector.Normalize();
                moveVector *= MOVESPEED * moveSpeed * (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (movingOnRoad)
                    moveVector *= 2;

                // Attempt to move, doing collision stuff.
                if (TryToMoveTo(moveVector, gameTime)) { }
                else if (!TryToMoveTo(new Vector3(0, 0, moveVector.Z), gameTime)) { }
                else if (!TryToMoveTo(new Vector3(moveVector.X, 0, 0), gameTime)) { }
            }
        }

        private bool TryToMoveTo(Vector3 moveVector, GameTime gameTime)
        {
            Vector3 target = _P.player.Position + moveVector;
            List<BlockType> obstacles = PlayerCollision.Obstacles(_P.blockEngine.BlockAtPoint,
                block => !_P.blockEngine.SolidAtPointForPlayer(block), _P.player.Position, target);
            if (obstacles.Count == 0)
            {
                _P.player.Position = target;
                return true;
            }

            // It's solid there, so see if it's a lava block. If so, touching it will kill us!
            if (obstacles.Contains(BlockType.Lava))
            {
                _P.KillPlayer("WAS INCINERATED BY LAVA!");
                return true;
            }

            // If it's a ladder, move up.
            if (obstacles.Contains(BlockType.Ladder))
            {
                _P.player.Velocity.Y = CLIMBVELOCITY;
                Vector3 footPosition = _P.player.Position + new Vector3(0f, -1.5f, 0f);
                if (_P.blockEngine.SolidAtPointForPlayer(footPosition))
                    _P.player.Position.Y += 0.1f;
                return true;
            }

            return false;
        }

        public override void OnRenderAtEnter(GraphicsDevice graphicsDevice)
        {

        }

        public override void OnRenderAtUpdate(GraphicsDevice graphicsDevice, GameTime gameTime)
        {
            // In pretty mode the 3D scene is rendered offscreen and bloomed; names and the UI go on top.
            Game.Bloom?.BeginScene();
            _P.skyplaneEngine.Render(graphicsDevice);
            _P.particleEngine.Render(graphicsDevice);
            _P.playerEngine.Render(graphicsDevice);
            _P.blockEngine.Render(graphicsDevice, gameTime);
            Game.Bloom?.EndScene();
            _P.playerEngine.RenderPlayerNames(graphicsDevice);
            _P.interfaceEngine.Render(graphicsDevice);

            _SM.Window.Title = "Infiniminer";
        }

        public override void OnKeyDown(Keys key)
        {
            // Escape on its own opens the menu when it is released (see OnKeyUp); with Y, O or K it is a shortcut.
            if (key == Keys.Escape)
            {
                escapePressedHere = true;
                escapeComboUsed = _P.chatMode != ChatMessageType.None;
            }
            else if (Keyboard.GetState().IsKeyDown(Keys.Escape) && (key == Keys.Y || key == Keys.O || key == Keys.K))
                escapeComboUsed = true;

            // Exit!
            if (key == Keys.Y && Keyboard.GetState().IsKeyDown(Keys.Escape))
            {
                Game.LeaveServer();
                nextState = typeof(ServerBrowserState);
                return;
            }

            // Options!
            if (key == Keys.O && Keyboard.GetState().IsKeyDown(Keys.Escape))
            {
                nextState = typeof(SettingsState);
                return;
            }

            // Pixelcide!
            if (key == Keys.K && Keyboard.GetState().IsKeyDown(Keys.Escape) && !_P.player.Dead)
            {
                _P.KillPlayer("HAS COMMMITTED PIXELCIDE!");
            }

            if (_P.chatMode != ChatMessageType.None)
            {
                // Put the characters in the chat buffer.
                if (key == Keys.Enter)
                {
                    // If we have an actual message to send, fire it off at the server.
                    if (_P.chatEntryBuffer.Length > 0)
                    {
                        _P.Send(ClientMessages.Chat(_P.chatMode, _P.chatEntryBuffer));
                    }
                    
                    _P.chatEntryBuffer = "";
                    _P.chatMode = ChatMessageType.None;
                }
                else if (key == Keys.Back)
                {
                    if (_P.chatEntryBuffer.Length > 0)
                        _P.chatEntryBuffer = _P.chatEntryBuffer.Substring(0, _P.chatEntryBuffer.Length - 1);
                }
                else if (key == Keys.Escape)
                {
                    _P.chatEntryBuffer = "";
                    _P.chatMode = ChatMessageType.None;
                }
                // Typed characters arrive through OnTextInput.
                return;
            }

            if (key == Game.Settings.Key(GameAction.SayAll))
                _P.chatMode = ChatMessageType.SayAll;

            if (key == Game.Settings.Key(GameAction.SayTeam))
                _P.chatMode = _P.player.Team == PlayerTeam.Red ? ChatMessageType.SayRedTeam : ChatMessageType.SayBlueTeam;

            if (!_P.player.Dead)
            {
                if (key == Game.Settings.Key(GameAction.Jump))
                    Jump();
                if (key == Game.Settings.Key(GameAction.NextTool))
                    CycleTool(1);
                if (key == Game.Settings.Key(GameAction.NextBlock))
                    CycleBlock(1);
                if (key == Game.Settings.Key(GameAction.DepositOre))
                    DepositOre();
                if (key == Game.Settings.Key(GameAction.WithdrawOre))
                    WithdrawOre();
                if (key == Game.Settings.Key(GameAction.Ping))
                    Ping();

                // Change class.
                if (key == Game.Settings.Key(GameAction.ChangeClass) && _P.player.Position.Y > _P.SurfaceHeight)
                    nextState = typeof(ClassSelectionState);

                // Change team.
                if (key == Game.Settings.Key(GameAction.ChangeTeam))
                    nextState = typeof(TeamSelectionState);
            }
        }

        void Jump()
        {
            Vector3 footPosition = _P.player.Position + new Vector3(0f, -1.5f, 0f);
            if (_P.blockEngine.SolidAtPointForPlayer(footPosition) && _P.player.Velocity.Y == 0)
            {
                _P.player.Velocity.Y = JUMPVELOCITY;
                float amountBelowSurface = ((ushort)footPosition.Y) + 1 - footPosition.Y;
                _P.player.Position.Y += amountBelowSurface + 0.01f;
            }
        }

        void CycleTool(int direction)
        {
            _P.PlaySound(InfiniminerSound.ClickLow);
            _P.player.CycleTool(direction);
        }

        void CycleBlock(int direction)
        {
            if (_P.player.CycleBlock(direction))
                _P.PlaySound(InfiniminerSound.ClickLow);
        }

        void DepositOre()
        {
            if (!_P.AtBankTerminal())
                return;
            _P.DepositOre();
            _P.PlaySound(InfiniminerSound.ClickHigh);
        }

        void WithdrawOre()
        {
            if (!_P.AtBankTerminal())
                return;
            _P.WithdrawOre();
            _P.PlaySound(InfiniminerSound.ClickHigh);
        }

        void Ping()
        {
            _P.Send(ClientMessages.Ping(_P.player.Id));
        }

        void Rumble(float strength, double seconds)
        {
            if (Game.Settings.Vibration && _SM.UsingGamePad)
                Pad.Rumble(strength, seconds);
        }

        public override string TextInputPrompt
        {
            get
            {
                if (_P.chatMode == ChatMessageType.None)
                    return null;
                return (_P.chatMode == ChatMessageType.SayAll ? "SAY TO ALL: " : "SAY TO TEAM: ") + _P.chatEntryBuffer + "_";
            }
        }

        public override void OnGamePadButtonDown(Buttons button)
        {
            // While typing a chat message the controller drives the on-screen keyboard.
            if (_P.chatMode != ChatMessageType.None)
                return;

            // Chat with the on-screen keyboard: right stick click for everyone, left stick click for the team.
            if (button == Buttons.RightStick)
            {
                _P.chatMode = ChatMessageType.SayAll;
                return;
            }
            if (button == Buttons.LeftStick)
            {
                _P.chatMode = _P.player.Team == PlayerTeam.Red ? ChatMessageType.SayRedTeam : ChatMessageType.SayBlueTeam;
                return;
            }

            if (button == Buttons.Start)
            {
                nextState = typeof(GameMenuState);
                return;
            }

            if (_P.player.Dead)
            {
                if (button == Buttons.A || button == Buttons.RightTrigger)
                    TryRespawn();
                return;
            }

            // LT + D-pad: quick team chat.
            if (Pad.IsDown(QuickChat.Modifier))
            {
                string message = QuickChat.MessageFor(button);
                if (message != null)
                {
                    _P.Send(ClientMessages.Chat(_P.player.Team == PlayerTeam.Red ? ChatMessageType.SayRedTeam : ChatMessageType.SayBlueTeam, message));
                    return;
                }
            }

            switch (button)
            {
                case Buttons.A: Jump(); break;
                case Buttons.RightTrigger: UseTool(); break;
                case Buttons.RightShoulder:
                case Buttons.DPadDown: CycleTool(1); break;
                case Buttons.DPadUp: CycleTool(-1); break;
                case Buttons.LeftShoulder:
                case Buttons.DPadRight: CycleBlock(1); break;
                case Buttons.DPadLeft: CycleBlock(-1); break;
                case Buttons.X: DepositOre(); break;
                case Buttons.B: WithdrawOre(); break;
                case Buttons.Y: Ping(); break;
            }
        }

        public override void OnTextInput(char character)
        {
            if (_P.chatMode != ChatMessageType.None)
                _P.chatEntryBuffer += character;
        }

        public override void OnKeyUp(Keys key)
        {
            // (A release left over from closing another menu with Escape doesn't count.)
            if (key == Keys.Escape && escapePressedHere && !escapeComboUsed && nextState == null)
                nextState = typeof(GameMenuState);
            if (key == Keys.Escape)
                escapePressedHere = false;
        }

        public override void OnMouseDown(MouseButton button, int x, int y)
        {
            if (_P.player.Dead)
                TryRespawn();
            else
                UseTool();
        }

        // Fires the selected tool (the pickaxe and drill dig while the button is held; see OnUpdate).
        void UseTool()
        {
            if (_P.player.ToolCooldown != 0)
                return;
            switch (_P.player.SelectedTool)
            {
                case PlayerTools.ConstructionGun:
                    _P.FireConstructionGun(_P.player.SelectedBlock);
                    break;

                case PlayerTools.DeconstructionGun:
                    _P.FireDeconstructionGun();
                    break;

                case PlayerTools.Detonator:
                    _P.PlaySound(InfiniminerSound.ClickHigh);
                    _P.FireDetonator();
                    break;

                case PlayerTools.ProspectingRadar:
                    _P.FireRadar();
                    break;
            }
        }

        // Come back to life once the death screen has had a moment.
        void TryRespawn()
        {
            if (_P.player.Dead && !_P.player.RespawnPending && _P.screenEffectCounter > 2)
                _P.RespawnPlayer();
        }

        public override void OnMouseUp(MouseButton button, int x, int y)
        {
            
        }

        public override void OnMouseScroll(int scrollDelta)
        {
            if (_P.player.Dead)
                return;
            if (scrollDelta > 0)
                CycleBlock(1);
            if (scrollDelta < 0)
                CycleBlock(-1);
        }
    }
}
