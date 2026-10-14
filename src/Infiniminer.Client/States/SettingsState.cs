using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Infiniminer.States
{
    // Options and key bindings. Every change is applied and saved to client.config.txt immediately.
    public class SettingsState : MenuListState
    {
        enum Page { Options, Controls, Controller }

        Type returnState;
        Page page = Page.Options;
        bool editingName = false;
        GameAction? waitingForKey = null;

        ClientSettings Settings
        {
            get { return Game.Settings; }
        }

        protected override int RowHeight
        {
            get { return page == Page.Options ? 36 : 32; }
        }

        protected override string Title
        {
            get { return page == Page.Options ? "OPTIONS" : page == Page.Controls ? "CONTROLS" : "CONTROLLER"; }
        }

        protected override bool Busy
        {
            get { return editingName || waitingForKey != null; }
        }

        protected override string Hint
        {
            get
            {
                if (editingName)
                    return "TYPE YOUR NAME, THEN PRESS ENTER" + (InGame ? " (USED THE NEXT TIME YOU JOIN)" : "");
                if (waitingForKey != null)
                    return "PRESS THE NEW KEY, OR ESCAPE TO CANCEL";
                return base.Hint;
            }
        }

        public override string TextInputPrompt
        {
            get { return editingName ? "YOUR NAME: " + Settings.Handle + "_" : null; }
        }

        // Opened from the game (directly or through its menu): stay connected.
        bool InGame
        {
            get { return returnState == typeof(MainGameState) || returnState == typeof(GameMenuState); }
        }

        public override void OnEnter(Type oldState)
        {
            base.OnEnter(oldState);
            returnState = oldState ?? typeof(ServerBrowserState);
            Rebuild();
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

        protected override void BuildRows()
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
                    // Fullscreen is borderless at the desktop's resolution; this sizes the window.
                    Value = () => Settings.Fullscreen ? "DESKTOP (FULLSCREEN)" : Settings.Width + " X " + Settings.Height,
                    Enabled = () => !Settings.Fullscreen,
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
                    Label = "CONTROLLER SENSITIVITY",
                    Value = () => Settings.ControllerSensitivity.ToString(),
                    Change = direction => { Settings.ControllerSensitivity = Math.Clamp(Settings.ControllerSensitivity + direction, ClientSettings.MinSensitivity, ClientSettings.MaxSensitivity); Changed(); },
                });
                rows.Add(new Row
                {
                    Label = "INVERT LOOK",
                    Value = () => OnOff(Settings.InvertY),
                    Change = direction => { Settings.InvertY = !Settings.InvertY; Changed(); },
                });
                rows.Add(new Row
                {
                    Label = "CONTROLLER VIBRATION",
                    Value = () => OnOff(Settings.Vibration),
                    Change = direction => { Settings.Vibration = !Settings.Vibration; Changed(); },
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
                    // Turning tips back on starts the guide again from the first tip.
                    Label = "TIPS FOR NEW PLAYERS",
                    Value = () => OnOff(Settings.ShowTips),
                    Change = direction =>
                    {
                        Settings.ShowTips = !Settings.ShowTips;
                        if (Settings.ShowTips)
                            Settings.TipsDone.Clear();
                        Changed();
                    },
                });
                rows.Add(new Row
                {
                    Label = "SHOW FPS",
                    Value = () => OnOff(Settings.ShowFps),
                    Change = direction => { Settings.ShowFps = !Settings.ShowFps; Changed(); },
                });
                rows.Add(new Row { Label = "KEYBOARD CONTROLS >", Activate = () => SwitchPage(Page.Controls) });
                rows.Add(new Row { Label = "CONTROLLER >", Activate = () => SwitchPage(Page.Controller) });
                rows.Add(new Row { Label = "BACK", Activate = Back });
            }
            else if (page == Page.Controls)
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
            else
            {
                // The controller layout is fixed; this page shows it.
                foreach (var (control, action) in ControllerLayout.InGame)
                {
                    string button = control;
                    rows.Add(new Row { Label = action, Value = () => button });
                }
                rows.Add(new Row { Label = "MENUS", Value = () => "D-PAD OR STICK, A, B" });
                rows.Add(new Row { Label = "< BACK", Activate = () => SwitchPage(Page.Options) });
            }
        }

        void SwitchPage(Page newPage)
        {
            page = newPage;
            selected = 0;
            waitingForKey = null;
            Rebuild();
            if (newPage == Page.Controller)
                selected = rows.Count - 1;
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

        protected override void Back()
        {
            if (page != Page.Options)
            {
                SwitchPage(Page.Options);
                return;
            }
            if (editingName)
                FinishNameEdit();
            Game.SaveSettings();
            _P.PlaySound(InfiniminerSound.ClickHigh);
            nextState = returnState;
        }

        public override Type OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            // Stay connected while the menu is open.
            if (InGame)
                Game.UpdateNetwork(gameTime);
            return base.OnUpdate(gameTime, keyState, mouseState);
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
                // A key binding needs a key: anything from the controller just cancels.
                if (key != Keys.Escape && !_SM.UsingGamePad)
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

            base.OnKeyDown(key);
        }
    }
}
