# PerplexPDF – Technisches Pflichtenheft

**Dokument-ID:** PP-SPEC-001  
**Version:** 1.0 · 08.11.2026  
**Status:** Umsetzungsbaseline für ChatGPT Work / Entwicklungsteam  
**Bezugsdokument:** PP-REQ-001 – PerplexPDF Anforderungsanalyse  
**Copyright in der Anwendung:** [© by www.perplex.click](https://www.perplex.click/) - Michael Lappenbusch

## 1. Zweck, Geltung und Erfolgskriterium

Dieses Pflichtenheft beschreibt die technische Umsetzung der **ersten lauffähigen PerplexPDF-Release R1 / Version 1.0** sowie sauber getrennte Erweiterungsstufen R2 bis R4. Der Text dient als verbindliche Implementierungsvorlage für ChatGPT Work. Ziel ist ein reproduzierbar gebautes, lokal funktionsfähiges **Windows-x64-Programm** inklusive bereitstellbarem ZIP-Archiv und ausführbarer Datei.

**Kein Missverständnis:** Das Pflichtenheft und die Anforderungsanalyse sind Planungsartefakte. Sie ersetzen weder Sourcecode noch die zukünftige Kompilierung und Tests unter Windows. Die Bezeichnung „Acrobat-ähnlich“ fordert vergleichbare Workflows, nicht die Nachbildung des Adobe-Designs oder sofortige vollständige Funktionsparität.

## 2. Technologische Grundentscheidung

**Empfohlene Umsetzung:** C# mit .NET 10, Windows Presentation Foundation (WPF), MVVM, x64. Die endgültige Sprache darf geändert werden, falls der neue Stack alle funktionalen, Lizenz- und Auslieferungsanforderungen besser erfüllt. Für R1 ist zunächst kein zusätzlicher Dienst und kein Webserver vorzusehen.

| Baustein | Technische Wahl / Adapter | Aufgabe | Lizenzhinweis |
|---|---|---|---|
| Desktop-UI | .NET 10 WPF, MVVM | Native Windows-Oberfläche | Microsoft-Bedingungen prüfen |
| Darstellung/Seitentext | PDFium mit eigenem `IPdfRenderEngine`-Adapter | Rendering, Zoom, Textselektion | BSD-3-Clause; vollständige Notices beilegen |
| Strukturoperationen | qpdf als gebündeltes Tool, über `IPdfStructureEngine` | Seitenmanipulation, Merge, Split, ggf. Verschlüsselung | Apache-2.0 |
| Textanalyse | PdfPig, optional | Text- und Bounding-Box-Extraktion | Apache-2.0; für R1 nicht zwingend, wenn PDFium ausreicht |
| Overlays und PDF-Erstellung | SkiaSharp `SKDocument.CreatePdf` mit `IPdfOverlayEngine` | Text/Bilder/Markierungen auf PDF-Seiten, neue Seiten | MIT; native Komponenten im Paket |
| OCR | Tesseract 5.x plus `eng`/`deu`-Daten | Lokale optische Zeichenerkennung | Apache-2.0; Tool+Sprachdaten mitliefern |
| App-Projektformat | System.IO.Compression + System.Text.Json | `.ppdf` als ZIP inkl. Manifest und Overlay-Daten | Teil von .NET |
| Logging/DI/Tests | .NET-Standardwerkzeuge und ausgewählte kleine Pakete | Wartbarkeit und automatisierte Qualität | Versionen im Lockfile fixieren |

**Lizenz-Gate:** PDFsharp ab Version 6.5 wird vom Hersteller als AGPL/kommerziell ausgewiesen; ebenso sind iText- und MuPDF-Lizenzmodelle vor Nutzung zu prüfen. Keine Bibliothek automatisch hinzufügen, die ohne schriftlich geklärte Lizenz die geplante proprietäre Distribution unmöglich macht. Diese Prüfung gilt ebenfalls für Tesseract-Binaries, PDFium-Redistributionen, Fonts und Codecs.

**Versionierungsregel:** Das Entwicklungsteam pinnt konkrete, geprüfte NuGet-Versionen, native Binary-Hashes und Lizenzstände in `DEPENDENCIES.md` und `THIRD_PARTY_NOTICES.txt`. Dieses Pflichtenheft schreibt bewusst keine ungeprüften Download-URLs oder Versionsnummern für native Bibliotheken fest.

## 3. Zielplattform und Packaging

| Parameter | R1-Festlegung |
|---|---|
| Primärziel | Windows 11 x64, unterstützte Editionen, aktueller Sicherheitsstand |
| Sekundärziel | Windows 10 x64 nur noch mit aktivem Microsoft-/Runtime-Support (z. B. geeignete LTSC-/Enterprise-Editionen) und bestandenen Tests |
| Endnutzer-Berechtigungen | Start aus Benutzerordner ohne Administratorrechte; Dateizugriff nach Windows-Rechten |
| Internet | Kein Internet für R1-Kernfunktionen erforderlich |
| Primäres Paket | `PerplexPDF-1.0.0-win-x64-portable.zip` |
| Enthalten | `PerplexPDF.exe`, alle erforderlichen native Dateien, `tools/qpdf`, `tools/tesseract`, `tessdata/eng+deu`, `licenses/`, `README.txt` |
| Optional | Standalone-EXE oder Installer nur, wenn auf Clean-Windows-VM nachgewiesen |
| Echtes Downloadziel | Bereitstellung als konkretes Release-Artefakt, z. B. Projekt-Release-Seite; keine bloße ZIP-Ankündigung |

Es wird **keine Endnutzerinstallation von NuGet-Paketen** oder einer .NET-Runtime verlangt. NuGet ist ein Build-Zeit-Mechanismus auf dem Entwicklungsrechner. Ein *portable ZIP mit enthaltenen Abhängigkeiten* ist stabiler als die unrealistische Garantie, dass das gesamte Programm inklusive großer OCR-Sprachdaten zwingend aus nur einer Datei bestehen müsse.

Beispiel für den Windows-Publish-Befehl (nach überprüfter Projektstruktur):

```powershell
dotnet publish .\src\PerplexPDF.App\PerplexPDF.App.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=embedded `
  --output .\artifacts\publish\win-x64
```

**Wichtig:** Native Werkzeuge/Daten und Drittanbieter-Lizenztexte können im resultierenden Ausgabeverzeichnis als Dateien liegen. Bei Single-File-Extraktion wird das Laufzeitverhalten auf Clean-Windows geprüft. `PublishTrimmed` ist im R1-Standard aus, bis die Verträglichkeit von WPF und nativen Komponenten getestet wurde.

## 4. Architektur, Modulgrenzen und Quellcode-Struktur

**Architekturstil:** MVVM für UI, Use-Case-orientierte Application-Schicht, schlanker Domain-Kern, austauschbare Infrastructure-Adapter. Keine direkte qpdf-/Tesseract-Prozessaufrufe aus ViewModels, keine PDFium-Handles in Domain-Objekten.

```text
PerplexPDF.sln
├─ src/
│  ├─ PerplexPDF.App/                  # WPF windows, views, resources, startup
│  ├─ PerplexPDF.Application/          # commands/use cases, orchestration
│  ├─ PerplexPDF.Domain/               # document/page/overlay models
│  ├─ PerplexPDF.Infrastructure.Pdf/   # PDFium/qpdf/PdfPig/SkiaSharp adapters
│  ├─ PerplexPDF.Infrastructure.Ocr/   # Tesseract adapter
│  └─ PerplexPDF.Infrastructure.Store/ # .ppdf persistence, settings, logs
├─ tests/
│  ├─ PerplexPDF.UnitTests/
│  ├─ PerplexPDF.IntegrationTests/
│  └─ PerplexPDF.WindowsSmokeTests/
├─ tests/Fixtures/                   # legally cleared sample PDFs
├─ tools/                            # pinned redistributable binaries
├─ docs/                             # requirements, spec, architecture, FAQ
├─ packaging/                        # create_release.ps1, hashes, templates
├─ licenses/                         # required OSS license notices
├─ Directory.Packages.props          # centralized dependency versions
├─ README.md
├─ DEPENDENCIES.md
└─ THIRD_PARTY_NOTICES.txt
```

**Zentrale Regeln:** Asynchrone, abbrechbare PDF-/OCR-Aufgaben; UI bleibt bedienbar. Alle Modifikationen laufen durch Command-Objekte und erhöhen den Dirty-State. Konvertierungen und Exporte schreiben erst in temporäre Dateien, validieren das Ergebnis und ersetzen erst danach die Zieldatei. PDF-Engines dürfen unabhängig austauschbar sein, insbesondere für eine mögliche kommerzielle Text-Editing-Engine in R2.

### 4.1 Interface-Verträge

```csharp
public interface IPdfRenderEngine
{
    Task<PdfOpenResult> OpenAsync(string path, string? password, CancellationToken ct);
    Task<RenderedPage> RenderPageAsync(Guid documentId, int pageZeroBased,
        double dpi, CancellationToken ct);
    Task<IReadOnlyList<TextHit>> SearchAsync(Guid documentId, string term,
        CancellationToken ct);
}

public interface IPdfStructureEngine
{
    Task<OperationResult> RebuildPagesAsync(string sourcePath,
        IReadOnlyList<PageReference> order, string targetPath, CancellationToken ct);
    Task<OperationResult> MergeAsync(IReadOnlyList<string> sources,
        string targetPath, CancellationToken ct);
    Task<OperationResult> SplitAsync(string sourcePath,
        IReadOnlyList<PageRange> ranges, string outputDirectory, CancellationToken ct);
}

public interface IPdfOverlayEngine
{
    Task<OperationResult> ExportAsync(DocumentProject project,
        string targetPdfPath, CancellationToken ct);
}

public interface IOcrEngine
{
    Task<OcrResult> CreateSearchablePdfAsync(string inputPdf,
        string outputPdf, IReadOnlyList<string> languages, CancellationToken ct);
}

public interface IProjectStore
{
    Task SaveAsync(DocumentProject project, string ppdfPath, CancellationToken ct);
    Task<DocumentProject> LoadAsync(string ppdfPath, CancellationToken ct);
}
```

Vertragsobjekte enthalten **Fehlercode, Fehlermeldung, Diagnose-ID und User-safe-Message**, nicht rohe Exceptions. Ausgabedateien dürfen bei Fehlern nicht als erfolgreich markiert werden. Tests verwenden Fake-Implementierungen dieser Interfaces.

## 5. Datenmodell und Dateiformate

### 5.1 Domain-Modell

- `DocumentProject`: Projekt-ID, Quell-PDF-Hash, Seitenliste, Overlay-Objekte, Erstell-/Änderungszeit, Schema-Version, Spracheinstellung für das Dokument (optional).
- `PageReference`: stabile Page-ID, ursprüngliche Dokument-ID/Quelldatei, Originalseitenindex, Rotation und Exportreihenfolge.
- `OverlayObject`: eigene GUID, Seiten-ID, Typ (`text`, `image`, `shape`, `highlight`, `note`), Bounding-Box in PDF-Punkten, Z-Reihenfolge, Stil und Nutzdaten.
- `DocumentSession`: aktuell geöffnete Tabs, Undo/Redo-Historie, Dirty-State und Viewport. Session-Daten sind nicht Bestandteil des veröffentlichten Standard-PDFs.
- `OperationResult`: Erfolg, Zielpfad, Warnungen, Exit-Code der Engine und Diagnose-ID.

### 5.2 Koordinaten und Rendering

Interne Bearbeitungskoordinaten verwenden PDF-Punkte (`1 pt = 1/72 inch`) und Ursprung unten links für PDF-Objekte. UI-Mauskoordinaten (oben links) werden zentral transformiert. Rotationen 0/90/180/270 und unterschiedliche Seitengrößen werden im selben Transformationsmodul behandelt. Alle Komponenten verwenden nur diese zentrale Koordinatentransformation; keine ad-hoc-Pixelpositionen in Exportbefehlen.

### 5.3 `.ppdf`-Container (R1)

```text
sample.ppdf  (ZIP)
├─ manifest.json
├─ source.pdf
├─ overlays.json
└─ resources/
   └─ <sha256>-image.png
```

Beispiel eines `manifest.json`:

```json
{
  "schemaVersion": "1.0",
  "product": "PerplexPDF",
  "projectId": "79cb4a0d-77d3-4f88-9381-83f89003906f",
  "documentFile": "source.pdf",
  "overlaysFile": "overlays.json",
  "pageOrder": [{ "sourcePage": 1, "rotation": 0 }],
  "createdWith": "1.0.0"
}
```

Aus dem Archiv dürfen **keine absoluten Pfade, Parent-Directory-Sequenzen oder Symlinks** ungeprüft extrahiert werden. Archivgrößen und Entpackverhältnisse erhalten Sicherheitslimits. Ein Projekt speichert Original-PDF und die zur Wiederbearbeitung notwendigen Zusatzobjekte. Änderungen an der Seitenstruktur gehören nachvollziehbar in `pageOrder`. Historie und temporäre Dateien müssen für die Projektkompatibilität nicht serialisiert werden.

### 5.4 Interoperabilität von Standard-PDF und Projekt

**Save Project As…** schreibt `.ppdf` mit editierbaren Overlays. **Save PDF As…** generiert ein konventionelles `.pdf`, indem die gewählte Seitenreihenfolge durch die Struktureinheit aufgebaut und anschließend Overlay-PDFs mittels qpdf als zusätzliche Inhalte eingebracht werden. Bei Wiederöffnung eines exportierten PDFs sind neu eingefügte Elemente nicht zwangsläufig als ursprüngliche PerplexPDF-Objekte erkannt. Dies wird in der Oberfläche dokumentiert.

Für Markierungen und Notizen ist R1 mindestens **optisch korrekter PDF-Export** festgelegt; standardisierte PDF-Annotation-Dictionaries und die vollständige Bearbeitbarkeit durch Fremdprogramme werden in einem eigenen Integrationstest als R2-Qualitätsausbau verfolgt, falls R1-Engine diese nicht sicher unterstützt.

## 6. Ausgestaltung der englischen Oberfläche

**Hauptfensterstruktur:**

```text
┌────────────────────────────────────────────────────────────────────────────┐
│ PerplexPDF | File Edit View Pages Annotate Tools Help                     │
├────────────────────────────────────────────────────────────────────────────┤
│ Open  Save PDF  Save Project  Undo  Redo | Add Text Image Highlight | OCR │
├──────────────┬─────────────────────────────────────────┬─────────────────┤
│ Thumbnails   │  document-1.pdf  |  document-2.pdf        │ Properties      │
│ Bookmarks    │                                         │ Comments        │
│              │         PDF page / editor canvas        │                 │
│              │                                         │                 │
├──────────────┴─────────────────────────────────────────┴─────────────────┤
│ Page 3 / 20 · 125% · Edited       © by www.perplex.click - Michael Lappenbusch │
└────────────────────────────────────────────────────────────────────────────┘
```

**Verbindlicher Copyright-String (sichtbar, exakt):** `© by www.perplex.click - Michael Lappenbusch`. `www.perplex.click` ist klickbar und öffnet `https://www.perplex.click/` im Standardbrowser nach aktivem Klick. Derselbe Text erscheint in **Help > About PerplexPDF**, zusammen mit Produktname, Version und „Third-Party Notices“. Der Hinweis muss auf typischen Desktop-Auflösungen (ab 1280 × 720) sichtbar bleiben, ohne durch Seitenzahlen/Zoom ausgeblendet zu werden. Kleiner als 9 pt ist unzulässig. Keine Schriftbildumwandlung, die das `©` ersetzt.

**Menüs:** `File > Open PDF… / Open Project… / Save PDF As… / Save Project As… / Export Pages… / Print… / Exit`; `Edit > Undo / Redo / Add Text / Add Image`; `View > Zoom In / Zoom Out / Fit Width / Fit Page / Show Thumbnails / Show Bookmarks`; `Pages > Rotate / Delete / Reorder / Extract / Merge / Split`; `Annotate > Highlight / Underline / Note / Rectangle`; `Tools > OCR / Create PDF from Images / Settings`; `Help > About PerplexPDF / Third-Party Notices`.

**Tastenkürzel (R1):** Ctrl+O, Ctrl+Shift+O für Projekt, Ctrl+Shift+S für Export-PDF, Ctrl+S für Projekt speichern, Ctrl+F, Ctrl+P, Ctrl+Z/Ctrl+Y, Ctrl++/Ctrl+-, Esc für Werkzeug abbrechen. Shortcuts müssen aus Menüs ablesbar und in Help dokumentiert sein.

**Lokalisierung:** `Resources/Strings.en-US.resx` ist vollständig; `Strings.de-DE.resx` optional. Der englische Text ist Fallback, keine hartcodierten deutschen Exceptions in Dialogen. Standardmäßig EN. Einstellung „Language: English / Deutsch“ nur anbieten, wenn die ausgewählte Lokalisierung ausreichend vollständig und getestet ist.

## 7. End-to-End-Workflows

### WF-01: PDF öffnen und lesen

1. Datei wird gewählt oder in das Fenster gezogen; Dateiendung und Header werden geprüft.
2. PDFium prüft Format und fordert bei verschlüsselter PDF nötigenfalls ein Kennwort an; Passwörter nicht loggen.
3. Metadaten, Seitenanzahl, Größe, Thumbnail-Liste und initiales Rendering werden geladen.
4. Benutzer kann navigieren, suchen, kopieren und zoomen; noch keine Änderungen am Original.
5. Fehler: klare Meldung bei beschädigten, nicht unterstützten oder falsch passwortgeschützten Dateien.

### WF-02: Seiten umstellen und exportieren

1. Benutzer markiert Seitenminiaturen, verschiebt bzw. rotiert sie oder löscht sie.
2. Jede Aktion erzeugt einen rückgängig zu machenden Command-Eintrag und aktualisiert die View.
3. Export baut temporäre PDF via qpdf auf, prüft Seitenzahl und Öffnungsfähigkeit via PDFium.
4. Bei Erfolg wird Ziel-PDF erstellt; Fehler lässt Original und etwaige schon vorhandene Ziel-PDF intakt.

### WF-03: Text/Bild/Markierung hinzufügen

1. Nutzer wählt Werkzeug und zieht Bereich auf der Seite auf.
2. WPF erstellt editierbares Overlay-Objekt mit PDF-Punkt-Koordinaten.
3. Änderungen an Font, Position, Farbe und Größe verändern nur das Overlay-Objekt.
4. `.ppdf` speichert alle Objekte strukturiert, normaler PDF-Export flacht Objekte auf Seite visuell ein.
5. Original-Text darunter ist weiter vorhanden. **Dieser Workflow ist keine Textentfernung und keine Schwärzung.**

### WF-04: OCR

1. Quelle wird geöffnet und `Tools > OCR` gewählt; Sprachen English / German / Both auswählbar.
2. PDFium rendert ausgewählte Scan-Seiten mit einstellbarer Auflösung (Default 300 dpi) in sichere temporäre Bilder.
3. Das lokal mitgelieferte Tesseract wird pro Seite aufgerufen; erzeugt PDF mit sichtbarem Bild und OCR-Textlayer.
4. qpdf setzt Seiten in korrekter Reihenfolge zusammen; Ausgabe wird neu geöffnet und auf suchbaren Beispieltext getestet.
5. Ergebnis wird als **neue** PDF gespeichert; Quelle unverändert; Fortschritt, Abbruch und mögliche OCR-Fehler angezeigt.
6. Auf gescannten Seiten können Bildqualität/Dateigröße und die Erkennung variieren; Handschrift nicht versprechen.

### WF-05: Projekt wieder öffnen

1. `.ppdf` wird sicher entpackt und validiert (Schema, Hash, zulässige Dateinamen).
2. Quelle, Seitenreihenfolge und Overlays werden wiederhergestellt.
3. Nutzer verändert ein zuvor hinzugefügtes Textfeld und speichert erneut ohne Doppelung.
4. Export erfolgt als standardkonforme PDF mit dem aktuellen Stand.

## 8. Implementierungsregeln und Fehlerbehandlung

- **Prozessaufrufe:** Nur `ProcessStartInfo` mit `UseShellExecute=false` und `ArgumentList`; keine Übergabe von Dateipfaden über Shell-Stringkonkatenation. Standardoutput und Fehlerausgabe erfassen, Timeouts konfigurieren und Prozessäste bei Abbruch beenden.
- **Dateisystem:** Temporär in `%LOCALAPPDATA%\PerplexPDF\Temp\<guid>`; restriktive Zugriffsrechte im Benutzerkontext. Nach Programmstart alte Temp-Dateien kontrolliert bereinigen, niemals fremde Verzeichnisse löschen.
- **Passwörter/Schlüssel:** Nicht in Logs, Crash-Reports oder Prozess-Argumentlisten speichern, wenn eine sichere Bibliotheks-API statt CLI möglich ist; ansonsten Risiko vor Freigabe bewerten.
- **Performance:** Thumbnail-Rendering lazy, sichtbare Seiten zuerst, Render-Caches mit Speicherbudget und sauberer Freigabe nativer Handles.
- **Threading:** Schwergewichtsoperationen nicht auf dem UI-Thread; Cancel-Button und Fortschrittsstatus.
- **Importe:** PDF-/ZIP-Eingaben als nicht vertrauenswürdig behandeln; Größen-/Seitenzahl- und Dekompressionslimits.
- **Links/Skripte:** PDF-JavaScript wird nicht ausgeführt. Externe Links erfordern aktive Benutzeraktion.
- **Logging:** Rotierende lokale Logs ohne PDF-Inhaltsauszug und ohne Kennwörter; Telemetrie aus.
- **Undo/Redo:** Command Pattern für R1-Mutationen, nach jeder erfolgreichen Aktion konsistente DocumentSession.
- **Versionierung:** SemVer (`MAJOR.MINOR.PATCH`), Projektformat schema-versioniert und mit Migrationsstrategie.

## 9. Funktions- und Test-Mapping (R1)

Jede Muss-Anforderung erhält mindestens einen automatisierten oder reproduzierbaren manuellen Abnahmetest.

| Test-ID | Referenz | Testablauf | Erwartetes Resultat |
|---|---|---|---|
| T-001 | F-001 | Normales, verschlüsseltes, defektes PDF öffnen | Normales öffnet; Kennwortdialog; definierte Fehlermeldung |
| T-002 | F-002 | Drei Dokumente öffnen, Tab wechseln, eines schließen | Kein Inhalt/Dirty-State geht verloren |
| T-003 | F-003 | Zoom 50/100/200%, Fit Width, Scroll | Seite sichtbar, kein UI-Freeze, korrektes Zoomlabel |
| T-004 | F-004 | Miniatur und Lesezeichen anklicken | Erwartete Seite wird angezeigt |
| T-005 | F-005 | Definierten Satz suchen/markieren/kopieren | Fundstelle und kopierter Text stimmen |
| T-006 | F-006 | Seite 2 vor Seite 1, Seite 3 drehen, Seite 4 löschen | PDF enthält richtige Reihenfolge und Rotation |
| T-007 | F-007 | Zwei PDFs mergen, Seite 2 extrahieren, splitten | Ausgabe hat erwartete Seiten, im Fremdviewer lesbar |
| T-008 | F-008 | Text und PNG-Logo einfügen, speichern, erneut öffnen | Lage/Farbe/Text im Export korrekt |
| T-009 | F-009 | Highlight und Notiz anlegen | Darstellung nach PDF-Export korrekt |
| T-010 | F-010 | 20 Editierbefehle, 20x Undo und Redo | deterministisch ursprünglicher und Endzustand |
| T-011 | F-011 | `.ppdf` speichern, neu laden, Objekt verändern | Overlay editierbar; keine Duplikate |
| T-012 | F-012 | Seite 3 als PNG exportieren und drucken | PNG-Abmessungen plausibel; Druckdialog funktioniert |
| T-013 | F-013 | OCR 2 Seiten mit deutschem/englischem Text offline | Erwartete Suchbegriffe in Ausgabe auffindbar |
| T-014 | F-014 | Zwei Bilder in vorgegebener Reihenfolge zu PDF | 2 Seiten, korrekte Anordnung |
| T-015 | F-015 | Ungespeicherten Tab schließen und Crash-Recovery prüfen | Warnung und Wiederherstellungsszenario vorhanden |
| T-016 | F-016, N-013 | Hauptfenster 1280×720/1920×1080 + About kontrollieren | Exakter Copyright-Text sichtbar, HTTPS-Link öffnet |
| T-017 | F-017 | UI bei neuem Profil starten | Englisch vollständig, keine harten deutschen Texte |
| T-018 | F-018 | Korrupte PDF und OCR-Abbruch provozieren | Fehlercode, nutzerfreundliche Meldung, Log ohne Datenleck |
| T-019 | N-002, N-003 | Clean-VM ohne .NET-/OCR-Installationen; Netzwerk aus | R1-Funktionen nutzbar; kein Runtime-Installer nötig |
| T-020 | N-006 | Beim Export absichtlich Schreibfehler simulieren | Ursprung/Zieldatei unbeschädigt; kein falsches „Saved“ |
| T-021 | N-005 | PDF-JavaScript und externe Links im Test-PDF | Keine automatische Skriptausführung oder URL-Öffnung |
| T-022 | N-008 | Lizenz-/Abhängigkeitsinventar gegen ZIP prüfen | Alle Third-Party-Notices und nötigen Lizenztexte vorhanden |
| T-023 | N-010 | Messung auf Referenzgerät, wiederholte Läufe | Zielwerte aus Anforderungsanalyse werden berichtet |
| T-024 | F-011 | Standard-PDF in zwei externen Viewern öffnen | Erfolgreich, erwartete Seiten/Anmerkungen sichtbar |
| T-025 | Security | Manipuliertes `.ppdf` mit `../` und Zip-Bomb-Probe | Sichere Ablehnung; keine Dateien außerhalb Temp-Ziel |
| T-026 | N-004 | R1 Kernfunktionen offline, Netzwerkmonitor aktiv | Keine ungefragte Übertragung von Dokumentinhalten |

**Testmaterial:** Mindestens: textbasierte PDF, reine Bild-PDF, gemischtsprachiger Scan, querformatige und gedrehte Seiten, unterschiedliche Mediengrößen, Kennwort-PDF, defekte PDF, Unicode-Schriften, PDF mit Link und PDF-JavaScript, große PDF und manipuliertes `.ppdf`. Nur rechtlich nutzbare Testdateien im Repository ablegen.

## 10. Qualitätssicherung und Definition of Done

**Für jedes Feature:** (1) Anforderungs-ID im Ticket/Commit, (2) Code plus geeignete Tests, (3) englische UI-Texte, (4) reproduzierbarer manueller Check, (5) keine ungeklärten Lizenzen, (6) README/Help aktualisiert, (7) keine Regression bei Golden-PDF-Tests.

**Release-Gates R1:**

1. Release-Build in Windows-Builder ohne Fehler erstellt; `dotnet test` grün; Smoke-Tests ausgeführt.
2. ZIP wurde auf einer sauberen, unterstützten Windows-11-x64-VM entpackt und direkt gestartet.
3. Alle Muss-Tests T-001 bis T-026 bestanden bzw. nachvollziehbar mit Datum/Geräteangabe protokolliert; optional priorisierte Soll-Tests separat.
4. App enthält exakt den geforderten Copyright-Link und den separaten Hinweis auf Drittanbieter-Lizenzen.
5. Öffnen/Speichern/Projektformat/OCR funktionieren nachweislich ohne Internet und ohne zusätzliche Runtime-Installation.
6. Keine sicherheitskritischen Fehler, stillen Datenverluste oder falsch deklarierten PDF-Funktionen.
7. Alle verteilten Komponenten und ihre Lizenzen sind vollständig dokumentiert und redistributionsfähig.
8. Release-Datei, SHA-256-Prüfsumme, Versionsnummer und Kurzhandbuch sind verfügbar.

**Einschränkung zum Test T-015:** Eine vollständige Crash-Recovery-Implementierung ist aufwendig. Für R1 gilt mindestens: atomar gesicherte Projekt-Zwischenstände nach festgelegtem Intervall oder bei Bearbeitungen, Wiederherstellungsdialog nach unerwartetem Ende; nicht die Behauptung, jeder Tastenanschlag sei garantiert gerettet.

## 11. Entwicklungsplan und Übergabe an ChatGPT Work

| Sprint/Meilenstein | Technische Lieferung | Verbindlicher Gate-Test |
|---|---|---|
| M0 – Grundlagen | Repo, Projektlösung, Build, UI-Shell, Ressourcensystem, CI, Lizenzinventar | Clean Build, Hauptfenster und Copyrightlink |
| M1 – Viewer | PDFium öffnen/rendern, Tabs, Zoom, Thumbnails, Textsuche | T-001 bis T-005 |
| M2 – Seitenoperationen | qpdf-Adapter, Merge/Split/Reorder/Rotate, Save PDF | T-006, T-007, T-020, T-024 |
| M3 – Editor | Overlays, Text/Bild/Markierungen, Undo, Projektformat | T-008 bis T-011, T-025 |
| M4 – OCR & Export | Tesseract Offline DE/EN, Image→PDF, PNG/Print | T-012 bis T-014 |
| M5 – Stabilisierung | Crash-Wiederherstellung, Security, Lizenzprüfung, Paketierung | T-015 bis T-026, Clean-VM |
| R1 – Freigabe | Portable ZIP, EXE, Prüfsumme, Release Notes, Benutzerhilfe | Alle R1-Muss-Tests bestanden |

**Arbeitsauftrag für Work:** Immer die beiden Version-1.0-Dokumente vollständig als Projektbaseline verwenden. Zu Beginn Repo und Build-Umgebung prüfen. Dann M0 → M1 → … → R1 implementieren, nach jedem Meilenstein Tests ausführen und jeweils den funktionierenden Code und Build-Artefakte sichern. Kein M2 beginnen, solange ein struktureller M1-Fehler die Grundlage betrifft. Geänderte Annahmen in `docs/DECISIONS.md` notieren, statt das Pflichtenheft still umzuschreiben. Unfertige Werkzeuge im UI klar als „Not available in this version“ kennzeichnen oder ausblenden.

**Work muss als Endergebnis liefern:** vollständiges Repository inklusive Sourcecode, lokaler Windows-Build-Anleitung, automatisierter Tests, gefixtem Abhängigkeits-/Lizenzinventar, `PerplexPDF.exe`, `PerplexPDF-1.0.0-win-x64-portable.zip`, `SHA256SUMS.txt`, einem Beispiel-PDF und einem kurzen englischen Benutzerhandbuch. Nach Möglichkeit zusätzlich einen reproduzierbaren Release-Workflow. Eine ZIP-Datei darf erst als „fertig“ bezeichnet werden, wenn sie tatsächlich erzeugt und unter Windows gestartet wurde.

## 12. Erweiterungskonzept R2/R3/R4

### R2: Native Bearbeitung und dokumentensichere Funktionen

Austauschbarer `IAdvancedPdfEditEngine`-Adapter. Vor Toolentscheidung sind Testfälle mit verschiedenen Fonts, eingebetteten Objekten, transformierten Texten und gescannten Inhalten anzulegen. Testkriterium für echte Textbearbeitung: alter Text ist im PDF-Inhalt nicht mehr vorhanden und neuer Text bleibt sichtbar/selektierbar. Whiteout-Overlay ist kein Ersatz.

**R2 Schwärzung:** `IRedactionEngine` darf nur als sicherheitsgeeignete Funktion freigegeben werden. Entweder eine validierte Inhaltsbereinigung auf PDF-Objektebene oder ein explizit ausgewiesener *sicherer Raster-Neuaufbau*, bei dem alle betroffenen Seiten in eine neue PDF ohne Originalinhalte neu erstellt werden. Raster-Neuaufbau zerstört u. U. Textsuche, Links, Vektorgrafiken, Barrierefreiheits-Tags und Signaturen; daher vor Ausführung deutliche Warnung. Post-Export-Validierung prüft extrahierten Text, eingebettete Dateien, Metadaten und alte Objekte. Abnahme durch unabhängige PDF-Analyse.

**R2 Formulare:** AcroForm-Felder lesen, schreiben und mit Fremdviewer testen; XFA wird gesondert behandelt und darf nicht ungeprüft versprochen werden. Formular-JavaScript-Ausführung nur nach separatem Sicherheitskonzept, standardmäßig deaktiviert.

### R3: Professionelle Compliance-Werkzeuge

Digitale Signaturen benötigen geprüfte PDF-Signing-Implementierung, geeignete Zertifikate und Validierung inkl. Änderungsstatus. PDF/A braucht einen unabhängigen Validator. Barrierefreiheit umfasst mehr als OCR: Struktur-Tags, Alternativtexte, Tabellen, Lesereihenfolge. DOCX/XLSX/PPTX-Konvertierung erfordert je Format festgelegte Genauigkeits- und Testkriterien. Die Erfüllung dieser Funktionen darf nicht aus bloßem Vorhandensein von UI-Schaltflächen abgeleitet werden.

### R4: Optionale Online-Dienste

KI/Cloud/Signaturdienst sind *separate Module* mit Opt-in, klaren Kosten, Datenschutzhinweisen, Zugangsdatenverwaltung und transparentem Datenfluss. Offline-R1 bleibt eigenständig verwendbar. Keine Fremdmarke ohne ausdrückliche Vereinbarung integrieren.

## 13. Risiko- und Entscheidungslog

| Nr. | Entscheidung / Risiko | Technische Konsequenz |
|---|---|---|
| D-01 | Abhängigkeiten für Endnutzer bündelbar | Portable ZIP statt Runtime-Setup |
| D-02 | Vollständige Acrobat-Parität ist kein R1-Ziel | R2/R3/R4 explizit getrennt |
| D-03 | Proprietäre Verbreitung möglich halten | AGPL-Komponenten nur nach Rechts-/Lizenzfreigabe |
| D-04 | Quelle nicht unbemerkt verändern | .ppdf als editierbares Projekt, PDF als Export |
| D-05 | Keine vorgetäuschte sichere Schwärzung | Schwärzung in R2 mit eigenen Security-Gates |
| D-06 | Keine falschen Signaturversprechen | Kryptografische Signaturen erst R3 |
| D-07 | OCR offline garantieren | Tesseract und Sprachdaten bündeln |
| D-08 | PDF-Parsing von untrusted data | Schutzmaßnahmen, Testkorpus, native Updates |
| D-09 | WPF/.NET 10 Windows-10-Lifecycle | Windows 11 garantieren, Windows 10 nur unterstützt/validiert |
| D-10 | Release-Artefakte nur nach Funktionsnachweis | Windows CI/VM, Hashes, Downloadpaket |

## 14. Nachverfolgbarkeit und Änderungsverfahren

Jedes Feature trägt eine **F-/N-ID** aus PP-REQ-001. Im Code verweist mindestens der Projekt-/Use-Case-Name oder das Ticket auf die Anforderungs-ID; jeder Muss-Punkt hat einen **T-Test** in Abschnitt 9. Anforderungen dürfen nicht nachträglich als „erfüllt“ ausgewiesen werden, wenn nur ein Prototyp existiert. Jede inhaltliche Änderung aktualisiert Versionsnummer, Änderungsdatum, Abhängigkeiten und (wenn nötig) die R1-Abnahmetests in beiden Dokumenten.

Für den künftigen Work-Auftrag sind diese zwei Dateien **gleichrangige Eingaben**. Vorrang bei Konflikten: rechtliche Sicherheit > technische Datenintegrität > verbindliche Muss-Anforderungen > Soll/Kann-Ziele > Designpräferenzen.

## 15. Verbindliche Dokumentations- und Release-Ausgaben

- `README.md`: Zweck, bekannte Grenzen, Build, Offline-Funktion, Release-Artefakte.
- `docs/USER_GUIDE_EN.md`: englische Bedienungsanleitung mit Screenshot der **eigenen** UI nach Fertigstellung.
- `docs/DECISIONS.md`: technische Entscheidungen mit Datum und Begründung.
- `docs/CHANGELOG.md`: Änderungen nach Version.
- `docs/RELEASE_CHECKLIST.md`: signierte/abgehakte Abnahmetests.
- `DEPENDENCIES.md`: Paketname, Version, Lizenz, URL, Hash, Verwendungszweck.
- `THIRD_PARTY_NOTICES.txt` und `licenses/`: alle nötigen Rechtehinweise der tatsächlichen Artefakte.
- `packaging/create_release.ps1`: Windows-Build, Kopie nativer Dateien, ZIP und SHA-256.
- `artifacts/release/PerplexPDF-1.0.0-win-x64-portable.zip`: freigegebenes Downloadpaket.

## 16. Beleg- und Architekturquellen

1. Microsoft, Single-file deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/
2. Microsoft, .NET for Windows: https://learn.microsoft.com/en-us/dotnet/core/install/windows
3. Chromium PDFium BSD-3: https://github.com/chromium/pdfium/blob/main/LICENSE
4. qpdf, Struktur- und Overlay-Funktionen: https://qpdf.sourceforge.io/ und https://github.com/qpdf/qpdf/blob/main/manual/cli.rst
5. Tesseract OCR und traineddata: https://tesseract-ocr.github.io/tessdoc/Installation
6. PdfPig Apache-2.0: https://uglytoad.github.io/PdfPig/
7. SkiaSharp MIT und PDF-Ausgabe: https://github.com/mono/SkiaSharp/blob/main/LICENSE.txt und https://learn.microsoft.com/de-de/dotnet/api/skiasharp.skdocument.createpdf?view=skiasharp
8. PDFsharp, Lizenzänderung ab 6.5: https://docs.pdfsharp.net/General/License/License.html

*Vor Umsetzung Versionsstände und Abhängigkeiten erneut prüfen. Das Pflichtenheft ist eine technische Produktspezifikation, keine Rechtsberatung zu Marken, Patenten oder Open-Source-Lizenzen.*
