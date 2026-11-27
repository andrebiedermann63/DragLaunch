from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import HexColor, white
from reportlab.platypus import Paragraph, Table, TableStyle
from reportlab.lib.styles import ParagraphStyle
import os, re
from PIL import Image

# Erzeugt docs/DragLaunch-Produktblatt-DE.pdf und docs/DragLaunch-Product-Sheet-EN.pdf
# Aufruf: python3 docs/make_sheets.py   (benötigt reportlab, Pillow und die Schrift Fira Sans)
HERE = os.path.dirname(os.path.abspath(__file__))

F = os.environ.get("FIRA_DIR", "/usr/share/fonts/truetype/google-fonts/ofl/firasans") + "/FiraSans-"
for name, file in [("Fira", "Regular"), ("FiraLight", "Light"), ("FiraMed", "Medium"),
                   ("FiraSemi", "SemiBold"), ("FiraBold", "Bold"), ("FiraItalic", "Italic")]:
    pdfmetrics.registerFont(TTFont(name, F + file + ".ttf"))
pdfmetrics.registerFontFamily("Fira", normal="Fira", bold="FiraSemi", italic="FiraItalic", boldItalic="FiraSemi")

RED, DARKRED, GOLD = HexColor("#D80000"), HexColor("#5A0000"), HexColor("#FFD24A")
INK, MUTED, PANEL, LINE, DARK = HexColor("#1D1F1D"), HexColor("#5E625E"), HexColor("#F6F4F0"), HexColor("#E2DED6"), HexColor("#0B140B")
W, H = A4
M = 40
ICON = os.path.join(HERE, "assets", "icon256.png")
SHOT = os.path.join(HERE, "assets", "screenshot.png")      # neuer Screenshot: einfach diese Datei ersetzen
SHOT_W, SHOT_H = Image.open(SHOT).size
# Version direkt aus dem Quellcode lesen (src/About.cs: AppInfo.Version)
VERSION = "%02d" % int(re.search(r"const int Version = (\d+);", open(os.path.join(HERE, "..", "src", "About.cs"), encoding="utf-8").read()).group(1))

lead = ParagraphStyle("lead", fontName="FiraLight", fontSize=11.8, leading=16.5, textColor=INK)
body = ParagraphStyle("body", fontName="Fira", fontSize=9.2, leading=12.8, textColor=INK)
cell = ParagraphStyle("cell", fontName="Fira", fontSize=9.3, leading=12.4, textColor=INK)
cellb = ParagraphStyle("cellb", parent=cell, fontName="FiraSemi")
tip = ParagraphStyle("tip", parent=body, textColor=HexColor("#E9EDE9"), fontSize=9, leading=12.4)

TEXT = {
    "de": dict(
        file="DragLaunch-Produktblatt-DE.pdf",
        doc_title="DragLaunch – Produktblatt",
        tagline="Die frei belegbare Startleiste für Windows 11",
        sub="Programme, Ordner, Dateien und Webseiten – immer eine Mausbewegung entfernt.",
        lead="Wer viele Programme, Werkzeuge und Ordner im Griff haben will, kennt das Problem: Das Startmenü "
             "sortiert selbst, Desktop-Icons verschwinden hinter Fenstern. <b>DragLaunch</b> ist ein Launcher im "
             "Geist des legendären Mac-Klassikers DragThing: Alles wird einfach hineingezogen, liegt in Kacheln "
             "genau dort, wo man es haben will, und ist auf Tabs thematisch geordnet.",
        caption="DragLaunch im Einsatz: Tabs, farbig markierte Kacheln mit Programmen, Ordnern, Skripten und Dokumenten.",
        h_features="Was DragLaunch kann",
        features=[
            ("Kacheln und Tabs", "Icons in festen Kacheln, mit Lücken frei gruppierbar, Kacheln und Tabs einfärbbar. "
                                 "Mit Strg+Klick mehrere wählen und gemeinsam verschieben oder einfärben."),
            ("Drag & Drop für alles", "Programme, Ordner, Dateien, Verknüpfungen und Weblinks hineinziehen. Icons verschieben, "
                                      "tauschen, mit <b>Shift</b> duplizieren. Dateien auf ein Programm ziehen öffnet sie damit."),
            ("Einblenden am Bildschirmrand", "Maus an die Randzone halten, und DragLaunch kommt nach vorne – auch beim Ziehen einer "
                                             "Datei. Rote Balken zeigen die Randzonen beim Einstellen. Bei VR und Spielen pausierbar."),
            ("Ordner als Pulldown-Liste", "Pro Ordner wählbar: direkt öffnen oder den Inhalt als verschachtelte Liste mit Icons "
                                          "zeigen. Schrift- und Icongrösse der Liste sind einstellbar."),
            ("Windows-Funktionen, Store & Steam", "Durchsuchbare Liste: rund 65 Einstellungsseiten, Systemsteuerung, "
                                                 "Konsolen, alle Apps inkl. Microsoft Store und die Steam-Spiele."),
            ("Echte Icons – auch eigene", "Scharfe Icons ohne Rahmen, Webseiten mit Favicon, eigene Icons als ICO, PNG, EXE oder DLL. "
                                          "Netzwerkziele zeigen offline eine Weltkugel."),
            ("Alles in einer INI-Datei", "Einstellungen und Kacheln stehen in DragLaunch.ini und werden sofort gespeichert. "
                                         "Backups mit Datum im Namen, Wiederherstellen mit einem Klick. Portabel."),
            ("Deutsch, Englisch und mehr", "Sprache in den Einstellungen umschaltbar. Die Texte stehen in Sprachdateien – "
                                           "eine weitere Sprache ist nur eine weitere .ini-Datei. Dazu Farben, Grössen und Klick-Sounds."),
        ],
        h_usage="Bedienung auf einen Blick",
        usage=[
            ("Icon hinzufügen", "Datei, Ordner, Programm oder Weblink ins Fenster ziehen. Es landet in der Kachel unter der Maus."),
            ("Starten", "Einfacher Klick auf das Icon."),
            ("Verschieben / tauschen", "Icon in eine andere Kachel ziehen. Ist sie belegt, tauschen die beiden den Platz."),
            ("Duplizieren", "Mit gedrückter Shift-Taste ziehen – auch in einen anderen Tab."),
            ("Einfärben", "Rechtsklick auf Kachel oder Tab → „Kachelfarbe…“ / „Tabfarbe…“."),
            ("Mehrere Kacheln", "Strg+Klick wählt aus, dann gemeinsam ziehen oder per Rechtsklick einfärben."),
            ("In anderen Tab", "Icon oder Datei kurz über einem Tab halten: der Tab wechselt."),
            ("Mit Programm öffnen", "Datei auf ein Programm-Icon ziehen."),
            ("Grösse ändern", "Rechten Rand für Spalten, oberen Rand für Zeilen ziehen – immer kachelweise."),
            ("Fenster verschieben", "Am roten Rahmen ziehen oder Strg + Ziehen auf einer leeren Stelle."),
            ("Einblenden / ausblenden", "Maus an die Randzone halten (Standard: unten). Esc oder Klick auf ein anderes Fenster legt DragLaunch nach hinten."),
            ("Menüs", "Rechtsklick auf Icon, Tab oder leere Fläche, oder der Menüknopf oben links."),
        ],
        h_spec="Technische Daten",
        spec=[
            ("System", "Windows 11, 64 Bit"),
            ("Voraussetzung", ".NET 10 Desktop Runtime (fehlt sie, erscheint ein Hinweis mit Download-Link)"),
            ("Technik", "C# / WPF, echte Windows-Icons über die Shell"),
            ("Sprachen", "Deutsch, Englisch – erweiterbar über Sprachdateien"),
            ("Speicherort", "Programm, INI, Backups, Icons, Sounds und Sprachen in einem Ordner – portabel"),
            ("Einstellungen", "DragLaunch.ini (Textdatei), jede Änderung sofort gespeichert"),
            ("Backups", "DragLaunch_JJJJMMTTa.ini, …b, …c im Unterordner Backup"),
            ("Autostart", "Verknüpfung im Autostart-Ordner, im Menü ein- und ausschaltbar"),
            ("Version", VERSION),
        ],
        h_install="Installation",
        steps=[
            ("Runtime", "Die .NET 10 Desktop Runtime installieren, falls sie noch fehlt."),
            ("Kopieren", "Den Ordner DragLaunch an einen festen Ort kopieren, z.&nbsp;B. C:\\Tools\\DragLaunch – nicht unter „Programme“."),
            ("Starten", "DragLaunch.exe starten und im Menü „Mit Windows starten“ aktivieren. Fertig."),
        ],
        tip_title="Tipp",
        tip="Zieht man eine Datei an den unteren Bildschirmrand, springt DragLaunch nach vorne – "
            "die Datei lässt sich dann direkt auf einer Kachel oder einem Tab ablegen.",
        end_title="DragLaunch – alles Wichtige, eine Mausbewegung entfernt.",
        end_sub="Kacheln, Tabs und Drag & Drop für Programme, Ordner, Dateien, Webseiten und Spiele.",
        foot="DragLaunch · Version " + VERSION + " · Startleiste für Windows 11",
        page="Seite {0} von 2",
        col1=140, spec_col=92,
    ),
    "en": dict(
        file="DragLaunch-Product-Sheet-EN.pdf",
        doc_title="DragLaunch – Product sheet",
        tagline="The freely arrangeable launcher for Windows 11",
        sub="Programs, folders, files and websites – always one mouse move away.",
        lead="Anyone juggling lots of programs, tools and folders knows the problem: the Start menu sorts things "
             "its own way, and desktop icons disappear behind windows. <b>DragLaunch</b> is a launcher in the spirit "
             "of the legendary Mac classic DragThing: just drag everything in, it sits in tiles exactly where you "
             "want it, and tabs keep it organised by topic.",
        caption="DragLaunch in use: tabs, colour-coded tiles with programs, folders, scripts and documents.",
        h_features="What DragLaunch does",
        features=[
            ("Tiles and tabs", "Icons sit in fixed tiles, grouped freely with gaps; tiles and tabs can be coloured. "
                               "Ctrl+click several tiles to move or colour them together."),
            ("Drag & drop for everything", "Drag in programs, folders, files, shortcuts and web links. Move and swap icons, "
                                           "duplicate them with <b>Shift</b>. Drop a file on a program to open it there."),
            ("Pops up at the screen edge", "Hold the mouse at the edge zone and DragLaunch comes to the front – even while dragging "
                                           "a file. Red bars show the edge zones while you set them. Can be paused for VR and games."),
            ("Folders as pull-down lists", "Per folder: open it directly or show its contents as a nested list with icons. "
                                           "Font and icon size of the list are adjustable."),
            ("Windows features, Store & Steam", "A searchable list: about 65 settings pages, Control Panel, consoles, "
                                                "all apps including Microsoft Store, and your Steam games."),
            ("Real icons – your own too", "Crisp icons without frames, websites with favicon, custom icons as ICO, PNG, EXE or DLL. "
                                          "Offline network targets show a globe."),
            ("Everything in one INI file", "Settings and tiles live in DragLaunch.ini and are saved instantly. Dated "
                                           "backups, restore with one click. Fully portable."),
            ("English, German and more", "Switch the language in the settings. All texts live in language files – a new "
                                         "language is just another .ini file. Plus colours, sizes and click sounds."),
        ],
        h_usage="Usage at a glance",
        usage=[
            ("Add an icon", "Drag a file, folder, program or web link into the window. It lands in the tile under the mouse."),
            ("Launch", "Single click on the icon."),
            ("Move / swap", "Drag an icon to another tile. If it is taken, the two icons swap places."),
            ("Duplicate", "Drag while holding Shift – also into another tab."),
            ("Colour", "Right-click a tile or tab → “Tile colour…” / “Tab colour…”."),
            ("Several tiles", "Ctrl+click to select, then drag together or right-click to colour."),
            ("Switch tab while dragging", "Hold an icon or file over a tab for a moment: the tab switches."),
            ("Open with program", "Drop a file on a program icon."),
            ("Resize", "Drag the right edge for columns, the top edge for rows – always tile by tile."),
            ("Move window", "Drag the red frame, or Ctrl + drag on an empty spot."),
            ("Show / hide", "Hold the mouse at the edge zone (default: bottom, 0.4 s). Esc or a click on another window sends DragLaunch back."),
            ("Menus", "Right-click an icon, a tab or an empty area, or use the menu button at the top left."),
        ],
        h_spec="Technical data",
        spec=[
            ("System", "Windows 11, 64-bit"),
            ("Requirement", ".NET 10 Desktop Runtime (if missing, a notice with a download link appears)"),
            ("Technology", "C# / WPF, real Windows icons via the shell"),
            ("Languages", "English, German – extensible via language files"),
            ("Location", "Program, INI, backups, icons, sounds and languages in one folder – portable"),
            ("Settings", "DragLaunch.ini (text file), every change saved instantly"),
            ("Backups", "DragLaunch_YYYYMMDDa.ini, …b, …c in the Backup subfolder"),
            ("Autostart", "Shortcut in the Startup folder, switchable from the menu"),
            ("Version", VERSION),
        ],
        h_install="Installation",
        steps=[
            ("Runtime", "Install the .NET 10 Desktop Runtime if it is not there yet."),
            ("Copy", "Copy the DragLaunch folder to a permanent place, e.g. C:\\Tools\\DragLaunch – not under “Program Files”."),
            ("Start", "Run DragLaunch.exe and enable “Start with Windows” in the menu. Done."),
        ],
        tip_title="Tip",
        tip="Drag a file to the bottom edge of the screen and DragLaunch jumps to the front – "
            "you can then drop the file straight onto a tile or a tab.",
        end_title="DragLaunch – everything important, one mouse move away.",
        end_sub="Tiles, tabs and drag & drop for programs, folders, files, websites and games.",
        foot="DragLaunch · Version " + VERSION + " · Launcher for Windows 11",
        page="Page {0} of 2",
        col1=150, spec_col=86,
    ),
}


def para(c, text, style, x, y_top, width):
    p = Paragraph(text, style)
    _, h = p.wrap(width, 1000)
    p.drawOn(c, x, y_top - h)
    return h


def heading(c, text, x, y, size=15):
    c.setFillColor(RED)
    c.rect(x, y - 2, 4, size * 0.95, stroke=0, fill=1)
    c.setFillColor(INK)
    c.setFont("FiraSemi", size)
    c.drawString(x + 12, y, text)


def footer(c, t, page):
    c.setStrokeColor(LINE)
    c.setLineWidth(0.6)
    c.line(M, 34, W - M, 34)
    c.setFont("Fira", 8)
    c.setFillColor(MUTED)
    c.drawString(M, 22, t["foot"])
    c.drawRightString(W - M, 22, t["page"].format(page))


def build(t):
    c = canvas.Canvas(os.path.join(HERE, t["file"]), pagesize=A4)
    c.setTitle(t["doc_title"])
    c.setAuthor("DragLaunch")
    c.setSubject(t["tagline"])

    # ---------------- Seite 1
    band = 140
    c.setFillColor(RED)
    c.rect(0, H - band, W, band, stroke=0, fill=1)
    c.setFillColor(DARKRED)
    c.rect(0, H - band, W, 5, stroke=0, fill=1)
    c.drawImage(ICON, M, H - band + 26, width=88, height=88, mask="auto")
    c.setFillColor(white)
    c.setFont("FiraBold", 40)
    c.drawString(M + 110, H - 72, "DragLaunch")
    c.setFillColor(GOLD)
    c.setFont("FiraMed", 15)
    c.drawString(M + 112, H - 96, t["tagline"])
    c.setFillColor(HexColor("#FFE7E7"))
    c.setFont("Fira", 10)
    c.drawString(M + 112, H - 115, t["sub"])

    y = H - band - 22
    y -= para(c, t["lead"], lead, M, y, W - 2 * M)

    y -= 14
    shot_w = W - 2 * M
    shot_h = shot_w * SHOT_H / SHOT_W
    c.setFillColor(DARK)
    c.roundRect(M - 3, y - shot_h - 3, shot_w + 6, shot_h + 6, 5, stroke=0, fill=1)
    c.drawImage(SHOT, M, y - shot_h, width=shot_w, height=shot_h)
    y -= shot_h + 14
    c.setFont("FiraItalic", 8.2)
    c.setFillColor(MUTED)
    c.drawString(M, y, t["caption"])
    y -= 24

    heading(c, t["h_features"], M, y)
    y -= 14

    col_gap, box_h, row_gap = 14, 88, 8
    col_w = (W - 2 * M - col_gap) / 2
    for i, (title, text) in enumerate(t["features"]):
        col, row = i % 2, i // 2
        x = M + col * (col_w + col_gap)
        top = y - row * (box_h + row_gap)
        c.setFillColor(PANEL)
        c.setStrokeColor(LINE)
        c.setLineWidth(0.6)
        c.roundRect(x, top - box_h, col_w, box_h, 6, stroke=1, fill=1)
        c.setFillColor(RED)
        c.roundRect(x + 12, top - 24, 10, 10, 2, stroke=0, fill=1)
        c.setFillColor(GOLD)
        c.rect(x + 15, top - 21, 4, 4, stroke=0, fill=1)
        c.setFillColor(INK)
        c.setFont("FiraSemi", 11.5)
        c.drawString(x + 30, top - 23, title)
        h = para(c, text.replace(" & ", " &amp; "), body, x + 12, top - 31, col_w - 24)
        assert 31 + h <= box_h - 4, ("Text zu lang", title, h)
    last_bottom = y - 3 * (box_h + row_gap) - box_h
    assert last_bottom > 40, ("Seite 1 läuft über", last_bottom)

    footer(c, t, 1)
    c.showPage()

    # ---------------- Seite 2
    c.setFillColor(RED)
    c.rect(0, H - 14, W, 14, stroke=0, fill=1)
    y = H - 52
    heading(c, t["h_usage"], M, y)
    y -= 14
    data = [[Paragraph(a, cellb), Paragraph(b, cell)] for a, b in t["usage"]]
    tb = Table(data, colWidths=[t["col1"], W - 2 * M - t["col1"]])
    tb.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("ROWBACKGROUNDS", (0, 0), (-1, -1), [PANEL, white]),
        ("LINEBELOW", (0, 0), (-1, -1), 0.5, LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 9), ("RIGHTPADDING", (0, 0), (-1, -1), 9),
        ("TOPPADDING", (0, 0), (-1, -1), 4.5), ("BOTTOMPADDING", (0, 0), (-1, -1), 4.5),
    ]))
    _, th = tb.wrap(W - 2 * M, 1000)
    tb.drawOn(c, M, y - th)
    y -= th + 28

    left_w = 300
    right_x = M + left_w + 20
    right_w = W - M - right_x
    heading(c, t["h_spec"], M, y)
    heading(c, t["h_install"], right_x, y)
    y -= 14

    sdata = [[Paragraph(a, cellb), Paragraph(b, cell)] for a, b in t["spec"]]
    st = Table(sdata, colWidths=[t["spec_col"], left_w - t["spec_col"]])
    st.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LINEBELOW", (0, 0), (-1, -1), 0.5, LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 0), ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
    ]))
    _, sh = st.wrap(left_w, 1000)
    st.drawOn(c, M, y - sh)

    sy = y
    for n, (title, text) in enumerate(t["steps"], 1):
        c.setFillColor(RED)
        c.circle(right_x + 11, sy - 11, 11, stroke=0, fill=1)
        c.setFillColor(white)
        c.setFont("FiraBold", 12)
        c.drawCentredString(right_x + 11, sy - 15.5, str(n))
        c.setFillColor(INK)
        c.setFont("FiraSemi", 10.5)
        c.drawString(right_x + 30, sy - 13, title)
        h = para(c, text, body, right_x + 30, sy - 19, right_w - 30)
        sy -= 19 + h + 14

    box_top = sy - 4
    box_h2 = 92
    c.setFillColor(DARK)
    c.roundRect(right_x, box_top - box_h2, right_w, box_h2, 6, stroke=0, fill=1)
    c.setFillColor(GOLD)
    c.setFont("FiraSemi", 10.5)
    c.drawString(right_x + 12, box_top - 20, t["tip_title"])
    para(c, t["tip"], tip, right_x + 12, box_top - 28, right_w - 24)

    y_end = min(y - sh, box_top - box_h2) - 22
    assert y_end - 64 > 44, ("Seite 2 läuft über", y_end)
    c.setFillColor(PANEL)
    c.setStrokeColor(LINE)
    c.roundRect(M, y_end - 64, W - 2 * M, 64, 6, stroke=1, fill=1)
    c.drawImage(ICON, M + 14, y_end - 54, width=44, height=44, mask="auto")
    c.setFillColor(INK)
    c.setFont("FiraSemi", 13)
    c.drawString(M + 72, y_end - 28, t["end_title"])
    c.setFont("Fira", 9.4)
    c.setFillColor(MUTED)
    c.drawString(M + 72, y_end - 45, t["end_sub"])

    footer(c, t, 2)
    c.showPage()
    c.save()


for lang in ("de", "en"):
    build(TEXT[lang])
    print("ok", lang)
