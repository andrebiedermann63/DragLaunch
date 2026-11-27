using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace DragLaunch
{
    public static class Launcher
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LnkCache = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] AppExt = { ".exe", ".com", ".bat", ".cmd", ".lnk", ".appref-ms" };

        public static bool IsUrl(string p)
        {
            // Alles mit "schema:" (http:, ms-settings:, shell:, mailto:, steam: ...). "C:\..." ist kein Schema.
            if (string.IsNullOrEmpty(p)) return false;
            int c = p.IndexOf(':');
            if (c < 2 || c > 40) return false;
            for (int i = 0; i < c; i++)
            {
                char ch = p[i];
                if (!(char.IsLetterOrDigit(ch) || ch == '-' || ch == '+' || ch == '.')) return false;
            }
            return true;
        }

        public static string Expand(string p) => Environment.ExpandEnvironmentVariables(p ?? "");

        /// <summary>Ziel einer .lnk-Verknüpfung (oder leer).</summary>
        public static string ResolveLnk(string lnk)
        {
            if (LnkCache.TryGetValue(lnk, out var t)) return t;
            t = "";
            try
            {
                Type st = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(st);
                dynamic sc = shell.CreateShortcut(lnk);
                t = (string)sc.TargetPath ?? "";
            }
            catch { }
            LnkCache[lnk] = t;
            return t;
        }

        /// <summary>Liefert den Ordnerpfad, falls das Element ein Ordner (oder eine Verknüpfung auf einen Ordner) ist.</summary>
        /// <summary>"\\\\SERVER" ohne Freigabe (Netzwerk-Computer).</summary>
        public static bool IsServerPath(string p)
            => p != null && System.Text.RegularExpressions.Regex.IsMatch(p, @"^\\\\[^\\/]+\\?$");

        /// <summary>Ordnerartige Elemente ohne echten Dateipfad: Netzwerk-Computer und Shell-Ordner wie "Netzwerk" (::{GUID}).</summary>
        public static bool IsShellContainer(string p) => p != null && (p.StartsWith("::") || IsServerPath(p));

        public static string FolderOf(ItemData it)
        {
            string p = Expand(it.Path);
            if (IsShellContainer(p)) return p;
            if (IsUrl(p)) return null;
            if (!ShellIcons.Reachable(p)) return null;   // nicht erreichbares Netzlaufwerk: nicht blockieren
            if (Directory.Exists(p)) return p;
            if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            {
                string t = ResolveLnk(p);
                if (t.Length > 0 && Directory.Exists(t)) return t;
            }
            return null;
        }

        /// <summary>Kann man Dateien auf dieses Element ziehen, um sie damit zu öffnen?</summary>
        public static bool AcceptsFiles(ItemData it)
        {
            string p = Expand(it.Path);
            if (IsUrl(p) || FolderOf(it) != null) return false;
            string ext = Path.GetExtension(p).ToLowerInvariant();
            return AppExt.Contains(ext);
        }

        /// <summary>
        /// Löst eine Verknüpfung (.url / .lnk) auf: Ziel, Argumente, Arbeitsordner und Icon werden übernommen,
        /// das Icon wird in den Icons-Ordner kopiert. Danach ist die Kachel unabhängig von der Verknüpfungsdatei.
        /// Gibt null zurück, wenn die Datei keine auflösbare Verknüpfung ist (dann bleibt sie, wie sie ist).
        /// </summary>
        public static ItemData FromShortcut(string file)
        {
            try
            {
                string ext = Path.GetExtension(file ?? "").ToLowerInvariant();
                if (!File.Exists(file) || (ext != ".url" && ext != ".lnk")) return null;
                string label = Path.GetFileNameWithoutExtension(file);

                if (ext == ".url")
                {
                    string url = null, iconFile = null;
                    int iconIndex = 0;
                    foreach (string raw in File.ReadAllLines(file))
                    {
                        string line = raw.Trim();
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                        if (k.Equals("URL", StringComparison.OrdinalIgnoreCase) && url == null) url = v;
                        else if (k.Equals("IconFile", StringComparison.OrdinalIgnoreCase)) iconFile = v;
                        else if (k.Equals("IconIndex", StringComparison.OrdinalIgnoreCase)) int.TryParse(v, out iconIndex);
                    }
                    if (string.IsNullOrWhiteSpace(url)) return null;
                    var it = new ItemData { Path = url, Label = label };
                    // Webseiten ohne mitgeliefertes Icon: leer lassen, DragLaunch holt danach das Favicon von der Seite
                    // (das Windows-Icon der .url wäre oft nur ein weisses Blatt)
                    it.IconFile = CollectIcon(iconFile, iconIndex)
                                  ?? (FaviconFetcher.IsWebUrl(url) ? "" : CollectIcon(file, 0) ?? "");
                    return it;
                }

                // .lnk
                Type st = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(st);
                dynamic sc = shell.CreateShortcut(file);
                string target = (string)sc.TargetPath ?? "";
                if (target.Length == 0 || !(File.Exists(target) || Directory.Exists(target))) return null;   // z.B. Installer-Verknüpfungen
                var item = new ItemData
                {
                    Path = target,
                    Label = label,
                    Arguments = (string)sc.Arguments ?? "",
                    WorkDir = (string)sc.WorkingDirectory ?? ""
                };
                string loc = (string)sc.IconLocation ?? "";       // Format "Datei,Index"
                int comma = loc.LastIndexOf(',');
                string iconPath = comma >= 0 ? loc.Substring(0, comma) : loc;
                int.TryParse(comma >= 0 ? loc.Substring(comma + 1) : "0", out int idx);
                iconPath = Environment.ExpandEnvironmentVariables(iconPath.Trim());
                // Eigenes Icon nur übernehmen, wenn es nicht ohnehin das Icon des Ziels ist
                if (iconPath.Length > 0 && !(string.Equals(iconPath, target, StringComparison.OrdinalIgnoreCase) && idx == 0))
                    item.IconFile = CollectIcon(iconPath, idx) ?? "";
                return item;
            }
            catch { return null; }
        }

        private static string CollectIcon(string iconFile, int index)
        {
            if (string.IsNullOrWhiteSpace(iconFile)) return null;
            iconFile = Environment.ExpandEnvironmentVariables(iconFile.Trim().Trim('"'));
            if (!File.Exists(iconFile)) return null;
            string ext = Path.GetExtension(iconFile).ToLowerInvariant();
            string spec = (index != 0 && ext != ".ico") ? iconFile + "," + index : iconFile;
            try { return IconStore.Collect(spec); } catch { return null; }
        }

        public static string MakeLabel(string path)
        {
            if (IsUrl(path))
            {
                if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    try { return new Uri(path).Host.Replace("www.", ""); } catch { return path; }
                }
                string rest = path.Substring(path.IndexOf(':') + 1).Trim('/');
                return rest.Length > 0 ? rest : path.TrimEnd(':');
            }
            if (path.StartsWith("::")) return path;
            string trimmed = path.TrimEnd('\\', '/');
            if (trimmed.Length <= 2) return trimmed;               // "C:"
            // Letzter Namensteil selbst bestimmen: Path.GetFileName liefert bei "\\\\SERVER\\Freigabe" nichts,
            // weil das für Windows die Wurzel des Pfads ist (wie "C:\\").
            string last = trimmed.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? trimmed;
            string ext = Path.GetExtension(last).ToLowerInvariant();
            if ((ext == ".lnk" || ext == ".url" || ext == ".exe" || ext == ".appref-ms") && !Directory.Exists(path))
                return Path.GetFileNameWithoutExtension(last);
            return last;
        }

        public static void Start(ItemData it, string extraArgs = null, bool? admin = null)
        {
            string p = Expand(it.Path);
            if (p.StartsWith("::")) { Open(p); return; }
            if (p.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
            {
                // Store-Apps & Startmenü-Einträge: zuverlässigster Start über den Explorer
                Run(new ProcessStartInfo("explorer.exe", "\"" + p + "\"") { UseShellExecute = true }, p);
                return;
            }
            var psi = new ProcessStartInfo(p) { UseShellExecute = true };
            string args = Expand(it.Arguments);
            if (!string.IsNullOrEmpty(extraArgs)) args = (args + " " + extraArgs).Trim();
            if (args.Length > 0) psi.Arguments = args;

            if (!string.IsNullOrWhiteSpace(it.WorkDir))
                psi.WorkingDirectory = Expand(it.WorkDir);
            else if (!IsUrl(p))
            {
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if ((ext == ".exe" || ext == ".bat" || ext == ".cmd") && File.Exists(p))
                    psi.WorkingDirectory = Path.GetDirectoryName(p);
            }

            if (admin ?? it.RunAsAdmin) psi.Verb = "runas";
            Run(psi, p);
        }

        public static void Open(string path)
        {
            if (path != null && path.StartsWith("::"))
                Run(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }, path);   // Shell-Ordner wie "Netzwerk"
            else
                Run(new ProcessStartInfo(path) { UseShellExecute = true }, path);
        }

        public static void ShowInExplorer(string path)
        {
            path = Expand(path);
            if (File.Exists(path) || Directory.Exists(path))
                Run(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }, path);
            else
                MessageBox.Show(L.F("launch.notfound", path), "DragLaunch");
        }

        /// <summary>Wird nach jedem erfolgreichen Start ausgelöst (Kachel, Ordnerliste, Explorer).</summary>
        public static event Action Launched;

        // 2/3 = Datei/Pfad nicht gefunden (bei Netzwerkpfaden typisch), 53 = Netzwerkpfad nicht gefunden,
        // 64/1231/1232 = Verbindung weg / Netz nicht erreichbar, 67 = Netzwerkname nicht gefunden, 1222 = kein Netzwerk
        private static bool IsNetworkError(int code)
            => code == 2 || code == 3 || code == 53 || code == 64 || code == 67 || code == 1222 || code == 1231 || code == 1232;

        private static bool IsNetworkPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            if (p.StartsWith(@"\\")) return true;
            try
            {
                string root = Path.GetPathRoot(p);
                return root != null && root.Length >= 2 && root[1] == ':' && new DriveInfo(root.Substring(0, 1)).DriveType == DriveType.Network;
            }
            catch { return false; }
        }

        /// <summary>"\\\\NAS\\Musik\\x.mp3" -> "\\\\NAS\\Musik"; Netzlaufwerk -> "V:\\"; sonst der Pfad selbst.</summary>
        private static string NetworkRoot(string p)
        {
            try
            {
                if (IsServerPath(p)) return p.TrimEnd('\\');
                string root = Path.GetPathRoot(p);
                return string.IsNullOrEmpty(root) ? p : root.TrimEnd('\\') + (root.EndsWith(":\\") ? "\\" : "");
            }
            catch { return p; }
        }

        private static void Run(ProcessStartInfo psi, string what)
        {
            try
            {
                Process.Start(psi)?.Dispose();
                Launched?.Invoke();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { /* UAC abgebrochen */ }
            catch (Win32Exception ex) when (IsNetworkError(ex.NativeErrorCode) && IsNetworkPath(what))
            {
                // Netzwerk-Computer, Freigabe oder Netzlaufwerk nicht erreichbar: kurze, verständliche Meldung
                MessageBox.Show(L.F("launch.unreachable", NetworkRoot(what)), "DragLaunch",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.F("launch.failed", what, ex.Message), "DragLaunch",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ------------------------------------------------------------ Ordner als Pulldown-Menü

        private const int MaxEntries = 400;

        public static ContextMenu BuildFolderMenu(string folder, double fontSize, double dpiScale)
        {
            var cm = new ContextMenu { FontSize = fontSize };
            Fill(cm, cm, folder, fontSize, dpiScale);
            return cm;
        }

        private static void Fill(ContextMenu root, ItemsControl parent, string folder, double fontSize, double dpiScale)
        {
            parent.Items.Clear();
            var open = new MenuItem { Header = L.T("folder.open"), FontWeight = FontWeights.SemiBold };
            open.Click += (s, e) => { Open(folder); e.Handled = true; };
            parent.Items.Add(open);
            parent.Items.Add(new Separator());

            if (IsShellContainer(folder))
            {
                FillShell(root, parent, folder, fontSize, dpiScale);
                foreach (object o in parent.Items) if (o is Control c) c.FontSize = fontSize;
                return;
            }

            List<FileSystemInfo> dirs, files;
            try
            {
                var di = new DirectoryInfo(folder);
                Func<FileSystemInfo, bool> visible = f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
                dirs = di.EnumerateDirectories().Cast<FileSystemInfo>().Where(visible).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                files = di.EnumerateFiles().Cast<FileSystemInfo>().Where(visible).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                parent.Items.Add(new MenuItem { Header = L.F("folder.noaccess", ex.Message), IsEnabled = false });
                return;
            }

            if (dirs.Count + files.Count == 0)
            {
                parent.Items.Add(new MenuItem { Header = L.T("folder.empty"), IsEnabled = false });
                return;
            }

            int n = 0;
            foreach (var d in dirs)
            {
                if (++n > MaxEntries) break;
                var mi = MakeEntry(d.FullName, d.Name, dpiScale);
                mi.Items.Add(new MenuItem { Header = "…", IsEnabled = false });
                bool loaded = false;
                string path = d.FullName;
                mi.SubmenuOpened += (s, e) =>
                {
                    if (!ReferenceEquals(e.OriginalSource, mi) || loaded) return;
                    loaded = true;
                    Fill(root, mi, path, fontSize, dpiScale);
                };
                // Klick auf den Ordner selbst öffnet ihn (Zeigen klappt weiterhin den Inhalt auf)
                mi.PreviewMouseLeftButtonUp += (s, e) =>
                {
                    if (!ReferenceEquals(OwningMenuItem(e.OriginalSource as DependencyObject), mi)) return;
                    e.Handled = true;
                    root.IsOpen = false;
                    Open(path);
                };
                parent.Items.Add(mi);
            }
            foreach (var f in files)
            {
                if (++n > MaxEntries) break;
                string ext = f.Extension.ToLowerInvariant();
                string name = (ext == ".lnk" || ext == ".url") ? Path.GetFileNameWithoutExtension(f.Name) : f.Name;
                var mi = MakeEntry(f.FullName, name, dpiScale);
                string path = f.FullName;
                mi.Click += (s, e) => { Open(path); e.Handled = true; };
                parent.Items.Add(mi);
            }
            if (n > MaxEntries)
                parent.Items.Add(new MenuItem { Header = L.T("folder.more"), IsEnabled = false });

            // Schriftgrösse auf jeder Ebene setzen (Untermenüs erben sie nicht zuverlässig)
            foreach (object o in parent.Items)
                if (o is Control c) c.FontSize = fontSize;
        }

        /// <summary>Das MenuItem, zu dem ein angeklicktes Element gehört (das nächste nach oben).</summary>
        private static MenuItem OwningMenuItem(DependencyObject d)
        {
            while (d != null && !(d is MenuItem))
            {
                DependencyObject p = (d is System.Windows.Media.Visual || d is System.Windows.Media.Media3D.Visual3D)
                    ? System.Windows.Media.VisualTreeHelper.GetParent(d) : null;
                d = p ?? LogicalTreeHelper.GetParent(d);
            }
            return d as MenuItem;
        }

        /// <summary>Inhalt eines Shell-Ordners (Netzwerk, Netzwerk-Computer mit seinen Freigaben) über Shell.Application.</summary>
        private static void FillShell(ContextMenu root, ItemsControl parent, string folder, double fontSize, double dpiScale)
        {
            var entries = new List<(string Name, string Path, bool IsFolder)>();
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic ns = shell.NameSpace(folder.StartsWith("::") ? "shell:" + folder : folder);
                if (ns == null) throw new IOException(folder);
                foreach (dynamic item in ns.Items())
                {
                    string name = item.Name, path = item.Path;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    entries.Add((name, path, (bool)item.IsFolder));
                }
            }
            catch (Exception ex)
            {
                parent.Items.Add(new MenuItem { Header = L.F("folder.noaccess", ex.Message), IsEnabled = false });
                return;
            }
            if (entries.Count == 0)
            {
                parent.Items.Add(new MenuItem { Header = L.T("folder.empty"), IsEnabled = false });
                return;
            }

            foreach (var en in entries.OrderBy(e => !e.IsFolder).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxEntries))
            {
                var mi = MakeEntry(en.Path, en.Name, dpiScale);
                string path = en.Path;
                if (en.IsFolder)
                {
                    mi.Items.Add(new MenuItem { Header = "…", IsEnabled = false });
                    bool loaded = false;
                    mi.SubmenuOpened += (s, e) =>
                    {
                        if (!ReferenceEquals(e.OriginalSource, mi) || loaded) return;
                        loaded = true;
                        Fill(root, mi, path, fontSize, dpiScale);
                    };
                    mi.PreviewMouseLeftButtonUp += (s, e) =>
                    {
                        if (!ReferenceEquals(OwningMenuItem(e.OriginalSource as DependencyObject), mi)) return;
                        e.Handled = true;
                        root.IsOpen = false;
                        Open(path);
                    };
                }
                else mi.Click += (s, e) => { Open(path); e.Handled = true; };
                parent.Items.Add(mi);
            }
        }

        private static MenuItem MakeEntry(string path, string name, double dpiScale)
        {
            // Unterstrich nicht als Zugriffstaste interpretieren
            var mi = new MenuItem { Header = new TextBlock { Text = name } };
            int size = ConfigStore.Settings.MenuIconSize;
            var icon = ShellIcons.Get(path, size, dpiScale);
            if (icon != null) mi.Icon = new Image { Source = icon, Width = size, Height = size };
            mi.Padding = new Thickness(mi.Padding.Left, 2, mi.Padding.Right, 2);
            return mi;
        }
    }
}
