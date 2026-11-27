using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DragLaunch
{
    /// <summary>
    /// Klick-Sound aus dem Unterordner "Sound" (.wav). Gespielt beim Klick auf ein Icon, beim Ablegen und beim Tab-Wechsel.
    /// Einstellung "Sound" = Dateiname, leer = kein Sound. Abspielen asynchron über winmm (keine Zusatzpakete).
    /// </summary>
    public static class Sounds
    {
        public static string Folder => Path.Combine(ConfigStore.AppDir, "Sound");

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern bool PlaySound(string file, IntPtr module, uint flags);
        private const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_FILENAME = 0x00020000;

        public static List<string> Available()
        {
            try
            {
                return Directory.GetFiles(Folder, "*.wav")
                    .Select(Path.GetFileName)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>Eingestellten Sound abspielen (falls einer gewählt ist).</summary>
        public static void Play() => PlayFile(ConfigStore.Settings.Sound);

        public static void PlayFile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                string path = Path.Combine(Folder, name);
                // im Hintergrund: ein schlafendes Audiogerät kann sonst die Oberfläche kurz aufhalten
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { if (File.Exists(path)) PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_NODEFAULT | SND_FILENAME); }
                    catch { }
                });
            }
            catch { }
        }
    }
}
