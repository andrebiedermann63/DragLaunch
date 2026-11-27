using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DragLaunch
{
    /// <summary>Häufig gebrauchte Windows-Fenster und -Einstellungen zum direkten Hinzufügen.</summary>
    public static class WindowsTools
    {
        public class Entry
        {
            public string Group, Name, Path, Args = "", Icon = "";
            public string Detail = "";   // grau angezeigter Zusatz in der Liste (z.B. Dateiname)
            public string GameDir;       // Steam: Installationsordner, Icon wird daraus ermittelt
        }

        // ------------------------------------------------------------------ Steam

        /// <summary>Steam-Installationsordner aus der Registry (oder null).</summary>
        private static string SteamDir()
        {
            try
            {
                string p = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                           ?? Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
                if (string.IsNullOrWhiteSpace(p)) return null;
                p = p.Replace('/', '\\');
                return Directory.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        private static string VdfValue(string text, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, "\"" + key + "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Replace(@"\\", @"\") : null;
        }

        // Nicht-Spiele, die Steam ebenfalls als "App" führt
        private static readonly HashSet<string> SteamSkip = new HashSet<string> { "228980" /* Steamworks Common Redistributables */ };

        /// <summary>Alle installierten Steam-Spiele aus allen Steam-Bibliotheken.</summary>
        public static List<Entry> SteamGames()
        {
            var list = new List<Entry>();
            string steam = SteamDir();
            if (steam == null) return list;

            var libraries = new List<string> { steam };
            try
            {
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                                 File.ReadAllText(vdf), "\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\""))
                        libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch { }

            var seen = new HashSet<string>();
            foreach (string lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                string[] manifests;
                try { manifests = Directory.GetFiles(apps, "appmanifest_*.acf"); } catch { continue; }
                foreach (string acf in manifests)
                {
                    try
                    {
                        string t = File.ReadAllText(acf);
                        string id = VdfValue(t, "appid"), name = VdfValue(t, "name"), dir = VdfValue(t, "installdir");
                        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || SteamSkip.Contains(id) || !seen.Add(id)) continue;
                        list.Add(new Entry
                        {
                            Group = L.T("grp.steam"),
                            Name = name,
                            Path = "steam://rungameid/" + id,
                            Detail = "AppID " + id,
                            GameDir = string.IsNullOrEmpty(dir) ? null : Path.Combine(apps, "common", dir),
                            Icon = Path.Combine(steam, "steam.exe")   // Ersatz, falls keine Spiel-.exe gefunden wird
                        });
                    }
                    catch { }
                }
            }
            return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static readonly string[] ExeSkip = { "unins", "crash", "report", "setup", "redist", "vcredist", "vc_", "dxsetup", "dotnet", "helper", "prereq", "easyanticheat", "be_service", "cleanup", "uploader" };

        /// <summary>Icon-Quelle eines Eintrags; bei Steam-Spielen wird die Spiel-.exe gesucht (einmalig).</summary>
        public static string IconSource(Entry e)
        {
            if (e.GameDir != null)
            {
                string exe = FindGameExe(e.GameDir, e.Name);
                if (exe != null) e.Icon = exe;
                e.GameDir = null;
            }
            return string.IsNullOrEmpty(e.Icon) ? e.Path : e.Icon;
        }

        private static string FindGameExe(string dir, string name)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                var dirs = new List<string> { dir };
                foreach (string sub in new[] { @"bin", @"Bin", @"x64", @"bin\x64", @"Binaries\Win64", @"game\bin\win64" })
                    dirs.Add(Path.Combine(dir, sub));
                try { dirs.AddRange(Directory.GetDirectories(dir).Select(d => Path.Combine(d, "Binaries", "Win64"))); } catch { }

                string key = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                foreach (string d in dirs.Where(Directory.Exists))
                {
                    var exes = Directory.GetFiles(d, "*.exe")
                        .Where(f => !ExeSkip.Any(x => Path.GetFileName(f).ToLowerInvariant().Contains(x)))
                        .ToList();
                    if (exes.Count == 0) continue;
                    // Bevorzugt: Dateiname ähnelt dem Spielnamen, sonst die grösste .exe
                    string match = exes.FirstOrDefault(f =>
                    {
                        string fn = new string(Path.GetFileNameWithoutExtension(f).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                        return fn.Length >= 3 && (key.Contains(fn) || fn.Contains(key));
                    });
                    return match ?? exes.OrderByDescending(f => new FileInfo(f).Length).First();
                }
            }
            catch { }
            return null;
        }


        /// <summary>Alle Verwaltungskonsolen (.msc), die auf diesem PC in System32 vorhanden sind.</summary>
        public static List<Entry> MscConsoles()
        {
            var list = new List<Entry>();
            try
            {
                string dir = Environment.ExpandEnvironmentVariables(Sys);
                foreach (string f in Directory.GetFiles(dir, "*.msc"))
                {
                    string file = System.IO.Path.GetFileName(f);
                    string key = "msc." + System.IO.Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    string name = L.T(key);
                    if (name == key) name = System.IO.Path.GetFileNameWithoutExtension(f);
                    list.Add(new Entry { Group = L.T("grp.mmc"), Name = name, Path = Sys + file, Detail = file });
                }
            }
            catch { }
            return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private const string Sys = @"%SystemRoot%\System32\";
        private const string Settings = ShellIcons.SettingsExe;

        private static Entry E(string group, string name, string path, string args = "", string icon = "")
            => new Entry { Group = group, Name = name, Path = path, Args = args, Icon = icon };

        /// <summary>Feste Liste; die Einstellungsseiten alphabetisch, „Einstellungen“ (Startseite) zuerst.</summary>
        public static List<Entry> All
        {
            get
            {
                var raw = RawList;
                string g = L.T("grp.settings");
                var settings = raw.Where(e => e.Group == g)
                    .OrderBy(e => e.Path == "ms-settings:" ? 0 : 1)
                    .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                return settings.Concat(raw.Where(e => e.Group != g)).ToList();
            }
        }

        private static List<Entry> RawList => new List<Entry>
        {
            E(L.T("grp.settings"), L.T("wt.update"), "ms-settings:windowsupdate", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.settings"), "ms-settings:", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.display"), "ms-settings:display", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.graphics"), "ms-settings:display-advancedgraphics", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.sound"), "ms-settings:sound", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.mixer"), "ms-settings:apps-volume", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.bluetooth"), "ms-settings:bluetooth", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.apps"), "ms-settings:appsfeatures", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.startupapps"), "ms-settings:startupapps", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.power"), "ms-settings:powersleep", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.network"), "ms-settings:network", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.gamemode"), "ms-settings:gaming-gamemode", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.security"), "windowsdefender:", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.about"), "ms-settings:about", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.storage"), "ms-settings:storagesense", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.storagesense"), "ms-settings:storagepolicies", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.storagerec"), "ms-settings:storagerecommendations", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.disks"), "ms-settings:disksandvolumes", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.notifications"), "ms-settings:notifications", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.focus"), "ms-settings:quiethours", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.multitasking"), "ms-settings:multitasking", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.clipboard"), "ms-settings:clipboard", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.nightlight"), "ms-settings:nightlight", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.displayadv"), "ms-settings:display-advanced", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.remotedesktop"), "ms-settings:remotedesktop", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.recovery"), "ms-settings:recovery", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.activation"), "ms-settings:activation", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.troubleshoot"), "ms-settings:troubleshoot", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.developers"), "ms-settings:developers", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.printers"), "ms-settings:printers", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.mousesettings"), "ms-settings:mousetouchpad", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.typing"), "ms-settings:typing", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.usb"), "ms-settings:usb", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.autoplay"), "ms-settings:autoplay", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.defaultapps"), "ms-settings:defaultapps", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.optionalfeatures"), "ms-settings:optionalfeatures", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.wifi"), "ms-settings:network-wifi", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.ethernet"), "ms-settings:network-ethernet", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.vpn"), "ms-settings:network-vpn", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.proxy"), "ms-settings:network-proxy", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.netadvanced"), "ms-settings:network-advancedsettings", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.background"), "ms-settings:personalization-background", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.colors"), "ms-settings:colors", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.themes"), "ms-settings:themes", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.lockscreen"), "ms-settings:lockscreen", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.startmenu"), "ms-settings:personalization-start", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.taskbar"), "ms-settings:taskbar", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.fonts"), "ms-settings:fonts", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.yourinfo"), "ms-settings:yourinfo", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.signin"), "ms-settings:signinoptions", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.otherusers"), "ms-settings:otherusers", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.datetime"), "ms-settings:dateandtime", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.region"), "ms-settings:regionformatting", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.language"), "ms-settings:regionlanguage", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.gamebar"), "ms-settings:gaming-gamebar", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.captures"), "ms-settings:gaming-gamedvr", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.textsize"), "ms-settings:easeofaccess-display", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.mousepointer"), "ms-settings:easeofaccess-mousepointer", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.privacy"), "ms-settings:privacy", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.camera"), "ms-settings:privacy-webcam", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.microphone"), "ms-settings:privacy-microphone", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.updatehistory"), "ms-settings:windowsupdate-history", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.updateoptions"), "ms-settings:windowsupdate-options", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.optionalupdates"), "ms-settings:windowsupdate-optionalupdates", icon: Settings),
            E(L.T("grp.settings"), L.T("wt.delivery"), "ms-settings:delivery-optimization", icon: Settings),

            E(L.T("grp.cpl"), L.T("wt.joy"), Sys + "joy.cpl"),
            E(L.T("grp.cpl"), L.T("wt.soundclassic"), Sys + "mmsys.cpl"),
            E(L.T("grp.cpl"), L.T("wt.mouse"), Sys + "main.cpl"),
            E(L.T("grp.cpl"), L.T("wt.powercfg"), Sys + "powercfg.cpl"),
            E(L.T("grp.cpl"), L.T("wt.ncpa"), Sys + "ncpa.cpl"),
            E(L.T("grp.cpl"), L.T("wt.appwiz"), Sys + "appwiz.cpl"),
            E(L.T("grp.cpl"), L.T("wt.sysdm"), Sys + "sysdm.cpl"),
            E(L.T("grp.cpl"), L.T("wt.envvars"), Sys + "rundll32.exe", "sysdm.cpl,EditEnvironmentVariables", Sys + "sysdm.cpl"),
            E(L.T("grp.cpl"), L.T("wt.timedate"), Sys + "timedate.cpl"),
            E(L.T("grp.cpl"), L.T("wt.control"), Sys + "control.exe"),

            E(L.T("grp.admin"), L.T("wt.taskmgr"), Sys + "Taskmgr.exe"),
            E(L.T("grp.admin"), L.T("wt.resmon"), Sys + "resmon.exe"),
            E(L.T("grp.admin"), L.T("wt.regedit"), @"%SystemRoot%\regedit.exe"),
            E(L.T("grp.admin"), L.T("wt.msinfo"), Sys + "msinfo32.exe"),
            E(L.T("grp.admin"), L.T("wt.cleanmgr"), Sys + "cleanmgr.exe"),

            E(L.T("grp.tools"), L.T("wt.cmd"), Sys + "cmd.exe"),
            E(L.T("grp.tools"), L.T("wt.powershell"), Sys + @"WindowsPowerShell\v1.0\powershell.exe"),
            E(L.T("grp.tools"), L.T("wt.osk"), Sys + "osk.exe"),
            E(L.T("grp.tools"), L.T("wt.calc"), "ms-calculator:", icon: Sys + "calc.exe"),

            E(L.T("grp.folders"), L.T("wt.thispc"), "shell:MyComputerFolder"),
            E(L.T("grp.folders"), L.T("wt.netplaces"), "shell:NetworkPlacesFolder"),
            E(L.T("grp.folders"), L.T("wt.recycle"), "shell:RecycleBinFolder"),
            E(L.T("grp.folders"), L.T("wt.startupfolder"), "shell:startup"),
            E(L.T("grp.folders"), L.T("wt.downloads"), "shell:Downloads"),
        };

        /// <summary>
        /// Alle installierten Apps aus "shell:AppsFolder" (Startmenü inkl. Microsoft-Store-Apps wie HP Smart).
        /// Gestartet werden sie über "shell:AppsFolder\&lt;App-Kennung&gt;" - eine .exe braucht es dafür nicht.
        /// </summary>
        public static List<Entry> InstalledApps()
        {
            var list = new List<Entry>();
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = Activator.CreateInstance(t);
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                dynamic items = folder.Items();
                int count = items.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic item = items.Item(i);
                    string name = item.Name;
                    string id = item.Path;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                    // Manche Startmenü-Einträge sind Internet-Verknüpfungen (z.B. steam://rungameid/...)
                    string target = id.Contains("://") ? id : @"shell:AppsFolder\" + id;
                    list.Add(E(L.T("grp.apps"), name, target));
                }
            }
            catch { }
            return list
                .GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Existiert das Ziel auf diesem PC? (Adressen wie ms-settings: gelten immer als vorhanden.)</summary>
        public static bool Available(Entry e)
        {
            if (Launcher.IsUrl(e.Path)) return true;
            return File.Exists(Environment.ExpandEnvironmentVariables(e.Path));
        }

        /// <summary>Neues Icon-Element für den Launcher. Eigenes Icon wird in den Icons-Ordner übernommen.</summary>
        public static ItemData ToItem(Entry e)
        {
            var it = new ItemData { Path = e.Path, Arguments = e.Args, Label = e.Name };
            string iconSrc = IconSource(e);
            if (!string.IsNullOrEmpty(e.Icon) && iconSrc == e.Icon)
            {
                try { it.IconFile = IconStore.Collect(Environment.ExpandEnvironmentVariables(e.Icon)); }
                catch { it.IconFile = ""; }
            }
            return it;
        }
    }

    /// <summary>Auswahlfenster: Windows-Funktionen anhaken und hinzufügen, oder eigenen Eintrag anlegen.</summary>
    public class WindowsToolsWindow : Window
    {
        private readonly ListBox _list = new ListBox { SelectionMode = SelectionMode.Extended };
        private readonly TextBox _search = new TextBox { Padding = new Thickness(3) };
        private readonly Action<List<ItemData>> _add;

        // Aufklappbare Gruppen; was zugeklappt ist, bleibt es bis zum Beenden von DragLaunch
        private static HashSet<string> _collapsed;
        private readonly Dictionary<string, ListBoxItem> _headers = new Dictionary<string, ListBoxItem>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        public WindowsToolsWindow(Action<List<ItemData>> add, double dpiScale)
        {
            _add = add;
            Title = L.T("wtw.title");
            Width = 460;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            string lastGroup = null;
            var entries = WindowsTools.All.Where(WindowsTools.Available).ToList();
            entries.AddRange(WindowsTools.MscConsoles());
            entries.AddRange(WindowsTools.SteamGames());
            entries.AddRange(WindowsTools.InstalledApps());
            if (_collapsed == null) _collapsed = new HashSet<string>(entries.Select(x => x.Group));   // anfangs alles zugeklappt
            foreach (var g in entries.GroupBy(x => x.Group)) _counts[g.Key] = g.Count();
            foreach (WindowsTools.Entry e in entries)
            {
                if (e.Group != lastGroup)
                {
                    string group = e.Group;
                    var header = new ListBoxItem
                    {
                        FontWeight = FontWeights.SemiBold,
                        Focusable = false,
                        Cursor = Cursors.Hand,
                        Margin = new Thickness(0, lastGroup == null ? 0 : 4, 0, 0),
                        Padding = new Thickness(2, 3, 2, 3)
                    };
                    header.PreviewMouseLeftButtonDown += (s, a) =>
                    {
                        if (!_collapsed.Remove(group)) _collapsed.Add(group);
                        Filter();
                        a.Handled = true;
                    };
                    _headers[group] = header;
                    _list.Items.Add(header);
                    lastGroup = e.Group;
                }
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var img = new Image { Width = 20, Height = 20, Margin = new Thickness(8, 1, 8, 1) };
                sp.Children.Add(img);
                // Icons erst nach dem Öffnen nachladen, damit das Fenster sofort erscheint
                WindowsTools.Entry entry = e;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                    img.Source = ShellIcons.Get(Environment.ExpandEnvironmentVariables(WindowsTools.IconSource(entry)), 20, dpiScale * UiScale.Factor)));
                sp.Children.Add(new TextBlock { Text = e.Name, VerticalAlignment = VerticalAlignment.Center });
                if (e.Detail.Length > 0)
                    sp.Children.Add(new TextBlock { Text = "  " + e.Detail, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
                _list.Items.Add(new ListBoxItem { Content = sp, Tag = e, ToolTip = e.Path + (e.Args.Length > 0 ? " " + e.Args : "") });
            }
            _list.MouseDoubleClick += (s, a) => AddSelected();

            _search.TextChanged += (s, a) => Filter();

            var bAdd = Btn(L.T("wtw.add"), AddSelected);
            bAdd.IsDefault = true;
            var bCustom = Btn(L.T("wtw.custom"), AddCustom);
            var bClose = Btn(L.T("close"), Close);
            bClose.IsCancel = true;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(bCustom);
            buttons.Children.Add(bAdd);
            buttons.Children.Add(bClose);

            var bExpand = Btn(L.T("wtw.expandall"), () => { _collapsed.Clear(); Filter(); });
            var bCollapse = Btn(L.T("wtw.collapseall"), () => { foreach (string g in _headers.Keys) _collapsed.Add(g); Filter(); });
            bExpand.Margin = new Thickness(0, 0, 6, 0);
            bCollapse.Margin = new Thickness(0);
            var groupButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            groupButtons.Children.Add(bExpand);
            groupButtons.Children.Add(bCollapse);

            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var lbl = new TextBlock { Text = L.T("wtw.search"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(lbl, Dock.Left);
            top.Children.Add(lbl);
            top.Children.Add(_search);

            var hint = new TextBlock
            {
                Text = L.T("wtw.hint"),
                Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
            };

            var root = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(groupButtons, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            DockPanel.SetDock(hint, Dock.Bottom);
            root.Children.Add(top);
            root.Children.Add(groupButtons);
            root.Children.Add(buttons);
            root.Children.Add(hint);
            root.Children.Add(_list);
            Content = root;
            UiScale.Apply(this);
            Loaded += (s, a) => _search.Focus();
            Filter();
        }

        /// <summary>
        /// Sichtbarkeit: Ohne Suchtext zeigen aufgeklappte Gruppen ihre Einträge. Mit Suchtext werden alle Treffer
        /// angezeigt (auch in zugeklappten Gruppen), Gruppen ohne Treffer verschwinden.
        /// </summary>
        private void Filter()
        {
            string q = _search.Text.Trim();
            var hits = new Dictionary<string, int>();
            foreach (ListBoxItem li in _list.Items)
            {
                if (!(li.Tag is WindowsTools.Entry e)) continue;
                bool hit = q.Length == 0
                           || e.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                           || e.Group.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                           || e.Detail.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
                if (hit) hits[e.Group] = (hits.TryGetValue(e.Group, out int n) ? n : 0) + 1;
                bool open = q.Length > 0 || !_collapsed.Contains(e.Group);
                li.Visibility = hit && open ? Visibility.Visible : Visibility.Collapsed;
                if (li.Visibility != Visibility.Visible) li.IsSelected = false;
            }
            foreach (var kv in _headers)
            {
                int n = hits.TryGetValue(kv.Key, out int h) ? h : 0;
                bool open = q.Length > 0 || !_collapsed.Contains(kv.Key);
                kv.Value.Content = (open ? "▾  " : "▸  ") + kv.Key + "  (" + (q.Length > 0 ? n : _counts[kv.Key]) + ")";
                kv.Value.Visibility = q.Length == 0 || n > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void AddSelected()
        {
            var items = _list.SelectedItems.Cast<ListBoxItem>()
                .Where(li => li.Visibility == Visibility.Visible)
                .Select(li => li.Tag as WindowsTools.Entry)
                .Where(e => e != null)
                .Select(WindowsTools.ToItem)
                .ToList();
            if (items.Count == 0) return;
            Mouse.OverrideCursor = Cursors.Wait;
            try { _add(items); }
            finally { Mouse.OverrideCursor = null; }
            _list.SelectedItems.Clear();
        }

        private void AddCustom()
        {
            var it = new ItemData { Label = L.T("new") };
            var dlg = new ItemPropertiesWindow(it) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                if (string.IsNullOrWhiteSpace(it.Label)) it.Label = Launcher.MakeLabel(it.Path);
                _add(new List<ItemData> { it });
            }
        }

        private static Button Btn(string text, Action a)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
            b.Click += (s, e) => a();
            return b;
        }
    }
}
