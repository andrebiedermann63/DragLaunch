using System;
using System.Text;
using System.Windows.Threading;

namespace DragLaunch
{
    /// <summary>
    /// Prüft alle 40 ms, ob die Maus in einer aktiven Randzone des Hauptbildschirms steht.
    /// Bleibt sie dort länger als die eingestellte Verzögerung, wird Triggered ausgelöst
    /// (auch während man Dateien zieht - so kann man direkt auf DragLaunch ablegen).
    /// </summary>
    public class EdgeWatcher
    {
        private readonly DispatcherTimer _timer;
        private DateTime? _enteredAt;
        private bool _fired;

        public event Action Triggered;
        public Func<IntPtr> OwnHandle;

        public EdgeWatcher()
        {
            _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
            _timer.Tick += Tick;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void Tick(object sender, EventArgs e)
        {
            AppSettings s = ConfigStore.Settings;
            if (!Native.GetCursorPos(out var p)) return;
            int w = Native.GetSystemMetrics(Native.SM_CXSCREEN);
            int h = Native.GetSystemMetrics(Native.SM_CYSCREEN);

            if (!InZone(s, p, w, h))
            {
                _enteredAt = null;
                _fired = false;
                return;
            }
            if (_fired) return;

            DateTime now = DateTime.UtcNow;
            if (_enteredAt == null) _enteredAt = now;
            if ((now - _enteredAt.Value).TotalMilliseconds < s.ActivationDelayMs) return;

            _fired = true;
            if (s.IgnoreFullscreen && ForegroundIsFullscreen(w, h)) return;
            if (PauseAppRunning(s.EdgePauseApps)) return;
            Triggered?.Invoke();
        }

        private static bool InZone(AppSettings s, Native.POINT p, int w, int h)
        {
            int th = Math.Max(1, s.EdgeThickness);
            if (p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h) return false;   // anderer Monitor
            double px = p.X * 100.0 / Math.Max(1, w - 1);
            double py = p.Y * 100.0 / Math.Max(1, h - 1);

            if (s.Bottom.Enabled && p.Y >= h - th && Between(px, s.Bottom)) return true;
            if (s.Top.Enabled && p.Y < th && Between(px, s.Top)) return true;
            if (s.Left.Enabled && p.X < th && Between(py, s.Left)) return true;
            if (s.Right.Enabled && p.X >= w - th && Between(py, s.Right)) return true;
            return false;
        }

        private static bool Between(double v, EdgeZone z)
        {
            double a = Math.Min(z.From, z.To), b = Math.Max(z.From, z.To);
            return v >= a && v <= b;
        }

        /// <summary>Läuft eines der eingetragenen Programme (z.B. VPinballX_GL64, vrserver)?</summary>
        private static bool PauseAppRunning(string list)
        {
            if (string.IsNullOrWhiteSpace(list)) return false;
            foreach (string raw in list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = raw.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (name.Length == 0) continue;
                try
                {
                    var procs = System.Diagnostics.Process.GetProcessesByName(name);
                    bool running = procs.Length > 0;
                    foreach (var p in procs) p.Dispose();
                    if (running) return true;
                }
                catch { }
            }
            return false;
        }

        private bool ForegroundIsFullscreen(int w, int h)
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || (OwnHandle != null && fg == OwnHandle())) return false;
            var cls = new StringBuilder(256);
            Native.GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;
            if (!Native.GetWindowRect(fg, out var r)) return false;
            return r.Left <= 0 && r.Top <= 0 && r.Right >= w && r.Bottom >= h;
        }
    }
}
