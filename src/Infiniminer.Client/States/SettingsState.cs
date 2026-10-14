using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StateMasher;

namespace Infiniminer.States
{
    // Options and key bindings. Every change is applied and saved to client.config.txt immediately.
    public class SettingsState : State
    {
        enum Page { Options, Controls }

        class Row
        {
            public string Label;
            public Func<string> Value = () => "";
            public Action<int> Change;  // adjust left (-1) or right (+1); null if not adjustable
            public Action Activate;     // Enter or click
        }

        string nextState = null;
        string returnState;
        Page page = Page.Options;
        int selected = 0;
        bool editingName = false;
        GameAction? waitingForKey = null;
        List<Row> rows = new List<Row>();
        Texture2D texBlank;
        SpriteFontBase uiFont, titleFont;

        const int RowTop = 120;
        const int LabelX = 170;
        const int ValueCenterX = 720;
        const int ArrowOffset = 130;

        ClientSettings Settings
        {
            get { return Game.Settings; }
        }

        int RowHeight
        {
            get { return page == Page.Options ? 38 : 32; }
        }

        bool InGame
        {
            get { return returnState == "Infiniminer.States.MainGameState"; }
        }

        public override void OnEnter(string oldState)
        {
            _SM.IsMouseVisible = true;
            returnState = oldState == "" ? "Infiniminer.States.ServerBrowserState" : oldState;
            uiFont = Game.Assets.UiFont;
            titleFont = Game.Assets.TitleFont;
            texBlank = new Texture2D(_SM.GraphicsDevice, 1, 1);
            texBlank.SetData(new[] { Color.White });
            BuildRows();
        }

        public override void OnLeave(string newState)
        {
            texBlank.Dispose();
        }

        static string OnOff(bool value)
        {
            return value ? "ON" : "OFF";
        }

        void Changed()
        {
            Game.SaveSettings();
            _P.PlaySound(InfiniminerSound.ClickLow);
        }

        List<Point> Resolutions()
        {
            DisplayMode desktop = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            HashSet<Point> sizes = new HashSet<Point>();
            foreach (DisplayMode mode in GraphicsAdapter.DefaultAdapter.SupportedDisplayModes)
                if (mode.Width >= 800 && mode.Height >= 600)
                    sizes.Add(new Point(mode.Width, mode.Height));
            foreach (Point common in new[] { new Point(1024, 768), new Point(1280, 720), new Point(1280, 800), new Point(1366, 768),
                                             new Point(1600, 900), new Point(1920, 1080), new Point(2560, 1440), new Point(3840, 2160) })
                if (Settings.Fullscreen ? sizes.Contains(common) : common.X <= desktop.Width && common.Y <= desktop.Height)
                    sizes.Add(common);
            sizes.Add(new Point(Settings.Width, Settings.Height));
            return sizes.OrderBy(p => p.X * p.Y).ThenBy(p => p.X).ToList();
        }

        static int Cycle(int index, int direction, int count)
        {
            return ((index + direction) % count + count) % count;
        }

        void BuildRows()
        {
            rows = new List<Row>();
            if (page == Page.Options)
            {
                rows.Add(new Row
                {
                    Label = "PLAYER NAME",
                    Value = () => Settings.Handle + (editingName ? "_" : ""),
                    Activate = () => { editingName = !editingName; if (!editingName) FinishNameEdit(); },
                });
                rows.Add(new Row
                {
                    Label = "RESOLUTION",
                    Value = () => Settings.Width + " X " + Settings.Height,
                    Change = direction =>
                    {
                        List<Point> sizes = Resolutions();
                        int index = Math.Max(0, sizes.IndexOf(new Point(Settings.Width, Settings.Height)));
                        Point size = sizes[Cycle(index, direction, sizes.Count)];
                        Settings.Width = size.X;
                        Settings.Height = size.Y;
                        Game.ApplyDisplaySettings();
                        Changed();
                    },
                });
                rows.Add(new Row
                {
                    Label = "FULLSCREEN",
                    Value = () => OnOff(Settings.Fullscreen),
                    Change = direction => { Settings.Fullscreen = !Settings.Fullscreen; Game.ApplyDisplaySettings(); Changed(); },
                });
                rows.Add(new Row
                {
                    Label = "INTERFACE SCALE",
                    Value = () => Settings.UiScale == 0 ? "AUTO (" + Game.HudScale + "X)" : Settings.UiScale + "X",
                    Change = direction => { Settings.UiScale = Cycle(Settings.UiScale, direction, ClientSettings.MaxUiScale + 1); Changed(); },
                });
                rows.Add(new Row
                {
                    Label = "FIELD OF VIEW",
                    Value = () => Settings.FieldOfView.ToString(),
                    Change = direction => { Settings.FieldOfView = Math.Clamp(Settings.FieldOfView + 5 * direction, ClientSettings.MinFieldOfView, ClientSettings.MaxFieldOfView); Changed(); },
                });
                rows.Add(new Row
                {
                    Label = "MOUSE SENSITIVITY",
                    Value = () => Settings.MouseSensitivity.ToString(),
                    Change = direction => { Settings.MouseSensitivity = Math.Clamp(Settings.MouseSensitivity + direction, ClientSettings.MinSensitivity, ClientSettings.MaxSensitivity); Changed(); },
                });
                rows.Add(new Row
                {
                    Label = "INVERT MOUSE",
                    Value = () => OnOff(Settings.InvertY),
                    Change = direction => { Settings.InvertY = !Settings.InvertY; Changed(); },
                });
                rows.Add(new Row
                {
                    Label = "VOLUME",
                    Value = () => (int)Math.Round(Settings.Volume * 100) + "%",
                    Change = direction => { Game.SetVolume((float)Math.Round(Settings.Volume * 10 + direction) / 10); Changed(); },
                });
                rows.Add(new Row
                {
                    Label = "BLOOM",
                    Value = () => OnOff(Settings.Pretty),
                    Change = direction => { Game.SetPretty(!Settings.Pretty); Changed(); },
                });
                rows.Add(new Row
                {
                    Label = "COLOR-BLIND MODE",
                    Value = () => OnOff(Settings.ColorBlind),
                    Change = direction => { Settings.ColorBlind = !Settings.ColorBlind; InfiniminerGame.ColorBlindMode = Settings.ColorBlind; Changed(); },
                });
                rows.Add(new Row
                {
                    Label = "SHOW FPS",
                    Value = () => OnOff(Settings.ShowFps),
                    Change = direction => { Settings.ShowFps = !Settings.ShowFps; Changed(); },
                });
                rows.Add(new Row { Label = "CONTROLS >", Activate = () => SwitchPage(Page.Controls) });
                rows.Add(new Row { Label = "BACK", Activate = Back });
            }
            else
            {
                foreach (GameAction action in Enum.GetValues<GameAction>())
                {
                    GameAction rowAction = action;
                    rows.Add(new Row
                    {
                        Label = ClientSettings.ActionName(rowAction),
                        Value = () => waitingForKey == rowAction ? "PRESS A KEY..." : ClientSettings.KeyName(Settings.Key(rowAction)),
                        Activate = () => waitingForKey = rowAction,
                    });
                }
                rows.Add(new Row
                {
                    Label = "RESET TO DEFAULTS",
                    Activate = () => { Settings.KeyBindings = ClientSettings.DefaultKeyBindings(); Changed(); },
                });
                rows.Add(new Row { Label = "< BACK", Activate = () => SwitchPage(Page.Options) });
            }
            selected = Math.Clamp(selected, 0, rows.Count - 1);
        }

        void SwitchPage(Page newPage)
        {
            page = newPage;
            selected = 0;
            waitingForKey = null;
            BuildRows();
            _P.PlaySound(InfiniminerSound.ClickHigh);
        }

        void FinishNameEdit()
        {
            editingName = false;
            Settings.Handle = TextUtil.Sanitize(Settings.Handle).Trim();
            if (Settings.Handle == "")
                Settings.Handle = "Player";
            Changed();
        }

        void Back()
        {
            if (editingName)
                FinishNameEdit();
            Game.SaveSettings();
            _P.PlaySound(InfiniminerSound.ClickHigh);
            nextState = returnState;
        }

        int RowAt(Point menuPoint)
        {
            int index = (menuPoint.Y - RowTop + RowHeight / 4) / RowHeight;
            if (menuPoint.Y < RowTop - RowHeight / 4 || index < 0 || index >= rows.Count || menuPoint.X < LabelX - 30 || menuPoint.X > 1024 - LabelX + 30)
                return -1;
            return index;
        }

        public override string OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            // Stay connected while the menu is open.
            if (InGame)
                Game.UpdateNetwork(gameTime);

            // Hovering selects a row.
            if (!editingName && waitingForKey == null && _SM.WindowHasFocus())
            {
                int hovered = RowAt(Game.ToMenu(mouseState.X, mouseState.Y));
                if (hovered >= 0)
                    selected = hovered;
            }

            return nextState;
        }

        public override void OnRenderAtUpdate(GraphicsDevice graphicsDevice, GameTime gameTime)
        {
            SpriteBatch spriteBatch = _SM.SpriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, Game.MenuSampler, null, null, null, Game.MenuTransform);

            string title = page == Page.Options ? "OPTIONS" : "CONTROLS";
            Vector2 titleSize = titleFont.MeasureString(title);
            spriteBatch.DrawString(titleFont, title, new Vector2((int)(InfiniminerGame.MenuWidth / 2 - titleSize.X / 2), 36), Color.White);

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                int y = RowTop + i * RowHeight;
                bool isSelected = i == selected;
                if (isSelected)
                    spriteBatch.Draw(texBlank, new Rectangle(LabelX - 20, y - 8, 1024 - 2 * (LabelX - 20), RowHeight - 6), new Color(40, 40, 40));

                Color color = isSelected ? Color.White : new Color(150, 150, 150);
                spriteBatch.DrawString(uiFont, row.Label, new Vector2(LabelX, y), color);

                string value = row.Value();
                if (value != "")
                {
                    Vector2 valueSize = uiFont.MeasureString(value);
                    spriteBatch.DrawString(uiFont, value, new Vector2((int)(ValueCenterX - valueSize.X / 2), y), isSelected ? InfiniminerGame.IM_BLUE : color);
                }
                if (row.Change != null)
                {
                    spriteBatch.DrawString(uiFont, "<", new Vector2(ValueCenterX - ArrowOffset, y), color);
                    spriteBatch.DrawString(uiFont, ">", new Vector2(ValueCenterX + ArrowOffset - 8, y), color);
                }
            }

            string hint;
            if (editingName)
                hint = "TYPE YOUR NAME, THEN PRESS ENTER" + (InGame ? " (USED THE NEXT TIME YOU JOIN)" : "");
            else if (waitingForKey != null)
                hint = "PRESS THE NEW KEY, OR ESCAPE TO CANCEL";
            else
                hint = "UP/DOWN: SELECT   LEFT/RIGHT: CHANGE   ENTER: CHOOSE   ESC: BACK";
            Vector2 hintSize = uiFont.MeasureString(hint);
            spriteBatch.DrawString(uiFont, hint, new Vector2((int)(InfiniminerGame.MenuWidth / 2 - hintSize.X / 2), 720), new Color(150, 150, 150));

            spriteBatch.End();
        }

        public override void OnTextInput(char character)
        {
            if (editingName && Settings.Handle.Length < 20)
                Settings.Handle += character;
        }

        public override void OnKeyDown(Keys key)
        {
            if (waitingForKey != null)
            {
                if (key != Keys.Escape)
                {
                    Settings.Bind(waitingForKey.Value, key);
                    Changed();
                }
                waitingForKey = null;
                return;
            }

            if (editingName)
            {
                if (key == Keys.Back && Settings.Handle.Length > 0)
                    Settings.Handle = Settings.Handle.Substring(0, Settings.Handle.Length - 1);
                else if (key == Keys.Enter || key == Keys.Escape)
                    FinishNameEdit();
                return;
            }

            switch (key)
            {
                case Keys.Up:
                    selected = Cycle(selected, -1, rows.Count);
                    break;
                case Keys.Down:
                    selected = Cycle(selected, 1, rows.Count);
                    break;
                case Keys.Left:
                    rows[selected].Change?.Invoke(-1);
                    break;
                case Keys.Right:
                    rows[selected].Change?.Invoke(1);
                    break;
                case Keys.Enter:
                case Keys.Space:
                    if (rows[selected].Activate != null)
                        rows[selected].Activate();
                    else
                        rows[selected].Change?.Invoke(1);
                    break;
                case Keys.Escape:
                    if (page == Page.Controls)
                        SwitchPage(Page.Options);
                    else
                        Back();
                    break;
            }
        }

        public override void OnMouseDown(MouseButton button, int x, int y)
        {
            if (waitingForKey != null || editingName)
                return;

            Point menuPoint = Game.ToMenu(x, y);
            int index = RowAt(menuPoint);
            if (index < 0)
                return;
            selected = index;
            Row row = rows[index];

            if (row.Change != null)
            {
                // Left of the value goes down, otherwise up; right-clicking also goes down.
                bool decrease = button == MouseButton.RightButton || menuPoint.X < ValueCenterX - ArrowOffset / 2;
                row.Change(decrease ? -1 : 1);
            }
            else
                row.Activate?.Invoke();
        }
    }
}
