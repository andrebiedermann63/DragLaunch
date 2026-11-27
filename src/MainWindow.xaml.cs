using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DragLaunch
{
    public partial class MainWindow : Window
    {
        private enum Mode { None, ItemPending, ItemDrag, Resize }

        private Mode _mode = Mode.None;

        // Ziehen eines Icons
        private ItemData _dragItem;
        private TabData _dragOriginTab;
        private FrameworkElement _dragElement;
        private Point _downPos;
        private Point _grabOffset;
        private const double TileInset = 2;   // Abstand der Kachelfläche zum Zellenrand
        private Image _ghost;
        private Border _plusBadge;
        private Border _countBadge;

        // Mehrfachauswahl (Strg+Klick): gilt für den aktiven Tab
        private readonly HashSet<ItemData> _sel = new HashSet<ItemData>(ReferenceEqualityComparer.Instance);
        private List<ItemData> _group;                       // beim Ziehen: die ganze Auswahl
        private readonly List<Border> _groupMarkers = new List<Border>();
        private int _hoverTab = -1;

        // Grösse ändern
        private string _resizeEdge;
        private Point _resizeStart;
        private int _startCols, _startRows;

        // Tab-Wechsel beim Darüberziehen
        private readonly DispatcherTimer _tabHoverTimer;
        private int _tabHoverTarget = -1;

        private readonly EdgeWatcher _edge = new EdgeWatcher();
        private SettingsWindow _settingsWindow;

        private Brush _labelBrush = Brushes.White, _tabText = Brushes.Gold, _activeTabBg = Brushes.DarkRed, _activeTabText = Brushes.White;
        private static readonly Brush HoverBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)));

        private static AppSettings S => ConfigStore.Settings;
        private static List<TabData> Tabs => ConfigStore.Tabs;
        private TabData ActiveTab => Tabs[Math.Max(0, Math.Min(S.ActiveTab, Tabs.Count - 1))];
        private double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;

        public MainWindow()
        {
            InitializeComponent();

            RootGrid.MouseMove += Root_MouseMove;
            RootGrid.MouseLeftButtonUp += Root_MouseUp;
            RootGrid.LostMouseCapture += Root_LostMouseCapture;

            _tabHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            _tabHoverTimer.Tick += (s, e) =>
            {
                _tabHoverTimer.Stop();
                if (_tabHoverTarget >= 0 && _tabHoverTarget != S.ActiveTab) SelectTab(_tabHoverTarget);
            };

            _edge.OwnHandle = () => new WindowInteropHelper(this).Handle;
            Launcher.Launched += OnLaunched;
            _edge.Triggered += () => BringToFront(S.ActivateFocus);

            // Nie während eines Layout-Durchgangs umpositionieren/neu zeichnen, sondern danach
            SizeChanged += (s, e) =>
            {
                Dbg.SizeChanges++;
                Dbg.W($"SizeChanged {e.PreviousSize.Width:0}x{e.PreviousSize.Height:0} -> {e.NewSize.Width:0}x{e.NewSize.Height:0}");
                PositionLater();
            };
            LayoutUpdated += (s, e) => Dbg.Layouts++;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            DpiChanged += (s, e) =>
            {
                // Das Ereignis kommt auch von jedem neu erzeugten Icon (und auch ohne echte Änderung).
                // Nur reagieren, wenn sich die Skalierung des Fensters selbst wirklich geändert hat -
                // sonst entsteht eine Endlosschleife (Neuzeichnen -> neue Icons -> Ereignis -> Neuzeichnen ...).
                if (!ReferenceEquals(e.OriginalSource, this)) return;
                if (Math.Abs(e.OldDpi.PixelsPerDip - e.NewDpi.PixelsPerDip) < 0.001) return;
                Dbg.DpiChanges++;
                Dbg.W($"DpiChanged {e.OldDpi.PixelsPerDip:0.##} -> {e.NewDpi.PixelsPerDip:0.##}");
                ShellIcons.ClearCache();
                RenderLater();
            };

            Loaded += (s, e) => { Dbg.W("Loaded"); UpdatePosition(); _edge.Start(); StartTopmostGuard(); };
            // Esc = DragLaunch in den Hintergrund
            KeyDown += (s, e) =>
            {
                if (_mode != Mode.None) return;
                if (e.Key == Key.Escape)
                {
                    if (_sel.Count > 0) ClearSelection(); else SendToBack();   // Esc: erst Auswahl aufheben
                    e.Handled = true;
                }
                else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    SelectAll();
                    e.Handled = true;
                }
            };
            Closed += (s, e) =>
            {
                ConfigStore.Flush();
                _edge.Stop();
                Launcher.Launched -= OnLaunched;
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            };

            Left = S.OffsetLeft;
            Top = Math.Max(0, SystemParameters.PrimaryScreenHeight - S.OffsetBottom - 200);
            ApplySettings();
        }

        // =====================================================================
        //  Einstellungen anwenden / Position
        // =====================================================================

        public void ApplySettings()
        {
            AppSettings s = S;
            FrameBorder.Background = BrushFrom(s.FrameColor, Color.FromRgb(0xD8, 0, 0));
            CanvasBorder.Background = BrushFrom(s.BackgroundColor, Color.FromRgb(5, 12, 5));
            _labelBrush = BrushFrom(s.LabelColor, Colors.White);
            _tabText = BrushFrom(s.TabTextColor, Colors.Gold);
            _activeTabBg = BrushFrom(s.ActiveTabColor, Color.FromRgb(0x3A, 0, 0));
            _activeTabText = BrushFrom(s.ActiveTabTextColor, Colors.White);

            MenuButton.ToolTip = L.T("tip.menu");
            GripRight.ToolTip = L.T("tip.cols");
            GripTop.ToolTip = L.T("tip.rows");
            MenuGlyph.Foreground = _tabText;
            MenuGlyph.FontSize = s.TabFontSize + 3;
            TabBar.Height = Math.Ceiling(s.TabFontSize * 1.45) + 6;
            ShowInTaskbar = s.ShowInTaskbar;

            bool changed = NormalizeCells();
            MinGrid(out int minCols, out int minRows);
            if (s.Columns < minCols) { s.Columns = minCols; changed = true; }
            if (s.Rows < minRows) { s.Rows = minRows; changed = true; }
            if (changed)
            {
                ConfigStore.Save();
                _settingsWindow?.RefreshValues();
            }

            ItemCanvas.Width = s.Columns * s.CellWidth;
            ItemCanvas.Height = s.Rows * s.CellHeight;
            BuildTileBrush();
            ShowTiles(false);

            IconLoader.Init(Dispatcher);
            StartNetRetry();
            RenderTabs();
            RenderItems();
            PositionLater();
        }

        /// <summary>Neu laden nach Wiederherstellen eines Backups.</summary>
        public void ReloadAll()
        {
            ShellIcons.ClearCache();
            ApplySettings();
            _settingsWindow?.RefreshValues();
        }

        /// <summary>
        /// Fenster immer im eingestellten Abstand zum linken und unteren Rand des Hauptbildschirms.
        /// Gerechnet wird in physischen Pixeln, damit es auch bei Monitoren mit unterschiedlicher Skalierung stimmt.
        /// </summary>
        public void UpdatePosition()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var r)) return;
            Native.RECT a = Native.PrimaryArea(S.UseWorkArea);
            double sc = Native.PrimaryScale();
            int height = r.Bottom - r.Top;
            int x = a.Left + (int)Math.Round(S.OffsetLeft * sc);
            int y = a.Bottom - (int)Math.Round(S.OffsetBottom * sc) - height;
            Dbg.Positions++;
            Dbg.W($"UpdatePosition Fenster=({r.Left},{r.Top},{r.Right},{r.Bottom}) Bereich=({a.Left},{a.Top},{a.Right},{a.Bottom}) Skala={sc:0.##} Ziel=({x},{y})");
            if (x != r.Left || y != r.Top)
                Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        private void SaveOffsetsFromCurrentPosition()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var r)) return;
            Native.RECT a = Native.PrimaryArea(S.UseWorkArea);
            double sc = Native.PrimaryScale();
            S.OffsetLeft = Math.Round((r.Left - a.Left) / sc);
            S.OffsetBottom = Math.Round((a.Bottom - r.Bottom) / sc);
            ConfigStore.Save();
            UpdatePosition();
            _settingsWindow?.RefreshValues();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            EdgePreview.Refresh();   // Randzonen-Anzeige an neue Auflösung anpassen (nur sichtbar bei offenen Einstellungen)
            // Auflösung oder Ausrichtung geändert: sofort und nochmals verzögert neu positionieren
            Dispatcher.BeginInvoke(new Action(UpdatePosition));
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            t.Tick += (s, a) => { t.Stop(); UpdatePosition(); };
            t.Start();
        }

        private void OnSystemParametersChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "WorkArea" || e.PropertyName == "PrimaryScreenHeight" || e.PropertyName == "PrimaryScreenWidth")
                Dispatcher.BeginInvoke(new Action(UpdatePosition));
        }

        /// <summary>Fenster über alle anderen bringen (ohne dauerhaft "immer im Vordergrund" zu sein).</summary>
        public void BringToFront(bool takeFocus)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            if (!IsVisible) Show();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            const uint flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, flags);
            Native.SetWindowPos(hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
            if (IsTopmost(hwnd)) Native.SetWindowPos(hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, flags);   // zweiter Versuch

            if (!takeFocus)
            {
                // Ohne Fokus bleibt das bisherige Fenster aktiv. Ein Klick darauf würde es nicht mehr nach vorne holen,
                // daher den nächsten Klick ausserhalb beobachten und DragLaunch dann dahinter schieben.
                StartClickAwayWatch();
                return;
            }
            StopClickAwayWatch();
            IntPtr fg = Native.GetForegroundWindow();
            uint fgThread = Native.GetWindowThreadProcessId(fg, IntPtr.Zero);
            uint me = Native.GetCurrentThreadId();
            if (fgThread != 0 && fgThread != me)
            {
                Native.AttachThreadInput(me, fgThread, true);
                Native.SetForegroundWindow(hwnd);
                Native.AttachThreadInput(me, fgThread, false);
            }
            else Native.SetForegroundWindow(hwnd);
            Activate();
        }

        // ---------------------------------------------------------------------
        //  Nach passivem Einblenden: Klick auf ein anderes Fenster -> DragLaunch geht dahinter
        // ---------------------------------------------------------------------

        private DispatcherTimer _clickAwayTimer;
        private bool _clickDown, _clickOutside;

        private void StartClickAwayWatch()
        {
            if (_clickAwayTimer == null)
            {
                _clickAwayTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
                _clickAwayTimer.Tick += ClickAwayTick;
            }
            // Ist beim Einblenden schon eine Taste gedrückt (z.B. Datei wird gezogen), zählt erst der nächste Klick
            _clickDown = AnyMouseButtonDown();
            _clickOutside = false;
            _clickAwayTimer.Start();
        }

        private void StopClickAwayWatch() => _clickAwayTimer?.Stop();

        /// <summary>Nach dem Starten eines Programms/Ordners: DragLaunch hinter alle Fenster, damit es nichts verdeckt.</summary>
        private void OnLaunched()
        {
            if (S.BackAfterLaunch) SendToBack();
        }

        /// <summary>DragLaunch hinter alle Fenster legen (Menü, Esc-Taste, nach dem Starten).</summary>
        public void SendToBack()
        {
            StopClickAwayWatch();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            ClearTopmost(hwnd);
            Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private static bool IsTopmost(IntPtr hwnd) => (Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;

        private void ClearTopmost(IntPtr hwnd)
        {
            if (Topmost) Topmost = false;
            if (IsTopmost(hwnd))
                Native.SetWindowPos(hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // Wächter: DragLaunch darf nie dauerhaft "immer im Vordergrund" bleiben (sonst lässt es sich nicht mehr wegklicken)
        private DispatcherTimer _topmostGuard;
        private bool _topmostReported;

        private void StartTopmostGuard()
        {
            _topmostGuard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _topmostGuard.Tick += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero || !IsTopmost(hwnd)) { _topmostReported = false; return; }
                ClearTopmost(hwnd);
                if (!_topmostReported) Log.Note("Hinweis: DragLaunch war 'immer im Vordergrund' und wurde zurückgesetzt.");
                _topmostReported = true;
            };
            _topmostGuard.Start();
        }

        private static bool AnyMouseButtonDown()
            => (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0
            || (Native.GetAsyncKeyState(Native.VK_RBUTTON) & 0x8000) != 0
            || (Native.GetAsyncKeyState(Native.VK_MBUTTON) & 0x8000) != 0;

        private void ClickAwayTick(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            bool down = AnyMouseButtonDown();

            if (down && !_clickDown)
            {
                _clickDown = true;
                Native.GetCursorPos(out var p);
                Native.GetWindowRect(hwnd, out var r);
                _clickOutside = !(p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom);
                if (!_clickOutside) StopClickAwayWatch();      // Klick in DragLaunch: ab jetzt normales Fenster
                return;
            }
            if (down || !_clickDown) return;

            // Taste losgelassen nach einem Klick ausserhalb
            _clickDown = false;
            if (!_clickOutside) return;

            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == hwnd) return;
            Native.GetWindowProcessId(fg, out uint pid);
            if (pid == (uint)Environment.ProcessId) return;   // eigene Liste, eigenes Menü oder eigener Dialog
            var cls = new StringBuilder(64);
            Native.GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return;   // Desktop / Taskleiste

            // DragLaunch direkt hinter das angeklickte Fenster legen
            StopClickAwayWatch();
            Native.SetWindowPos(hwnd, fg, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // ---------------------------------------------------------------------
        //  Favicon einer Webseite holen (im Hintergrund)
        // ---------------------------------------------------------------------

        private async void FetchFavicon(ItemData it, bool manual)
        {
            string url = it.Path;
            List<byte[]> candidates;
            try { candidates = await FaviconFetcher.DownloadCandidatesAsync(url); }   // zurück auf dem UI-Thread
            catch { candidates = new List<byte[]>(); }

            // Bestes lesbares Bild wählen (grösste Breite)
            byte[] best = null;
            int bestW = 0;
            foreach (byte[] d in candidates)
            {
                int w = FaviconFetcher.UsableWidth(d);
                if (w >= 16 && w > bestW) { best = d; bestW = w; }
            }
            if (best == null)
            {
                if (manual) MessageBox.Show(this, L.T("favicon.none"), "DragLaunch", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (it.Path != url) return;   // inzwischen geändert

            string name = "favicon";
            try { name = new Uri(url).Host.Replace("www.", ""); } catch { }
            try
            {
                it.IconFile = IconStore.SaveBytes(name, FaviconFetcher.ImageExtension(best), best);
                ShellIcons.ClearCache();
                RenderItems();
                ConfigStore.Save();
            }
            catch (Exception ex) { Log.Write("Favicon", ex); }
        }

        // =====================================================================
        //  Tabs
        // =====================================================================

        private bool _renderPending, _positionPending, _rendering;

        /// <summary>Tabs und Icons neu zeichnen, sobald WPF mit dem aktuellen Layout fertig ist.</summary>
        private void RenderLater()
        {
            if (_renderPending) return;
            _renderPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _renderPending = false;
                RenderTabs();
                RenderItems();
            }));
        }

        private void PositionLater()
        {
            if (_positionPending) return;
            _positionPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _positionPending = false;
                UpdatePosition();
            }));
        }

        private void RenderTabs()
        {
            TabStrip.Children.Clear();
            for (int i = 0; i < Tabs.Count; i++)
            {
                int idx = i;
                bool active = i == S.ActiveTab;
                Brush tabBg = active ? _activeTabBg : Brushes.Transparent;
                Brush tabFg = active ? _activeTabText : _tabText;
                if (TryColor(Tabs[i].Color, out Color tc))
                {
                    // eigene Tabfarbe; der aktive Tab wird dunkler dargestellt, damit man ihn erkennt
                    if (active) tc = Color.FromArgb(tc.A, (byte)(tc.R * 0.6), (byte)(tc.G * 0.6), (byte)(tc.B * 0.6));
                    tabBg = Freeze(new SolidColorBrush(tc));
                    tabFg = ReadableOn(tabFg, tc);
                }
                var tb = new TextBlock
                {
                    Text = Tabs[i].Name,
                    FontSize = S.TabFontSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = tabFg,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(4, 0, 4, 0)
                };
                var b = new Border
                {
                    Child = tb,
                    Background = tabBg,
                    CornerRadius = new CornerRadius(5, 5, 0, 0),
                    Margin = new Thickness(1, 0, 1, 0),
                    Tag = idx,
                    AllowDrop = true,
                    Cursor = Cursors.Hand
                };
                b.MouseLeftButtonDown += (s, e) => { if (_mode == Mode.None) SelectTab(idx); e.Handled = true; };
                b.MouseRightButtonUp += (s, e) => { ShowTabMenu(idx, b); e.Handled = true; };

                // Dateien aus dem Explorer: kurz über dem Tab halten = Tab wechseln, loslassen = in diesen Tab
                b.DragEnter += (s, e) =>
                {
                    if (!HasDropData(e.Data)) return;
                    _tabHoverTarget = idx;
                    _tabHoverTimer.Stop();
                    _tabHoverTimer.Start();
                };
                b.DragLeave += (s, e) => _tabHoverTimer.Stop();
                b.DragOver += (s, e) =>
                {
                    e.Effects = HasDropData(e.Data) ? AcceptEffect(e) : DragDropEffects.None;
                    e.Handled = true;
                };
                b.Drop += (s, e) =>
                {
                    _tabHoverTimer.Stop();
                    AddDropped(e.Data, Tabs[idx], null);
                    e.Handled = true;
                };
                TabStrip.Children.Add(b);
            }
        }

        private void SelectTab(int i, bool save = true)
        {
            if (i < 0 || i >= Tabs.Count) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (i != S.ActiveTab) Sounds.Play();
            long tSound = sw.ElapsedMilliseconds;
            S.ActiveTab = i;
            if (_mode != Mode.ItemDrag) _sel.Clear();   // Auswahl gilt nur im Tab (beim Ziehen bleibt sie)
            RenderTabs();
            RenderItems();
            long tRender = sw.ElapsedMilliseconds - tSound;
            if (save) ConfigStore.SaveSoon();   // aktiver Tab: verzögert speichern, nicht bei jedem Klick auf die Platte
            if (sw.ElapsedMilliseconds > 400)
                Log.Note($"Langsamer Tab-Wechsel zu \"{Tabs[i].Name}\": {sw.ElapsedMilliseconds} ms (Sound {tSound} ms, Zeichnen {tRender} ms, {Tabs[i].Items.Count} Icons)");
        }

        private int TabIndexAt(MouseEventArgs e)
        {
            foreach (FrameworkElement child in TabStrip.Children)
            {
                Point p = e.GetPosition(child);
                if (p.X >= 0 && p.Y >= 0 && p.X < child.ActualWidth && p.Y < child.ActualHeight)
                    return (int)child.Tag;
            }
            return -1;
        }

        private void ShowTabMenu(int idx, FrameworkElement target)
        {
            var cm = NewMenu();
            cm.Items.Add(MI(L.T("tab.rename"), () => RenameTab(idx)));
            cm.Items.Add(MI(L.T("tab.newright"), () => NewTab(idx + 1)));
            cm.Items.Add(new Separator());
            cm.Items.Add(MI(L.T("tab.left"), () => MoveTab(idx, idx - 1), idx > 0));
            cm.Items.Add(MI(L.T("tab.right"), () => MoveTab(idx, idx + 1), idx < Tabs.Count - 1));
            cm.Items.Add(new Separator());
            cm.Items.Add(MI(L.T("tab.color"), () =>
            {
                string c = ColorPicker.Pick(this, string.IsNullOrEmpty(Tabs[idx].Color) ? "#FFD24A" : Tabs[idx].Color);
                if (c == null) return;
                Tabs[idx].Color = c;
                RenderTabs();
                ConfigStore.Save();
            }));
            if (!string.IsNullOrEmpty(Tabs[idx].Color))
                cm.Items.Add(MI(L.T("tab.colorremove"), () => { Tabs[idx].Color = ""; RenderTabs(); ConfigStore.Save(); }));
            cm.Items.Add(new Separator());
            cm.Items.Add(MI(L.T("tab.delete"), () => DeleteTab(idx), Tabs.Count > 1));
            cm.PlacementTarget = target;
            cm.IsOpen = true;
        }

        private void RenameTab(int idx)
        {
            string name = InputBox.Show(this, L.T("tab.renametitle"), L.T("tab.nameprompt"), Tabs[idx].Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            Tabs[idx].Name = name.Trim();
            RenderTabs();
            ConfigStore.Save();
        }

        private void NewTab(int at)
        {
            string name = InputBox.Show(this, L.T("tab.newtitle"), L.T("tab.newprompt"), L.T("new"));
            if (string.IsNullOrWhiteSpace(name)) return;
            at = Math.Max(0, Math.Min(at, Tabs.Count));
            Tabs.Insert(at, new TabData { Name = name.Trim() });
            SelectTab(at);
        }

        private void MoveTab(int from, int to)
        {
            if (to < 0 || to >= Tabs.Count) return;
            TabData t = Tabs[from];
            Tabs[from] = Tabs[to];
            Tabs[to] = t;
            if (S.ActiveTab == from) S.ActiveTab = to;
            else if (S.ActiveTab == to) S.ActiveTab = from;
            RenderTabs();
            ConfigStore.Save();
        }

        private void DeleteTab(int idx)
        {
            if (Tabs.Count <= 1) return;
            TabData t = Tabs[idx];
            string msg = t.Items.Count > 0
                ? L.F("tab.delitems", t.Name, t.Items.Count)
                : L.F("tab.delempty", t.Name);
            if (MessageBox.Show(this, msg, "DragLaunch", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Tabs.RemoveAt(idx);
            if (S.ActiveTab > idx || S.ActiveTab >= Tabs.Count) S.ActiveTab = Math.Max(0, S.ActiveTab - 1);
            RenderTabs();
            RenderItems();
            ConfigStore.Save();
        }

        // =====================================================================
        //  Icons
        // =====================================================================

        private void RenderItems()
        {
            if (_rendering) { RenderLater(); return; }   // kein verschachteltes Neuzeichnen
            _rendering = true;
            try { RenderItemsCore(); }
            finally { _rendering = false; }
        }

        private void RenderItemsCore()
        {
            Dbg.Renders++;
            Dbg.W($"RenderItems Tab={S.ActiveTab} Icons={ActiveTab.Items.Count}");
            ItemCanvas.Children.Clear();
            _groupMarkers.Clear();
            _netIcons.Clear();
            if (_mode != Mode.ItemDrag) _sel.RemoveWhere(x => !ActiveTab.Items.Contains(x));
            int gen = IconLoader.NextGeneration();
            double dpi = Dpi;
            foreach (ItemData it in ActiveTab.Items.ToList())
                ItemCanvas.Children.Add(BuildItem(it, gen, dpi));

            // Markierung für die Ziel-Kachel beim Ziehen
            _marker = new Border
            {
                Width = S.CellWidth,
                Height = S.CellHeight,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                BorderBrush = _tabText,
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            ItemCanvas.Children.Add(_marker);
        }

        /// <summary>Icon sofort aus dem Speicher, sonst im Hintergrund laden (Kachel erscheint sofort).</summary>
        private void SetIcon(ItemData it, Image img, int gen, double dpi)
        {
            ImageSource cached = ShellIcons.CachedForItem(it, S.IconSize, dpi);
            if (cached != null) img.Source = cached;
            // Eigene Icons kommen aus dem Icons-Ordner (lokal) - auch wenn das Ziel im Netz liegt
            bool network = string.IsNullOrWhiteSpace(it.IconFile) && ShellIcons.IsNetworkPath(Launcher.Expand(it.Path));
            if (network) _netIcons.Add((it, img));       // Netzwerk: Erreichbarkeit immer im Hintergrund prüfen
            else if (cached != null) return;
            Dbg.W("  Icon in Warteschlange: " + it.Path);
            IconLoader.Request(it, S.IconSize, dpi, img, gen, network);
        }

        // Netzwerk-Kacheln des aktiven Tabs: alle 20 s prüfen, ob das Ziel (wieder) erreichbar ist -> echtes Icon bzw. Weltkugel
        private readonly List<(ItemData Item, Image Img)> _netIcons = new List<(ItemData, Image)>();
        private DispatcherTimer _netRetry;

        private void StartNetRetry()
        {
            _netRetry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _netRetry.Tick += (s, e) =>
            {
                if (_netIcons.Count == 0 || _mode != Mode.None || !IsVisible) return;
                double dpi = Dpi;
                int gen = IconLoader.CurrentGeneration;
                foreach (var (it, img) in _netIcons.ToList())
                    IconLoader.Request(it, S.IconSize, dpi, img, gen, true);
            };
            _netRetry.Start();
        }

        private string TracedLabel(ItemData it)
        {
            Dbg.W("  Beschriftung: " + it.Label);
            string t = WrapLabel(it.Label ?? "", S.CellWidth - 12, S.MaxLabelLines, S.FontSize);
            Dbg.W("  Beschriftung fertig: " + t.Replace("\n", " | "));
            return t;
        }

        private FrameworkElement BuildItem(ItemData it, int gen, double dpi)
        {
            AppSettings s = S;
            var img = new Image
            {
                Width = s.IconSize,
                Height = s.IconSize,
                Margin = new Thickness(0, 5, 0, 3),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            SetIcon(it, img, gen, dpi);
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var sp = new StackPanel();
            sp.Children.Add(img);
            if (s.MaxLabelLines > 0)
            {
                sp.Children.Add(new TextBlock
                {
                    // eigener Umbruch statt WPF-Wrap+Trimming (das kann sich bei langen Wörtern aufhängen)
                    Text = TracedLabel(it),
                    FontSize = s.FontSize,
                    Foreground = LabelBrushFor(it.TileColor),
                    TextWrapping = TextWrapping.NoWrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    LineHeight = s.LineHeight,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    MaxHeight = s.LineHeight * s.MaxLabelLines,
                    Margin = new Thickness(3, 0, 3, 3)
                });
            }

            var b = new Border
            {
                // Fläche = Kachelrahmen (2 px Rand ringsum), damit eingefärbte Kacheln Zwischenräume haben
                Width = s.CellWidth - 2 * TileInset,
                Margin = new Thickness(TileInset),
                Child = sp,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(5),
                Tag = it,
                AllowDrop = true,
                ToolTip = it.Label + "\n" + it.Path + (string.IsNullOrEmpty(it.Arguments) ? "" : " " + it.Arguments)
            };
            ToolTipService.SetInitialShowDelay(b, 900);
            if (_mode == Mode.ItemDrag && (ReferenceEquals(it, _dragItem) || (_group != null && _group.Contains(it)))) b.Opacity = 0.3;

            bool selected = _sel.Contains(it);
            Brush baseBrush = TileBrush(it.TileColor, selected), hoverBrush = TileBrush(it.TileColor, true);
            if (selected)
            {
                b.BorderBrush = _tabText;                       // ausgewählt: Rahmen in der Tab-Schriftfarbe
                b.BorderThickness = new Thickness(2);
            }
            b.Background = baseBrush;
            b.MouseEnter += (o, e) => { if (_mode == Mode.None) b.Background = hoverBrush; };
            b.MouseLeave += (o, e) => b.Background = baseBrush;
            b.MouseLeftButtonDown += Item_MouseDown;
            b.MouseRightButtonUp += (o, e) =>
            {
                if (_sel.Contains(it) && _sel.Count > 1) ShowGroupMenu(b);
                else { if (_sel.Count > 0) ClearSelection(); ShowItemMenu(it, b); }
                e.Handled = true;
            };
            b.DragOver += Item_DragOver;
            b.Drop += Item_Drop;

            b.Height = s.CellHeight - 2 * TileInset;
            Canvas.SetLeft(b, it.Col * s.CellWidth);
            Canvas.SetTop(b, it.Row * s.CellHeight);
            return b;
        }

        /// <summary>
        /// Bricht die Beschriftung auf max. maxLines Zeilen um. Bevorzugt nach Leerzeichen, _ - .
        /// sonst mitten im Wort. Was nicht passt, wird mit … gekürzt.
        /// </summary>
        private string WrapLabel(string text, double maxWidth, int maxLines, double fontSize)
        {
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length == 0 || maxLines <= 0) return "";
            var tf = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double ppd = Dpi;
            Func<string, double> width = t => new FormattedText(t, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, tf, fontSize, Brushes.White, ppd).WidthIncludingTrailingWhitespace;
            maxWidth = Math.Max(10, maxWidth);

            var lines = new List<string>();
            string rest = text;
            while (rest.Length > 0 && lines.Count < maxLines)
            {
                if (width(rest) <= maxWidth) { lines.Add(rest); break; }

                if (lines.Count == maxLines - 1)
                {
                    int n = rest.Length;
                    while (n > 1 && width(rest.Substring(0, n) + "…") > maxWidth) n--;
                    lines.Add(rest.Substring(0, n).TrimEnd() + "…");
                    break;
                }

                int fit = 1;
                while (fit < rest.Length && width(rest.Substring(0, fit + 1)) <= maxWidth) fit++;
                int cut = fit;
                for (int i = fit; i > fit / 3 && i > 0; i--)
                {
                    char c = rest[i - 1];
                    if (c == ' ' || c == '_' || c == '-' || c == '.') { cut = i; break; }
                }
                lines.Add(rest.Substring(0, cut).TrimEnd());
                rest = rest.Substring(cut).TrimStart();
            }
            return string.Join("\n", lines);
        }

        private void ActivateItem(ItemData it, FrameworkElement element)
        {
            string folder = Launcher.FolderOf(it);
            if (folder != null)
            {
                bool menu = it.FolderMode == FolderMode.Default ? S.FolderOpensMenu : it.FolderMode == FolderMode.Menu;
                if (menu) { ShowFolderMenu(folder, element); return; }
            }
            Launcher.Start(it);
        }

        private void ShowFolderMenu(string folder, FrameworkElement element)
        {
            ContextMenu cm = Launcher.BuildFolderMenu(folder, S.MenuFontSize, Dpi);
            cm.PlacementTarget = element;
            cm.Placement = PlacementMode.Bottom;
            cm.IsOpen = true;
        }

        private void ShowItemMenu(ItemData it, FrameworkElement el)
        {
            var cm = NewMenu();
            string folder = Launcher.FolderOf(it);
            bool isUrl = Launcher.IsUrl(Launcher.Expand(it.Path));

            if (folder != null)
            {
                cm.Items.Add(MI(L.T("folder.open"), () => Launcher.Open(folder)));
                cm.Items.Add(MI(L.T("item.showlist"), () => ShowFolderMenu(folder, el)));
                cm.Items.Add(new Separator());
                bool showsList = it.FolderMode == FolderMode.Default ? S.FolderOpensMenu : it.FolderMode == FolderMode.Menu;
                cm.Items.Add(new MenuItem { Header = L.T("item.clickheader"), IsEnabled = false, FontWeight = FontWeights.SemiBold });
                cm.Items.Add(ModeItem(it, FolderMode.Menu, L.T("item.clicklist"), showsList));
                cm.Items.Add(ModeItem(it, FolderMode.Open, L.T("item.clickopen"), !showsList));
            }
            else
            {
                cm.Items.Add(MI(L.T("item.open"), () => Launcher.Start(it)));
                cm.Items.Add(MI(L.T("item.runadmin"), () => Launcher.Start(it, null, true), !isUrl));
            }
            cm.Items.Add(MI(L.T("item.location"), () => Launcher.ShowInExplorer(it.Path), !isUrl));
            if (FaviconFetcher.IsWebUrl(it.Path))
                cm.Items.Add(MI(L.T("item.favicon"), () => FetchFavicon(it, true)));
            cm.Items.Add(new Separator());
            cm.Items.Add(MI(L.T("item.color"), () =>
            {
                string c = ColorPicker.Pick(this, string.IsNullOrEmpty(it.TileColor) ? "#3A0000" : it.TileColor);
                if (c == null) return;
                it.TileColor = c;
                RenderItems();
                ConfigStore.Save();
            }));
            if (!string.IsNullOrEmpty(it.TileColor))
                cm.Items.Add(MI(L.T("item.colorremove"), () => { it.TileColor = ""; RenderItems(); ConfigStore.Save(); }));
            cm.Items.Add(MI(L.T("item.properties"), () => EditItem(it)));
            cm.Items.Add(MI(L.T("item.remove"), () => RemoveItem(it)));
            cm.PlacementTarget = el;
            cm.IsOpen = true;
        }

        private MenuItem ModeItem(ItemData it, FolderMode mode, string text, bool isChecked)
        {
            var mi = new MenuItem { Header = text, IsCheckable = true, IsChecked = isChecked };
            mi.Click += (s, e) => { it.FolderMode = mode; ConfigStore.Save(); };
            return mi;
        }

        private void EditItem(ItemData it)
        {
            var dlg = new ItemPropertiesWindow(it) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                ShellIcons.ClearCache();
                RenderItems();
                ConfigStore.Save();
            }
        }

        private void RemoveItem(ItemData it)
        {
            foreach (TabData t in Tabs) t.Items.Remove(it);
            RenderItems();
            ConfigStore.Save();
        }

        // =====================================================================
        //  Kacheln
        // =====================================================================

        private static ItemData ItemAt(TabData tab, int c, int r, ItemData except = null)
            => tab.Items.FirstOrDefault(i => i.Col == c && i.Row == r && !ReferenceEquals(i, except));

        /// <summary>Kachel unter einem Punkt der Icon-Fläche (immer innerhalb des Rasters).</summary>
        private void CellAt(Point p, out int c, out int r)
        {
            c = Math.Max(0, Math.Min(S.Columns - 1, (int)Math.Floor(p.X / S.CellWidth)));
            r = Math.Max(0, Math.Min(S.Rows - 1, (int)Math.Floor(p.Y / S.CellHeight)));
        }

        /// <summary>Nächstgelegene freie Kachel zu (c,r). Gibt false zurück, wenn alles belegt ist.</summary>
        private bool NearestFree(TabData tab, int c, int r, out int fc, out int fr, ItemData except = null)
        {
            fc = fr = -1;
            double best = double.MaxValue;
            for (int row = 0; row < S.Rows; row++)
                for (int col = 0; col < S.Columns; col++)
                {
                    if (ItemAt(tab, col, row, except) != null) continue;
                    double dx = col - c, dy = row - r;
                    double d = dx * dx + dy * dy * 1.5 + row * 0.001 + col * 0.0001;
                    if (d < best) { best = d; fc = col; fr = row; }
                }
            return fc >= 0;
        }

        /// <summary>Icon in die freie Kachel legen, die (c,r) am nächsten ist. Ist alles voll, kommt eine Zeile dazu.</summary>
        private void PlaceNear(TabData tab, ItemData it, int c, int r)
        {
            if (!NearestFree(tab, c, r, out int fc, out int fr, it))
            {
                S.Rows++;
                ItemCanvas.Height = S.Rows * S.CellHeight;
                NearestFree(tab, c, r, out fc, out fr, it);
            }
            it.Col = fc;
            it.Row = fr;
        }

        /// <summary>
        /// Alle Icons in gültige, eindeutige Kacheln bringen (z.B. nach dem Laden alter INI-Dateien
        /// mit freien Positionen). Gibt true zurück, wenn etwas geändert wurde.
        /// </summary>
        private bool NormalizeCells()
        {
            bool changed = false;
            double cw = S.CellWidth, ch = S.CellHeight;
            foreach (TabData tab in Tabs)
            {
                var placed = new List<ItemData>();
                foreach (ItemData it in tab.Items.ToList())
                {
                    int c = it.Col, r = it.Row;
                    if (c < 0 || r < 0)
                    {
                        c = (int)Math.Round(it.X / cw);
                        r = (int)Math.Round(it.Y / ch);
                    }
                    c = Math.Max(0, c);
                    r = Math.Max(0, r);
                    // Raster ggf. vergrössern, damit nichts verloren geht
                    if (c >= S.Columns) { S.Columns = Math.Min(100, c + 1); changed = true; }
                    if (r >= S.Rows) { S.Rows = Math.Min(50, r + 1); changed = true; }
                    c = Math.Min(c, S.Columns - 1);
                    r = Math.Min(r, S.Rows - 1);

                    if (placed.Any(o => o.Col == c && o.Row == r))
                    {
                        var tmp = new TabData { Items = placed };
                        if (!NearestFree(tmp, c, r, out int fc, out int fr))
                        {
                            S.Rows++;
                            NearestFree(tmp, c, r, out fc, out fr);
                        }
                        c = fc;
                        r = fr;
                    }
                    if (it.Col != c || it.Row != r) { it.Col = c; it.Row = r; changed = true; }
                    placed.Add(it);
                }
            }
            return changed;
        }

        /// <summary>Kleinste Spalten-/Zeilenzahl, bei der kein Icon (in keinem Tab) abgeschnitten wird.</summary>
        private void MinGrid(out int minCols, out int minRows)
        {
            minCols = 1;
            minRows = 1;
            foreach (TabData t in Tabs)
                foreach (ItemData it in t.Items)
                {
                    minCols = Math.Max(minCols, it.Col + 1);
                    minRows = Math.Max(minRows, it.Row + 1);
                }
        }

        /// <summary>Icons des aktiven Tabs ohne Lücken in Lesereihenfolge anordnen.</summary>
        private void CompactIcons()
        {
            TabData tab = ActiveTab;
            int cols = S.Columns;
            int needRows = (int)Math.Ceiling(tab.Items.Count / (double)cols);
            if (needRows > S.Rows) S.Rows = needRows;
            int k = 0;
            foreach (ItemData it in tab.Items.OrderBy(i => i.Row).ThenBy(i => i.Col).ToList())
            {
                it.Col = k % cols;
                it.Row = k / cols;
                k++;
            }
            ApplySettings();
            ConfigStore.Save();
        }

        // ---- Kachelraster sichtbar machen

        private Brush _tileBrush;
        private Border _marker;

        private void BuildTileBrush()
        {
            double cw = S.CellWidth, ch = S.CellHeight;
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, cw, ch))));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 1);
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)), pen,
                new RectangleGeometry(new Rect(TileInset, TileInset, cw - 2 * TileInset, ch - 2 * TileInset), 5, 5)));
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, cw, ch),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, cw, ch),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.Freeze();
            _tileBrush = brush;
        }

        private void ShowTiles(bool show)
        {
            ItemCanvas.Background = (show || S.ShowTiles) && _tileBrush != null ? _tileBrush : Brushes.Transparent;
            if (!show && _marker != null) _marker.Visibility = Visibility.Collapsed;
        }

        /// <summary>Ziel-Kachel hervorheben (Punkt in Koordinaten der Icon-Fläche).</summary>
        private void ShowMarker(Point p)
        {
            if (_marker == null) return;
            bool inside = p.X >= 0 && p.Y >= 0 && p.X < ItemCanvas.ActualWidth && p.Y < ItemCanvas.ActualHeight;
            if (!inside) { _marker.Visibility = Visibility.Collapsed; return; }
            CellAt(p, out int c, out int r);
            Canvas.SetLeft(_marker, c * S.CellWidth);
            Canvas.SetTop(_marker, r * S.CellHeight);
            _marker.Visibility = Visibility.Visible;
        }

        // =====================================================================
        //  Maus: Icons verschieben, klicken, Grösse ändern
        // =====================================================================

        private void Item_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_mode != Mode.None) return;
            var b = (FrameworkElement)sender;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                // Strg+Klick: Kachel zur Auswahl hinzufügen bzw. wieder herausnehmen (nicht starten)
                var item = (ItemData)b.Tag;
                if (!_sel.Remove(item)) _sel.Add(item);
                RenderLater();
                e.Handled = true;
                return;
            }
            _dragElement = b;
            _dragItem = (ItemData)b.Tag;
            _dragOriginTab = ActiveTab;
            _downPos = e.GetPosition(ItemCanvas);
            _grabOffset = e.GetPosition(b);
            _hoverTab = -1;
            _mode = Mode.ItemPending;
            RootGrid.CaptureMouse();
            e.Handled = true;
        }

        private void Grip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_mode != Mode.None) return;
            _resizeEdge = (string)((FrameworkElement)sender).Tag;
            _resizeStart = ScreenDip(e);
            _startCols = S.Columns;
            _startRows = S.Rows;
            _mode = Mode.Resize;
            ShowTiles(true);
            RootGrid.CaptureMouse();
            e.Handled = true;
        }

        private Point ScreenDip(MouseEventArgs e)
        {
            Point p = PointToScreen(e.GetPosition(this));
            double d = Dpi;
            return new Point(p.X / d, p.Y / d);
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            if (_mode == Mode.ItemPending)
            {
                if ((e.GetPosition(ItemCanvas) - _downPos).Length < 5) return;
                StartItemDrag();
            }

            if (_mode == Mode.ItemDrag)
            {
                Point o = e.GetPosition(Overlay);
                Canvas.SetLeft(_ghost, o.X - _grabOffset.X);
                Canvas.SetTop(_ghost, o.Y - _grabOffset.Y);
                bool dup = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                Canvas.SetLeft(_plusBadge, o.X + 8);
                Canvas.SetTop(_plusBadge, o.Y + 8);
                _plusBadge.Visibility = dup ? Visibility.Visible : Visibility.Collapsed;
                if (_dragElement != null && _dragElement.Parent != null) _dragElement.Opacity = dup ? 1.0 : 0.3;
                if (_group != null) ShowGroupMarkers(e.GetPosition(ItemCanvas));
                else ShowMarker(e.GetPosition(ItemCanvas));
                if (_countBadge != null) { Canvas.SetLeft(_countBadge, o.X - _grabOffset.X - 8); Canvas.SetTop(_countBadge, o.Y - _grabOffset.Y - 8); }

                int t = TabIndexAt(e);
                if (t != _hoverTab)
                {
                    _hoverTab = t;
                    _tabHoverTimer.Stop();
                    if (t >= 0 && t != S.ActiveTab)
                    {
                        _tabHoverTarget = t;
                        _tabHoverTimer.Start();
                    }
                }
            }
            else if (_mode == Mode.Resize)
            {
                Point p = ScreenDip(e);
                double dx = p.X - _resizeStart.X, dy = p.Y - _resizeStart.Y;
                int cols = _startCols, rows = _startRows;
                MinGrid(out int minCols, out int minRows);
                if (_resizeEdge.Contains("R")) cols = Math.Max(minCols, Math.Min(100, _startCols + (int)Math.Round(dx / S.CellWidth)));
                if (_resizeEdge.Contains("T")) rows = Math.Max(minRows, Math.Min(50, _startRows + (int)Math.Round(-dy / S.CellHeight)));
                if (cols != S.Columns || rows != S.Rows)
                {
                    S.Columns = cols;
                    S.Rows = rows;
                    ItemCanvas.Width = cols * S.CellWidth;
                    ItemCanvas.Height = rows * S.CellHeight;
                }
            }
        }

        private void StartItemDrag()
        {
            _mode = Mode.ItemDrag;
            FrameworkElement b = _dragElement;
            double sc = Dpi;
            double w = Math.Max(1, b.ActualWidth), h = Math.Max(1, b.ActualHeight);

            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
                dc.DrawRectangle(new VisualBrush(b), null, new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * sc), (int)Math.Ceiling(h * sc), 96 * sc, 96 * sc, PixelFormats.Pbgra32);
            rtb.Render(dv);

            _ghost = new Image { Source = rtb, Width = w, Height = h, Opacity = 0.85 };
            Overlay.Children.Add(_ghost);
            _plusBadge = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
                Background = _tabText, Visibility = Visibility.Collapsed,
                Child = new TextBlock { Text = "+", FontWeight = FontWeights.Bold, FontSize = 16, Foreground = Brushes.Black,
                                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) }
            };
            Overlay.Children.Add(_plusBadge);
            b.Opacity = 0.3;

            // Ist die gezogene Kachel Teil einer Auswahl, wird die ganze Gruppe bewegt
            _group = null;
            if (_sel.Contains(_dragItem) && _sel.Count > 1)
            {
                _group = _dragOriginTab.Items.Where(_sel.Contains).ToList();
                foreach (UIElement child in ItemCanvas.Children)
                    if (child is FrameworkElement fe && fe.Tag is ItemData d && _group.Contains(d)) fe.Opacity = 0.3;
                _countBadge = new Border
                {
                    MinWidth = 24, Height = 24, CornerRadius = new CornerRadius(12), Padding = new Thickness(6, 0, 6, 0),
                    Background = _activeTabBg, BorderBrush = _tabText, BorderThickness = new Thickness(1.5),
                    Child = new TextBlock { Text = _group.Count.ToString(), FontWeight = FontWeights.Bold, FontSize = 13, Foreground = _activeTabText,
                                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                Overlay.Children.Add(_countBadge);
            }
            ShowTiles(true);
        }

        private void Root_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Mode mode = _mode;
            _mode = Mode.None;
            if (RootGrid.IsMouseCaptured) RootGrid.ReleaseMouseCapture();

            switch (mode)
            {
                case Mode.ItemPending:
                    if (_sel.Count > 0) ClearSelection();   // normaler Klick: Auswahl aufheben, Kachel starten
                    Sounds.Play();
                    ActivateItem(_dragItem, _dragElement);
                    break;
                case Mode.ItemDrag:
                    FinishItemDrag(e);
                    break;
                case Mode.Resize:
                    ShowTiles(false);
                    ConfigStore.Save();
                    _settingsWindow?.RefreshValues();
                    break;
            }
            _dragItem = null;
            _dragElement = null;
            _dragOriginTab = null;
        }

        private void Root_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_mode == Mode.None) return;
            Mode mode = _mode;
            _mode = Mode.None;
            if (mode == Mode.ItemDrag)
            {
                RemoveGhost();
                _group = null;
                RenderItems();
            }
            else if (mode == Mode.Resize) { ShowTiles(false); ConfigStore.Save(); }
        }

        private void RemoveGhost()
        {
            _tabHoverTimer.Stop();
            if (_ghost != null) Overlay.Children.Remove(_ghost);
            _ghost = null;
            if (_plusBadge != null) Overlay.Children.Remove(_plusBadge);
            _plusBadge = null;
            if (_countBadge != null) Overlay.Children.Remove(_countBadge);
            _countBadge = null;
            foreach (Border m in _groupMarkers) m.Visibility = Visibility.Collapsed;
            ShowTiles(false);
        }

        private void FinishItemDrag(MouseButtonEventArgs e)
        {
            Sounds.Play();
            RemoveGhost();
            ItemData it = _dragItem;
            TabData origin = _dragOriginTab;

            if (_group != null)
            {
                List<ItemData> group = _group;
                _group = null;
                FinishGroupDrag(e, group, it, origin, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                return;
            }

            // Shift gedrückt: Kopie ablegen, Original bleibt stehen
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                FinishDuplicate(e, it);
                return;
            }

            int t = TabIndexAt(e);
            if (t >= 0)
            {
                // Auf einen Tab losgelassen: in diesen Tab verschieben
                TabData target = Tabs[t];
                if (!ReferenceEquals(target, origin))
                {
                    origin.Items.Remove(it);
                    target.Items.Add(it);
                    PlaceNear(target, it, 0, 0);
                }
            }
            else
            {
                Point p = e.GetPosition(ItemCanvas);
                if (p.X >= 0 && p.Y >= 0 && p.X <= ItemCanvas.ActualWidth && p.Y <= ItemCanvas.ActualHeight)
                {
                    TabData target = ActiveTab;
                    CellAt(p, out int c, out int r);
                    ItemData other = ItemAt(target, c, r, it);
                    bool sameTab = ReferenceEquals(target, origin);
                    if (other != null && sameTab)
                    {
                        // gleiche Seite: Plätze tauschen
                        other.Col = it.Col;
                        other.Row = it.Row;
                    }
                    if (!sameTab)
                    {
                        origin.Items.Remove(it);
                        target.Items.Add(it);
                    }
                    it.Col = c;
                    it.Row = r;
                    // aus anderem Tab auf belegte Kachel: das vorhandene Icon weicht in die nächste freie Kachel aus
                    if (other != null && !sameTab) PlaceNear(target, other, c, r);
                }
            }
            RenderItems();
            ConfigStore.Save();
        }

        private void FinishDuplicate(MouseButtonEventArgs e, ItemData original)
        {
            Sounds.Play();
            ItemData copy = original.Clone();
            int t = TabIndexAt(e);
            if (t >= 0)
            {
                TabData target = Tabs[t];
                target.Items.Add(copy);
                PlaceNear(target, copy, 0, 0);
            }
            else
            {
                Point p = e.GetPosition(ItemCanvas);
                if (!(p.X >= 0 && p.Y >= 0 && p.X <= ItemCanvas.ActualWidth && p.Y <= ItemCanvas.ActualHeight))
                {
                    RenderItems();
                    return;
                }
                TabData target = ActiveTab;
                CellAt(p, out int c, out int r);
                target.Items.Add(copy);
                PlaceNear(target, copy, c, r);   // belegte Kachel: Kopie kommt in die nächste freie
            }
            ItemCanvas.Height = S.Rows * S.CellHeight;
            RenderItems();
            ConfigStore.Save();
        }

        // =====================================================================
        //  Mehrfachauswahl
        // =====================================================================

        private void ClearSelection()
        {
            _sel.Clear();
            RenderItems();
        }

        private void SelectAll()
        {
            foreach (ItemData it in ActiveTab.Items) _sel.Add(it);
            RenderItems();
        }

        /// <summary>Verschiebung der Gruppe so begrenzen, dass alle Kacheln im Raster bleiben.</summary>
        private void GroupDelta(List<ItemData> group, ItemData anchor, int c, int r, out int dc, out int dr)
        {
            dc = c - anchor.Col;
            dr = r - anchor.Row;
            int minC = group.Min(i => i.Col), maxC = group.Max(i => i.Col);
            int minR = group.Min(i => i.Row), maxR = group.Max(i => i.Row);
            dc = Math.Max(dc, -minC); dc = Math.Min(dc, S.Columns - 1 - maxC);
            dr = Math.Max(dr, -minR); dr = Math.Min(dr, S.Rows - 1 - maxR);
        }

        /// <summary>Beim Ziehen einer Gruppe alle Ziel-Kacheln markieren.</summary>
        private void ShowGroupMarkers(Point p)
        {
            bool inside = p.X >= 0 && p.Y >= 0 && p.X < ItemCanvas.ActualWidth && p.Y < ItemCanvas.ActualHeight;
            if (_marker != null) _marker.Visibility = Visibility.Collapsed;
            while (_groupMarkers.Count < _group.Count)
            {
                var m = new Border
                {
                    Width = S.CellWidth, Height = S.CellHeight, CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(2), BorderBrush = _tabText,
                    Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                    IsHitTestVisible = false, Visibility = Visibility.Collapsed
                };
                _groupMarkers.Add(m);
                ItemCanvas.Children.Add(m);
            }
            if (!inside) { foreach (Border m in _groupMarkers) m.Visibility = Visibility.Collapsed; return; }
            CellAt(p, out int c, out int r);
            GroupDelta(_group, _dragItem, c, r, out int dc, out int dr);
            for (int i = 0; i < _groupMarkers.Count; i++)
            {
                Border m = _groupMarkers[i];
                if (i >= _group.Count) { m.Visibility = Visibility.Collapsed; continue; }
                Canvas.SetLeft(m, (_group[i].Col + dc) * S.CellWidth);
                Canvas.SetTop(m, (_group[i].Row + dr) * S.CellHeight);
                m.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Gruppe ablegen: auf einen Tab (verschieben bzw. mit Shift kopieren) oder an eine Stelle im aktiven Tab.
        /// Die Anordnung bleibt erhalten; Kacheln, die im Weg liegen, weichen in die nächste freie Kachel aus.
        /// </summary>
        private void FinishGroupDrag(MouseButtonEventArgs e, List<ItemData> group, ItemData anchor, TabData origin, bool dup)
        {
            int t = TabIndexAt(e);
            var moved = new List<ItemData>();
            if (t >= 0)
            {
                TabData target = Tabs[t];
                if (!ReferenceEquals(target, origin) || dup)
                    moved = MoveOrCopyToTab(group, origin, target, dup);
                else moved = group;
            }
            else
            {
                Point p = e.GetPosition(ItemCanvas);
                if (p.X >= 0 && p.Y >= 0 && p.X <= ItemCanvas.ActualWidth && p.Y <= ItemCanvas.ActualHeight)
                {
                    TabData target = ActiveTab;
                    CellAt(p, out int c, out int r);
                    GroupDelta(group, anchor, c, r, out int dc, out int dr);
                    var targets = group.Select(i => (Col: i.Col + dc, Row: i.Row + dr)).ToList();
                    for (int k = 0; k < group.Count; k++)
                    {
                        ItemData x = dup ? group[k].Clone() : group[k];
                        if (dup || !ReferenceEquals(target, origin))
                        {
                            if (!dup) origin.Items.Remove(x);
                            target.Items.Add(x);
                        }
                        x.Col = targets[k].Col;
                        x.Row = targets[k].Row;
                        moved.Add(x);
                    }
                    var movedSet = new HashSet<ItemData>(moved, ReferenceEqualityComparer.Instance);
                    foreach (ItemData other in target.Items.Where(o => !movedSet.Contains(o)).ToList())
                        if (moved.Any(m => m.Col == other.Col && m.Row == other.Row))
                            PlaceNear(target, other, other.Col, other.Row);
                }
                else moved = group;   // ausserhalb losgelassen: nichts ändern
            }
            _sel.Clear();
            foreach (ItemData m in moved) _sel.Add(m);
            ItemCanvas.Height = S.Rows * S.CellHeight;
            RenderItems();
            ConfigStore.Save();
        }

        /// <summary>Kacheln in einen anderen Tab verschieben oder kopieren, möglichst an dieselben Stellen.</summary>
        private List<ItemData> MoveOrCopyToTab(List<ItemData> items, TabData origin, TabData target, bool copy)
        {
            var result = new List<ItemData>();
            foreach (ItemData it in items.OrderBy(i => i.Row).ThenBy(i => i.Col).ToList())
            {
                ItemData x = copy ? it.Clone() : it;
                int c = it.Col, r = it.Row;
                if (!copy) origin.Items.Remove(it);
                target.Items.Add(x);
                PlaceNear(target, x, c, r);
                result.Add(x);
            }
            return result;
        }

        /// <summary>Kontextmenü für mehrere ausgewählte Kacheln.</summary>
        private void ShowGroupMenu(FrameworkElement el)
        {
            List<ItemData> items = ActiveTab.Items.Where(_sel.Contains).ToList();
            var cm = NewMenu();
            cm.Items.Add(new MenuItem { Header = L.F("sel.count", items.Count), IsEnabled = false, FontWeight = FontWeights.SemiBold });
            cm.Items.Add(MI(L.T("sel.startall"), () =>
            {
                Sounds.Play();
                foreach (ItemData it in items)
                {
                    string folder = Launcher.FolderOf(it);
                    if (folder != null) Launcher.Open(folder); else Launcher.Start(it);
                }
            }));
            cm.Items.Add(new Separator());
            cm.Items.Add(MI(L.T("item.color"), () =>
            {
                string first = items.Select(i => i.TileColor).FirstOrDefault(x => !string.IsNullOrEmpty(x));
                string c = ColorPicker.Pick(this, first ?? "#3A0000");
                if (c == null) return;
                foreach (ItemData it in items) it.TileColor = c;
                RenderItems();
                ConfigStore.Save();
            }));
            if (items.Any(i => !string.IsNullOrEmpty(i.TileColor)))
                cm.Items.Add(MI(L.T("item.colorremove"), () =>
                {
                    foreach (ItemData it in items) it.TileColor = "";
                    RenderItems();
                    ConfigStore.Save();
                }));
            cm.Items.Add(new Separator());
            cm.Items.Add(TabSubmenu(L.T("sel.moveto"), items, false));
            cm.Items.Add(TabSubmenu(L.T("sel.copyto"), items, true));
            cm.Items.Add(new Separator());
            cm.Items.Add(MI(L.T("sel.clear"), ClearSelection));
            cm.Items.Add(MI(L.F("sel.remove", items.Count), () =>
            {
                foreach (ItemData it in items) foreach (TabData t in Tabs) t.Items.Remove(it);
                _sel.Clear();
                RenderItems();
                ConfigStore.Save();
            }));
            cm.PlacementTarget = el;
            cm.IsOpen = true;
        }

        private MenuItem TabSubmenu(string header, List<ItemData> items, bool copy)
        {
            var parent = new MenuItem { Header = header };
            TabData origin = ActiveTab;
            for (int i = 0; i < Tabs.Count; i++)
            {
                TabData target = Tabs[i];
                if (ReferenceEquals(target, origin) && !copy) continue;
                parent.Items.Add(MI(target.Name, () =>
                {
                    List<ItemData> moved = MoveOrCopyToTab(items, origin, target, copy);
                    _sel.Clear();
                    if (ReferenceEquals(target, origin)) foreach (ItemData m in moved) _sel.Add(m);
                    ItemCanvas.Height = S.Rows * S.CellHeight;
                    RenderItems();
                    ConfigStore.Save();
                }));
            }
            parent.IsEnabled = parent.Items.Count > 0;
            return parent;
        }

        // =====================================================================
        //  Drag & Drop aus dem Explorer / Browser
        // =====================================================================

        private const string ShellIdList = "Shell IDList Array";

        /// <summary>
        /// Explorer-Elemente ohne Dateipfad (Netzwerk, Netzwerk-Computer …) kommen nur als "Shell IDList Array" (CIDA).
        /// Liefert je Element (Parsing-Name, Anzeigename), z.B. ("\\\\SERVER", "SERVER").
        /// </summary>
        private static List<(string Path, string Name)> ReadShellIdList(IDataObject d)
        {
            var list = new List<(string, string)>();
            try
            {
                if (!(d.GetData(ShellIdList) is MemoryStream ms)) return list;
                byte[] b = ms.ToArray();
                int count = BitConverter.ToInt32(b, 0);
                if (count <= 0 || count > 500) return list;
                IntPtr mem = Marshal.AllocHGlobal(b.Length);
                try
                {
                    Marshal.Copy(b, 0, mem, b.Length);
                    IntPtr parent = mem + BitConverter.ToInt32(b, 4);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr child = mem + BitConverter.ToInt32(b, 8 + 4 * i);
                        IntPtr full = Native.ILCombine(parent, child);
                        if (full == IntPtr.Zero) continue;
                        try
                        {
                            string path = null, name = null;
                            if (Native.SHGetNameFromIDList(full, Native.SIGDN_DESKTOPABSOLUTEPARSING, out IntPtr p) == 0)
                            { path = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); }
                            if (Native.SHGetNameFromIDList(full, Native.SIGDN_NORMALDISPLAY, out IntPtr n) == 0)
                            { name = Marshal.PtrToStringUni(n); Marshal.FreeCoTaskMem(n); }
                            if (!string.IsNullOrEmpty(path)) list.Add((path, string.IsNullOrEmpty(name) ? path : name));
                        }
                        finally { Native.ILFree(full); }
                    }
                }
                finally { Marshal.FreeHGlobal(mem); }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Ablegeart, die die Quelle erlaubt. Dateien kommen als "Kopieren", Netzwerk-Computer und Shell-Ordner
        /// (z.B. \\SERVER, "Netzwerk") erlaubt der Explorer nur als "Verknüpfen". Mit festem Copy würde das Ablegen verweigert.
        /// </summary>
        private static DragDropEffects AcceptEffect(DragEventArgs e)
        {
            if ((e.AllowedEffects & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            if ((e.AllowedEffects & DragDropEffects.Link) != 0) return DragDropEffects.Link;
            if ((e.AllowedEffects & DragDropEffects.Move) != 0) return DragDropEffects.Move;
            return DragDropEffects.None;
        }

        private static bool HasDropData(IDataObject d)
        {
            return d.GetDataPresent(DataFormats.FileDrop)
                   || d.GetDataPresent(ShellIdList)
                   || d.GetDataPresent("UniformResourceLocatorW")
                   || d.GetDataPresent(DataFormats.UnicodeText);
        }

        private static string GetUrl(IDataObject d)
        {
            try
            {
                if (d.GetDataPresent("UniformResourceLocatorW") && d.GetData("UniformResourceLocatorW") is MemoryStream ms)
                {
                    string u = Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0').Trim();
                    if (Launcher.IsUrl(u)) return u;
                }
                if (d.GetDataPresent(DataFormats.UnicodeText))
                {
                    string t = (d.GetData(DataFormats.UnicodeText) as string ?? "").Trim();
                    if (Launcher.IsUrl(t) && !t.Contains(" ") && !t.Contains("\n")) return t;
                }
            }
            catch { }
            return null;
        }

        private void Canvas_DragOver(object sender, DragEventArgs e)
        {
            bool ok = HasDropData(e.Data);
            e.Effects = ok ? AcceptEffect(e) : DragDropEffects.None;
            if (ok)
            {
                ShowTiles(true);
                ShowMarker(e.GetPosition(ItemCanvas));
            }
            e.Handled = true;
        }

        private void Canvas_DragLeave(object sender, DragEventArgs e)
        {
            // DragLeave kommt auch beim Wechsel auf ein Icon - nur ausblenden, wenn die Maus die Fläche verlässt
            Point p = e.GetPosition(ItemCanvas);
            if (p.X < 0 || p.Y < 0 || p.X >= ItemCanvas.ActualWidth || p.Y >= ItemCanvas.ActualHeight) ShowTiles(false);
        }

        private void Canvas_Drop(object sender, DragEventArgs e)
        {
            Dbg.W("Drop auf Fläche");
            ShowTiles(false);
            AddDropped(e.Data, ActiveTab, e.GetPosition(ItemCanvas));
            e.Handled = true;
        }

        private void AddDropped(IDataObject data, TabData tab, Point? at)
        {
            var paths = new List<string>();
            var shellNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
                paths.AddRange(files);
            else if (data.GetDataPresent(ShellIdList) && ReadShellIdList(data) is var shellItems && shellItems.Count > 0)
            {
                foreach (var (p, n) in shellItems) { paths.Add(p); shellNames[p] = n; }
            }
            else
            {
                string u = GetUrl(data);
                if (u != null) paths.Add(u);
            }
            if (paths.Count == 0) return;
            Sounds.Play();

            int startC = 0, startR = 0;
            if (at.HasValue) CellAt(at.Value, out startC, out startR);
            for (int i = 0; i < paths.Count; i++)
            {
                // Verknüpfungen (.url/.lnk) auflösen, damit die Kachel nicht von der Verknüpfungsdatei abhängt
                ItemData it = Launcher.FromShortcut(paths[i]) ?? new ItemData
                {
                    Path = paths[i],
                    Label = shellNames.TryGetValue(paths[i], out string sn) ? sn : Launcher.MakeLabel(paths[i])
                };
                tab.Items.Add(it);
                PlaceNear(tab, it, startC + i, startR);
                if (FaviconFetcher.IsWebUrl(it.Path) && string.IsNullOrEmpty(it.IconFile)) FetchFavicon(it, false);
            }
            ItemCanvas.Height = S.Rows * S.CellHeight;
            ConfigStore.Save();
            Dbg.W("Drop gespeichert: " + string.Join("; ", paths));
            // Erst nach dem Drop neu zeichnen, damit der Explorer nicht auf uns warten muss
            if (ReferenceEquals(tab, ActiveTab)) RenderLater();
        }

        // Dateien auf ein Programm-Icon ziehen = damit öffnen (Shift gedrückt = stattdessen als Icon hinzufügen)
        private void Item_DragOver(object sender, DragEventArgs e)
        {
            var it = (ItemData)((FrameworkElement)sender).Tag;
            if (e.Data.GetDataPresent(DataFormats.FileDrop)
                && (e.KeyStates & DragDropKeyStates.ShiftKey) == 0
                && Launcher.AcceptsFiles(it))
            {
                e.Effects = DragDropEffects.Link;
                e.Handled = true;
            }
        }

        private void Item_Drop(object sender, DragEventArgs e)
        {
            var it = (ItemData)((FrameworkElement)sender).Tag;
            if (e.Data.GetDataPresent(DataFormats.FileDrop)
                && (e.KeyStates & DragDropKeyStates.ShiftKey) == 0
                && Launcher.AcceptsFiles(it)
                && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                Sounds.Play();
                Launcher.Start(it, string.Join(" ", files.Select(f => "\"" + f + "\"")));
                e.Handled = true;
            }
        }

        // =====================================================================
        //  Rahmen, Hauptmenü
        // =====================================================================

        private void Frame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, FrameBorder)) return;
            StartWindowMove();
            e.Handled = true;
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Klick auf eine leere Stelle hebt die Auswahl auf
            if (ReferenceEquals(e.OriginalSource, ItemCanvas) && _sel.Count > 0 && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
                ClearSelection();
            // Strg + Ziehen auf leerer Fläche verschiebt das Fenster
            if (ReferenceEquals(e.OriginalSource, ItemCanvas) && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                StartWindowMove();
                e.Handled = true;
            }
        }

        private void StartWindowMove()
        {
            if (S.PositionLocked) return;
            try { DragMove(); } catch { return; }
            SaveOffsetsFromCurrentPosition();
        }

        private void Frame_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, FrameBorder)) return;
            ShowMainMenu(FrameBorder);
            e.Handled = true;
        }

        private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, ItemCanvas)) return;
            ShowMainMenu(ItemCanvas);
            e.Handled = true;
        }

        private void MenuButton_Click(object sender, MouseButtonEventArgs e)
        {
            ShowMainMenu(MenuButton, true);
            e.Handled = true;
        }

        private void ShowMainMenu(FrameworkElement target, bool below = false)
        {
            var cm = NewMenu();
            cm.Items.Add(MI(L.T("main.settings"), OpenSettings));
            cm.Items.Add(MI(L.T("main.wintools"), OpenWindowsTools));
            cm.Items.Add(MI(L.T("main.newtab"), () => NewTab(Tabs.Count)));
            cm.Items.Add(MI(L.T("main.compact"), CompactIcons));
            cm.Items.Add(MI(L.T("sel.all"), SelectAll, ActiveTab.Items.Count > 0));
            cm.Items.Add(new Separator());

            cm.Items.Add(MI(L.T("main.toback"), SendToBack));
            var lockItem = new MenuItem { Header = L.T("main.lock"), IsCheckable = true, IsChecked = S.PositionLocked };
            lockItem.Click += (s, e) => { S.PositionLocked = lockItem.IsChecked; ConfigStore.Save(); _settingsWindow?.RefreshValues(); };
            cm.Items.Add(lockItem);
            cm.Items.Add(new Separator());

            cm.Items.Add(MI(L.T("main.backup"), DoBackup));
            cm.Items.Add(MI(L.T("main.restore"), OpenRestore));
            cm.Items.Add(new Separator());

            var auto = new MenuItem { Header = L.T("main.autostart"), IsCheckable = true, IsChecked = Autostart.Exists };
            auto.Click += (s, e) =>
            {
                try
                {
                    if (auto.IsChecked) Autostart.Create(); else Autostart.Remove();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, L.F("main.autostartfail", ex.Message), "DragLaunch");
                }
            };
            cm.Items.Add(auto);
            cm.Items.Add(MI(L.T("main.appfolder"), () => Launcher.Open(ConfigStore.AppDir)));
            cm.Items.Add(new Separator());
            cm.Items.Add(MI(L.T("main.about"), () => new AboutWindow { Owner = this }.ShowDialog()));
            cm.Items.Add(MI(L.T("main.exit"), Close));

            cm.PlacementTarget = target;
            if (below) cm.Placement = PlacementMode.Bottom;
            cm.IsOpen = true;
        }

        private void OpenWindowsTools()
        {
            var w = new WindowsToolsWindow(items =>
            {
                TabData tab = ActiveTab;
                foreach (ItemData it in items)
                {
                    tab.Items.Add(it);
                    PlaceNear(tab, it, 0, 0);
                }
                ItemCanvas.Height = S.Rows * S.CellHeight;
                ConfigStore.Save();
                RenderItems();
            }, Dpi) { Owner = this };
            w.ShowDialog();
        }

        public void OpenSettings()
        {
            if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
            _settingsWindow = new SettingsWindow(this) { Owner = this };
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        private void DoBackup()
        {
            try
            {
                string p = ConfigStore.CreateBackup();
                MessageBox.Show(this, L.F("backup.created", Path.GetFileName(p)), "DragLaunch",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.F("backup.failed", ex.Message), "DragLaunch",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenRestore()
        {
            new BackupWindow(this) { Owner = this }.ShowDialog();
        }

        // =====================================================================
        //  Helfer
        // =====================================================================

        /// <summary>Menü mit eingestellter Schriftgrösse (gilt auch für Untermenüs).</summary>
        private ContextMenu NewMenu()
        {
            var cm = new ContextMenu { FontSize = S.ContextFontSize };
            cm.Opened += (s, e) => SetMenuFont(cm.Items, S.ContextFontSize);
            return cm;
        }

        private static void SetMenuFont(ItemCollection items, double size)
        {
            foreach (object o in items)
                if (o is MenuItem mi)
                {
                    mi.FontSize = size;
                    SetMenuFont(mi.Items, size);
                }
        }

        /// <summary>
        /// Beschriftungsfarbe für eine Kachel: die eingestellte Farbe, solange sie auf der Kachelfarbe gut lesbar ist
        /// (Kontrast nach WCAG mindestens 3:1). Sonst Schwarz oder Weiss, je nachdem, was besser lesbar ist.
        /// </summary>
        private Brush LabelBrushFor(string tileColor)
            => TryColor(tileColor, out Color bg) ? ReadableOn(_labelBrush, bg) : _labelBrush;

        /// <summary>Gewünschte Schriftfarbe, falls auf bg gut lesbar (Kontrast ≥ 3:1), sonst Schwarz oder Weiss.</summary>
        private static Brush ReadableOn(Brush preferred, Color bg)
        {
            double lbg = Luminance(bg);
            if (preferred is SolidColorBrush pb && Contrast(Luminance(pb.Color), lbg) >= 3.0) return preferred;
            return Contrast(0.0, lbg) >= Contrast(1.0, lbg) ? Brushes.Black : Brushes.White;
        }

        private static bool TryColor(string s, out Color c)
        {
            c = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(s)) return false;
            try { c = (Color)ColorConverter.ConvertFromString(s); return true; } catch { return false; }
        }

        private static double Luminance(Color c)
        {
            double Ch(byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }

        private static double Contrast(double l1, double l2) => (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);

        /// <summary>Hintergrund einer Kachel: eigene Farbe (beim Überfahren etwas heller) oder transparent.</summary>
        private static Brush TileBrush(string color, bool hover)
        {
            if (string.IsNullOrWhiteSpace(color)) return hover ? HoverBrush : Brushes.Transparent;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(color);
                if (hover) c = Color.FromArgb(c.A, (byte)(c.R + (255 - c.R) / 5), (byte)(c.G + (255 - c.G) / 5), (byte)(c.B + (255 - c.B) / 5));
                return Freeze(new SolidColorBrush(c));
            }
            catch { return hover ? HoverBrush : Brushes.Transparent; }
        }

        private static MenuItem MI(string header, Action action, bool enabled = true)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled };
            m.Click += (s, e) => action();
            return m;
        }

        private static SolidColorBrush BrushFrom(string hex, Color fallback)
        {
            Color c;
            try { c = (Color)ColorConverter.ConvertFromString(hex); }
            catch { c = fallback; }
            return Freeze(new SolidColorBrush(c));
        }

        private static T Freeze<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }
    }
}
