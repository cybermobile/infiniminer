using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Infiniminer
{
    public class InterfaceEngine
    {
        InfiniminerGame gameInstance;
        PropertyBag _P;
        SpriteBatch spriteBatch;
        SpriteFontBase uiFont, radarFont;
        Rectangle drawRect;

        Texture2D texCrosshairs, texBlank, texHelp;
        Texture2D texRadarBackground, texRadarForeground, texRadarPlayerSame, texRadarPlayerAbove, texRadarPlayerBelow, texRadarPlayerPing, texRadarNorth;
        Texture2D texToolRadarRed, texToolRadarBlue, texToolRadarGold, texToolRadarDiamond, texToolRadarLED, texToolRadarPointer, texToolRadarFlash;
        Texture2D texToolDetonatorDownRed, texToolDetonatorUpRed, texToolDetonatorDownBlue, texToolDetonatorUpBlue;
        Texture2D texToolBuild, texToolBuildCharge, texToolBuildBlast, texToolBuildSmoke;

        Dictionary<BlockType, Texture2D> blockIcons = new Dictionary<BlockType, Texture2D>();

        // The interface is laid out on a virtual screen (the window divided by the HUD scale) and scaled up.
        int ScreenWidth
        {
            get { return gameInstance.GraphicsDevice.Viewport.Width / gameInstance.HudScale; }
        }

        int ScreenHeight
        {
            get { return gameInstance.GraphicsDevice.Viewport.Height / gameInstance.HudScale; }
        }

        public InterfaceEngine(InfiniminerGame gameInstance)
        {
            this.gameInstance = gameInstance;
            spriteBatch = gameInstance.SpriteBatch;
            Assets assets = gameInstance.Assets;

            // Load textures.
            texCrosshairs = assets.Texture("ui/tex_ui_crosshair");
            texBlank = new Texture2D(gameInstance.GraphicsDevice, 1, 1);
            texBlank.SetData(new uint[1] { 0xFFFFFFFF });
            texRadarBackground = assets.Texture("ui/tex_radar_background");
            texRadarForeground = assets.Texture("ui/tex_radar_foreground");
            texRadarPlayerSame = assets.Texture("ui/tex_radar_player_same");
            texRadarPlayerAbove = assets.Texture("ui/tex_radar_player_above");
            texRadarPlayerBelow = assets.Texture("ui/tex_radar_player_below");
            texRadarPlayerPing = assets.Texture("ui/tex_radar_player_ping");
            texRadarNorth = assets.Texture("ui/tex_radar_north");
            texHelp = assets.Texture("menus/tex_menu_help");

            texToolRadarRed = assets.Texture("tools/tex_tool_radar_red");
            texToolRadarBlue = assets.Texture("tools/tex_tool_radar_blue");
            texToolRadarGold = assets.Texture("tools/tex_tool_radar_screen_gold");
            texToolRadarDiamond = assets.Texture("tools/tex_tool_radar_screen_diamond");
            texToolRadarLED = assets.Texture("tools/tex_tool_radar_led");
            texToolRadarPointer = assets.Texture("tools/tex_tool_radar_pointer");
            texToolRadarFlash = assets.Texture("tools/tex_tool_radar_flash");

            texToolBuild = assets.Texture("tools/tex_tool_build");
            texToolBuildCharge = assets.Texture("tools/tex_tool_build_charge");
            texToolBuildBlast = assets.Texture("tools/tex_tool_build_blast");
            texToolBuildSmoke = assets.Texture("tools/tex_tool_build_smoke");

            texToolDetonatorDownRed = assets.Texture("tools/tex_tool_detonator_down_red");
            texToolDetonatorUpRed = assets.Texture("tools/tex_tool_detonator_up_red");
            texToolDetonatorDownBlue = assets.Texture("tools/tex_tool_detonator_down_blue");
            texToolDetonatorUpBlue = assets.Texture("tools/tex_tool_detonator_up_blue");

            drawRect = new Rectangle(ScreenWidth / 2 - 1024 / 2,
                                     ScreenHeight / 2 - 768 / 2,
                                     1024,
                                     1024);

            // Load icons.
            blockIcons[BlockType.BankBlue] = assets.Texture("icons/tex_icon_bank_blue");
            blockIcons[BlockType.BankRed] = assets.Texture("icons/tex_icon_bank_red");
            blockIcons[BlockType.Explosive] = assets.Texture("icons/tex_icon_explosive");
            blockIcons[BlockType.Jump] = assets.Texture("icons/tex_icon_jump");
            blockIcons[BlockType.Ladder] = assets.Texture("icons/tex_icon_ladder");
            blockIcons[BlockType.SolidBlue] = assets.Texture("icons/tex_icon_solid_blue");
            blockIcons[BlockType.SolidRed] = assets.Texture("icons/tex_icon_solid_red");
            blockIcons[BlockType.Shock] = assets.Texture("icons/tex_icon_spikes");
            blockIcons[BlockType.TransBlue] = assets.Texture("icons/tex_icon_translucent_blue");
            blockIcons[BlockType.TransRed] = assets.Texture("icons/tex_icon_translucent_red");
            blockIcons[BlockType.BeaconRed] = assets.Texture("icons/tex_icon_beacon");
            blockIcons[BlockType.BeaconBlue] = assets.Texture("icons/tex_icon_beacon");
            blockIcons[BlockType.Road] = assets.Texture("icons/tex_icon_road");
            blockIcons[BlockType.Teleporter] = assets.Texture("icons/tex_icon_teleporter");
            blockIcons[BlockType.None] = assets.Texture("icons/tex_icon_deconstruction");
            // Sandbox building: the world's own blocks, shown by their textures.
            blockIcons[BlockType.Dirt] = assets.Texture("blocks/tex_block_dirt");
            blockIcons[BlockType.Rock] = assets.Texture("blocks/tex_block_rock");
            blockIcons[BlockType.Ore] = assets.Texture("blocks/tex_block_ore");
            blockIcons[BlockType.Gold] = assets.Texture("blocks/tex_block_silver");
            blockIcons[BlockType.Diamond] = assets.Texture("blocks/tex_block_diamond");
            blockIcons[BlockType.Lava] = assets.Texture("blocks/tex_block_lava");
            blockIcons[BlockType.Metal] = assets.Texture("blocks/tex_block_metal");

            // Load fonts.
            uiFont = assets.UiFont;
            radarFont = assets.RadarFont;
        }

        public void RenderMessageCenter(SpriteBatch spriteBatch, string text, Vector2 pointCenter, Color colorText, Color colorBackground)
        {
            Vector2 textSize = uiFont.MeasureString(text);
            spriteBatch.Draw(texBlank, new Rectangle((int)(pointCenter.X - textSize.X / 2 - 10), (int)(pointCenter.Y - textSize.Y / 2 - 10), (int)(textSize.X + 20), (int)(textSize.Y + 20)), colorBackground);
            spriteBatch.DrawString(uiFont, text, pointCenter - textSize / 2, colorText);
        }

        double currentTime = 0;

        public void Update(GameTime gameTime)
        {
            currentTime = gameTime.TotalGameTime.TotalSeconds;
            if (_P == null)
                return;

            _P.chatLog.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

            if (_P.player.ConstructionGunAnimation > 0)
            {
                if (_P.player.ConstructionGunAnimation > gameTime.ElapsedGameTime.TotalSeconds)
                    _P.player.ConstructionGunAnimation -= (float)gameTime.ElapsedGameTime.TotalSeconds;
                else
                    _P.player.ConstructionGunAnimation = 0;
            }
            else
            {
                if (_P.player.ConstructionGunAnimation < -gameTime.ElapsedGameTime.TotalSeconds)
                    _P.player.ConstructionGunAnimation += (float)gameTime.ElapsedGameTime.TotalSeconds;
                else
                    _P.player.ConstructionGunAnimation = 0;
            }
        }

        public void RenderRadarBlip(SpriteBatch spriteBatch, Vector3 position, Color color, bool ping, string text)
        {
            // Figure out the relative position for the radar blip.
            Vector3 relativePosition = position - _P.player.Position;
            float relativeAltitude = relativePosition.Y;
            relativePosition.Y = 0;
            Matrix rotationMatrix = Matrix.CreateRotationY(-_P.playerCamera.Yaw);
            relativePosition = Vector3.Transform(relativePosition, rotationMatrix) * 10;
            float relativeLength = Math.Min(relativePosition.Length(), 93);
            if (relativeLength != 0)
                relativePosition.Normalize();
            relativePosition *= relativeLength;

            // Draw the radar blip.
            if (text == "")
            {
                relativePosition.X = (int)relativePosition.X;
                relativePosition.Z = (int)relativePosition.Z;
                Texture2D texRadarSprite = texRadarPlayerSame;
                if (relativeAltitude > 2)
                    texRadarSprite = texRadarPlayerAbove;
                else if (relativeAltitude < -2)
                    texRadarSprite = texRadarPlayerBelow;
                spriteBatch.Draw(texRadarSprite, new Vector2(10 + 99 + relativePosition.X - texRadarSprite.Width / 2, 30 + 99 + relativePosition.Z - texRadarSprite.Height / 2), color);
                if (ping)
                    spriteBatch.Draw(texRadarPlayerPing, new Vector2(10 + 99 + relativePosition.X - texRadarPlayerPing.Width / 2, 30 + 99 + relativePosition.Z - texRadarPlayerPing.Height / 2), color);
            }
            
            // Render text.
            if (text != "")
            {
                relativePosition *= 0.9f;
                relativePosition.X = (int)relativePosition.X;
                relativePosition.Z = (int)relativePosition.Z;

                if (text == "NORTH")
                {
                    spriteBatch.Draw(texRadarNorth, new Vector2(10 + 99 + relativePosition.X - texRadarNorth.Width / 2, 30 + 99 + relativePosition.Z - texRadarNorth.Height / 2), color);
                }
                else
                {
                    if (relativeAltitude > 2)
                        text += " ^";
                    else if (relativeAltitude < -2)
                        text += " v";
                    Vector2 textSize = radarFont.MeasureString(text);
                    spriteBatch.DrawString(radarFont, text, new Vector2(10 + 99 + relativePosition.X - textSize.X / 2, 30 + 99 + relativePosition.Z - textSize.Y / 2), color);
                }
            }
        }

        public void RenderDetonator(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch)
        {
            int screenWidth = ScreenWidth;
            int screenHeight = ScreenHeight;

            Texture2D textureToUse;
            if (Mouse.GetState().LeftButton == ButtonState.Pressed || Mouse.GetState().MiddleButton == ButtonState.Pressed || Mouse.GetState().RightButton == ButtonState.Pressed)
                textureToUse = _P.player.Team == PlayerTeam.Red ? texToolDetonatorDownRed : texToolDetonatorDownBlue;
            else
                textureToUse = _P.player.Team == PlayerTeam.Red ? texToolDetonatorUpRed : texToolDetonatorUpBlue;

            spriteBatch.Draw(textureToUse, new Rectangle(screenWidth / 2 /*- 22 * 3*/, screenHeight - 77 * 3 + 14 * 3, 75 * 3, 77 * 3), Color.White);
        }

        public void RenderProspectron(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch)
        {
            int screenWidth = ScreenWidth;
            int screenHeight = ScreenHeight;

            int drawX = screenWidth / 2 - 32 * 3;
            int drawY = screenHeight - 102 * 3;

            spriteBatch.Draw(_P.player.Team == PlayerTeam.Red ? texToolRadarRed : texToolRadarBlue, new Rectangle(drawX, drawY, 70 * 3, 102 * 3), Color.White);

            if (_P.player.RadarValue > 0)
                spriteBatch.Draw(texToolRadarLED, new Rectangle(drawX, drawY, 70 * 3, 102 * 3), Color.White);
            if (_P.player.RadarValue == 200)
                spriteBatch.Draw(texToolRadarGold, new Rectangle(drawX, drawY, 70 * 3, 102 * 3), Color.White);
            if (_P.player.RadarValue == 1000)
                spriteBatch.Draw(texToolRadarDiamond, new Rectangle(drawX, drawY, 70 * 3, 102 * 3), Color.White);
            if (_P.player.ToolCooldown > 0.2f)
                spriteBatch.Draw(texToolRadarFlash, new Rectangle(drawX, drawY, 70 * 3, 102 * 3), Color.White);

            int pointerOffset = (int)(30 - _P.player.RadarDistance) / 2;  // ranges from 0 to 15 inclusive
            if (_P.player.RadarDistance == 30)
                pointerOffset = 15;
            spriteBatch.Draw(texToolRadarPointer, new Rectangle(drawX + 54 * 3, drawY + 20 * 3 + pointerOffset * 3, 4 * 3, 5 * 3), Color.White);
        }

        public void RenderConstructionGun(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, BlockType blockType)
        {
            int screenWidth = ScreenWidth;
            int screenHeight = ScreenHeight;

            int drawX = screenWidth / 2 - 60 * 3;
            int drawY = screenHeight - 91 * 3;

            Texture2D gunSprite = texToolBuild;
            if (_P.player.ConstructionGunAnimation < -0.001)
                gunSprite = texToolBuildCharge;
            else if (_P.player.ConstructionGunAnimation > 0.3)
                gunSprite = texToolBuildBlast;
            else if (_P.player.ConstructionGunAnimation > 0.001)
                gunSprite = texToolBuildSmoke;
            spriteBatch.Draw(gunSprite, new Rectangle(drawX, drawY, 120 * 3, 126 * 3), Color.White);
            spriteBatch.Draw(blockIcons[blockType], new Rectangle(drawX+37*3, drawY+50*3, 117, 63), Color.White);
        }

        // Splits text into lines no wider than `width`.
        List<string> Wrap(string text, float width)
        {
            List<string> lines = new List<string>();
            string line = "";
            foreach (string word in text.Split(' '))
            {
                string longer = line == "" ? word : line + " " + word;
                if (line != "" && uiFont.MeasureString(longer).X > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                    line = longer;
            }
            if (line != "")
                lines.Add(line);
            return lines;
        }

        GuideControls TipControls()
        {
            if (gameInstance.UsingGamePad)
                return new GuideControls("RT", "RB", "LB", "BACK");
            ClientSettings settings = gameInstance.Settings;
            return new GuideControls("LEFT CLICK", ClientSettings.KeyName(settings.Key(GameAction.NextTool)),
                ClientSettings.KeyName(settings.Key(GameAction.NextBlock)), ClientSettings.KeyName(settings.Key(GameAction.Scoreboard)));
        }

        PlayerTools lastTool;
        BlockType lastBlock;
        double equipmentChangedAt = double.NegativeInfinity;

        // What's in hand, at the bottom of the screen: the tool, and the construction gun's block and its price. With
        // tips on, what it does is shown for a few seconds after changing.
        void RenderEquipment(SpriteBatch spriteBatch)
        {
            LocalPlayer player = _P.player;
            PlayerTools tool = player.SelectedTool;
            BlockType block = tool == PlayerTools.ConstructionGun ? player.SelectedBlock : BlockType.None;
            if (tool != lastTool || block != lastBlock)
            {
                lastTool = tool;
                lastBlock = block;
                equipmentChangedAt = currentTime;
            }

            string label = BlockGuide.ToolName(tool);
            Color color = Color.White;
            string explanation = BlockGuide.ToolDescription(tool, player.Sandbox);
            if (tool == PlayerTools.ConstructionGun)
            {
                uint cost = player.BuildCost(block);
                label += ": " + BlockGuide.Name(block) + (cost == 0 ? " (FREE)" : " (" + cost + " ORE)");
                if (player.Ore < cost)
                {
                    label += " - NOT ENOUGH ORE";
                    color = InfiniminerGame.IM_RED;
                }
                explanation = BlockGuide.Description(block, player.Team, player.Class);
            }
            RenderMessageCenter(spriteBatch, label, new Vector2(ScreenWidth / 2, ScreenHeight - 20), color, Color.Black);
            if (gameInstance.Settings.ShowTips && currentTime - equipmentChangedAt < 4 && explanation != "")
                RenderMessageCenter(spriteBatch, explanation, new Vector2(ScreenWidth / 2, ScreenHeight - 52), Color.White, new Color(Color.Black, 0.75f));
        }

        // The guide's current tip, near the top of the screen, and the name of the block under the crosshair.
        void RenderTips(SpriteBatch spriteBatch)
        {
            if (!gameInstance.Settings.ShowTips || _P.player.Dead || _P.player.Spectating)
                return;

            // What the crosshair is on (not plain dirt, which is everywhere).
            BlockType looking = _P.LookingAt();
            if (looking != BlockType.None && looking != BlockType.Dirt && looking != BlockType.DirtSign)
            {
                string description = BlockGuide.Description(looking, _P.player.Team, _P.player.Class);
                RenderMessageCenter(spriteBatch, BlockGuide.Name(looking) + (description != "" ? ": " + description : ""),
                    new Vector2(ScreenWidth / 2, ScreenHeight / 2 + 32), Color.White, new Color(Color.Black, 0.6f));
            }

            GuideState state = _P.GuideState(true);
            string tip = _P.guide.Current == null ? null : _P.guide.Text(state, TipControls());
            if (tip == null)
                return;
            // Between the radar (top left) and the right edge, below the score bar and match clock.
            int width = Math.Clamp(ScreenWidth - 2 * 230, 300, 600);
            List<string> lines = Wrap(tip, width - 24);
            int lineHeight = 20, top = 56;
            int height = (lines.Count + 1) * lineHeight + 16;
            Rectangle box = new Rectangle(ScreenWidth / 2 - width / 2, top, width, height);
            spriteBatch.Draw(texBlank, box, new Color(Color.Black, 0.75f));
            spriteBatch.Draw(texBlank, new Rectangle(box.X, box.Y, 3, box.Height), new Color(255, 200, 40));
            spriteBatch.DrawString(uiFont, "TIP  (TURN TIPS OFF IN OPTIONS)", new Vector2(box.X + 14, top + 8), new Color(255, 200, 40));
            for (int i = 0; i < lines.Count; i++)
                spriteBatch.DrawString(uiFont, lines[i], new Vector2(box.X + 14, top + 8 + (i + 1) * lineHeight), Color.White);
        }

        public void Render(GraphicsDevice graphicsDevice)
        {
            // If we don't have _P, grab it from the current gameInstance.
            // We can't do this in the constructor because we are created in the property bag's constructor!
            if (_P == null)
                _P = gameInstance.propertyBag;

            // Draw the UI.
            // Textures are not premultiplied, as in XNA 3.0.
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, null, null, null, Matrix.CreateScale(gameInstance.HudScale));
            drawRect = new Rectangle(ScreenWidth / 2 - 1024 / 2, ScreenHeight / 2 - 768 / 2, 1024, 1024);
            
            // Draw the crosshair.
            spriteBatch.Draw(texCrosshairs, new Rectangle(ScreenWidth / 2 - texCrosshairs.Width / 2,
                                                            ScreenHeight / 2 - texCrosshairs.Height / 2,
                                                            texCrosshairs.Width,
                                                            texCrosshairs.Height), Color.White);

            // If equipped, draw the detonator.
            switch (_P.player.SelectedTool)
            {
                case PlayerTools.Detonator:
                    RenderDetonator(graphicsDevice, spriteBatch);
                    break;

                case PlayerTools.ProspectingRadar:
                    RenderProspectron(graphicsDevice, spriteBatch);
                    break;

                case PlayerTools.ConstructionGun:
                    RenderConstructionGun(graphicsDevice, spriteBatch, _P.player.SelectedBlock);
                    break;

                case PlayerTools.DeconstructionGun:
                    RenderConstructionGun(graphicsDevice, spriteBatch, BlockType.None);
                    break;

            }

            if (!_P.player.Dead && !_P.player.Spectating)
                RenderEquipment(spriteBatch);

            if (gameInstance.DrawFrameRate)
                RenderMessageCenter(spriteBatch, String.Format("FPS: {0:000}", gameInstance.FrameRate), new Vector2(60, ScreenHeight - 20), Color.Gray, Color.Black);

            // Show the altimeter.
            int altitude = (int)(_P.player.Position.Y - _P.SurfaceHeight);
            RenderMessageCenter(spriteBatch, String.Format("ALTITUDE: {0:00}", altitude), new Vector2(ScreenWidth - 90, ScreenHeight - 20), altitude >= 0 ? Color.Gray : InfiniminerGame.IM_RED, Color.Black);

            // Draw bank instructions.
            if (_P.AtBankTerminal())
            {
                string deposit = gameInstance.UsingGamePad ? "X" : ClientSettings.KeyName(gameInstance.Settings.Key(GameAction.DepositOre));
                string withdraw = gameInstance.UsingGamePad ? "B" : ClientSettings.KeyName(gameInstance.Settings.Key(GameAction.WithdrawOre));
                RenderMessageCenter(spriteBatch, deposit + ": DEPOSIT 50 ORE  " + withdraw + ": WITHDRAW 50 ORE", new Vector2(ScreenWidth / 2, ScreenHeight / 2 + 60), Color.White, Color.Black);
            }

            // Are they trying to change class when they cannot?
            if (Keyboard.GetState().IsKeyDown(gameInstance.Settings.Key(GameAction.ChangeClass)) && _P.player.Position.Y <= _P.SurfaceHeight && _P.chatMode == ChatMessageType.None)
                RenderMessageCenter(spriteBatch, "YOU CANNOT CHANGE YOUR CLASS BELOW THE SURFACE", new Vector2(ScreenWidth / 2, ScreenHeight / 2 + 90), Color.White, Color.Black);

            // Draw the text-based information panel.
            // Laid out for 1024 wide: centred on wider screens, squeezed on narrower ones.
            int textStart = Math.Max(0, (ScreenWidth - 1024) / 2);
            float span = Math.Min(1f, ScreenWidth / 1024f);
            spriteBatch.Draw(texBlank, new Rectangle(0, 0, ScreenWidth, 20), Color.Black);
            spriteBatch.DrawString(uiFont, "ORE: " + _P.player.Ore + "/" + _P.player.OreMax, new Vector2(textStart+3, 2), Color.White);
            spriteBatch.DrawString(uiFont, "LOOT: $" + _P.player.Cash, new Vector2(textStart + (int)(170 * span), 2), Color.White);
            spriteBatch.DrawString(uiFont, "WEIGHT: " + _P.player.Weight + "/" + _P.player.WeightMax, new Vector2(textStart + (int)(340 * span), 2), Color.White);
            spriteBatch.DrawString(uiFont, "TEAM ORE: " + _P.teamOre, new Vector2(textStart + (int)(515 * span), 2), Color.White);
            spriteBatch.DrawString(uiFont, "RED: $" + _P.teamRedCash, new Vector2(textStart + (int)(700 * span), 2), InfiniminerGame.IM_RED);
            spriteBatch.DrawString(uiFont, "BLUE: $" + _P.teamBlueCash, new Vector2(textStart + (int)(860 * span), 2), InfiniminerGame.IM_BLUE);

            // Match clock, when the server has a time limit.
            if (_P.overtime && _P.teamWinners == PlayerTeam.None)
                RenderMessageCenter(spriteBatch, "OVERTIME: NEXT DEPOSIT WINS", new Vector2(ScreenWidth / 2, 34), InfiniminerGame.IM_RED, Color.Black);
            else if (!double.IsNaN(_P.matchEndTime) && _P.teamWinners == PlayerTeam.None)
            {
                int secondsLeft = (int)Math.Max(0, Math.Ceiling(_P.matchEndTime - currentTime));
                RenderMessageCenter(spriteBatch, string.Format("TIME LEFT: {0}:{1:00}", secondsLeft / 60, secondsLeft % 60), new Vector2(ScreenWidth / 2, 34), secondsLeft <= 60 ? InfiniminerGame.IM_RED : Color.White, Color.Black);
            }

            // Tips for new players.
            if (_P.chatMode == ChatMessageType.None)
                RenderTips(spriteBatch);

            // Draw player information.
            bool scoreboardHeld = Keyboard.GetState().IsKeyDown(gameInstance.Settings.Key(GameAction.Scoreboard)) || gameInstance.GamePadInput.IsDown(Buttons.Back);
            if ((scoreboardHeld && _P.screenEffect == ScreenEffect.None) || _P.teamWinners != PlayerTeam.None)
            {
                spriteBatch.Draw(texBlank, new Rectangle(0, 0, ScreenWidth, ScreenHeight), new Color(Color.Black, 0.7f));

                if (_P.teamWinners != PlayerTeam.None)
                {
                    string teamName = _P.teamWinners == PlayerTeam.Red ? "RED" : "BLUE";
                    Color teamColor = _P.teamWinners == PlayerTeam.Red ? InfiniminerGame.IM_RED : InfiniminerGame.IM_BLUE;
                    string gameOverMessage = "GAME OVER - " + teamName + " TEAM WINS!";
                    RenderMessageCenter(spriteBatch, gameOverMessage, new Vector2(ScreenWidth / 2, 150), teamColor, new Color(0, 0, 0, 0));
                }
                
                if (_P.teamWinners == PlayerTeam.None && _P.winningCash > 0)
                    RenderMessageCenter(spriteBatch, "THE FIRST TEAM TO EARN $" + _P.winningCash + " WINS", new Vector2(ScreenWidth / 2, 150), Color.White, new Color(0, 0, 0, 0));

                // Team headings, so the columns don't rely on colour alone.
                RenderMessageCenter(spriteBatch, "RED TEAM", new Vector2(ScreenWidth / 4, 175), InfiniminerGame.IM_RED, new Color(0, 0, 0, 0));
                RenderMessageCenter(spriteBatch, "BLUE TEAM", new Vector2(ScreenWidth * 3 / 4, 175), InfiniminerGame.IM_BLUE, new Color(0, 0, 0, 0));

                int drawY = 210;
                foreach (Player p in _P.playerList.Values)
                {
                    if (p.Team != PlayerTeam.Red)
                        continue;
                    RenderMessageCenter(spriteBatch, p.Handle + " ( $" + p.Score + " )", new Vector2(ScreenWidth/4, drawY), InfiniminerGame.IM_RED, new Color(0, 0, 0, 0));
                    drawY += 35;
                }
                drawY = 210;
                foreach (Player p in _P.playerList.Values)
                {
                    if (p.Team != PlayerTeam.Blue)
                        continue;
                    RenderMessageCenter(spriteBatch, p.Handle + " ( $" + p.Score + " )", new Vector2(ScreenWidth * 3 / 4, drawY), InfiniminerGame.IM_BLUE, new Color(0, 0, 0, 0));
                    drawY += 35;
                }

                // Everyone else is watching (or still choosing a team).
                string watching = string.Join(", ", _P.playerList.Values.Where(p => p.Team == PlayerTeam.None).Select(p => p.Handle));
                if (watching != "")
                    RenderMessageCenter(spriteBatch, "WATCHING: " + watching, new Vector2(ScreenWidth / 2, ScreenHeight - 120), Color.Gray, new Color(0, 0, 0, 0));
            }

            // Spectators: how to get into the game.
            if (_P.player.Spectating && _P.chatMode == ChatMessageType.None)
                RenderMessageCenter(spriteBatch, gameInstance.UsingGamePad
                        ? "SPECTATING - LEFT STICK TO FLY, A/B UP/DOWN, START > CHANGE TEAM TO PLAY"
                        : "SPECTATING - FLY WITH " + ClientSettings.KeyName(gameInstance.Settings.Key(GameAction.MoveForward)) + "/"
                          + ClientSettings.KeyName(gameInstance.Settings.Key(GameAction.MoveLeft)) + "/"
                          + ClientSettings.KeyName(gameInstance.Settings.Key(GameAction.MoveBack)) + "/"
                          + ClientSettings.KeyName(gameInstance.Settings.Key(GameAction.MoveRight)) + ", "
                          + ClientSettings.KeyName(gameInstance.Settings.Key(GameAction.Jump)) + "/SHIFT UP/DOWN, "
                          + ClientSettings.KeyName(gameInstance.Settings.Key(GameAction.ChangeTeam)) + " TO JOIN A TEAM",
                    new Vector2(ScreenWidth / 2, ScreenHeight - 50), Color.White, Color.Black);

            // Draw the chat buffer.
            if (_P.chatMode == ChatMessageType.SayAll)
            {
                spriteBatch.DrawString(uiFont, "ALL> " + _P.chatEntryBuffer, new Vector2(22, ScreenHeight - 98), Color.Black);
                spriteBatch.DrawString(uiFont, "ALL> " + _P.chatEntryBuffer, new Vector2(20, ScreenHeight - 100), Color.White);
            }
            else if (_P.chatMode == ChatMessageType.SayBlueTeam || _P.chatMode == ChatMessageType.SayRedTeam)
            {
                spriteBatch.DrawString(uiFont, "TEAM> " + _P.chatEntryBuffer, new Vector2(22, ScreenHeight - 98), Color.Black);
                spriteBatch.DrawString(uiFont, "TEAM> " + _P.chatEntryBuffer, new Vector2(20, ScreenHeight - 100), Color.White);
            }
            for (int i = 0; i < _P.chatLog.Messages.Count; i++)
            {
                Color chatColor = Color.White;
                if (_P.chatLog.Messages[i].type == ChatMessageType.SayRedTeam)
                    chatColor = InfiniminerGame.IM_RED;
                if (_P.chatLog.Messages[i].type == ChatMessageType.SayBlueTeam)
                    chatColor = InfiniminerGame.IM_BLUE;
                string chatText = _P.chatLog.Messages[i].message;
                if (InfiniminerGame.ColorBlindMode && _P.chatLog.Messages[i].type == ChatMessageType.SayRedTeam)
                    chatText = "[R] " + chatText;
                else if (InfiniminerGame.ColorBlindMode && _P.chatLog.Messages[i].type == ChatMessageType.SayBlueTeam)
                    chatText = "[B] " + chatText;
                spriteBatch.DrawString(uiFont, chatText, new Vector2(22, ScreenHeight - 114 - 16 * i), Color.Black);
                spriteBatch.DrawString(uiFont, chatText, new Vector2(20, ScreenHeight - 116 - 16 * i), chatColor);
            }

            // Draw the player radar.
            spriteBatch.Draw(texRadarBackground, new Vector2(10, 30), Color.White);
            foreach (Player p in _P.playerList.Values)
                if (p.Team == _P.player.Team && p.Alive)
                    RenderRadarBlip(spriteBatch, p.ID == _P.player.Id ? _P.player.Position : p.Position, p.Team == PlayerTeam.Red ? InfiniminerGame.IM_RED : InfiniminerGame.IM_BLUE, p.Ping > 0, "");
            foreach (KeyValuePair<Vector3, Beacon> bPair in _P.beaconList)
                if (bPair.Value.Team == _P.player.Team)
                    RenderRadarBlip(spriteBatch, bPair.Key, Color.White, false, bPair.Value.ID);
            RenderRadarBlip(spriteBatch, new Vector3(100000, 0, 32), Color.White, false, "NORTH");

            spriteBatch.Draw(texRadarForeground, new Vector2(10, 30), Color.White);

            // Quick chat choices while LT is held.
            if (gameInstance.GamePadInput.IsDown(QuickChat.Modifier) && !_P.player.Dead && _P.chatMode == ChatMessageType.None)
            {
                int top = ScreenHeight / 2 + 60, lineHeight = 22;
                string[] lines = new[] { "QUICK CHAT (TEAM)" }
                    .Concat(QuickChat.Messages.Select(m => m.label + ": " + m.message)).ToArray();
                spriteBatch.Draw(texBlank, new Rectangle(ScreenWidth / 2 - 160, top - 10, 320, lines.Length * lineHeight + 14), new Color(Color.Black, 0.75f));
                for (int i = 0; i < lines.Length; i++)
                {
                    Vector2 size = uiFont.MeasureString(lines[i]);
                    spriteBatch.DrawString(uiFont, lines[i], new Vector2((int)(ScreenWidth / 2 - size.X / 2), top + i * lineHeight), i == 0 ? Color.Gray : Color.White);
                }
            }

            // Draw escape message.
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            {
                RenderMessageCenter(spriteBatch, "RELEASE ESCAPE FOR THE MENU, OR:", new Vector2(ScreenWidth / 2, ScreenHeight / 2 + 30), Color.White, Color.Black);
                RenderMessageCenter(spriteBatch, "PRESS Y TO CONFIRM THAT YOU WANT TO QUIT.", new Vector2(ScreenWidth / 2, ScreenHeight / 2 + 80), Color.White, Color.Black);
                RenderMessageCenter(spriteBatch, "PRESS K TO COMMIT PIXELCIDE.", new Vector2(ScreenWidth / 2, ScreenHeight / 2 + 130), Color.White, Color.Black);
                RenderMessageCenter(spriteBatch, "PRESS O FOR OPTIONS.", new Vector2(ScreenWidth / 2, ScreenHeight / 2 + 180), Color.White, Color.Black);
            }
           
            // Draw the current screen effect.
            if (_P.screenEffect == ScreenEffect.Death)
            {
                Color drawColor = new Color(1 - (float)_P.screenEffectCounter * 0.5f, 0f, 0f);
                spriteBatch.Draw(texBlank, new Rectangle(0, 0, ScreenWidth, ScreenHeight), drawColor);
                if (_P.screenEffectCounter >= 2)
                    RenderMessageCenter(spriteBatch, gameInstance.UsingGamePad ? "You have died. Press A to respawn." : "You have died. Click to respawn.", new Vector2(ScreenWidth / 2, ScreenHeight/2), Color.White, Color.Black);
            }
            if (_P.screenEffect == ScreenEffect.Teleport || _P.screenEffect == ScreenEffect.Explosion)
            {
                Color drawColor = new Color(1, 1, 1, 1 - (float)_P.screenEffectCounter * 0.5f);
                spriteBatch.Draw(texBlank, new Rectangle(0, 0, ScreenWidth, ScreenHeight), drawColor);
                if (_P.screenEffectCounter > 2)
                    _P.screenEffect = ScreenEffect.None;
            }
            if (_P.screenEffect == ScreenEffect.Fall)
            {
                Color drawColor = new Color(1, 0, 0, 1 - (float)_P.screenEffectCounter * 0.5f);
                spriteBatch.Draw(texBlank, new Rectangle(0, 0, ScreenWidth, ScreenHeight), drawColor);
                if (_P.screenEffectCounter > 2)
                    _P.screenEffect = ScreenEffect.None;
            }

            // Draw the help screen.
            if (Keyboard.GetState().IsKeyDown(gameInstance.Settings.Key(GameAction.Help)))
            {
                spriteBatch.Draw(texBlank, new Rectangle(0, 0, ScreenWidth, ScreenHeight), Color.Black);
                spriteBatch.Draw(texHelp, drawRect, Color.White);
            }

            spriteBatch.End();
        }
    }
}
