# Start-Prompt für einen neuen Chat

Den folgenden Text zusammen mit der aktuellen **DragLaunch.zip** (und bei Bedarf der eigenen `DragLaunch.ini` oder Screenshots) in einen neuen Chat kopieren:

---

Ich entwickle mit dir die Windows-11-App **DragLaunch** weiter: eine Startleiste mit Kacheln und Tabs im Stil von DragThing, geschrieben in C# und WPF mit .NET 10. Im Anhang ist der aktuelle Projektstand als ZIP.

Bitte so vorgehen:
1. Entpacke die ZIP und lies zuerst **`HANDOFF.md`** vollständig. Dort stehen Funktionsumfang, Projektstruktur, INI-Format, Konventionen und bekannte Stolpersteine.
2. Halte dich an die Konventionen aus HANDOFF.md, insbesondere:
   - Antworten auf Deutsch in Schweizer Schreibweise (ss statt ß).
   - Versionen nur als ganze Zahlen (01, 02, …). Bei jeder Auslieferung `AppInfo.Version` in `src/About.cs` und `<Version>` in `src/DragLaunch.csproj` erhöhen.
   - Keine festen UI-Texte im Code. Neue Texte gehören als Deutsch und Englisch in `src/Lang.cs` (`Raw`) und werden über `L.T()` bzw. `L.F()` verwendet.
   - Meine INI muss rückwärtskompatibel bleiben. Wenn sich doch etwas ändert, eine Migration in `ConfigStore.Load()` einbauen.
3. In deiner Umgebung gibt es kein .NET/Windows. Prüfe den Code deshalb sorgfältig (Klammerbilanz, alle Sprachschlüssel vorhanden). Ich baue mit `build.bat` und melde Fehler zurück.
4. Liefere nach jeder Änderung die **ganze ZIP** und führe dabei immer nach:
   - `HANDOFF.md` (Stand, Versionsverlauf, neue Stolpersteine, Backlog),
   - `CHANGELOG-DE.md` und `CHANGELOG-EN.md` (neuer Eintrag oben, in Benutzersprache),
   - `PROMPT.md` (falls nötig),
   - `README.md` (falls sich die Bedienung ändert),
   - die Produktblätter in `docs/` mit `python3 docs/make_sheets.py`, wenn sich Funktionen oder die Version ändern. Gib mir die beiden PDFs dann ebenfalls mit.

Mein nächstes Anliegen:
**[hier den Wunsch eintragen]**
