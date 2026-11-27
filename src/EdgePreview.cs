using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DragLaunch
{
    /// <summary>
    /// Zeigt die eingestellten Randzonen direkt am Bildschirmrand an, solange die Einstellungen offen sind.
    /// Gerechnet wird genau wie in EdgeWatcher (GetSystemMetrics, gleiche Pixel-Koordinaten wie GetCursorPos),
    /// deshalb passt die Anzeige zur aktuellen Auflösung. Die Balken sind klickdurchlässig und nehmen keinen Fokus.
    /// </summary>
    public static class EdgePreview
    {
        /// <summary>Mindestdicke der Anzeige, damit auch eine 1-px-Zone sichtbar ist.</summary>
        private const int MinVisible = 8;

        private static readonly List<Bar> Bars = new List<Bar>();
        private static bool _shown;

        public static bool Enabled { get; set; } = true;

        public static void Show() { _shown = true; Refresh(); }

        public static void Hide()
        {
            _shown = false;
            foreach (var b in Bars) { try { b.Close(); b.Dispose(); } catch { } }
            Bars.Clear();
        }

        /// <summary>Nach jeder Änderung der Einstellungen oder der Auflösung neu zeichnen.</summary>
        public static void Refresh()
        {
            foreach (var b in Bars) { try { b.Close(); b.Dispose(); } catch { } }
            Bars.Clear();
            if (!_shown || !Enabled) return;

            AppSettings s = ConfigStore.Settings;
            int w = Native.GetSystemMetrics(Native.SM_CXSCREEN);
            int h = Native.GetSystemMetrics(Native.SM_CYSCREEN);
            int t = Math.Max(Math.Max(1, s.EdgeThickness), MinVisible);

            if (s.Bottom.Enabled) { Span(s.Bottom, w, out int a, out int len); Add(a, h - t, len, t); }
            if (s.Top.Enabled) { Span(s.Top, w, out int a, out int len); Add(a, 0, len, t); }
            if (s.Left.Enabled) { Span(s.Left, h, out int a, out int len); Add(0, a, t, len); }
            if (s.Right.Enabled) { Span(s.Right, h, out int a, out int len); Add(w - t, a, t, len); }
        }

        private static void Span(EdgeZone z, int length, out int start, out int len)
        {
            double from = Math.Min(z.From, z.To), to = Math.Max(z.From, z.To);
            start = (int)Math.Round(from * (length - 1) / 100.0);
            int end = (int)Math.Round(to * (length - 1) / 100.0);
            len = Math.Max(1, end - start + 1);
        }

        private static void Add(int x, int y, int width, int height)
        {
            try
            {
                var b = new Bar();
                b.Show();
                Native.SetWindowPos(b.Handle, Native.HWND_TOPMOST, x, y, width, height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                Bars.Add(b);
            }
            catch (Exception ex) { Log.Write("EdgePreview", ex); }
        }

        private sealed class Bar : Form
        {
            public Bar()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.FromArgb(216, 0, 0);
                Opacity = 0.55;
                TopMost = true;
                Bounds = new Rectangle(-32000, -32000, 1, 1);
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                    return cp;
                }
            }
        }
    }
}
