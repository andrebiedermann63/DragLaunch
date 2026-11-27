using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace DragLaunch
{
    /// <summary>
    /// Liest/schreibt alle Einstellungen und Icons in DragLaunch.ini im Ordner der App.
    /// Jede Änderung wird sofort geschrieben.
    /// </summary>
    public static class ConfigStore
    {
        public const string BaseName = "DragLaunch";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string AppDir => AppContext.BaseDirectory.TrimEnd('\\', '/');
        public static string IniPath => Path.Combine(AppDir, BaseName + ".ini");

        public static AppSettings Settings = new AppSettings();
        public static List<TabData> Tabs = new List<TabData>();

        private static bool _saveErrorShown;

        // ---------------------------------------------------------------- Laden

        public static void Load()
        {
            Settings = new AppSettings();
            Tabs = new List<TabData>();

            Dictionary<string, Dictionary<string, string>> ini = null;
            if (File.Exists(IniPath))
            {
                try { ini = Parse(File.ReadAllLines(IniPath, Encoding.UTF8)); }
                catch (Exception ex)
                {
                    MessageBox.Show(L.F("cfg.readfail", ex.Message), "DragLaunch");
                }
            }

            if (ini == null)
            {
                Tabs.Add(new TabData { Name = "Apps" });
                Tabs.Add(new TabData { Name = "Tools" });
                Save();
                return;
            }

            var s = Settings;
            Dictionary<string, string> g = Sec(ini, "Settings");
            s.FontSize = D(g, "FontSize", s.FontSize, 6, 40);
            s.TabFontSize = D(g, "TabFontSize", s.TabFontSize, 6, 40);
            s.IconSize = I(g, "IconSize", s.IconSize, 16, 256);
            s.CellWidth = I(g, "CellWidth", s.CellWidth, 30, 400);
            s.MaxLabelLines = I(g, "MaxLabelLines", s.MaxLabelLines, 0, 6);
            s.Columns = I(g, "Columns", s.Columns, 1, 100);
            s.Rows = I(g, "Rows", s.Rows, 1, 50);
            s.ShowTiles = B(g, "ShowTiles", s.ShowTiles);
            s.Language = S(g, "Language", s.Language);
            s.DialogScale = D(g, "DialogScale", s.DialogScale, 0.8, 3);
            s.Debug = B(g, "Debug", s.Debug);
            s.CustomColors = S(g, "CustomColors", s.CustomColors);

            s.FrameColor = S(g, "FrameColor", s.FrameColor);
            s.BackgroundColor = S(g, "BackgroundColor", s.BackgroundColor);
            s.TabTextColor = S(g, "TabTextColor", s.TabTextColor);
            s.ActiveTabColor = S(g, "ActiveTabColor", s.ActiveTabColor);
            s.ActiveTabTextColor = S(g, "ActiveTabTextColor", s.ActiveTabTextColor);
            s.LabelColor = S(g, "LabelColor", s.LabelColor);

            s.OffsetLeft = D(g, "OffsetLeft", s.OffsetLeft, -10000, 10000);
            s.OffsetBottom = D(g, "OffsetBottom", s.OffsetBottom, -10000, 10000);
            s.UseWorkArea = B(g, "UseWorkArea", s.UseWorkArea);
            s.PositionLocked = B(g, "PositionLocked", s.PositionLocked);
            s.ShowInTaskbar = B(g, "ShowInTaskbar", s.ShowInTaskbar);

            s.ActivationDelayMs = I(g, "ActivationDelayMs", s.ActivationDelayMs, 0, 10000);
            s.EdgeThickness = I(g, "EdgeThickness", s.EdgeThickness, 1, 50);
            s.ActivateFocus = B(g, "ActivateFocus", s.ActivateFocus);
            s.IgnoreFullscreen = B(g, "IgnoreFullscreen", s.IgnoreFullscreen);
            s.EdgePauseApps = S(g, "EdgePauseApps", s.EdgePauseApps);
            s.BackAfterLaunch = B(g, "BackAfterLaunch", s.BackAfterLaunch);
            s.Sound = S(g, "Sound", s.Sound);
            // Version 23: die drei alten Sounds wurden durch 01_Tick … 12_Tick ersetzt. Fehlt der gewählte alte Sound, den ersten neuen nehmen.
            if ((s.Sound == "Klick.wav" || s.Sound == "Plopp.wav" || s.Sound == "Tick.wav")
                && !File.Exists(Path.Combine(AppDir, "Sound", s.Sound)) && File.Exists(Path.Combine(AppDir, "Sound", "01 Tick.wav")))
                s.Sound = "01 Tick.wav";
            s.ContextFontSize = D(g, "ContextFontSize", s.ContextFontSize, 8, 40);
            ReadEdge(g, "Bottom", s.Bottom);
            ReadEdge(g, "Top", s.Top);
            ReadEdge(g, "Left", s.Left);
            ReadEdge(g, "Right", s.Right);

            s.MenuFontSize = D(g, "MenuFontSize", s.MenuFontSize, 8, 40);
            s.MenuIconSize = I(g, "MenuIconSize", s.MenuIconSize, 16, 64);
            s.FolderOpensMenu = !string.Equals(S(g, "FolderClick", "Menu"), "Open", StringComparison.OrdinalIgnoreCase);

            Dictionary<string, string> t = Sec(ini, "Tabs");
            int count = I(t, "Count", 0, 0, 200);
            s.ActiveTab = I(t, "Active", 1, 1, 200) - 1;

            for (int i = 1; i <= count; i++)
            {
                Dictionary<string, string> ts = Sec(ini, "Tab" + i);
                var tab = new TabData { Name = S(ts, "Name", "Tab " + i), Color = S(ts, "Color", "") };
                int ic = I(ts, "ItemCount", 0, 0, 5000);
                for (int k = 1; k <= ic; k++)
                {
                    string p = "Item" + k + ".";
                    string path = S(ts, p + "Path", "");
                    if (path.Length == 0) continue;
                    var it = new ItemData
                    {
                        Path = path,
                        Label = S(ts, p + "Label", ""),
                        Arguments = S(ts, p + "Arguments", ""),
                        WorkDir = S(ts, p + "WorkDir", ""),
                        IconFile = S(ts, p + "Icon", ""),
                        Col = I(ts, p + "Col", -1, -1, 1000),
                        Row = I(ts, p + "Row", -1, -1, 1000),
                        X = D(ts, p + "X", 0, -100000, 100000),
                        Y = D(ts, p + "Y", 0, -100000, 100000),
                        RunAsAdmin = B(ts, p + "RunAsAdmin", false),
                        TileColor = S(ts, p + "Color", ""),
                    };
                    string fm = S(ts, p + "FolderClick", "Default");
                    it.FolderMode = fm.Equals("Menu", StringComparison.OrdinalIgnoreCase) ? FolderMode.Menu
                                  : fm.Equals("Open", StringComparison.OrdinalIgnoreCase) ? FolderMode.Open
                                  : FolderMode.Default;
                    tab.Items.Add(it);
                }
                Tabs.Add(tab);
            }

            if (Tabs.Count == 0) Tabs.Add(new TabData { Name = "Apps" });
            if (s.ActiveTab < 0 || s.ActiveTab >= Tabs.Count) s.ActiveTab = 0;

            // Ältere Einträge reparieren: "shell:AppsFolder\steam://..." -> "steam://..."
            bool repaired = false;

            // Kacheln ohne Beschriftung (z.B. Netzwerk-Freigaben aus älteren Versionen) bekommen einen Namen
            foreach (TabData tab in Tabs)
                foreach (ItemData it in tab.Items)
                    if (string.IsNullOrWhiteSpace(it.Label) && !string.IsNullOrWhiteSpace(it.Path))
                    {
                        it.Label = Launcher.MakeLabel(Launcher.Expand(it.Path));
                        repaired = true;
                    }
            foreach (TabData tab in Tabs)
                foreach (ItemData it in tab.Items)
                    if (it.Path.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase) && it.Path.Contains("://"))
                    {
                        it.Path = it.Path.Substring(@"shell:AppsFolder\".Length);
                        repaired = true;
                    }

            // Eigene Icons aus älteren Versionen in den Icons-Ordner übernehmen
            // Kacheln, die noch auf eine .url/.lnk-Verknüpfung zeigen, davon unabhängig machen
            foreach (TabData tab in Tabs)
                foreach (ItemData it in tab.Items)
                {
                    string p = Launcher.Expand(it.Path);
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    if ((ext != ".url" && ext != ".lnk") || !File.Exists(p)) continue;
                    ItemData r = Launcher.FromShortcut(p);
                    if (r == null) continue;
                    it.Path = r.Path;
                    if (string.IsNullOrEmpty(it.Arguments)) it.Arguments = r.Arguments;
                    if (string.IsNullOrEmpty(it.WorkDir)) it.WorkDir = r.WorkDir;
                    if (string.IsNullOrEmpty(it.IconFile)) it.IconFile = r.IconFile;
                    repaired = true;
                }

            if (IconStore.CollectAll() || repaired) Save();
            MoveOldBackups();
        }

        private static void ReadEdge(Dictionary<string, string> g, string name, EdgeZone z)
        {
            z.Enabled = B(g, "Edge" + name, z.Enabled);
            z.From = D(g, "Edge" + name + "From", z.From, 0, 100);
            z.To = D(g, "Edge" + name + "To", z.To, 0, 100);
        }

        /// <summary>Nur die Sprache aus der INI lesen (für Meldungen, bevor alles geladen ist).</summary>
        public static string PeekLanguage()
        {
            try
            {
                if (!File.Exists(IniPath)) return "";
                foreach (string line in File.ReadLines(IniPath, Encoding.UTF8))
                    if (line.TrimStart().StartsWith("Language=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(line.IndexOf('=') + 1).Trim();
            }
            catch { }
            return "";
        }

        // ---------------------------------------------------------------- Speichern

        private static System.Windows.Threading.DispatcherTimer _saveTimer;

        /// <summary>In 1,5 s speichern (mehrere schnelle Änderungen, z.B. Tab-Klicks, ergeben einen Schreibvorgang).</summary>
        public static void SaveSoon()
        {
            if (_saveTimer == null)
            {
                _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); Save(); };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Ausstehendes verzögertes Speichern sofort ausführen (beim Beenden).</summary>
        public static void Flush()
        {
            if (_saveTimer != null && _saveTimer.IsEnabled) { _saveTimer.Stop(); Save(); }
        }

        public static void Save()
        {
            _saveTimer?.Stop();   // ein direktes Speichern erledigt auch ein ausstehendes
            var s = Settings;
            var sb = new StringBuilder();
            sb.AppendLine("; DragLaunch Einstellungen - wird bei jeder Änderung automatisch geschrieben");
            sb.AppendLine("[Settings]");
            W(sb, "FontSize", s.FontSize);
            W(sb, "TabFontSize", s.TabFontSize);
            W(sb, "IconSize", s.IconSize);
            W(sb, "CellWidth", s.CellWidth);
            W(sb, "MaxLabelLines", s.MaxLabelLines);
            W(sb, "Columns", s.Columns);
            W(sb, "Rows", s.Rows);
            W(sb, "Language", s.Language);
            W(sb, "ShowTiles", s.ShowTiles);
            W(sb, "DialogScale", s.DialogScale);
            if (s.Debug) W(sb, "Debug", true);
            if (!string.IsNullOrEmpty(s.CustomColors)) W(sb, "CustomColors", s.CustomColors);
            W(sb, "FrameColor", s.FrameColor);
            W(sb, "BackgroundColor", s.BackgroundColor);
            W(sb, "TabTextColor", s.TabTextColor);
            W(sb, "ActiveTabColor", s.ActiveTabColor);
            W(sb, "ActiveTabTextColor", s.ActiveTabTextColor);
            W(sb, "LabelColor", s.LabelColor);
            W(sb, "OffsetLeft", s.OffsetLeft);
            W(sb, "OffsetBottom", s.OffsetBottom);
            W(sb, "UseWorkArea", s.UseWorkArea);
            W(sb, "PositionLocked", s.PositionLocked);
            W(sb, "ShowInTaskbar", s.ShowInTaskbar);
            W(sb, "ActivationDelayMs", s.ActivationDelayMs);
            W(sb, "EdgeThickness", s.EdgeThickness);
            W(sb, "ActivateFocus", s.ActivateFocus);
            W(sb, "IgnoreFullscreen", s.IgnoreFullscreen);
            W(sb, "EdgePauseApps", s.EdgePauseApps);
            W(sb, "BackAfterLaunch", s.BackAfterLaunch);
            W(sb, "Sound", s.Sound);
            W(sb, "ContextFontSize", s.ContextFontSize);
            WriteEdge(sb, "Bottom", s.Bottom);
            WriteEdge(sb, "Top", s.Top);
            WriteEdge(sb, "Left", s.Left);
            WriteEdge(sb, "Right", s.Right);
            W(sb, "FolderClick", s.FolderOpensMenu ? "Menu" : "Open");
            W(sb, "MenuFontSize", s.MenuFontSize);
            W(sb, "MenuIconSize", s.MenuIconSize);
            sb.AppendLine();

            sb.AppendLine("[Tabs]");
            W(sb, "Count", Tabs.Count);
            W(sb, "Active", s.ActiveTab + 1);
            sb.AppendLine();

            for (int i = 0; i < Tabs.Count; i++)
            {
                TabData tab = Tabs[i];
                sb.AppendLine("[Tab" + (i + 1) + "]");
                W(sb, "Name", tab.Name);
                if (!string.IsNullOrEmpty(tab.Color)) W(sb, "Color", tab.Color);
                W(sb, "ItemCount", tab.Items.Count);
                for (int k = 0; k < tab.Items.Count; k++)
                {
                    ItemData it = tab.Items[k];
                    string p = "Item" + (k + 1) + ".";
                    W(sb, p + "Path", it.Path);
                    W(sb, p + "Label", it.Label);
                    if (!string.IsNullOrEmpty(it.Arguments)) W(sb, p + "Arguments", it.Arguments);
                    if (!string.IsNullOrEmpty(it.WorkDir)) W(sb, p + "WorkDir", it.WorkDir);
                    if (!string.IsNullOrEmpty(it.IconFile)) W(sb, p + "Icon", it.IconFile);
                    W(sb, p + "Col", it.Col);
                    W(sb, p + "Row", it.Row);
                    if (it.RunAsAdmin) W(sb, p + "RunAsAdmin", true);
                    if (it.FolderMode != FolderMode.Default) W(sb, p + "FolderClick", it.FolderMode.ToString());
                    if (!string.IsNullOrEmpty(it.TileColor)) W(sb, p + "Color", it.TileColor);
                }
                sb.AppendLine();
            }

            try
            {
                string tmp = IniPath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
                File.Move(tmp, IniPath, true);
                _saveErrorShown = false;
            }
            catch (Exception ex)
            {
                if (!_saveErrorShown)
                {
                    _saveErrorShown = true;
                    MessageBox.Show(L.F("cfg.savefail", IniPath, ex.Message),
                                    "DragLaunch", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private static void WriteEdge(StringBuilder sb, string name, EdgeZone z)
        {
            W(sb, "Edge" + name, z.Enabled);
            W(sb, "Edge" + name + "From", z.From);
            W(sb, "Edge" + name + "To", z.To);
        }

        // ---------------------------------------------------------------- Backup

        /// <summary>Erstellt DragLaunch_JJJJMMTTa.ini, ...b, ...c usw. im App-Ordner.</summary>
        public static string BackupDir => Path.Combine(AppDir, "Backup");

        /// <summary>Backups älterer Versionen (direkt im App-Ordner) in den Ordner "Backup" verschieben.</summary>
        private static void MoveOldBackups()
        {
            try
            {
                var old = Directory.GetFiles(AppDir, BaseName + "_*.ini")
                    .Where(f => Path.GetFileNameWithoutExtension(f).Length >= BaseName.Length + 10).ToList();
                if (old.Count == 0) return;
                Directory.CreateDirectory(BackupDir);
                foreach (string f in old)
                {
                    string target = Path.Combine(BackupDir, Path.GetFileName(f));
                    if (!File.Exists(target)) File.Move(f, target);
                }
            }
            catch { }
        }

        public static string CreateBackup()
        {
            Save();
            Directory.CreateDirectory(BackupDir);
            string date = DateTime.Now.ToString("yyyyMMdd", Inv);
            string target = null;
            for (char c = 'a'; c <= 'z' && target == null; c++)
            {
                string p = Path.Combine(BackupDir, BaseName + "_" + date + c + ".ini");
                if (!File.Exists(p)) target = p;
            }
            for (int n = 1; target == null; n++)
            {
                string p = Path.Combine(BackupDir, BaseName + "_" + date + "z" + n + ".ini");
                if (!File.Exists(p)) target = p;
            }
            File.Copy(IniPath, target);
            return target;
        }

        public static List<string> ListBackups()
        {
            try
            {
                MoveOldBackups();
                if (!Directory.Exists(BackupDir)) return new List<string>();
                return Directory.GetFiles(BackupDir, BaseName + "_*.ini")
                    .Where(f => Path.GetFileNameWithoutExtension(f).Length >= BaseName.Length + 10)
                    .OrderByDescending(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        public static void RestoreBackup(string path)
        {
            File.Copy(path, IniPath, true);
            Load();
        }

        // ---------------------------------------------------------------- INI-Helfer

        private static Dictionary<string, Dictionary<string, string>> Parse(string[] lines)
        {
            var res = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> cur = null;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string name = line.Substring(1, line.Length - 2).Trim();
                    if (!res.TryGetValue(name, out cur))
                    {
                        cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        res[name] = cur;
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0 || cur == null) continue;
                cur[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return res;
        }

        private static Dictionary<string, string> Sec(Dictionary<string, Dictionary<string, string>> ini, string name)
        {
            return ini.TryGetValue(name, out var d) ? d : new Dictionary<string, string>();
        }

        private static string S(Dictionary<string, string> d, string k, string def)
            => d.TryGetValue(k, out var v) ? v : def;

        private static double D(Dictionary<string, string> d, string k, double def, double min, double max)
        {
            if (d.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, Inv, out double r))
                return Math.Max(min, Math.Min(max, r));
            return def;
        }

        private static int I(Dictionary<string, string> d, string k, int def, int min, int max)
            => (int)Math.Round(D(d, k, def, min, max));

        private static bool B(Dictionary<string, string> d, string k, bool def)
        {
            if (!d.TryGetValue(k, out var v)) return def;
            v = v.Trim().ToLowerInvariant();
            return v == "1" || v == "true" || v == "ja" || v == "yes";
        }

        private static void W(StringBuilder sb, string k, object v)
        {
            string s;
            if (v is bool b) s = b ? "1" : "0";
            else if (v is double dd) s = dd.ToString("0.##", Inv);
            else if (v is IFormattable f) s = f.ToString(null, Inv);
            else s = v?.ToString() ?? "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            sb.Append(k).Append('=').AppendLine(s);
        }
    }
}
