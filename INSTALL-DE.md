# DragLaunch – Installation und Updates

DragLaunch wird aus dem Quellcode gebaut. Das erledigt die Datei `build.bat` mit einem Doppelklick. Dieselbe Datei dient auch für Updates: Sie beendet einen laufenden DragLaunch, baut die neue Version und startet sie wieder.

## Zwei Wege

- **Fertige Version (einfach):** Unter **Releases** die Datei `DragLaunch-<Version>-Windows.zip` herunterladen. Dafür genügt die kleinere **.NET 10 Desktop Runtime**, gebaut wird nichts. Anleitung in der beiliegenden `LIESMICH-README.txt`, kurz: entpacken, `DragLaunch.exe` starten. Update: neue ZIP über den Ordner entpacken.
- **Aus dem Quellcode (für eigene Anpassungen):** wie unten beschrieben mit `build.bat`.

## Voraussetzungen

- Windows 11, 64 Bit
- **.NET 10 SDK** (einmalig, kostenlos von Microsoft):
  https://dotnet.microsoft.com/download/dotnet/10.0
  Oder in der Eingabeaufforderung: `winget install Microsoft.DotNet.SDK.10`

Das SDK enthält alles, was zum Bauen und zum Ausführen nötig ist. Fehlt es, meldet `build.bat` das mit dem Download-Link.

## Erste Installation

1. Auf der GitHub-Seite **Code → Download ZIP** wählen (oder die ZIP-Datei unter **Releases** herunterladen).
2. Vor dem Entpacken: Rechtsklick auf die ZIP-Datei → **Eigenschaften** → unten **Zulassen** anhaken → OK. Sonst warnt Windows beim Start von `build.bat`.
3. Die ZIP an einen festen Ort entpacken, an dem du schreiben darfst, z. B. `C:\Tools\DragLaunch`.
   Nicht unter `C:\Programme`: DragLaunch speichert seine Einstellungen im eigenen Ordner.
4. `build.bat` doppelklicken. Nach rund einer Minute steht im Fenster „Fertig“.
5. `App\DragLaunch.exe` starten.
6. Im Menü (☰ oben links) **Mit Windows starten** einschalten, wenn DragLaunch automatisch starten soll.

Fertig. Programme, Ordner und Dateien einfach ins Fenster ziehen.

## Update auf eine neue Version

1. Die neue Version wie oben herunterladen und **Zulassen** anhaken.
2. Die ZIP **in denselben Ordner** entpacken und vorhandene Dateien überschreiben lassen.
3. `build.bat` doppelklicken. DragLaunch wird beendet, neu gebaut und wieder gestartet.

Deine Daten bleiben dabei erhalten. Sie liegen im Ordner `App` und sind nicht in der ZIP enthalten:

| Im Ordner `App` | Inhalt |
|---|---|
| `DragLaunch.ini` | Alle Einstellungen, Tabs und Kacheln |
| `Backup\` | Deine Sicherungen |
| `Icons\` | Eigene Icons und Favicons |
| `Sound\`, `Languages\` | Sounds und Sprachdateien (eigene bleiben erhalten) |

**Den Ordner `App` nie löschen.** Wer ganz sicher gehen will, legt vor dem Update im Menü ein Backup an.

### Mit Git

Wer das Repository mit Git geklont hat, aktualisiert mit `git pull` und startet danach `build.bat`.

## Eigene Anpassungen

Der Quellcode liegt im Ordner `src` (C# / WPF). Nach einer Änderung einfach `build.bat` starten. Hinweise zum Aufbau stehen in `HANDOFF.md`.
Eigene Sprachen: eine Kopie von `App\Languages\English.ini` anlegen und übersetzen. Eigene Sounds: `.wav`-Dateien in `App\Sound` legen.

## Für den Herausgeber: fertige ZIP erstellen

`make_release.bat` baut DragLaunch in einen eigenen, leeren Ordner `Release` (ohne deine Einstellungen, Backups und Icons) und erstellt `Release\DragLaunch-<Version>-Windows.zip`. Diese Datei auf GitHub unter **Releases → Draft a new release** anhängen.

## Deinstallieren

1. Im Menü **Mit Windows starten** ausschalten.
2. DragLaunch beenden und den ganzen Ordner löschen. Es gibt keine Einträge in der Registry.

## Probleme?

- **„Das .NET 10 SDK fehlt“:** SDK installieren (Link oben) und `build.bat` erneut starten.
- **Windows warnt beim Start von `build.bat`:** „Weitere Informationen“ → „Trotzdem ausführen“, oder vor dem Entpacken **Zulassen** anhaken (siehe oben).
- **Build fehlgeschlagen:** Die Meldungen im Fenster als Issue auf GitHub melden.
- **DragLaunch reagiert langsam oder hängt:** Die Datei `App\DragLaunch.log` hilft bei der Fehlersuche.
