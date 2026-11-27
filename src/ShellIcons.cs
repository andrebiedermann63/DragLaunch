using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragLaunch
{
    /// <summary>Holt die echten Windows-Icons (wie im Explorer) in beliebiger Grösse.</summary>
    public static class ShellIcons
    {
        // Thread-sicher: Icons werden im Hintergrund geladen (IconLoader), gelesen auch im UI-Thread
        private static readonly ConcurrentDictionary<string, ImageSource> Cache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, ImageSource> ItemCache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache() { Cache.Clear(); ItemCache.Clear(); }

        private static string ItemKey(ItemData it, int sizeDip, double dpiScale)
            => (it.IconFile ?? "") + "|" + it.Path + "|" + Math.Max(16, (int)Math.Round(sizeDip * dpiScale));

        /// <summary>Schon geladenes Icon einer Kachel (ohne Datei- oder Netzzugriff), sonst null.</summary>
        public static ImageSource CachedForItem(ItemData it, int sizeDip, double dpiScale)
            => ItemCache.TryGetValue(ItemKey(it, sizeDip, dpiScale), out var img) ? img : null;

        // ---- Netzlaufwerke: kurz prüfen, statt die Oberfläche minutenlang zu blockieren
        private static readonly Dictionary<string, (bool Ok, DateTime At)> DriveState = new Dictionary<string, (bool, DateTime)>(StringComparer.OrdinalIgnoreCase);

        private const int OkSeconds = 60, FailSeconds = 15;   // nicht erreichbar: schon nach 15 s wieder prüfen

        /// <summary>Liegt der Pfad im Netz (Netzwerk-Computer, UNC-Freigabe, verbundenes Netzlaufwerk)? Ohne Netzzugriff.</summary>
        public static bool IsNetworkPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || Launcher.IsUrl(path)) return false;
                if (path.StartsWith(@"\\")) return true;
                string root = Path.GetPathRoot(path);
                return root != null && root.Length >= 2 && root[1] == ':'
                       && new DriveInfo(root.Substring(0, 1)).DriveType == DriveType.Network;
            }
            catch { return false; }
        }

        private static bool Fresh(string key, out bool ok)
        {
            ok = true;
            lock (DriveState)
            {
                if (!DriveState.TryGetValue(key, out var st)) return false;
                ok = st.Ok;
                return (DateTime.UtcNow - st.At).TotalSeconds < (st.Ok ? OkSeconds : FailSeconds);
            }
        }

        /// <summary>Antwortet der Rechner auf dem Windows-Freigabe-Port 445? (höchstens 1,5 s warten)</summary>
        public static bool HostReachable(string host)
        {
            string key = @"\\" + host;
            if (Fresh(key, out bool cached)) return cached;
            bool ok;
            try
            {
                var task = System.Threading.Tasks.Task.Run(async () =>
                {
                    using (var c = new System.Net.Sockets.TcpClient())
                    {
                        await c.ConnectAsync(host, 445);
                        return c.Connected;
                    }
                });
                ok = task.Wait(1500) && task.Result;
            }
            catch { ok = false; }
            lock (DriveState) DriveState[key] = (ok, DateTime.UtcNow);
            return ok;
        }

        private static string HostOf(string path) => path.TrimStart('\\').Split('\\')[0];

        /// <summary>
        /// False, wenn der Pfad im Netz liegt und gerade nicht antwortet (Rechner: Port 445 in 1,5 s,
        /// Freigabe/Laufwerk: Ordner in 0,8 s). Lokale Pfade gelten immer als erreichbar.
        /// Ergebnis wird zwischengespeichert: erreichbar 60 s, nicht erreichbar 15 s.
        /// </summary>
        public static bool Reachable(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || Launcher.IsUrl(path)) return true;
                if (Launcher.IsServerPath(path)) return HostReachable(HostOf(path));
                if (Launcher.IsShellContainer(path)) return true;
                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root)) return true;
                bool network = root.StartsWith(@"\\");
                if (!network && root.Length >= 2 && root[1] == ':')
                    network = new DriveInfo(root.Substring(0, 1)).DriveType == DriveType.Network;
                if (!network) return true;

                if (Fresh(root, out bool cached)) return cached;
                if (root.StartsWith(@"\\") && !HostReachable(HostOf(root)))
                {
                    lock (DriveState) DriveState[root] = (false, DateTime.UtcNow);
                    return false;
                }
                var task = System.Threading.Tasks.Task.Run(() => Directory.Exists(root));
                bool ok = task.Wait(800) && task.Result;
                lock (DriveState) DriveState[root] = (ok, DateTime.UtcNow);
                return ok;
            }
            catch { return true; }
        }

        public static ImageSource ForItem(ItemData it, int sizeDip, double dpiScale)
        {
            string key = ItemKey(it, sizeDip, dpiScale);
            if (string.IsNullOrWhiteSpace(it.IconFile))
            {
                // Netzwerk-Ziel gerade nicht erreichbar: Ersatz-Icon zeigen (auch wenn das echte schon bekannt ist)
                string p0 = Environment.ExpandEnvironmentVariables(it.Path);
                if (IsNetworkPath(p0) && !Reachable(p0)) return OfflineIcon(p0);
            }
            if (ItemCache.TryGetValue(key, out var hit)) return hit;
            if (!string.IsNullOrWhiteSpace(it.IconFile))
            {
                var custom = FromFile(IconStore.Resolve(it.IconFile), sizeDip, dpiScale);
                if (custom != null) { ItemCache[key] = custom; return custom; }
            }
            string path = Environment.ExpandEnvironmentVariables(it.Path);
            var img = Get(path, sizeDip, dpiScale);
            // Ersatz-Icon eines nicht erreichbaren Netzlaufwerks nicht merken - später nochmals versuchen
            if (Launcher.IsUrl(path) || Reachable(path)) ItemCache[key] = img;
            return img;
        }

        /// <summary>Ersatz-Icon für ein nicht erreichbares Netzwerk-Ziel: Weltkugel für Rechner und Ordner, sonst das Icon des Dateityps.</summary>
        private static ImageSource OfflineIcon(string path)
        {
            string ext = Launcher.IsServerPath(path) ? "" : Path.GetExtension(path.TrimEnd('\\'));
            return FromExtension(string.IsNullOrEmpty(ext) ? ".url" : ext);
        }

        public static ImageSource Get(string path, int sizeDip, double dpiScale)
        {
            int px = Math.Max(16, (int)Math.Round(sizeDip * dpiScale));
            string key = path + "|" + px;
            if (Cache.TryGetValue(key, out var img)) return img;

            img = null;
            if (Launcher.IsServerPath(path))
            {
                // Netzwerk-Computer: nur wenn er antwortet, sein Icon holen und merken; sonst Weltkugel, später erneut versuchen
                if (!Reachable(path)) return OfflineIcon(path);
                img = ShellNoFrame(path.TrimEnd('\\'), px, mustExist: false) ?? FromExtension(".url");
                Cache[key] = img;
                return img;
            }
            if (!Launcher.IsUrl(path) && !Reachable(path))
                return OfflineIcon(path);                              // Netzlaufwerk antwortet nicht: Ersatz-Icon, NICHT zwischenspeichern
            if (ResolveAppsFolderFile(path) is string realFile)
                img = Get(realFile, sizeDip, dpiScale);                                         // Startmenü-Eintrag eines normalen Programms
            else if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::"))
                img = ShellNoFrame(path, px, mustExist: false) ?? FromExtension(".url");      // Store-Apps, Shell-Ordner wie Papierkorb
            else if (path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
                img = FromShell(Environment.ExpandEnvironmentVariables(SettingsExe), px) ?? FromExtension(".url");
            else if (Launcher.IsUrl(path))
                img = FromExtension(".url");
            else if (TrySplitIndex(path, out string resFile, out int resIndex))
                img = FromIconResource(resFile, resIndex, px);
            else if (HasEmbeddedIcons(path))
                img = FromIconResource(path, 0, px) ?? ShellNoFrame(path, px) ?? FromExtension(Path.GetExtension(path));
            else
                img = ShellNoFrame(path, px) ?? FromExtension(Path.GetExtension(path));

            Cache[key] = img;
            return img;
        }

        /// <summary>Eigenes Icon: Bilddatei/ICO direkt laden, sonst (exe/dll/...) über die Shell.</summary>
        /// <summary>
        /// Systemsteuerungs-Module (.cpl) und DLLs: Windows zeigt dafür nur ein allgemeines Datei-Symbol,
        /// das eigentliche Icon steckt als Ressource in der Datei (wie "joy.cpl,-1").
        /// </summary>
        private static bool HasEmbeddedIcons(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return (ext == ".exe" || ext == ".cpl" || ext == ".dll" || ext == ".icl") && File.Exists(path);
        }

        public const string SettingsExe = @"%SystemRoot%\ImmersiveControlPanel\SystemSettings.exe";

        /// <summary>Erkennt Icon-Angaben im Windows-Format "datei,index" (z.B. "joy.cpl,-1" oder "shell32.dll,12").</summary>
        public static bool TrySplitIndex(string spec, out string file, out int index)
        {
            file = null;
            index = 0;
            if (string.IsNullOrEmpty(spec)) return false;
            int comma = spec.LastIndexOf(',');
            if (comma <= 0) return false;
            if (!int.TryParse(spec.Substring(comma + 1).Trim(), out index)) return false;
            file = Environment.ExpandEnvironmentVariables(spec.Substring(0, comma).Trim().Trim('"'));
            return File.Exists(file);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy,
            IntPtr[] icons, uint[] ids, uint count, uint flags);

        /// <summary>Icon aus einer Programm-/DLL-Ressource in Wunschgrösse (index &lt; 0 = Ressourcen-ID).</summary>
        public static BitmapSource FromIconResource(string file, int index, int px)
        {
            var icons = new IntPtr[1];
            var ids = new uint[1];
            try
            {
                uint n = PrivateExtractIcons(file, index, px, px, icons, ids, 1, 0);
                if (n == 0 || n == 0xFFFFFFFF || icons[0] == IntPtr.Zero) return null;
                var src = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally { if (icons[0] != IntPtr.Zero) DestroyIcon(icons[0]); }
        }

        private static ImageSource FromFile(string file, int sizeDip, double dpiScale)
        {
            int px = Math.Max(16, (int)Math.Round(sizeDip * dpiScale));
            if (TrySplitIndex(file, out string resFile, out int resIndex))
            {
                string k = "res|" + resFile + "|" + resIndex + "|" + px;
                if (!Cache.TryGetValue(k, out var r)) { r = FromIconResource(resFile, resIndex, px); Cache[k] = r; }
                return r;
            }
            if (!File.Exists(file)) return null;
            string key = "custom|" + file + "|" + px;
            if (Cache.TryGetValue(key, out var img)) return img;

            string ext = Path.GetExtension(file).ToLowerInvariant();
            try
            {
                if (ext == ".ico")
                {
                    img = FromIco(file, px) ?? FromShell(file, px);
                }
                else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif")
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(file);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = px;
                    bi.EndInit();
                    bi.Freeze();
                    img = bi;
                }
                else if (HasEmbeddedIcons(file))
                {
                    img = FromIconResource(file, 0, px) ?? ShellNoFrame(file, px);
                }
                else
                {
                    img = ShellNoFrame(file, px);
                }
            }
            catch { img = null; }

            if (img != null) Cache[key] = img;
            return img;
        }

        /// <summary>
        /// Wählt aus einer .ico-Datei die beste Variante: höchste Farbtiefe, dann die kleinste Grösse,
        /// die mindestens px gross ist (sonst die grösste). Unterstützt BMP- und PNG-Einträge.
        /// </summary>
        private static ImageSource FromIco(string file, int px)
        {
            byte[] d = File.ReadAllBytes(file);
            if (d.Length < 6 || BitConverter.ToUInt16(d, 0) != 0 || BitConverter.ToUInt16(d, 2) != 1) return null;
            int count = BitConverter.ToUInt16(d, 4);

            var entries = new List<(int size, int bpp, int offset, int length, bool png)>();
            for (int i = 0; i < count; i++)
            {
                int e = 6 + 16 * i;
                if (e + 16 > d.Length) break;
                int w = d[e] == 0 ? 256 : d[e];
                int bpp = BitConverter.ToUInt16(d, e + 6);
                int len = BitConverter.ToInt32(d, e + 8);
                int off = BitConverter.ToInt32(d, e + 12);
                if (off < 0 || len <= 0 || off + len > d.Length) continue;
                bool png = len > 8 && d[off] == 0x89 && d[off + 1] == (byte)'P' && d[off + 2] == (byte)'N' && d[off + 3] == (byte)'G';
                if (png && len > 26)
                {
                    // Farbtiefe aus dem PNG-Kopf: Bittiefe x Kanäle
                    int depth = d[off + 24], colorType = d[off + 25];
                    int ch = colorType == 6 ? 4 : colorType == 2 ? 3 : colorType == 4 ? 2 : 1;
                    bpp = Math.Max(bpp, depth * ch);
                    if (w == 256) w = (d[off + 16] << 24) | (d[off + 17] << 16) | (d[off + 18] << 8) | d[off + 19];
                }
                else if (!png && bpp == 0 && len > 16)
                {
                    bpp = BitConverter.ToUInt16(d, off + 14);
                }
                entries.Add((w, bpp, off, len, png));
            }
            if (entries.Count == 0) return null;

            int bestBpp = entries.Max(x => x.bpp);
            var sameDepth = entries.Where(x => x.bpp == bestBpp).ToList();
            var pick = sameDepth.Where(x => x.size >= px).OrderBy(x => x.size).FirstOrDefault();
            if (pick.length == 0) pick = sameDepth.OrderByDescending(x => x.size).First();

            BitmapSource result;
            if (pick.png)
            {
                using (var ms = new MemoryStream(d, pick.offset, pick.length))
                    result = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            else
            {
                // Einzelnes Bild als Mini-.ico verpacken, damit Windows Farben + Transparenzmaske korrekt liest
                var one = new byte[6 + 16 + pick.length];
                one[2] = 1; one[4] = 1;
                one[6] = (byte)(pick.size >= 256 ? 0 : pick.size);
                one[7] = one[6];
                BitConverter.GetBytes((ushort)1).CopyTo(one, 10);
                BitConverter.GetBytes((ushort)pick.bpp).CopyTo(one, 12);
                BitConverter.GetBytes(pick.length).CopyTo(one, 14);
                BitConverter.GetBytes(22).CopyTo(one, 18);
                Buffer.BlockCopy(d, pick.offset, one, 22, pick.length);
                using (var ms = new MemoryStream(one))
                    result = new IconBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            }
            result.Freeze();
            return result;
        }

        /// <summary>
        /// Shell-Icon ohne weissen Rahmen: Über 48 px nimmt Windows die "Jumbo"-Liste und setzt kleine Icons
        /// in ein weisses Quadrat. Wird so ein Rahmen erkannt, wird das 48-px-Icon (ohne Rahmen) verwendet.
        /// </summary>
        private static ImageSource ShellNoFrame(string path, int px, bool mustExist = true)
        {
            ImageSource img = FromShell(path, px, mustExist);
            if (px > 48 && img is BitmapSource b && LooksFramed(b))
                return FromShell(path, 48, mustExist) ?? img;
            return img;
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        /// <summary>
        /// "shell:AppsFolder\{Ordner-GUID}\Garmin\POILoader\POILoader.exe" -> echter Pfad der Datei (oder null).
        /// So kommen Startmenü-Einträge normaler Programme zu ihrem richtigen Icon.
        /// </summary>
        public static string ResolveAppsFolderFile(string path)
        {
            const string prefix = @"shell:AppsFolder\{";
            if (path == null || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            int end = path.IndexOf('}', prefix.Length);
            if (end < 0 || end + 2 > path.Length || path[end + 1] != '\\') return null;
            if (!Guid.TryParse(path.Substring(prefix.Length - 1, end - prefix.Length + 2), out Guid folder)) return null;
            IntPtr p = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderPath(folder, 0, IntPtr.Zero, out p) != 0 || p == IntPtr.Zero) return null;
                string full = Path.Combine(Marshal.PtrToStringUni(p), path.Substring(end + 2));
                return File.Exists(full) ? full : null;
            }
            catch { return null; }
            finally { if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p); }
        }

        /// <summary>Hellgrauer/weisser, undurchsichtiger Rand in allen vier Ecken = von Windows gezeichneter Rahmen.</summary>
        private static bool LooksFramed(BitmapSource src)
        {
            try
            {
                BitmapSource b = src.Format == PixelFormats.Pbgra32 || src.Format == PixelFormats.Bgra32
                    ? src : new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
                int w = b.PixelWidth, h = b.PixelHeight;
                if (w < 8 || h < 8) return false;
                var pts = new[] { (1, 1), (w - 2, 1), (1, h - 2), (w - 2, h - 2) };
                var px = new byte[4];
                foreach (var (x, y) in pts)
                {
                    b.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
                    int a = px[3];
                    if (a < 200) return false;
                    int brightness = (px[0] + px[1] + px[2]) / 3;
                    if (brightness < 170) return false;
                }
                return true;
            }
            catch { return false; }
        }

        private static ImageSource FromShell(string path, int px, bool mustExist = true)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (mustExist && !File.Exists(path) && !Directory.Exists(path)) return null;

            IntPtr hbm = IntPtr.Zero;
            try
            {
                Guid iid = Native.IID_IShellItemImageFactory;
                int hr = Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
                if (hr != 0 || factory == null) return null;
                try
                {
                    var size = new Native.SIZE { cx = px, cy = px };
                    hr = factory.GetImage(size, Native.SIIGBF_ICONONLY | Native.SIIGBF_BIGGERSIZEOK, out hbm);
                    if (hr != 0 || hbm == IntPtr.Zero) return null;
                    return HBitmapToSource(hbm);
                }
                finally { Marshal.ReleaseComObject(factory); }
            }
            catch { return null; }
            finally { if (hbm != IntPtr.Zero) Native.DeleteObject(hbm); }
        }

        /// <summary>HBITMAP (32bpp mit Alpha) korrekt in ein WPF-Bild umwandeln.</summary>
        private static BitmapSource HBitmapToSource(IntPtr hbm)
        {
            var bm = new Native.BITMAP();
            if (Native.GetObject(hbm, Marshal.SizeOf(typeof(Native.BITMAP)), ref bm) == 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0) return null;

            var bi = new Native.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)),
                biWidth = w,
                biHeight = -h,   // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };
            byte[] bits = new byte[w * h * 4];
            IntPtr hdc = Native.GetDC(IntPtr.Zero);
            try
            {
                if (Native.GetDIBits(hdc, hbm, 0, (uint)h, bits, ref bi, 0) == 0) return null;
            }
            finally { Native.ReleaseDC(IntPtr.Zero, hdc); }

            bool hasAlpha = false;
            for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;

            var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bits, w * 4);
            src.Freeze();
            return src;
        }

        // ---- Fallback über Dateityp (auch für nicht vorhandene Dateien und URLs)

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO psfi, uint cb, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

        private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        private static ImageSource FromExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) ext = ".xyz";
            string key = "ext|" + ext;
            if (Cache.TryGetValue(key, out var img)) return img;
            try
            {
                var info = new SHFILEINFO();
                SHGetFileInfo("x" + ext, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(info),
                    SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
                if (info.hIcon != IntPtr.Zero)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    DestroyIcon(info.hIcon);
                    src.Freeze();
                    img = src;
                }
            }
            catch { img = null; }
            Cache[key] = img;
            return img;
        }
    }
}
