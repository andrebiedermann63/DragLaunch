using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DragLaunch
{
    /// <summary>Windows-Farbwähler. Eigene Farben werden in der INI (CustomColors) gespeichert.</summary>
    public static class ColorPicker
    {
        /// <summary>Rückgabe "#RRGGBB" (bzw. mit bisherigem Alpha) oder null bei Abbruch.</summary>
        public static string Pick(Window owner, string current)
        {
            Color cur = Colors.White;
            try { if (!string.IsNullOrWhiteSpace(current)) cur = (Color)ColorConverter.ConvertFromString(current); } catch { }

            using (var dlg = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B),
                CustomColors = LoadCustomColors()
            })
            {
                var w = new Win32Owner(owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle);
                if (dlg.ShowDialog(w) != System.Windows.Forms.DialogResult.OK) return null;
                SaveCustomColors(dlg.CustomColors);
                var c = dlg.Color;
                string rgb = c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                return cur.A < 255 ? "#" + cur.A.ToString("X2") + rgb : "#" + rgb;
            }
        }

        // Der Windows-Dialog speichert eigene Farben als 0x00BBGGRR
        private static int[] LoadCustomColors()
        {
            var list = new List<int>();
            foreach (string part in (ConfigStore.Settings.CustomColors ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(part.Trim());
                    list.Add(c.R | (c.G << 8) | (c.B << 16));
                }
                catch { }
            }
            while (list.Count < 16) list.Add(0xFFFFFF);
            return list.Take(16).ToArray();
        }

        private static void SaveCustomColors(int[] colors)
        {
            var parts = new List<string>();
            foreach (int v in colors ?? new int[0])
                parts.Add("#" + (v & 0xFF).ToString("X2") + ((v >> 8) & 0xFF).ToString("X2") + ((v >> 16) & 0xFF).ToString("X2"));
            ConfigStore.Settings.CustomColors = string.Join(",", parts);
            ConfigStore.Save();
        }

        private sealed class Win32Owner : System.Windows.Forms.IWin32Window
        {
            public Win32Owner(IntPtr h) { Handle = h; }
            public IntPtr Handle { get; }
        }
    }
}
