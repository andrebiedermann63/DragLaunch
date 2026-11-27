using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DragLaunch
{
    /// <summary>
    /// Lädt Kachel-Icons auf einem eigenen Hintergrund-Thread (STA, wegen der Windows-Shell).
    /// So blockiert ein langsames Icon (Netzlaufwerk, Cloud-Datei, Store-App) nie den Tab-Wechsel:
    /// die Kacheln erscheinen sofort, die Icons kommen nach.
    /// Aufträge eines alten Tabs werden übersprungen, sobald ein neuer Tab gezeichnet wird.
    /// </summary>
    public static class IconLoader
    {
        private sealed class Job
        {
            public ItemData Item; public int Size; public double Dpi; public Image Target; public int Generation;
        }

        // Zwei getrennte Warteschlangen: lokale Icons und eigene Icons sind sofort da,
        // Netzwerk-Pfade (Rechner, Freigaben, Netzlaufwerke) können warten, ohne die anderen aufzuhalten.
        private static readonly BlockingCollection<Job> Fast = new BlockingCollection<Job>();
        private static readonly BlockingCollection<Job> Slow = new BlockingCollection<Job>();
        private static Dispatcher _ui;
        private static int _generation;

        /// <summary>Neue Zeichnung: offene Aufträge der vorherigen verfallen.</summary>
        public static int NextGeneration() => Interlocked.Increment(ref _generation);
        public static int CurrentGeneration => Volatile.Read(ref _generation);

        public static void Init(Dispatcher ui)
        {
            if (_ui != null) return;
            _ui = ui;
            StartThread("IconLoader", Fast, true);
            StartThread("IconLoader-Netz", Slow, false);
        }

        private static void StartThread(string name, BlockingCollection<Job> queue, bool logSlow)
        {
            var t = new Thread(() => Run(queue, logSlow)) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public static void Request(ItemData it, int size, double dpi, Image target, int generation, bool network)
            => (network ? Slow : Fast).Add(new Job { Item = it, Size = size, Dpi = dpi, Target = target, Generation = generation });

        private static void Run(BlockingCollection<Job> queue, bool logSlow)
        {
            foreach (Job j in queue.GetConsumingEnumerable())
            {
                if (j.Generation != Volatile.Read(ref _generation)) continue;   // Tab inzwischen gewechselt
                ImageSource img = null;
                var sw = Stopwatch.StartNew();
                try { img = ShellIcons.ForItem(j.Item, j.Size, j.Dpi); }
                catch (Exception ex) { Log.Write("Icon " + j.Item.Path, ex); }
                sw.Stop();
                if (logSlow && sw.ElapsedMilliseconds > 1000)   // Netzwerk-Wartezeiten sind normal, nicht protokollieren
                    Log.Note($"Langsames Icon ({sw.ElapsedMilliseconds} ms): {j.Item.Path}");
                Dbg.W($"  Icon (Hintergrund) {sw.ElapsedMilliseconds} ms: {j.Item.Path}");
                if (img == null) continue;
                Image target = j.Target;
                _ui.BeginInvoke(DispatcherPriority.Background, new Action(() => target.Source = img));
            }
        }
    }
}
