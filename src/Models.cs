using System.Collections.Generic;

namespace DragLaunch
{
    public enum FolderMode { Default = 0, Menu = 1, Open = 2 }

    public class ItemData
    {
        public string Path = "";
        public string Label = "";
        public string Arguments = "";
        public string WorkDir = "";
        public string IconFile = "";
        public int Col = -1;     // Kachel-Spalte (0-basiert)
        public int Row = -1;     // Kachel-Zeile (0-basiert)
        public double X;         // nur noch für Übernahme alter INI-Dateien
        public double Y;
        public bool RunAsAdmin;
        public FolderMode FolderMode = FolderMode.Default;
        public string TileColor = "";   // Hintergrundfarbe der Kachel (#RRGGBB), leer = keine

        /// <summary>Kopie aller Angaben (für Duplizieren mit Shift).</summary>
        public ItemData Clone() => (ItemData)MemberwiseClone();
    }

    public class TabData
    {
        public string Name = "Neu";
        public string Color = "";   // Farbe des Tab-Reiters (#RRGGBB), leer = Standard
        public List<ItemData> Items = new List<ItemData>();
    }

    public class EdgeZone
    {
        public bool Enabled;
        public double From;   // Prozent der Randlänge (0..100)
        public double To;

        public EdgeZone(bool enabled, double from, double to)
        {
            Enabled = enabled; From = from; To = to;
        }
    }

    public class AppSettings
    {
        // Darstellung
        public double FontSize = 12;
        public double TabFontSize = 15;
        public int IconSize = 40;
        public int CellWidth = 78;
        public int MaxLabelLines = 3;
        public int Columns = 22;
        public int Rows = 2;
        public string Language = "";       // Name der Sprachdatei in "Languages" (leer = nach Windows-Sprache)
        public double DialogScale = 1.5;     // Vergrösserung der Dialogfenster (1.0 = normal)
        public bool ShowTiles = false;   // Kacheln immer dezent anzeigen (sonst nur beim Ziehen)
        public bool Debug = false;
        public string CustomColors = "";   // eigene Farben im Farbwähler (#RRGGBB, durch Komma getrennt)       // Diagnose-Protokoll DragLaunch_trace.log

        // Farben (#RRGGBB oder #AARRGGBB)
        public string FrameColor = "#D80000";
        public string BackgroundColor = "#050C05";
        public string TabTextColor = "#FFD24A";
        public string ActiveTabColor = "#3A0000";
        public string ActiveTabTextColor = "#FFFFFF";
        public string LabelColor = "#FFFFFF";

        // Position (Abstand in Pixel bei 100 % Skalierung)
        public double OffsetLeft = 6;
        public double OffsetBottom = 6;
        public bool UseWorkArea = false;      // false = ganzer Bildschirm, true = ohne Taskleiste
        public bool PositionLocked = false;
        public bool ShowInTaskbar = false;

        // Aktivierung über Bildschirmrand
        public int ActivationDelayMs = 400;
        public int EdgeThickness = 2;
        public bool ActivateFocus = false;    // Tastaturfokus übernehmen
        public bool IgnoreFullscreen = true;  // nicht über Vollbild-Apps (Spiele, VR, Videos)
        public string EdgePauseApps = "";     // Randzone aus, solange eines dieser Programme läuft (Namen ohne .exe, Komma-getrennt)
        public bool BackAfterLaunch = true;
        public string Sound = "";
        public double ContextFontSize = 14;   // Schriftgrösse der Rechtsklick- und Hauptmenüs            // .wav im Ordner "Sound", leer = kein Sound   // nach dem Starten eines Elements hinter alle Fenster
        public EdgeZone Bottom = new EdgeZone(true, 0, 100);
        public EdgeZone Top = new EdgeZone(false, 0, 100);
        public EdgeZone Left = new EdgeZone(false, 0, 100);
        public EdgeZone Right = new EdgeZone(false, 0, 100);

        // Ordner
        public bool FolderOpensMenu = true;
        public double MenuFontSize = 14;      // Schriftgrösse der Ordnerliste
        public int MenuIconSize = 24;         // Icongrösse der Ordnerliste   // Standard für Ordner: true = Pulldown-Liste, false = öffnen

        public int ActiveTab = 0;

        // Abgeleitete Grössen
        public double LineHeight => System.Math.Ceiling(FontSize * 1.3);
        public double CellHeight => IconSize + 10 + MaxLabelLines * LineHeight + 6;
    }
}
