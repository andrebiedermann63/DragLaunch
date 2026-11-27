using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DragLaunch
{
    /// <summary>
    /// Sprachverwaltung. Texte stehen in "Languages\&lt;Sprache&gt;.ini" im App-Ordner (Abschnitt [Strings], key=Text).
    /// Deutsch.ini und English.ini werden aus den eingebauten Texten erzeugt und bei neuen Versionen
    /// um fehlende Einträge ergänzt (eigene Änderungen bleiben erhalten). Weitere Sprachen: einfach eine
    /// weitere .ini in den Ordner legen. Fehlt ein Text, wird der eingebaute (sonst der deutsche) verwendet.
    /// Zeilenumbruch im Text: \n
    /// </summary>
    public static class L
    {
        public static string Folder => Path.Combine(ConfigStore.AppDir, "Languages");
        public static string Current { get; private set; } = "Deutsch";

        private static Dictionary<string, string> _file = new Dictionary<string, string>();
        private static Dictionary<string, string> _builtin;   // erst in Load gesetzt (Reihenfolge der statischen Felder!)

        public static string T(string key)
        {
            if (_file.TryGetValue(key, out string v) && v.Length > 0) return v;
            if (_builtin != null && _builtin.TryGetValue(key, out v)) return v;
            if (De.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string F(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch (FormatException) { return T(key); }
        }

        /// <summary>Sprache aus den Einstellungen; leer = nach Windows-Sprache (Deutsch oder English).</summary>
        public static string DefaultName =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Deutsch" : "English";

        public static List<string> Available()
        {
            try
            {
                return Directory.GetFiles(Folder, "*.ini")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch { return new List<string> { "Deutsch", "English" }; }
        }

        public static void Load(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = DefaultName;
            EnsureFiles();
            Current = name;
            _builtin = name.Equals("English", StringComparison.OrdinalIgnoreCase) ? En
                     : name.Equals("Deutsch", StringComparison.OrdinalIgnoreCase) ? De
                     : En;   // unbekannte Sprache: fehlende Texte lieber englisch
            _file = Read(Path.Combine(Folder, name + ".ini"));
        }

        /// <summary>Deutsch.ini / English.ini anlegen bzw. um neue Einträge ergänzen.</summary>
        public static void EnsureFiles()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                Merge("Deutsch", De, "; DragLaunch Sprachdatei - Deutsch");
                Merge("English", En, "; DragLaunch language file - English");
            }
            catch { /* App-Ordner schreibgeschützt: dann eben nur eingebaute Texte */ }
        }

        private static void Merge(string name, Dictionary<string, string> builtin, string header)
        {
            string path = Path.Combine(Folder, name + ".ini");
            Dictionary<string, string> existing = File.Exists(path) ? Read(path) : new Dictionary<string, string>();
            if (File.Exists(path) && builtin.Keys.All(existing.ContainsKey)) return;

            var sb = new StringBuilder();
            sb.AppendLine(header);
            sb.AppendLine("; Zeilenumbruch / line break: \\n   Platzhalter / placeholders: {0}, {1}");
            sb.AppendLine("[Strings]");
            foreach (var kv in Texts)
            {
                string val = existing.TryGetValue(kv.Key, out string v) ? v : builtin[kv.Key];
                sb.Append(kv.Key).Append('=').AppendLine(Escape(val));
            }
            // Eigene Zusatzschlüssel nicht verlieren
            foreach (var kv in existing.Where(k => !builtin.ContainsKey(k.Key)))
                sb.Append(kv.Key).Append('=').AppendLine(Escape(kv.Value));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static Dictionary<string, string> Read(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return d;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.TrimEnd();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("[")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    d[line.Substring(0, eq).Trim()] = Unescape(line.Substring(eq + 1));
                }
            }
            catch { }
            return d;
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");

        private static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ================================================================ Texte (Schlüssel, Deutsch, English)

        private static readonly List<(string Key, string De, string En)> Raw = new List<(string, string, string)>
        {
            // Allgemein
            ("ok", "OK", "OK"),
            ("cancel", "Abbrechen", "Cancel"),
            ("close", "Schliessen", "Close"),
            ("new", "Neu", "New"),

            // Start / Fehler
            ("app.hung", "DragLaunch läuft bereits, reagiert aber nicht.\n\nDie hängende Instanz beenden und DragLaunch neu starten?",
                         "DragLaunch is already running but not responding.\n\nEnd the frozen instance and restart DragLaunch?"),
            ("app.killfailed", "Die andere Instanz konnte nicht beendet werden.\nBitte im Task-Manager DragLaunch.exe beenden.",
                               "The other instance could not be ended.\nPlease end DragLaunch.exe in Task Manager."),
            ("app.error", "Unerwarteter Fehler:\n\n{0}\n\nDetails stehen in DragLaunch.log im App-Ordner.",
                          "Unexpected error:\n\n{0}\n\nDetails can be found in DragLaunch.log in the app folder."),
            ("cfg.readfail", "INI-Datei konnte nicht gelesen werden:\n{0}", "The INI file could not be read:\n{0}"),
            ("cfg.savefail", "Einstellungen konnten nicht gespeichert werden:\n{0}\n\n{1}\n\nTipp: DragLaunch nicht unter \"C:\\Programme\" ablegen, sondern z.B. in C:\\Tools\\DragLaunch.",
                             "Settings could not be saved:\n{0}\n\n{1}\n\nTip: do not place DragLaunch under \"C:\\Program Files\", use e.g. C:\\Tools\\DragLaunch instead."),

            // Icons
            ("icon.notinfile", "Icon {0} nicht in {1} gefunden.", "Icon {0} not found in {1}."),
            ("icon.filemissing", "Icon-Datei nicht gefunden", "Icon file not found"),
            ("icon.unreadable", "Aus dieser Datei konnte kein Icon gelesen werden.", "No icon could be read from this file."),

            // Starten / Ordnerliste
            ("launch.notfound", "Nicht gefunden:\n{0}", "Not found:\n{0}"),
            ("launch.unreachable", "{0} ist nicht erreichbar.", "{0} is not reachable."),
            ("launch.failed", "Konnte nicht gestartet werden:\n{0}\n\n{1}", "Could not be started:\n{0}\n\n{1}"),
            ("folder.open", "Ordner öffnen", "Open folder"),
            ("folder.noaccess", "(kein Zugriff: {0})", "(no access: {0})"),
            ("folder.empty", "(leer)", "(empty)"),
            ("folder.more", "… weitere Einträge (Ordner öffnen)", "… more entries (open folder)"),

            // Hauptmenü
            ("main.settings", "Einstellungen…", "Settings…"),
            ("main.wintools", "Windows-Funktion hinzufügen…", "Add Windows feature…"),
            ("main.newtab", "Neuer Tab…", "New tab…"),
            ("main.compact", "Icons zusammenrücken (Lücken schliessen)", "Compact icons (close gaps)"),
            ("main.lock", "Position sperren", "Lock position"),
            ("main.backup", "Backup der Einstellungen erstellen", "Create settings backup"),
            ("main.restore", "Backup wiederherstellen…", "Restore backup…"),
            ("main.autostart", "Mit Windows starten (Autostart)", "Start with Windows (autostart)"),
            ("main.autostartfail", "Autostart konnte nicht geändert werden:\n{0}", "Autostart could not be changed:\n{0}"),
            ("main.appfolder", "App-Ordner öffnen", "Open app folder"),
            ("main.about", "Über DragLaunch…", "About DragLaunch…"),
            ("main.exit", "Beenden", "Exit"),
            ("tip.menu", "Menü", "Menu"),
            ("tip.cols", "Spalten hinzufügen/entfernen", "Add/remove columns"),
            ("tip.rows", "Zeilen hinzufügen/entfernen", "Add/remove rows"),

            // Tabs
            ("tab.rename", "Umbenennen…", "Rename…"),
            ("tab.newright", "Neuer Tab rechts davon…", "New tab to the right…"),
            ("tab.left", "Nach links verschieben", "Move left"),
            ("tab.right", "Nach rechts verschieben", "Move right"),
            ("tab.delete", "Tab löschen…", "Delete tab…"),
            ("tab.color", "Tabfarbe…", "Tab colour…"),
            ("tab.colorremove", "Tabfarbe entfernen", "Remove tab colour"),
            ("tab.renametitle", "Tab umbenennen", "Rename tab"),
            ("tab.nameprompt", "Name des Tabs:", "Tab name:"),
            ("tab.newtitle", "Neuer Tab", "New tab"),
            ("tab.newprompt", "Name des neuen Tabs:", "Name of the new tab:"),
            ("tab.delitems", "Tab \"{0}\" mit {1} Icon(s) löschen?\n(Die Dateien selbst bleiben erhalten.)",
                             "Delete tab \"{0}\" with {1} icon(s)?\n(The files themselves are kept.)"),
            ("tab.delempty", "Tab \"{0}\" löschen?", "Delete tab \"{0}\"?"),

            // Icon-Menü
            ("item.showlist", "Inhalt als Liste anzeigen", "Show contents as list"),
            ("item.clickheader", "Bei Klick auf dieses Icon:", "When this icon is clicked:"),
            ("item.clicklist", "   Liste anzeigen (Pulldown)", "   Show list (pull-down)"),
            ("item.clickopen", "   Ordner öffnen", "   Open folder"),
            ("item.open", "Öffnen", "Open"),
            ("item.runadmin", "Als Administrator ausführen", "Run as administrator"),
            ("item.location", "Dateispeicherort öffnen", "Open file location"),
            ("item.properties", "Eigenschaften…", "Properties…"),
            ("item.remove", "Entfernen", "Remove"),
            ("item.favicon", "Favicon neu laden", "Reload favicon"),
            ("item.color", "Kachelfarbe…", "Tile colour…"),
            ("item.colorremove", "Kachelfarbe entfernen", "Remove tile colour"),
            ("main.toback", "In den Hintergrund (Esc)", "Send to background (Esc)"),
            ("s.contextfont", "Schriftgrösse Menüs", "Menu font size"),
            ("favicon.none", "Für diese Webseite wurde kein Favicon gefunden.", "No favicon was found for this website."),

            // Backup
            ("backup.created", "Backup erstellt:\n{0}", "Backup created:\n{0}"),
            ("backup.failed", "Backup fehlgeschlagen:\n{0}", "Backup failed:\n{0}"),
            ("bw.title", "DragLaunch – Backups", "DragLaunch – Backups"),
            ("bw.new", "Neues Backup", "New backup"),
            ("bw.restore", "Wiederherstellen", "Restore"),
            ("bw.delete", "Löschen", "Delete"),
            ("bw.info", "Backups liegen im Unterordner \"Backup\" des App-Ordners (Name: DragLaunch_JJJJMMTT + Buchstabe).",
                        "Backups are stored in the \"Backup\" subfolder of the app folder (name: DragLaunch_YYYYMMDD + letter)."),
            ("bw.restoreq", "\"{0}\" wiederherstellen?\n\nDie aktuellen Einstellungen werden ersetzt.\nSoll der aktuelle Stand vorher als Backup gesichert werden?",
                            "Restore \"{0}\"?\n\nThe current settings will be replaced.\nBack up the current state first?"),
            ("bw.restoretitle", "Backup wiederherstellen", "Restore backup"),
            ("bw.restored", "Einstellungen wurden wiederhergestellt.", "Settings have been restored."),
            ("bw.restorefail", "Wiederherstellen fehlgeschlagen:\n{0}", "Restore failed:\n{0}"),
            ("bw.deleteq", "Backup \"{0}\" endgültig löschen?", "Permanently delete backup \"{0}\"?"),

            // Eigenschaften
            ("props.title", "Eigenschaften – {0}", "Properties – {0}"),
            ("props.label", "Beschriftung", "Label"),
            ("props.path", "Pfad / URL", "Path / URL"),
            ("props.file", "Datei…", "File…"),
            ("props.folder", "Ordner…", "Folder…"),
            ("props.pathhint", "Auch Windows-Adressen wie ms-settings:windowsupdate, shell:startup oder steam://… sind möglich.",
                               "Windows addresses such as ms-settings:windowsupdate, shell:startup or steam://… also work."),
            ("props.args", "Argumente", "Arguments"),
            ("props.workdir", "Arbeitsordner", "Working folder"),
            ("props.icon", "Eigenes Icon", "Custom icon"),
            ("props.iconhint", "Leer = Windows-Icon der Datei. Auch \"datei,nummer\" wie C:\\Windows\\System32\\shell32.dll,12. Wird in den Ordner \"Icons\" kopiert.",
                               "Empty = the file's Windows icon. \"file,number\" such as C:\\Windows\\System32\\shell32.dll,12 also works. Copied to the \"Icons\" folder."),
            ("props.fmdefault", "Standard (aus den Einstellungen)", "Default (from settings)"),
            ("props.fmlist", "Inhalt als Liste (Pulldown)", "Contents as list (pull-down)"),
            ("props.folderclick", "Klick auf Ordner", "Folder click"),
            ("props.needpath", "Bitte einen Pfad oder eine URL angeben.", "Please enter a path or URL."),
            ("props.iconfail", "Das Icon konnte nicht in den Icons-Ordner übernommen werden:\n{0}", "The icon could not be copied to the Icons folder:\n{0}"),
            ("filter.all", "Alle Dateien|*.*", "All files|*.*"),
            ("filter.icons", "Icons und Bilder|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|Alle Dateien|*.*",
                             "Icons and images|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|All files|*.*"),

            // About
            ("about.title", "Über DragLaunch", "About DragLaunch"),
            ("about.version", "Version {0}", "Version {0}"),
            ("about.built", "Erstellt am {0}", "Built on {0}"),
            ("about.desc", "Startleiste mit frei belegbaren Kacheln und Tabs für Windows 11.", "Launcher with freely arrangeable tiles and tabs for Windows 11."),
            ("about.appfolder", "App-Ordner:", "App folder:"),
            ("about.settings", "Einstellungen:", "Settings:"),

            // Einstellungen
            ("s.title", "DragLaunch – Einstellungen ({0})", "DragLaunch – Settings ({0})"),
            ("s.footer", "Änderungen werden sofort übernommen und in DragLaunch.ini gespeichert.", "Changes are applied immediately and saved to DragLaunch.ini."),
            ("s.language", "Sprache", "Language"),
            ("s.languagenote", "Weitere Sprachen: eine eigene .ini-Datei in den Ordner \"Languages\" im App-Ordner legen.",
                               "More languages: put your own .ini file into the \"Languages\" folder in the app folder."),
            ("s.appearance", "Darstellung", "Appearance"),
            ("s.fontsize", "Schriftgrösse Beschriftung", "Label font size"),
            ("s.tabfontsize", "Schriftgrösse Tabs", "Tab font size"),
            ("s.iconsize", "Icongrösse", "Icon size"),
            ("s.cellwidth", "Zellenbreite", "Cell width"),
            ("s.maxlines", "Max. Zeilen unter dem Icon", "Max. lines below icon"),
            ("s.notext", "(0 = ohne Text)", "(0 = no text)"),
            ("s.columns", "Spalten", "Columns"),
            ("s.rows", "Zeilen", "Rows"),
            ("s.showtiles", "Kacheln immer anzeigen", "Always show tiles"),
            ("s.dialogscale", "Grösse der Dialogfenster", "Dialog window size"),
            ("s.factor", "Faktor (1.5 = 150 %)", "factor (1.5 = 150 %)"),
            ("s.gridnote", "Spalten und Zeilen lassen sich auch kachelweise am rechten bzw. oberen Fensterrand ziehen. Kacheln mit Icons werden dabei nie abgeschnitten.",
                           "Columns and rows can also be dragged tile by tile at the right or top window edge. Tiles with icons are never cut off."),
            ("s.sound", "Sound", "Sound"),
            ("s.nosound", "(kein Sound)", "(no sound)"),
            ("s.soundnote", "Erklingt beim Klick auf ein Icon, beim Ablegen und beim Tab-Wechsel. Eigene Sounds: .wav-Dateien in den Ordner \"Sound\" im App-Ordner legen.",
                            "Plays when clicking an icon, dropping and switching tabs. Own sounds: put .wav files into the \"Sound\" folder in the app folder."),
            ("s.colors", "Farben", "Colours"),
            ("s.colornote", "Auf das Farbfeld klicken öffnet den Farbwähler. Alternativ den Wert als #RRGGBB eintippen.",
                            "Click the colour swatch to open the colour picker, or type the value as #RRGGBB."),
            ("s.c.frame", "Rahmen / Tableiste", "Frame / tab bar"),
            ("s.c.background", "Hintergrund", "Background"),
            ("s.c.tabtext", "Tab-Text", "Tab text"),
            ("s.c.activetab", "Aktiver Tab", "Active tab"),
            ("s.c.activetabtext", "Aktiver Tab Text", "Active tab text"),
            ("s.c.label", "Beschriftung", "Labels"),
            ("s.pickcolor", "Klicken, um eine Farbe zu wählen", "Click to choose a colour"),
            ("s.position", "Position", "Position"),
            ("s.offsetleft", "Abstand vom linken Rand", "Distance from left edge"),
            ("s.offsetbottom", "Abstand vom unteren Rand", "Distance from bottom edge"),
            ("s.measured", "Gemessen ab", "Measured from"),
            ("s.screenedge", "Bildschirmrand", "Screen edge"),
            ("s.workarea", "Arbeitsbereich (über der Taskleiste)", "Work area (above the taskbar)"),
            ("s.lock", "Position sperren", "Lock position"),
            ("s.taskbar", "In der Taskleiste anzeigen", "Show in taskbar"),
            ("s.movenote", "Verschieben: am roten Rahmen ziehen oder Strg + Ziehen auf einer leeren Stelle.",
                           "To move: drag the red frame, or Ctrl + drag on an empty spot."),
            ("s.edge", "Einblenden über den Bildschirmrand", "Show via screen edge"),
            ("s.delay", "Verzögerung", "Delay"),
            ("s.delayunit", "ms (400 = 0,4 s)", "ms (400 = 0.4 s)"),
            ("s.edgewidth", "Breite der Randzone", "Edge zone width"),
            ("s.e.bottom", "Unterer Rand", "Bottom edge"),
            ("s.e.top", "Oberer Rand", "Top edge"),
            ("s.e.left", "Linker Rand", "Left edge"),
            ("s.e.right", "Rechter Rand", "Right edge"),
            ("s.e.active", "aktiv", "active"),
            ("s.e.from", "von", "from"),
            ("s.e.to", "%  bis", "%  to"),
            ("s.edgenote", "Von/bis in Prozent der Randlänge (links→rechts bzw. oben→unten). Beispiel: 0–30 = nur das linke Drittel.",
                           "From/to as a percentage of the edge length (left→right or top→bottom). Example: 0–30 = left third only."),
            ("s.focus", "Tastaturfokus übernehmen", "Take keyboard focus"),
            ("s.pauseapps", "Nicht einblenden, solange diese Programme laufen", "Do not show while these programs are running"),
            ("s.pauseappsnote", "Programmnamen ohne .exe, durch Komma getrennt, z.B. VPinballX_GL64, VPinballX64. Gut für VR und Spiele, bei denen die Maus an den Rand geraten kann.",
                                "Program names without .exe, comma-separated, e.g. VPinballX_GL64, VPinballX64. Useful for VR and games where the mouse may drift to the edge."),
            ("s.backafterlaunch", "Nach dem Starten in den Hintergrund", "Move to the background after launching"),
            ("s.fullscreen", "Nicht über Vollbild-Apps einblenden", "Do not show over full-screen apps"),
            ("s.folders", "Ordner", "Folders"),
            ("s.folderclick", "Klick auf Ordner (Standard)", "Folder click (default)"),
            ("s.foldernote", "Gilt für Ordner ohne eigene Wahl. Pro Ordner-Icon: Rechtsklick → \"Bei Klick auf dieses Icon\".",
                             "Applies to folders without their own choice. Per folder icon: right-click → \"When this icon is clicked\"."),
            ("s.menufont", "Schriftgrösse Ordnerliste", "Folder list font size"),
            ("s.menuicon", "Icongrösse Ordnerliste", "Folder list icon size"),
            ("s.backup", "Backup", "Backup"),
            ("s.backupcreate", "Backup erstellen", "Create backup"),
            ("s.backuprestore", "Wiederherstellen…", "Restore…"),
            ("s.backuprow", "Einstellungen sichern", "Back up settings"),

            // Windows-Funktion hinzufügen
            ("wtw.title", "Windows-Funktion hinzufügen", "Add Windows feature"),
            ("wtw.add", "Hinzufügen", "Add"),
            ("wtw.custom", "Eigener Eintrag…", "Custom entry…"),
            ("wtw.search", "Suchen:", "Search:"),
            ("wtw.hint", "Gruppen per Klick auf- und zuklappen. Mehrere mit Strg oder Shift auswählen. Doppelklick fügt direkt hinzu. Unter \"Steam-Spiele\" stehen alle installierten Spiele, unter \"Installierte Apps\" alle Programme des Startmenüs.",
                         "Click a group to expand or collapse it. Select several with Ctrl or Shift. Double-click adds directly. \"Steam games\" lists all installed games, \"Installed apps\" all Start menu programs."),
            ("grp.settings", "Einstellungen", "Settings"),
            ("grp.cpl", "Systemsteuerung", "Control Panel"),
            ("grp.admin", "Verwaltung", "Administration"),
            ("grp.tools", "Werkzeuge", "Tools"),
            ("grp.folders", "Ordner", "Folders"),
            ("grp.mmc", "Verwaltungskonsolen (MMC)", "Management consoles (MMC)"),
            ("grp.steam", "Steam-Spiele", "Steam games"),
            ("grp.apps", "Installierte Apps", "Installed apps"),

            ("wt.update", "Windows Update", "Windows Update"),
            ("wt.settings", "Einstellungen", "Settings"),
            ("wt.display", "Anzeige / Bildschirm", "Display"),
            ("wt.graphics", "Grafikeinstellungen (GPU pro App)", "Graphics settings (GPU per app)"),
            ("wt.sound", "Sound", "Sound"),
            ("wt.mixer", "Lautstärkemixer", "Volume mixer"),
            ("wt.bluetooth", "Bluetooth und Geräte", "Bluetooth & devices"),
            ("wt.apps", "Installierte Apps", "Installed apps"),
            ("wt.startupapps", "Autostart-Apps", "Startup apps"),
            ("wt.power", "Energie und Akku", "Power & battery"),
            ("wt.network", "Netzwerk und Internet", "Network & internet"),
            ("wt.gamemode", "Spielmodus", "Game Mode"),
            ("wt.about", "Info (Gerätedaten)", "About (device specs)"),
            ("wt.storage", "Speicher", "Storage"),
            ("wt.storagesense", "Speicheroptimierung", "Storage Sense"),
            ("wt.storagerec", "Bereinigungsempfehlungen", "Cleanup recommendations"),
            ("wt.disks", "Datenträger und Volumes", "Disks & volumes"),
            ("wt.notifications", "Benachrichtigungen", "Notifications"),
            ("wt.focus", "Fokus / Nicht stören", "Focus / Do not disturb"),
            ("wt.multitasking", "Multitasking", "Multitasking"),
            ("wt.clipboard", "Zwischenablage", "Clipboard"),
            ("wt.nightlight", "Nachtmodus", "Night light"),
            ("wt.displayadv", "Erweiterte Anzeigeeinstellungen", "Advanced display"),
            ("wt.remotedesktop", "Remotedesktop", "Remote Desktop"),
            ("wt.recovery", "Wiederherstellung", "Recovery"),
            ("wt.activation", "Aktivierung", "Activation"),
            ("wt.troubleshoot", "Problembehandlung", "Troubleshoot"),
            ("wt.developers", "Für Entwickler", "For developers"),
            ("wt.printers", "Drucker und Scanner", "Printers & scanners"),
            ("wt.mousesettings", "Maus", "Mouse"),
            ("wt.typing", "Eingabe", "Typing"),
            ("wt.usb", "USB", "USB"),
            ("wt.autoplay", "Automatische Wiedergabe", "AutoPlay"),
            ("wt.defaultapps", "Standard-Apps", "Default apps"),
            ("wt.optionalfeatures", "Optionale Features", "Optional features"),
            ("wt.wifi", "WLAN", "Wi-Fi"),
            ("wt.ethernet", "Ethernet", "Ethernet"),
            ("wt.vpn", "VPN", "VPN"),
            ("wt.proxy", "Proxy", "Proxy"),
            ("wt.netadvanced", "Erweiterte Netzwerkeinstellungen", "Advanced network settings"),
            ("wt.background", "Hintergrund", "Background"),
            ("wt.colors", "Farben", "Colours"),
            ("wt.themes", "Designs", "Themes"),
            ("wt.lockscreen", "Sperrbildschirm", "Lock screen"),
            ("wt.startmenu", "Start (Startmenü)", "Start menu"),
            ("wt.taskbar", "Taskleiste", "Taskbar"),
            ("wt.fonts", "Schriftarten", "Fonts"),
            ("wt.yourinfo", "Ihre Infos (Konto)", "Your info (account)"),
            ("wt.signin", "Anmeldeoptionen", "Sign-in options"),
            ("wt.otherusers", "Andere Benutzer", "Other users"),
            ("wt.datetime", "Datum und Uhrzeit", "Date & time"),
            ("wt.region", "Region (Formate)", "Region (formats)"),
            ("wt.language", "Sprache", "Language"),
            ("wt.gamebar", "Xbox Game Bar", "Xbox Game Bar"),
            ("wt.captures", "Aufnahmen (Spiele)", "Captures (gaming)"),
            ("wt.textsize", "Textgrösse (Barrierefreiheit)", "Text size (accessibility)"),
            ("wt.mousepointer", "Mauszeiger und Toucheingabe", "Mouse pointer and touch"),
            ("wt.privacy", "Datenschutz und Sicherheit", "Privacy & security"),
            ("wt.camera", "Kamerazugriff", "Camera access"),
            ("wt.microphone", "Mikrofonzugriff", "Microphone access"),
            ("wt.updatehistory", "Updateverlauf", "Update history"),
            ("wt.updateoptions", "Erweiterte Update-Optionen", "Advanced update options"),
            ("wt.optionalupdates", "Optionale Updates", "Optional updates"),
            ("wt.delivery", "Übermittlungsoptimierung", "Delivery Optimization"),
            ("wt.cleanmgr", "Datenträgerbereinigung", "Disk Cleanup"),
            ("wtw.expandall", "Alle aufklappen", "Expand all"),
            ("wtw.collapseall", "Alle zuklappen", "Collapse all"),
            ("sel.count", "{0} Kacheln ausgewählt", "{0} tiles selected"),
            ("sel.startall", "Alle starten / öffnen", "Open all"),
            ("sel.moveto", "In Tab verschieben", "Move to tab"),
            ("sel.copyto", "In Tab kopieren", "Copy to tab"),
            ("sel.clear", "Auswahl aufheben (Esc)", "Clear selection (Esc)"),
            ("sel.remove", "Entfernen ({0} Kacheln)", "Remove ({0} tiles)"),
            ("sel.all", "Alle Kacheln auswählen (Strg+A)", "Select all tiles (Ctrl+A)"),
            ("s.edgepreview", "Randzonen am Bildschirm anzeigen (solange dieses Fenster offen ist)", "Show edge zones on screen (while this window is open)"),
            ("wt.security", "Windows-Sicherheit", "Windows Security"),
            ("wt.joy", "Gamecontroller", "Game controllers"),
            ("wt.soundclassic", "Sound (klassisch)", "Sound (classic)"),
            ("wt.mouse", "Maus", "Mouse"),
            ("wt.powercfg", "Energieoptionen", "Power options"),
            ("wt.ncpa", "Netzwerkverbindungen", "Network connections"),
            ("wt.appwiz", "Programme und Features", "Programs and Features"),
            ("wt.sysdm", "Systemeigenschaften", "System properties"),
            ("wt.envvars", "Umgebungsvariablen", "Environment variables"),
            ("wt.timedate", "Datum und Uhrzeit", "Date and time"),
            ("wt.control", "Systemsteuerung", "Control Panel"),
            ("wt.taskmgr", "Task-Manager", "Task Manager"),
            ("wt.resmon", "Ressourcenmonitor", "Resource Monitor"),
            ("wt.regedit", "Registrierungs-Editor", "Registry Editor"),
            ("wt.msinfo", "Systeminformationen", "System Information"),
            ("wt.cmd", "Eingabeaufforderung", "Command Prompt"),
            ("wt.powershell", "PowerShell", "PowerShell"),
            ("wt.osk", "Bildschirmtastatur", "On-Screen Keyboard"),
            ("wt.calc", "Rechner", "Calculator"),
            ("wt.thispc", "Dieser PC", "This PC"),
            ("wt.netplaces", "Netzwerk", "Network"),
            ("wt.recycle", "Papierkorb", "Recycle Bin"),
            ("wt.startupfolder", "Autostart-Ordner", "Startup folder"),
            ("wt.downloads", "Downloads", "Downloads"),

            ("msc.azman", "Autorisierungs-Manager", "Authorization Manager"),
            ("msc.certlm", "Zertifikate – Lokaler Computer", "Certificates – Local Computer"),
            ("msc.certmgr", "Zertifikate – Aktueller Benutzer", "Certificates – Current User"),
            ("msc.comexp", "Komponentendienste", "Component Services"),
            ("msc.compmgmt", "Computerverwaltung", "Computer Management"),
            ("msc.devmgmt", "Geräte-Manager", "Device Manager"),
            ("msc.diskmgmt", "Datenträgerverwaltung", "Disk Management"),
            ("msc.eventvwr", "Ereignisanzeige", "Event Viewer"),
            ("msc.fsmgmt", "Freigegebene Ordner", "Shared Folders"),
            ("msc.gpedit", "Editor für lokale Gruppenrichtlinien", "Local Group Policy Editor"),
            ("msc.gpmc", "Gruppenrichtlinienverwaltung", "Group Policy Management"),
            ("msc.lusrmgr", "Lokale Benutzer und Gruppen", "Local Users and Groups"),
            ("msc.perfmon", "Leistungsüberwachung", "Performance Monitor"),
            ("msc.printmanagement", "Druckverwaltung", "Print Management"),
            ("msc.rsop", "Richtlinienergebnissatz", "Resultant Set of Policy"),
            ("msc.secpol", "Lokale Sicherheitsrichtlinie", "Local Security Policy"),
            ("msc.services", "Dienste", "Services"),
            ("msc.taskschd", "Aufgabenplanung", "Task Scheduler"),
            ("msc.tpm", "TPM-Verwaltung", "TPM Management"),
            ("msc.wf", "Windows Defender Firewall mit erweiterter Sicherheit", "Windows Defender Firewall with Advanced Security"),
            ("msc.wmimgmt", "WMI-Steuerung", "WMI Control"),
            ("msc.hdwwiz", "Hardware-Assistent", "Add Hardware Wizard"),
            ("msc.iscsicpl", "iSCSI-Initiator", "iSCSI Initiator"),
            ("msc.virtmgmt", "Hyper-V-Manager", "Hyper-V Manager"),
            ("msc.dfrgui", "Laufwerke optimieren", "Optimize Drives"),
            ("msc.devmoderunasuserconfig", "Gerätemodus: Als Benutzer ausführen", "Device mode: run as user"),
        };

        private static readonly Dictionary<string, string> De = Raw.ToDictionary(r => r.Key, r => r.De);
        private static readonly Dictionary<string, string> En = Raw.ToDictionary(r => r.Key, r => r.En);
        private static IEnumerable<KeyValuePair<string, string>> Texts => Raw.Select(r => new KeyValuePair<string, string>(r.Key, r.De));
    }
}
