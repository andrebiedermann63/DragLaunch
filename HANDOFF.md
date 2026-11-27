# DragLaunch – Übergabe (HANDOFF)

**Stand:** Version 24 · 26.11.2026
**Zweck:** Damit ein neuer Chat ohne Vorgeschichte nahtlos weiterarbeiten kann. Bei **jeder** neuen Version aktualisieren.

---

## 1. Was ist DragLaunch?

Startleiste für Windows 11 im Stil des Mac-Klassikers **DragThing**. Ein rotes Fenster mit Tabs oben und darunter einem Raster aus **Kacheln**. Programme, Ordner, Dateien, Verknüpfungen und Weblinks werden hineingezogen und per Klick gestartet. Ersetzt beim Benutzer *Winstep Nexus/Tabs*: Dort lassen sich Icons nicht frei platzieren.

Der Benutzer schreibt Deutsch (Schweiz, also „ss“ statt „ß“). Er baut und testet selbst auf Windows 11, mit 4K-Monitor und 100 % Skalierung. Seine Themen sind u. a. VR-Pinball (Visual Pinball X), Steam/VR, Garmin/GPX und Elektronik (Arduino, KiCad, ESP32).

## 2. Funktionsumfang (alles umgesetzt)

**Kacheln und Tabs**
- Feste Kacheln, Lücken erlaubt. Beliebig viele Tabs (umbenennen, verschieben, löschen über Rechtsklick).
- **Tabfarbe** pro Tab (Rechtsklick → „Tabfarbe…“, `TabN.Color`). Der aktive Tab zeigt die Farbe dunkler (×0,6). Die Schrift wird bei zu wenig Kontrast automatisch schwarz oder weiss (`ReadableOn`, wie bei den Kacheln).
- Spalten und Zeilen kachelweise ändern: am **rechten** Rand für Spalten, am **oberen** Rand für Zeilen. Das Fenster bleibt links/unten verankert. Kacheln mit Icons werden nie abgeschnitten.
- Kachelraster wird beim Ziehen/Ändern dezent eingeblendet. Option „Kacheln immer anzeigen“.
- **Kachelfarbe** pro Icon (Rechtsklick → „Kachelfarbe…“ / „Kachelfarbe entfernen“). Wird in `ItemN.Color` gespeichert und beim Verschieben und Duplizieren mitgenommen (Feld `TileColor` in `ItemData`). Die Beschriftung bleibt in der eingestellten Farbe, solange der Kontrast nach WCAG mindestens 3:1 beträgt. Sonst wird automatisch Schwarz oder Weiss verwendet (`LabelBrushFor`).

**Drag & Drop**
- Reinziehen aus Explorer und Browser (FileDrop, URL). Das Icon landet in der Kachel unter der Maus, sonst in der nächsten freien.
- Icon ziehen = verschieben. Auf eine belegte Kachel = Plätze tauschen. **Shift** = duplizieren (Badge „+“).
- **Mehrfachauswahl** (`_sel`, nur im aktiven Tab, nicht gespeichert): Strg+Klick schaltet eine Kachel um (startet nicht), Strg+A wählt alle, Esc / Klick ins Leere / normaler Klick / Tabwechsel heben auf (ausser während eines Gruppen-Drags). Ausgewählte Kacheln: Rahmen in Tab-Schriftfarbe. Ziehen einer ausgewählten Kachel bewegt die Gruppe (`_group`, Zähl-Badge, Markierungen aller Ziel-Kacheln); Anordnung bleibt, Verschiebung wird ans Raster begrenzt (`GroupDelta`), im Weg liegende Kacheln weichen aus (`PlaceNear`). Shift = Gruppe kopieren, auf Tab loslassen = in den Tab. Rechtsklick → `ShowGroupMenu`: alle starten, Kachelfarbe / entfernen, in Tab verschieben/kopieren (`MoveOrCopyToTab`), Auswahl aufheben, entfernen (ohne Rückfrage).
- Über einem Tab halten = Tab wechselt. Auf einen Tab loslassen = dorthin verschieben.
- Dateien auf ein Programm-Icon = damit öffnen. Mit Shift stattdessen als Icon hinzufügen.
- `.url`- und `.lnk`-Verknüpfungen werden **aufgelöst** (Ziel, Argumente, Arbeitsordner, Icon), damit die Kachel unabhängig von der Verknüpfungsdatei ist. Bestehende Kacheln werden beim Start migriert.

**Einblenden**
- Das Fenster ist **nicht** topmost. Maus in einer Randzone des Hauptbildschirms halten: unten, oben, links oder rechts, jeweils mit Von/Bis in Prozent, Randbreite und Verzögerung (Standard 400 ms). Danach kommt das Fenster nach vorne, auch während eines Datei-Drags.
- Optional Fokus übernehmen. Standardmässig nicht über Vollbild-Apps.
- **Randzonen-Anzeige** (`EdgePreview.cs`): Solange die Einstellungen offen sind, zeigen halbtransparente rote Balken die aktiven Zonen direkt am Bildschirmrand (WinForms-Fenster, klickdurchlässig, ohne Fokus). Berechnung wie im `EdgeWatcher` (`GetSystemMetrics`), mindestens 8 px dick; aktualisiert bei jeder Änderung und bei Auflösungswechsel. Abschaltbar per Häkchen (nicht gespeichert).
- **Randzone pausieren:** `EdgePauseApps` (z.B. `VPinballX_GL64, VPinballX64`). Solange eines dieser Programme läuft, reagiert die Randzone nicht. Gedacht für VR, wo die Maus unbemerkt an den Rand gerät.
- **Nach dem Starten in den Hintergrund** (`BackAfterLaunch`, Standard an): Jeder erfolgreiche Start (Kachel, Ordnerliste, Explorer) löst `Launcher.Launched` aus, DragLaunch geht dann auf `HWND_BOTTOM`. Grund: VR-/Spiel-Fenster öffnen sich teils ohne Aktivierung und lagen sonst hinter DragLaunch.
- **Topmost-Wächter** (jede Sekunde): Hat das Fenster `WS_EX_TOPMOST`, wird es zurückgesetzt und einmal in `DragLaunch.log` notiert. Zusätzlich „In den Hintergrund“ im Menü und die **Esc**-Taste (`SendToBack`, `HWND_BOTTOM`).
- Ohne Fokus-Übernahme bleibt das vorherige Fenster aktiv. Deshalb beobachtet DragLaunch danach den nächsten Klick (`StartClickAwayWatch`): Bei einem Klick auf ein fremdes Fenster legt es sich direkt dahinter (`SetWindowPos(hwnd, fg)`). Klicks in DragLaunch beenden das Beobachten. Eigene Listen, Desktop und Taskleiste werden ignoriert.

**Position**
- Fester Abstand zum linken und unteren Rand. Bezug wahlweise Bildschirmrand oder Arbeitsbereich.
- Gerechnet in **physischen Pixeln** über `SetWindowPos`.
- Fenster verschieben: am Rahmen ziehen oder Strg+Ziehen auf einer leeren Stelle. Position sperrbar.

**Ordner**
- Klick öffnet den Ordner **oder** zeigt eine verschachtelte Pulldown-Liste. Die Wahl gilt global (Standard) oder pro Icon (Rechtsklick).
- In der Liste öffnet ein Klick auf einen Unterordner diesen.
- **Netzwerk (nur teilweise):** Netzwerk-Computer wie `\\SERVER` lassen sich reinziehen und öffnen. Laut Test des Benutzers (Version 14) funktionieren **nicht**: „Netzwerk“ selbst reinziehen, und die Wahl „Liste anzeigen“ für `\\SERVER`. Der Benutzer verzichtet darauf. **Ersatz:** „Netzwerk“ steht in „Windows-Funktion hinzufügen“ unter Ordner (`shell:NetworkPlacesFolder`). Der Code (`ReadShellIdList`, `IsShellContainer`, `FillShell`) ist vorhanden, aber ungeprüft und nicht weiter verfolgen, ausser der Benutzer wünscht es.
- Schrift- und Icongrösse der Liste sind einstellbar.

**„Windows-Funktion hinzufügen…“** – durchsuchbarer Dialog mit Mehrfachauswahl:
- Gruppen sind auf- und zuklappbar (Klick auf die Überschrift, Knöpfe „Alle aufklappen“/„Alle zuklappen“); anfangs alles zu, Zustand bis zum Beenden gemerkt. Die Suche zeigt Treffer auch in zugeklappten Gruppen.
- Einstellungsseiten (`ms-settings:`, rund 65 Seiten, alphabetisch, nur offiziell dokumentierte Adressen) und Systemsteuerung (`.cpl`); Datenträgerbereinigung (`cleanmgr.exe`) unter Verwaltung. „Temporäre Dateien“ hat keine dokumentierte Adresse – nächstliegend „Speicher“ (`ms-settings:storagesense`).
- Alle `.msc` aus System32, mit Klartext
- Werkzeuge und Shell-Ordner
- **Steam-Spiele**: `libraryfolders.vdf` + `appmanifest_*.acf`, Start über `steam://rungameid/ID`, Icon aus der Spiel-.exe
- **Installierte Apps** aus `shell:AppsFolder`, inkl. Store-Apps
- „Eigener Eintrag…“

**Icons**
- Echte Windows-Icons ohne weissen „Jumbo“-Rahmen.
- Eigene Icons (ICO/PNG/JPG/EXE/DLL, auch `datei,index`) werden in den Unterordner **`Icons`** kopiert, in der INI steht ein relativer Pfad.
- ICO: Es wird die beste Variante gewählt (höchste Farbtiefe, passende Grösse).
- **Favicons:** Für Webadressen (http/https) ohne Icon lädt `FaviconFetcher` im Hintergrund die Seite, wertet `<link rel="icon"/"apple-touch-icon">` aus (grösstes zuerst, ohne SVG), sonst `/apple-touch-icon.png` und `/favicon.ico`. Das Bild wird in `Icons\<host>.png/.ico` gespeichert. Anfragen gehen nur an die Seite selbst, nicht an fremde Dienste. Rechtsklick „Favicon neu laden“. Ohne Treffer bleibt das Browser-Icon der .url.

**Einstellungen**
- Fenster mit Live-Übernahme: Schriftgrössen (inkl. Menüs: `ContextFontSize`, Standard 14), Icongrösse, Zellenbreite, maximale Zeilen unter dem Icon, Spalten/Zeilen, Farben per Windows-Farbwähler (eigene Farben werden gespeichert), Position, Randzonen, Ordner, Dialoggrösse (Standard 1.5), Sprache.

**INI**
- Alle Einstellungen und Kacheln in `DragLaunch.ini` im App-Ordner, **sofort** geschrieben (atomar über eine .tmp-Datei).
- Backup und Restore: `Backup\DragLaunch_JJJJMMTTa.ini`, `…b` usw. Alte Backups direkt im App-Ordner werden beim Start dorthin verschoben (`MoveOldBackups`).

**Sound**
- Einstellung `Sound=` (Dateiname aus dem Unterordner `Sound`, leer = kein Sound). Gespielt beim Klick auf ein Icon, beim Ablegen (Icon, Kopie, Explorer-Drop, Datei auf Programm) und beim Tab-Wechsel. Abspielen asynchron über winmm `PlaySound`.
- Zwölf Sounds vom Benutzer (`01 Tick.wav` … `12 Tick.wav`, mit Leerzeichen wie beim Benutzer) liegen in `src/Sound` und werden beim Build nach `App\Sound` kopiert. Die früheren drei (Klick, Plopp, Tick) sind entfernt; ist einer davon in der INI gewählt und nicht mehr vorhanden, wird beim Laden `01 Tick.wav` gesetzt.

**Sprachen**
- `Languages\Deutsch.ini` und `Languages\English.ini`, umschaltbar in den Einstellungen.
- Weitere `.ini`-Dateien im Ordner erscheinen automatisch in der Auswahl.

**Sonstiges**
- Autostart über eine Verknüpfung im Autostart-Ordner (Menü).
- About-Fenster mit Version.
- Eine Instanz pro App-Ordner. Eine hängende Instanz wird erkannt und kann beendet werden.
- App-Icon „Start-Raster“: rotes 3×3-Raster mit goldenem Play-Knopf.

## 3. Projektstruktur

```
DragLaunch/
  build.bat            Baut nach .\App (dotnet build, OHNE Runtime-Identifier/NuGet); beendet laufendes DragLaunch und startet es neu
  README.md            Anleitung für den Benutzer
  HANDOFF.md           diese Datei
  PROMPT.md            Start-Prompt für einen neuen Chat
  CHANGELOG-DE.md      Versionshistorie für Benutzer (Deutsch)
  INSTALL-DE.md        Installation und Updates für Benutzer (Deutsch)
  INSTALL-EN.md        Installation und Updates für Benutzer (Englisch)
  LICENSE              PolyForm Noncommercial 1.0.0 (Name und voller Text vom Benutzer einzusetzen)
  make_release.bat     Baut sauber nach Release\DragLaunch (ohne Benutzerdaten, ohne .pdb) + LIESMICH-README.txt, LICENSE.txt, CHANGELOGs und zippt zu Release\DragLaunch-<Version>-Windows.zip (Version aus der .csproj)
  LIESMICH-README.txt  Kurzanleitung DE/EN für die fertige ZIP (Desktop Runtime, Installation, Update)
  .gitignore           App/, Release/, src/bin/, src/obj/ – Benutzerdaten nie ins Repository
  CHANGELOG-EN.md      Versionshistorie für Benutzer (Englisch)
  docs/                Produktblätter DE/EN + Generator (make_sheets.py, assets/screenshot.png, assets/icon256.png)
  src/
    DragLaunch.csproj  net10.0-windows, WPF + WinForms (nur für ColorDialog), Version N.0.0, ApplicationIcon, Resources
    App.xaml(.cs)      Start, Single-Instance (Mutex/Events, Hänger-Erkennung), Fehlerbehandlung, Log (DragLaunch.log), Dbg (Trace bei Debug=1)
    MainWindow.xaml(.cs) Hauptfenster: Tabs, Kacheln, Drag&Drop, Greifer, Menüs, Positionierung, Kachel-Logik
    Models.cs          ItemData (Path, Label, Arguments, WorkDir, IconFile, Col, Row, RunAsAdmin, FolderMode), TabData, AppSettings, EdgeZone
    ConfigStore.cs     INI lesen/schreiben, Migrationen beim Laden, Backups, PeekLanguage
    Lang.cs            Sprachverwaltung L.T(key) / L.F(key, args); ALLE Texte (Schlüssel, Deutsch, Englisch) in der Liste Raw
    ShellIcons.cs      Icon-Beschaffung (IShellItemImageFactory, PrivateExtractIcons, ICO-Parser, Rahmen-Erkennung, AppsFolder-Auflösung), Caches thread-sicher (ConcurrentDictionary), ItemCache pro Kachel
    IconLoader.cs      Lädt Kachel-Icons auf eigenem STA-Hintergrund-Thread; Aufträge alter Tabs verfallen (Generation)
    IconStore.cs       Eigene Icons in den Ordner "Icons" übernehmen (Kopie oder PNG-Export)
    FaviconFetcher.cs  Favicon von Webseiten holen (HttpClient, HTML-Link-Auswertung, Formaterkennung)
    Launcher.cs        Starten (ShellExecute, runas, AppsFolder über explorer.exe), Ordner-Pulldown, Verknüpfungen auflösen, URL-Erkennung
    EdgeWatcher.cs     Randzonen-Überwachung (Timer 40 ms, GetCursorPos)
    SettingsWindow.cs  Einstellungsfenster (im Code gebaut, Debounce 350 ms)
    Dialogs.cs         Autostart, InputBox, ItemPropertiesWindow, BackupWindow
    EdgePreview.cs     Anzeige der Randzonen am Bildschirm, solange die Einstellungen offen sind
    WindowsTools.cs    Listen für „Windows-Funktion hinzufügen“ (statisch, MMC, Steam, AppsFolder) + Fenster
    About.cs           AppInfo.Version (ganzzahlig), UiScale (Dialog-Vergrösserung), AboutWindow
    Sounds.cs          Klick-Sound (winmm PlaySound, Auswahl aus dem Ordner Sound)
    Sound/*.wav        Mitgelieferte Sounds (werden nach App\Sound kopiert)
    ColorPicker.cs     Windows-Farbwähler (für Einstellungen und Kachelfarbe)
    Native.cs          P/Invoke
    DragLaunch.ico / DragLaunch256.png / DragLaunch-icon.svg   App-Icon
```

Laufzeit-Ordner beim Benutzer: `App\DragLaunch.exe` (+ .dll/.json), `DragLaunch.ini`, `Backup\` (Backups), `Icons\`, `Languages\`, `Sound\`, `DragLaunch.log` (nur bei Fehlern) und `DragLaunch_trace.log` (nur bei `Debug=1`).

## 4. INI-Format (DragLaunch.ini)

```ini
[Settings]
FontSize=18
TabFontSize=22
IconSize=70
CellWidth=95
MaxLabelLines=3
Columns=18
Rows=2
Language=            ; leer = nach Windows-Sprache
ShowTiles=1
DialogScale=1.5
CustomColors=#FFFFFF,...
FrameColor=#D80000
BackgroundColor=#050C05
TabTextColor=...
ActiveTabColor=...
ActiveTabTextColor=...
LabelColor=...
OffsetLeft=150
OffsetBottom=5
UseWorkArea=1
PositionLocked=0
ShowInTaskbar=0
ActivationDelayMs=400
EdgeThickness=2
ActivateFocus=0
IgnoreFullscreen=1
EdgeBottom=1
EdgeBottomFrom=10
EdgeBottomTo=40
...                  ; Top/Left/Right analog
EdgePauseApps=VPinballX_GL64, VPinballX64
BackAfterLaunch=1
Sound=01 Tick.wav    ; leer = kein Sound
ContextFontSize=14   ; Schrift der Menüs
FolderClick=Open|Menu
MenuFontSize=18
MenuIconSize=32
Debug=1              ; optional

[Tabs]
Count=14
Active=9             ; 1-basiert

[Tab1]
Name=Util
ItemCount=12
Item1.Path=...       ; Datei, Ordner, URL, ms-settings:…, shell:AppsFolder\…, steam://rungameid/…, %SystemRoot%\…
Item1.Label=...
Item1.Arguments=...  ; optional
Item1.WorkDir=...    ; optional
Item1.Icon=Icons\x.png   ; optional, relativ zum App-Ordner
Item1.Col=0          ; Kachel-Spalte, 0-basiert
Item1.Row=0          ; Kachel-Zeile, 0-basiert
Item1.RunAsAdmin=1   ; optional
Item1.FolderClick=Menu|Open   ; optional, sonst globaler Standard
Item1.Color=#204060  ; optional, Kachelfarbe
; je Tab zusätzlich optional: Color=#RRGGBB (Tabfarbe)
```

**Rückwärtskompatibilität:** Neue Schlüssel bekommen immer einen Standardwert, damit alte INIs ohne Anpassung weiterlaufen. Alte `X/Y`-Positionen werden in Kacheln umgerechnet (`NormalizeCells`).

Migrationen in `ConfigStore.Load()`:
- `shell:AppsFolder\steam://…` wird zu `steam://…`.
- Kacheln, die auf `.url`/`.lnk` zeigen, werden aufgelöst.
- Eigene Icons werden in den Ordner `Icons` eingesammelt.

## 5. Konventionen (wichtig!)

**Versionen**
- Nur **ganze Zahlen**, angezeigt als `01, 02, …`.
- Bei jeder Auslieferung `AppInfo.Version` in `src/About.cs` **und** `<Version>N.0.0</Version>` in `src/DragLaunch.csproj` erhöhen.

**Texte**
- **Keine festen UI-Texte** im Code. Neuer Text = neuer Eintrag `("schlüssel", "Deutsch", "English")` in `Lang.cs` → `Raw`. Im Code `L.T("schlüssel")` bzw. `L.F("schlüssel", args)`.
- Die Sprachdateien werden beim Start automatisch um neue Schlüssel ergänzt, Änderungen des Benutzers bleiben erhalten.

**Sprache und Stil**
- Deutsch in Schweizer Schreibweise: **ss statt ß**.

**Rückfragen**
- Beim Entfernen von Icons gibt es **keine** Rückfrage (Benutzerwunsch).

**Auslieferung**
- Immer die ganze ZIP (`DragLaunch.zip`) mit `src/`, `build.bat`, `README.md`, `HANDOFF.md`, `PROMPT.md`, `docs/`.
- **Veröffentlichung auf GitHub** (quelloffen, nicht kommerziell): README.md ist die zweisprachige Startseite (EN/DE). Jeder baut selbst mit `build.bat` (prüft .NET 10 SDK, Meldungen DE/EN); Updates = neue ZIP über den Ordner entpacken + `build.bat`, der Ordner `App` mit den Benutzerdaten bleibt unberührt. Keine persönlichen Daten (Rechnernamen, Benutzerpfade) in Code, Kommentaren und Doku.
- HANDOFF.md, PROMPT.md, INSTALL-DE/EN (falls Installation betroffen) und beide CHANGELOG-Dateien (DE/EN, benutzerfreundlich formuliert, neueste Version oben) bei jeder Version nachführen.
- Die Produktblätter (`python3 docs/make_sheets.py`) neu erzeugen, wenn sich Funktionen oder die Version ändern. Neuer Screenshot: `docs/assets/screenshot.png` ersetzen.

**Prüfen ohne Compiler**
- Im Claude-Arbeitsbereich gibt es **kein .NET/Windows**. Der Code kann dort nicht kompiliert werden.
- Vor der Auslieferung mindestens prüfen: Klammerbilanz pro Datei, alle `L.T`/`L.F`-Schlüssel in `Lang.cs` vorhanden.
- Der Benutzer baut mit `build.bat` und meldet Fehler zurück.

**Benutzerumgebung**
- .NET 10 SDK 10.0.404 installiert. Auf dem Benutzer-PC ist **keine NuGet-Quelle** eingerichtet, deshalb ohne `-r win-x64` / self-contained bauen.

## 6. Stolpersteine, die schon einmal Probleme gemacht haben

1. **`DpiChanged` ist ein Routed Event.** Jedes neue `Image` löst es aus, auch ohne echte Änderung. Ohne Filter (`OriginalSource == this` und tatsächlich geänderter DPI-Wert) entsteht eine **Endlosschleife**: Die Oberfläche friert ein.
2. **Nie** innerhalb von Layout- oder Größen-Ereignissen Kinder neu aufbauen oder das Fenster verschieben. Stattdessen `RenderLater()` / `PositionLater()` verwenden (Dispatcher, Priorität Loaded). Sonst kommt „Der Enumerator ist ungültig…“.
3. TextBlock mit Wrap und TextTrimming bei langen Wörtern war verdächtig für Hänger. Deshalb gibt es einen eigenen Umbruch (`WrapLabel`, bevorzugt nach Leerzeichen, `_`, `-` oder `.`).
4. Die Shell liefert über 48 px kleine Icons **mit weissem Rahmen** (Jumbo-Liste). Deshalb: `.exe/.cpl/.dll` über `PrivateExtractIcons`, sonst `ShellNoFrame` (Ecken-Erkennung, dann 48 px). `shell:AppsFolder\{KnownFolder}\…\x.exe` wird zuerst in einen echten Pfad aufgelöst.
5. `.cpl`/`.dll` liefern über die Shell nur ein generisches Icon, deshalb kommt das Icon aus der Ressource mit Index 0.
6. `string.GetHashCode()` ist pro Prozess zufällig. Für den Mutex-Namen wird deshalb FNV-1a verwendet.
7. Statische Felder werden in Textreihenfolge initialisiert (betraf `Lang.cs`: `_builtin` erst in `Load` setzen).
8. Ein WPF-`MenuItem` mit Untermenü löst kein `Click` aus. Lösung: `PreviewMouseLeftButtonUp` mit `OwningMenuItem`.
9. Die App wurde aus `App\` gestartet, während gebaut wurde: Datei gesperrt. `build.bat` beendet sie jetzt vorher.
10. Für `.url`-Dateien ohne Icon zeigt Windows nur ein weisses Blatt. Deshalb bei Webadressen kein Windows-Icon übernehmen, sondern das Favicon holen.
11. Nach passivem Einblenden (ohne Fokus) holt ein Klick auf das bereits aktive Fenster dieses **nicht** nach vorne, denn Windows ändert die Z-Reihenfolge nur bei einem Aktivierungswechsel. Gelöst mit der Klick-Beobachtung (siehe Einblenden).
12. **Netzlaufwerke** (z.B. `V:\`): Ist das Laufwerk nicht erreichbar, blockieren `File.Exists` und die Shell-Icons sehr lange. Deshalb `ShellIcons.Reachable()`: Netzwerk-Computer (`\\SERVER`) per TCP-Port 445 (1,5 s), Freigaben zuerst Rechner, dann Ordner (0,8 s), Netzlaufwerke Ordner (0,8 s). Ergebnis zwischengespeichert: erreichbar 60 s, nicht erreichbar 15 s. Nicht erreichbar → `OfflineIcon` (Weltkugel für Rechner/Ordner, sonst Dateityp), nie dauerhaft gespeichert. Netzwerk-Icons laden auf einem eigenen Thread (`IconLoader-Netz`), damit lokale und eigene Icons nicht warten. Der Hauptfenster-Timer `_netRetry` (20 s) prüft alle Netzwerk-Kacheln des aktiven Tabs erneut: wieder erreichbar → echtes Icon, weg → Weltkugel. Wartezeiten im Netz-Thread werden nicht protokolliert.
13. **Drop-Effekt:** Der Explorer erlaubt bei Netzwerk-Computern und Shell-Ordnern nur `Link`. Wird in DragOver fest `Copy` gesetzt, verweigert Windows das Ablegen. Deshalb `AcceptEffect` (Copy, sonst Link, sonst Move).
13a. `Path.GetFileName("\\\\SERVER\\Freigabe")` liefert einen leeren Text, weil das die Pfad-Wurzel ist. `MakeLabel` bestimmt den letzten Namensteil deshalb selbst.
13b. **Hänger-Protokoll:** Der Watchdog läuft immer und schreibt Hänger über 5 s nach `DragLaunch.log` (Beginn und Dauer). Die ausführliche Diagnose (`DragLaunch_trace.log`) gibt es weiterhin nur mit `Debug=1`.
13c. **Tab-Wechsel langsam:** Früher wurden alle Icons eines Tabs beim Zeichnen synchron im UI-Thread geladen (Shell, Netzlaufwerke, Cloud-Dateien). Nach jedem `ShellIcons.ClearCache()` (Favicon geholt, Eigenschaften geändert, DPI-Wechsel, Backup) dauerte der nächste Wechsel deshalb Sekunden. Seit 20: Kacheln sofort zeichnen, fehlende Icons über `IconLoader` im Hintergrund. Alles, was ShellIcons zurückgibt, muss **eingefroren** (`Freeze`) sein. Langsame Icons (> 1 s) und langsame Tab-Wechsel (> 0,4 s) stehen in `DragLaunch.log`.
14. Position: In WPF-Einheiten lag das Fenster bei gemischten DPI-Werten ausserhalb des Bildschirms. Deshalb physische Pixel mit `SetWindowPos`.

## 7. Versionsverlauf

| Version | Inhalt |
|---|---|
| 1.0.x (vor Umstellung) | Grundgerüst, Build-Fix, DPI-Endlosschleife, Diagnose |
| 1.1.x | Kachelraster, App-Icon, Farbwähler, Ordnerliste klickbar, Icons-Ordner, ICO-Auswahl |
| 1.2.x | Windows-Funktionen, Store-Apps, Grössen der Ordnerliste, .cpl-Icons, Entfernen ohne Rückfrage, MMC-Konsolen |
| 01 | Ganzzahlige Versionen, About-Fenster, Dialog-Vergrösserung |
| 02 | Duplizieren mit Shift |
| 03 | Keine weissen Rahmen um Programm-Icons |
| 04 | Steam-Spiele; Reparatur `shell:AppsFolder\steam://` |
| 05 | Rahmenlose Icons für `shell:AppsFolder`-Einträge (Known-Folder-Auflösung) |
| 06 | `.url`/`.lnk` beim Reinziehen auflösen, Icon übernehmen, Migration |
| 07 | Sprachdateien Deutsch/Englisch (`Languages\`), Umschaltung in den Einstellungen |
| 08 | Nach dem Einblenden geht DragLaunch beim Klick auf ein anderes Fenster wieder dahinter |
| 09 | Favicons von Webseiten automatisch laden, „Favicon neu laden“ im Rechtsklick-Menü |
| 10 | Nach dem Starten in den Hintergrund; Randzone pausieren bei bestimmten Programmen (VR); Schutz vor Hängern bei Netzlaufwerken; Hänger-Protokoll |
| 11 | Backups im Unterordner `Backup`; einstellbarer Klick-Sound aus dem Ordner `Sound` |
| 12 | Kachelfarben; Schriftgrösse der Menüs; Topmost-Wächter, „In den Hintergrund“ und Esc |
| 13 | Beschriftung passt sich hellen Kachelfarben an (Schwarz/Weiss nach Kontrast) |
| 14 | Tabfarben mit automatischer Schriftfarbe; Netzwerk-Computer (`\\SERVER`) reinziehen und öffnen (Netzwerk-Ordner und Liste funktionieren nicht, vom Benutzer verworfen) |
| 15 | „Netzwerk“ (`shell:NetworkPlacesFolder`) in „Windows-Funktion hinzufügen“ unter Ordner |
| 16 | Ablegen mit der von der Quelle erlaubten Art (Copy, sonst Link/Move), damit Netzwerk-Computer und Shell-Ordner abgelegt werden können |
| 17 | Kurze Meldung „… ist nicht erreichbar.“ bei Netzwerkfehlern (Sprachschlüssel `launch.unreachable`) |
| 18 | Netzwerk-Freigaben (`\\SERVER\Freigabe`) bekommen beim Reinziehen einen Namen; leere Beschriftungen werden beim Laden ergänzt |
| 19 | Kachelfläche (Farbe, Hover) auf den Kachelrahmen begrenzt (`TileInset` = 2 px), dadurch Zwischenräume |
| 20 | Schneller Tab-Wechsel: Icons im Hintergrund laden (`IconLoader`), aktiven Tab verzögert speichern (`ConfigStore.SaveSoon`, `Flush` beim Beenden), Sound im Hintergrund; langsame Wechsel und Icons werden protokolliert |
| 21 | Randzonen werden bei offenen Einstellungen am Bildschirmrand angezeigt; rund 50 weitere Windows-Einstellungsseiten und die Datenträgerbereinigung in „Windows-Funktion hinzufügen“ |
| 22 | Mehrfachauswahl mit Strg+Klick (Gruppe ziehen/kopieren, Kontextmenü: einfärben, starten, in Tab verschieben/kopieren, entfernen); „Windows-Funktion hinzufügen“ mit aufklappbaren Gruppen |
| 23 | Zwölf neue Klick-Sounds (01 Tick … 12 Tick) statt Klick/Plopp/Tick |
| 24 | Netzwerk-Icons auf eigenem Thread (eigene/lokale Icons zuerst); Netzwerk-Kacheln alle 20 s neu geprüft (echtes Icon ↔ Weltkugel); Netzwerk-Computer per Port 445 geprüft; Sounds mit Leerzeichen benannt |

## 8. Offene Ideen / Backlog (nicht beauftragt, nur angeboten)

- Rückgängig (Strg+Z) für Entfernen/Verschieben.
- `build-portable.bat` (self-contained). Braucht `dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org`.
- Veröffentlichung auf GitHub: englisches README mit GIF, LICENSE (MIT), GitHub Actions für Release-ZIPs, winget/Scoop, AlternativeTo, Code-Signierung (z. B. SignPath Foundation).
- Mehrere Monitore (Randzonen und Position derzeit nur auf dem Hauptbildschirm).
- Ausgegraute Steam-Kacheln für deinstallierte Spiele (vom Benutzer **nicht** gewünscht, nur Idee).
