using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DragLaunch
{
    /// <summary>Einstellungen - jede Änderung wird sofort angewendet und in die INI geschrieben.</summary>
    public class SettingsWindow : Window
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Brush ErrBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0xD0));

        private readonly MainWindow _main;
        private readonly Grid _grid = new Grid();
        private readonly List<Action> _refreshers = new List<Action>();
        private readonly DispatcherTimer _applyTimer;
        private bool _refreshing;

        private static AppSettings S => ConfigStore.Settings;

        public SettingsWindow(MainWindow main)
        {
            _main = main;
            Title = L.F("s.title", AppInfo.VersionText);
            Width = 560;
            Height = 760;
            MinWidth = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _applyTimer.Tick += (s, e) => ApplyNow();
            Closed += (s, e) => { if (_applyTimer.IsEnabled) ApplyNow(); EdgePreview.Hide(); };
            Loaded += (s, e) => EdgePreview.Show();

            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            BuildContent();

            var close = new Button { Content = L.T("close"), Padding = new Thickness(18, 4, 18, 4), IsCancel = true, IsDefault = true };
            close.Click += (s, e) => Close();
            var bottom = new DockPanel { Margin = new Thickness(12, 8, 12, 12), LastChildFill = true };
            DockPanel.SetDock(close, Dock.Right);
            bottom.Children.Add(close);
            bottom.Children.Add(new TextBlock
            {
                Text = L.T("s.footer"),
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            var root = new DockPanel();
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _grid,
                Padding = new Thickness(14, 6, 14, 6)
            });
            Content = root;
            UiScale.Apply(this);
        }

        private void BuildContent()
        {
            Header(L.T("s.language"));
            var langs = L.Available();
            Choice(L.T("s.language"), langs.ToArray(),
                () => Math.Max(0, langs.FindIndex(n => n.Equals(L.Current, StringComparison.OrdinalIgnoreCase))),
                i =>
                {
                    if (langs[i].Equals(L.Current, StringComparison.OrdinalIgnoreCase)) return;
                    S.Language = langs[i];
                    L.Load(S.Language);
                    ConfigStore.Save();
                    // Fenster in der neuen Sprache neu öffnen
                    Dispatcher.BeginInvoke(new Action(() => { Close(); _main.ApplySettings(); _main.OpenSettings(); }));
                });
            Note(L.T("s.languagenote"));

            Header(L.T("s.appearance"));
            Num(L.T("s.fontsize"), () => S.FontSize, v => S.FontSize = v, 6, 40, "pt", false);
            Num(L.T("s.tabfontsize"), () => S.TabFontSize, v => S.TabFontSize = v, 6, 40, "pt", false);
            Num(L.T("s.iconsize"), () => S.IconSize, v => S.IconSize = (int)v, 16, 256, "px");
            Num(L.T("s.cellwidth"), () => S.CellWidth, v => S.CellWidth = (int)v, 30, 400, "px");
            Num(L.T("s.maxlines"), () => S.MaxLabelLines, v => S.MaxLabelLines = (int)v, 0, 6, L.T("s.notext"));
            Num(L.T("s.columns"), () => S.Columns, v => S.Columns = (int)v, 1, 100, null);
            Num(L.T("s.rows"), () => S.Rows, v => S.Rows = (int)v, 1, 50, null);
            Check(L.T("s.showtiles"), () => S.ShowTiles, v => S.ShowTiles = v);
            Num(L.T("s.contextfont"), () => S.ContextFontSize, v => S.ContextFontSize = v, 8, 40, "pt", false);
            Num(L.T("s.dialogscale"), () => S.DialogScale, v => S.DialogScale = v, 0.8, 3, L.T("s.factor"), false);
            Note(L.T("s.gridnote"));

            Header(L.T("s.sound"));
            var sounds = Sounds.Available();
            var soundNames = new List<string> { L.T("s.nosound") };
            soundNames.AddRange(sounds);
            Choice(L.T("s.sound"), soundNames.ToArray(),
                () => Math.Max(0, sounds.FindIndex(n => n.Equals(S.Sound, StringComparison.OrdinalIgnoreCase)) + 1),
                i => { S.Sound = i == 0 ? "" : sounds[i - 1]; Sounds.PlayFile(S.Sound); });
            Note(L.T("s.soundnote"));

            Header(L.T("s.colors"));
            Note(L.T("s.colornote"));
            ColorRow(L.T("s.c.frame"), () => S.FrameColor, v => S.FrameColor = v);
            ColorRow(L.T("s.c.background"), () => S.BackgroundColor, v => S.BackgroundColor = v);
            ColorRow(L.T("s.c.tabtext"), () => S.TabTextColor, v => S.TabTextColor = v);
            ColorRow(L.T("s.c.activetab"), () => S.ActiveTabColor, v => S.ActiveTabColor = v);
            ColorRow(L.T("s.c.activetabtext"), () => S.ActiveTabTextColor, v => S.ActiveTabTextColor = v);
            ColorRow(L.T("s.c.label"), () => S.LabelColor, v => S.LabelColor = v);

            Header(L.T("s.position"));
            Num(L.T("s.offsetleft"), () => S.OffsetLeft, v => S.OffsetLeft = v, -10000, 10000, "px", false);
            Num(L.T("s.offsetbottom"), () => S.OffsetBottom, v => S.OffsetBottom = v, -10000, 10000, "px", false);
            Choice(L.T("s.measured"), new[] { L.T("s.screenedge"), L.T("s.workarea") },
                () => S.UseWorkArea ? 1 : 0, i => S.UseWorkArea = i == 1);
            Check(L.T("s.lock"), () => S.PositionLocked, v => S.PositionLocked = v);
            Check(L.T("s.taskbar"), () => S.ShowInTaskbar, v => S.ShowInTaskbar = v);
            Note(L.T("s.movenote"));

            Header(L.T("s.edge"));
            Num(L.T("s.delay"), () => S.ActivationDelayMs, v => S.ActivationDelayMs = (int)v, 0, 10000, L.T("s.delayunit"));
            Num(L.T("s.edgewidth"), () => S.EdgeThickness, v => S.EdgeThickness = (int)v, 1, 50, "px");
            EdgeRow(L.T("s.e.bottom"), () => S.Bottom);
            EdgeRow(L.T("s.e.top"), () => S.Top);
            EdgeRow(L.T("s.e.left"), () => S.Left);
            EdgeRow(L.T("s.e.right"), () => S.Right);
            Note(L.T("s.edgenote"));
            Check(L.T("s.edgepreview"), () => EdgePreview.Enabled, v => EdgePreview.Enabled = v);
            Check(L.T("s.focus"), () => S.ActivateFocus, v => S.ActivateFocus = v);
            Check(L.T("s.fullscreen"), () => S.IgnoreFullscreen, v => S.IgnoreFullscreen = v);
            TextRow(L.T("s.pauseapps"), () => S.EdgePauseApps, v => S.EdgePauseApps = v);
            Note(L.T("s.pauseappsnote"));
            Check(L.T("s.backafterlaunch"), () => S.BackAfterLaunch, v => S.BackAfterLaunch = v);

            Header(L.T("s.folders"));
            Choice(L.T("s.folderclick"), new[] { L.T("props.fmlist"), L.T("folder.open") },
                () => S.FolderOpensMenu ? 0 : 1, i => S.FolderOpensMenu = i == 0);
            Note(L.T("s.foldernote"));
            Num(L.T("s.menufont"), () => S.MenuFontSize, v => S.MenuFontSize = v, 8, 40, "pt", false);
            Num(L.T("s.menuicon"), () => S.MenuIconSize, v => S.MenuIconSize = (int)v, 16, 64, "px");

            Header(L.T("s.backup"));
            var bp = new StackPanel { Orientation = Orientation.Horizontal };
            var bNew = new Button { Content = L.T("s.backupcreate"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
            bNew.Click += (s, e) =>
            {
                try
                {
                    string p = ConfigStore.CreateBackup();
                    MessageBox.Show(this, L.F("backup.created", System.IO.Path.GetFileName(p)), "DragLaunch");
                }
                catch (Exception ex) { MessageBox.Show(this, L.F("backup.failed", ex.Message), "DragLaunch"); }
            };
            var bRes = new Button { Content = L.T("s.backuprestore"), Padding = new Thickness(10, 3, 10, 3) };
            bRes.Click += (s, e) => new BackupWindow(_main) { Owner = this }.ShowDialog();
            bp.Children.Add(bNew);
            bp.Children.Add(bRes);
            AddRow(L.T("s.backuprow"), bp);
        }

        public void RefreshValues()
        {
            _refreshing = true;
            try { foreach (Action r in _refreshers) r(); }
            finally { _refreshing = false; }
        }

        private void Changed()
        {
            if (_refreshing) return;
            _applyTimer.Stop();
            _applyTimer.Start();
        }

        private void ApplyNow()
        {
            _applyTimer.Stop();
            UiScale.Apply(this);
            _main.ApplySettings();
            ConfigStore.Save();
            EdgePreview.Refresh();
        }

        // ------------------------------------------------------------ Bausteine

        private void AddRow(string label, UIElement control)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int r = _grid.RowDefinitions.Count - 1;
            if (label != null)
            {
                var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4), TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(tb, r);
                _grid.Children.Add(tb);
            }
            if (control is FrameworkElement fe) fe.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(control, r);
            Grid.SetColumn(control, 1);
            _grid.Children.Add(control);
        }

        private void Header(string text)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, _grid.RowDefinitions.Count > 1 ? 16 : 4, 0, 4)
            };
            Grid.SetRow(tb, _grid.RowDefinitions.Count - 1);
            Grid.SetColumnSpan(tb, 2);
            _grid.Children.Add(tb);
        }

        private void Note(string text)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tb = new TextBlock { Text = text, Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetRow(tb, _grid.RowDefinitions.Count - 1);
            Grid.SetColumnSpan(tb, 2);
            _grid.Children.Add(tb);
        }

        private TextBox NumBox(Func<double> get, Action<double> set, double min, double max, bool integer)
        {
            var tb = new TextBox { Width = 70, Text = Fmt(get()), VerticalContentAlignment = VerticalAlignment.Center };
            tb.TextChanged += (s, e) =>
            {
                if (_refreshing) return;
                if (double.TryParse(tb.Text.Trim().Replace(',', '.'), NumberStyles.Float, Inv, out double v) && v >= min && v <= max)
                {
                    tb.ClearValue(BackgroundProperty);
                    set(integer ? Math.Round(v) : v);
                    Changed();
                }
                else tb.Background = ErrBrush;
            };
            _refreshers.Add(() => { tb.Text = Fmt(get()); tb.ClearValue(BackgroundProperty); });
            return tb;
        }

        private void Num(string label, Func<double> get, Action<double> set, double min, double max, string unit, bool integer = true)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(NumBox(get, set, min, max, integer));
            if (unit != null)
                sp.Children.Add(new TextBlock { Text = unit, Foreground = Brushes.Gray, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            AddRow(label, sp);
        }

        private void TextRow(string label, Func<string> get, Action<string> set)
        {
            var tb = new TextBox { Text = get(), MinWidth = 240, VerticalContentAlignment = VerticalAlignment.Center };
            tb.TextChanged += (s, e) => { if (_refreshing) return; set(tb.Text.Trim()); Changed(); };
            _refreshers.Add(() => tb.Text = get());
            AddRow(label, tb);
        }

        private void Check(string label, Func<bool> get, Action<bool> set)
        {
            var cb = new CheckBox { IsChecked = get(), VerticalAlignment = VerticalAlignment.Center };
            cb.Click += (s, e) => { set(cb.IsChecked == true); Changed(); };
            _refreshers.Add(() => cb.IsChecked = get());
            AddRow(label, cb);
        }

        private void Choice(string label, string[] options, Func<int> get, Action<int> set)
        {
            var cb = new ComboBox { ItemsSource = options, SelectedIndex = get(), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220 };
            cb.SelectionChanged += (s, e) =>
            {
                if (_refreshing || cb.SelectedIndex < 0) return;
                set(cb.SelectedIndex);
                Changed();
            };
            _refreshers.Add(() => cb.SelectedIndex = get());
            AddRow(label, cb);
        }

        private void ColorRow(string label, Func<string> get, Action<string> set)
        {
            var preview = new Border
            {
                Width = 34, Height = 22, CornerRadius = new CornerRadius(3),
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = L.T("s.pickcolor")
            };
            var tb = new TextBox { Width = 90, Text = get(), VerticalContentAlignment = VerticalAlignment.Center };
            Action updatePreview = () =>
            {
                try { preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tb.Text.Trim())); }
                catch { preview.Background = Brushes.Transparent; }
            };
            updatePreview();
            tb.TextChanged += (s, e) =>
            {
                if (_refreshing) return;
                string t = tb.Text.Trim();
                bool ok;
                try { ok = t.StartsWith("#") && ColorConverter.ConvertFromString(t) is Color; }
                catch { ok = false; }
                if (ok)
                {
                    tb.ClearValue(BackgroundProperty);
                    set(t);
                    updatePreview();
                    Changed();
                }
                else tb.Background = ErrBrush;
            };
            _refreshers.Add(() => { tb.Text = get(); updatePreview(); });
            preview.MouseLeftButtonUp += (s, e) =>
            {
                string picked = PickColor(tb.Text.Trim());
                if (picked != null) tb.Text = picked;   // TextChanged übernimmt und speichert
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(tb);
            sp.Children.Add(preview);
            AddRow(label, sp);
        }

        private string PickColor(string current) => ColorPicker.Pick(this, current);

        private void EdgeRow(string label, Func<EdgeZone> zone)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var cb = new CheckBox { Content = L.T("s.e.active"), IsChecked = zone().Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            cb.Click += (s, e) => { zone().Enabled = cb.IsChecked == true; Changed(); };
            _refreshers.Add(() => cb.IsChecked = zone().Enabled);
            sp.Children.Add(cb);
            sp.Children.Add(new TextBlock { Text = L.T("s.e.from"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var from = NumBox(() => zone().From, v => zone().From = v, 0, 100, false);
            from.Width = 50;
            sp.Children.Add(from);
            sp.Children.Add(new TextBlock { Text = L.T("s.e.to"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            var to = NumBox(() => zone().To, v => zone().To = v, 0, 100, false);
            to.Width = 50;
            sp.Children.Add(to);
            sp.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            AddRow(label, sp);
        }

        private static string Fmt(double v) => v.ToString("0.##", Inv);
    }
}
