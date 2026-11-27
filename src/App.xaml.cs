using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace DragLaunch
{
    public partial class App : Application
    {
        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private string _id;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log.Write("Absturz", a.ExceptionObject as Exception);

            // Eine Instanz pro App-Ordner (mehrere Kopien in verschiedenen Ordnern sind erlaubt)
            _id = "DragLaunch_" + StableHash(ConfigStore.AppDir.ToLowerInvariant()).ToString("X8");
            _mutex = new Mutex(false, _id + "_Mutex");
            L.Load(ConfigStore.PeekLanguage());
            if (!TryOwnMutex(0) && !HandleRunningInstance())
            {
                Shutdown();
                return;
            }

            ConfigStore.Load();
            L.Load(ConfigStore.Settings.Language);
            Dbg.Enabled = ConfigStore.Settings.Debug;
            Dbg.Init();
            var win = new MainWindow();
            MainWindow = win;
            win.Show();
            Dbg.W("Fenster angezeigt");
            Dbg.StartWatchdog(Dispatcher);   // Hänger landen immer in DragLaunch.log

            // Zweiter Start holt das laufende Fenster nach vorne und bestätigt, dass diese Instanz lebt
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _id + "_Show");
            var t = new Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { win.BringToFront(true); } catch { }
                        try
                        {
                            using (var ack = new EventWaitHandle(false, EventResetMode.AutoReset, _id + "_Ack")) ack.Set();
                        }
                        catch { }
                    }));
                }
            }) { IsBackground = true };
            t.Start();
        }

        private bool TryOwnMutex(int timeoutMs)
        {
            try { return _mutex.WaitOne(timeoutMs); }
            catch (AbandonedMutexException) { return true; }   // vorherige Instanz ist abgestürzt
        }

        /// <summary>
        /// Es läuft schon eine Instanz. Antwortet sie, wird sie nach vorne geholt und dieser Start beendet.
        /// Antwortet sie nicht (hängt oder unsichtbar), kann sie beendet werden.
        /// Rückgabe true = dieser Start soll weiterlaufen.
        /// </summary>
        private bool HandleRunningInstance()
        {
            bool answered = false;
            try
            {
                using (var ack = new EventWaitHandle(false, EventResetMode.AutoReset, _id + "_Ack"))
                {
                    using (var ev = EventWaitHandle.OpenExisting(_id + "_Show")) ev.Set();
                    answered = ack.WaitOne(2500);
                }
            }
            catch { answered = false; }
            if (answered) return false;

            if (MessageBox.Show(L.T("app.hung"),
                    "DragLaunch", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return false;

            KillOtherInstances();
            if (TryOwnMutex(5000)) return true;
            MessageBox.Show(L.T("app.killfailed"),
                "DragLaunch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private static void KillOtherInstances()
        {
            using (Process me = Process.GetCurrentProcess())
            {
                string myPath = Environment.ProcessPath ?? "";
                foreach (Process p in Process.GetProcessesByName(me.ProcessName))
                {
                    try
                    {
                        if (p.Id != me.Id && string.Equals(p.MainModule?.FileName, myPath, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
        }

        // string.GetHashCode() ist in .NET pro Prozess zufällig - daher eigener, stabiler Hash (FNV-1a)
        private static uint StableHash(string text)
        {
            uint h = 2166136261;
            foreach (char c in text) { h ^= c; h *= 16777619; }
            return h;
        }

        private bool _inErrorDialog;

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            Log.Write("Fehler", e.Exception);
            Dbg.W("FEHLER: " + e.Exception.GetType().Name + ": " + e.Exception.Message);
            if (_inErrorDialog) return;
            _inErrorDialog = true;
            try
            {
                MessageBox.Show(L.F("app.error", e.Exception.Message),
                    "DragLaunch", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { _inErrorDialog = false; }

            // Kein unsichtbarer "Zombie": wenn das Fenster weg ist, sauber beenden
            if (MainWindow == null || !MainWindow.IsLoaded) Shutdown();
        }
    }

    /// <summary>
    /// Diagnose-Protokoll (DragLaunch_trace.log): jeder wichtige Schritt mit Zeit und Thread.
    /// Ein Wächter-Thread prüft alle 2 s, ob die Oberfläche noch reagiert.
    /// </summary>
    public static class Dbg
    {
        private static readonly object Lock = new object();
        public static bool Enabled;
        public static int Layouts, Renders, Positions, SizeChanges, DpiChanges;
        private static long _lastPingTicks = DateTime.UtcNow.Ticks;

        private static string FilePath => Path.Combine(ConfigStore.AppDir, ConfigStore.BaseName + "_trace.log");

        public static void Init()
        {
            if (!Enabled) return;
            try
            {
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 5_000_000) File.Delete(FilePath);
            }
            catch { }
            W("================ Start DragLaunch " + AppInfo.VersionText + " ================");
        }

        public static void W(string msg)
        {
            if (!Enabled) return;
            try
            {
                lock (Lock)
                    File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss.fff") + " [T" +
                        Environment.CurrentManagedThreadId + "] " + msg + "\r\n");
            }
            catch { }
        }

        public static void StartWatchdog(Dispatcher ui)
        {
            var t = new Thread(() =>
            {
                bool reported = false;
                DateTime? hungSince = null;
                while (true)
                {
                    Thread.Sleep(2000);
                    ui.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                        Interlocked.Exchange(ref _lastPingTicks, DateTime.UtcNow.Ticks)));
                    double age = (DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastPingTicks))).TotalSeconds;
                    string counters = $"Layouts={Layouts} Renders={Renders} Positionen={Positions} Groessen={SizeChanges} DPI={DpiChanges}";
                    if (age > 5)
                    {
                        W($"!!! Oberflaeche reagiert seit {age:0.0} s nicht. {counters}");
                        if (!reported) Log.Note($"HAENGER: Oberflaeche reagiert seit {age:0} s nicht.");
                        reported = true;
                        hungSince = hungSince ?? DateTime.UtcNow.AddSeconds(-age);
                    }
                    else if (reported)
                    {
                        W("Oberflaeche reagiert wieder. " + counters);
                        Log.Note($"Oberflaeche reagiert wieder (Haenger ca. {(DateTime.UtcNow - hungSince.Value).TotalSeconds:0} s).");
                        reported = false;
                        hungSince = null;
                    }
                }
            }) { IsBackground = true, Name = "Watchdog" };
            t.Start();
        }
    }

    /// <summary>Schreibt Fehler mit allen Details nach DragLaunch.log (max. ~1 MB).</summary>
    public static class Log
    {
        public static void Note(string text) => Write(text, null, false);

        public static void Write(string kind, Exception ex) => Write(kind, ex, true);

        private static void Write(string kind, Exception ex, bool details)
        {
            try
            {
                string path = Path.Combine(ConfigStore.AppDir, ConfigStore.BaseName + ".log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Delete(path);
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + kind + "\r\n" +
                    (details ? (ex?.ToString() ?? "(keine Details)") + "\r\n" : "") + "\r\n");
            }
            catch { }
        }
    }
}
