using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragLaunch
{
    /// <summary>
    /// Sammelt eigene Icons im Unterordner "Icons" des App-Ordners.
    /// In der INI steht dann nur noch ein relativer Pfad wie "Icons\pinball.png".
    /// Nicht mehr benutzte Icons bleiben liegen.
    /// </summary>
    public static class IconStore
    {
        public const string FolderName = "Icons";
        public static string Folder => Path.Combine(ConfigStore.AppDir, FolderName);

        private static readonly string[] ImageExt = { ".ico", ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        /// <summary>Vollständiger Pfad zu einem gespeicherten Icon-Eintrag (relativ oder absolut).</summary>
        public static string Resolve(string iconFile)
        {
            if (string.IsNullOrWhiteSpace(iconFile)) return "";
            string p = Environment.ExpandEnvironmentVariables(iconFile.Trim().Trim('"'));
            return Path.IsPathRooted(p) ? p : Path.Combine(ConfigStore.AppDir, p);
        }

        /// <summary>Liegt das Icon schon im Icons-Ordner?</summary>
        public static bool IsCollected(string iconFile)
        {
            string full = Path.GetFullPath(Resolve(iconFile));
            string folder = Path.GetFullPath(Folder).TrimEnd('\\') + "\\";
            return full.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Übernimmt eine Icon-Quelle in den Icons-Ordner und gibt den relativen Pfad zurück.
        /// Bilder/ICO werden kopiert, bei .exe/.dll usw. wird das Icon als PNG (256 px) gespeichert.
        /// Wirft eine Ausnahme, wenn die Quelle fehlt oder nicht gelesen werden kann.
        /// </summary>
        public static string Collect(string iconFile)
        {
            if (string.IsNullOrWhiteSpace(iconFile)) return "";

            // "datei,index" (z.B. "C:\Windows\System32\joy.cpl,-1"): dieses eine Icon als PNG ablegen
            if (ShellIcons.TrySplitIndex(iconFile, out string resFile, out int resIndex))
            {
                BitmapSource res = ShellIcons.FromIconResource(resFile, resIndex, 256)
                    ?? throw new InvalidOperationException(L.F("icon.notinfile", resIndex, resFile));
                Directory.CreateDirectory(Folder);
                byte[] data = Png(res);
                string target = FreeOrIdentical(Sanitize(Path.GetFileNameWithoutExtension(resFile) + "_" + resIndex), ".png", data);
                if (!File.Exists(target)) File.WriteAllBytes(target, data);
                return Relative(target);
            }

            string src = Resolve(iconFile);
            if (IsCollected(iconFile)) return Relative(src);
            if (!File.Exists(src)) throw new FileNotFoundException(L.T("icon.filemissing"), src);

            Directory.CreateDirectory(Folder);
            string ext = Path.GetExtension(src).ToLowerInvariant();
            string baseName = Sanitize(Path.GetFileNameWithoutExtension(src));

            if (ImageExt.Contains(ext))
            {
                byte[] data = File.ReadAllBytes(src);
                string target = FreeOrIdentical(baseName, ext, data);
                if (!File.Exists(target)) File.WriteAllBytes(target, data);
                return Relative(target);
            }

            // Programm, DLL, Verknüpfung o.ä.: Icon herauslösen und als PNG ablegen
            if (!(ShellIcons.Get(src, 256, 1.0) is BitmapSource bmp))
                throw new InvalidOperationException(L.T("icon.unreadable"));
            byte[] png = Png(bmp);
            string t = FreeOrIdentical(baseName, ".png", png);
            if (!File.Exists(t)) File.WriteAllBytes(t, png);
            return Relative(t);
        }

        private static byte[] Png(BitmapSource bmp)
        {
            using (var ms = new MemoryStream())
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                enc.Save(ms);
                return ms.ToArray();
            }
        }

        /// <summary>Alle Icons aller Tabs einsammeln (für ältere INI-Dateien). True = etwas geändert.</summary>
        public static bool CollectAll()
        {
            bool changed = false;
            foreach (TabData tab in ConfigStore.Tabs)
                foreach (ItemData it in tab.Items)
                {
                    if (string.IsNullOrWhiteSpace(it.IconFile) || IsCollected(it.IconFile)) continue;
                    try
                    {
                        string rel = Collect(it.IconFile);
                        if (rel != it.IconFile) { it.IconFile = rel; changed = true; }
                    }
                    catch { /* Quelle fehlt: Eintrag unverändert lassen */ }
                }
            return changed;
        }

        // Gleicher Name + gleicher Inhalt = wiederverwenden, sonst _2, _3 ...
        /// <summary>Heruntergeladenes Bild (z.B. Favicon) im Icons-Ordner speichern, gibt den relativen Pfad zurück.</summary>
        public static string SaveBytes(string baseName, string ext, byte[] data)
        {
            Directory.CreateDirectory(Folder);
            string target = FreeOrIdentical(Sanitize(baseName), ext, data);
            if (!File.Exists(target)) File.WriteAllBytes(target, data);
            return Relative(target);
        }

        private static string FreeOrIdentical(string baseName, string ext, byte[] data)
        {
            for (int n = 1; ; n++)
            {
                string name = n == 1 ? baseName + ext : baseName + "_" + n + ext;
                string p = Path.Combine(Folder, name);
                if (!File.Exists(p)) return p;
                try
                {
                    if (new FileInfo(p).Length == data.Length && File.ReadAllBytes(p).SequenceEqual(data)) return p;
                }
                catch { }
            }
        }

        private static string Relative(string full)
            => Path.GetRelativePath(ConfigStore.AppDir, full);

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "icon" : name;
        }
    }
}
