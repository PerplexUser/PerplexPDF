# PerplexPDF – Anforderungsanalyse

**Dokument-ID:** PP-REQ-001  
**Version:** 1.0 · 08.11.2026  
**Status:** Entwicklungsbaseline / zur Freigabe  
**Produkt:** PerplexPDF – PDF Editor for Windows  
**Urheberhinweis:** [© by www.perplex.click](https://www.perplex.click/) - Michael Lappenbusch

## 1. Ziel und Management-Zusammenfassung

PerplexPDF soll eine eigenständige, lokal ausführbare PDF-Anwendung für Windows sein. Das Produkt orientiert sich an den *Funktionskategorien* von Adobe Acrobat Pro, übernimmt jedoch weder dessen Oberfläche, Produktkennzeichen noch geschützte Gestaltungsbestandteile. Die erste auslieferbare Version (R1) soll vollständige Kernabläufe für Lesen, Organisieren, Ergänzen, Kommentieren, OCR und PDF-Export beherrschen. Weiterführende Profi-Funktionen werden bewusst gestaffelt entwickelt.

**Nicht verhandelbar:** englischsprachige Oberfläche; optional Deutsch; als kompilierte Windows-x64-Anwendung herunterladbar; keine nachträgliche Installation von .NET, Python, OCR-Sprachdaten oder PDF-Werkzeugen durch Endnutzer; gut sichtbarer anklickbarer Copyright-Vermerk `© by www.perplex.click - Michael Lappenbusch` innerhalb der App und unter **Help > About PerplexPDF**. Der Link führt zu `https://www.perplex.click/`.

**Prinzip:** Lieber eine nachvollziehbar begrenzte, technisch korrekte Funktion als eine scheinbare Acrobat-Funktion, die Inhalte nur verdeckt, Signaturen fälscht oder PDF-Strukturen beschädigt.

## 2. Ausgangslage und Produktvision

Viele PDF-Anwendungen können Dokumente lediglich anzeigen oder mit kostenpflichtigen Cloud-Diensten bearbeiten. PerplexPDF soll ein klar strukturiertes, überwiegend offline nutzbares Windows-Werkzeug bieten. Die Anwendung wird so spezifiziert, dass ChatGPT Work oder ein anderes Entwicklungsteam einzelne Module und Abnahmetests aus den Dokumenten schrittweise in Code überführen kann.

**Produktversprechen für R1:** „Open, review, organize, annotate and export PDFs — locally, with no runtime installation.“ Dies ist eine Zielaussage, kein Nachweis einer bereits existierenden Windows-Binärdatei.

## 3. Zielgruppen und Nutzungsszenarien

| Zielgruppe | Hauptaufgabe | Erwartung |
|---|---|---|
| Büro und Verwaltung | Dokumente zusammenführen, sortieren, kommentieren | Schnelle, verständliche Bedienung |
| Selbstständige und KMU | Angebote, Anhänge, Rechnungen bearbeiten | Offline-Funktion und nachvollziehbarer Export |
| Bildung und Privatnutzer | Lesen, markieren, scannen, OCR | Geringe Einstiegshürde |
| Dokumentenprofis | Formulare, Schwärzung, Vergleich, Signaturen | Spätere Pro-Ausbaustufen mit verifizierter Korrektheit |
| Technische Entwicklung/Work | Funktionsblöcke reproduzierbar implementieren | Eindeutige IDs, Schnittstellen und Testfälle |

### 3.1 Hauptanwendungsfälle

1. Eine Nutzerin öffnet eine 40-seitige PDF, sucht einen Begriff, markiert eine Stelle und speichert die kommentierte PDF.
2. Ein Nutzer führt drei PDF-Dateien zusammen, verschiebt Seiten, dreht einen Scan und exportiert das Resultat.
3. Ein Büro ergänzt auf einer vorhandenen PDF Textfelder, ein Logo und ein Wasserzeichen ohne das ursprüngliche Dokument unbemerkt zu verändern.
4. Eine gescannte englische oder deutsche PDF wird offline per OCR in eine durchsuchbare Kopie umgewandelt.
5. Die Nutzerin speichert den Arbeitszustand als PerplexPDF-Projekt und gibt eine standardkonforme PDF für andere Empfänger aus.
6. Eine spätere Pro-Version schwärzt vertrauliche Inhalte *nachweislich irreversibel* und validiert das Ergebnis.

## 4. Anforderungsklassen, Releases und Priorisierung

**Priorität:** M = Muss / S = Soll / K = Kann. **Release:** R1 = nutzbare erste Version, R2 = fortgeschrittene Bearbeitung, R3 = Profi-Werkzeuge, R4 = optionale Integrationen. „R2/R3“ bedeutet ausdrücklich **nicht** in R1 versprochen.

| Release | Ziel | Abnahmebild |
|---|---|---|
| R1 · 1.0 | Desktop-Grundprodukt | Portable ZIP mit startbarer EXE; PDF-Alltagsabläufe; EN UI; OCR; PDF-Export |
| R2 · 1.5 | Erweiterte Bearbeitung | PDF-Textbearbeitung mit passender Engine, Formulare, stärkere Sicherheitswerkzeuge |
| R3 · 2.0 | Acrobat-nahe Profi-Funktionen | Signaturen, Vergleich, Archivierung/Barrierefreiheit, Stapelaktionen |
| R4 · später | Optionale Cloud/KI | Nur nach gesonderter Entscheidung und Datenschutzprüfung |

## 5. Funktionale Anforderungen – R1

| ID | Prio | Anforderung | Akzeptanzkern |
|---|---|---|---|
| F-001 | M | PDF öffnen: Dateidialog, Drag & Drop, Dateipfad | Normale und passwortgeschützte PDFs; verständliche Fehlermeldungen |
| F-002 | M | Mehrere Dokumente als Tabs | Zwischen offenen PDFs ohne Datenverlust wechseln |
| F-003 | M | PDF darstellen: Einzelseite, kontinuierlich, Fit Width/Page, Zoom | Fließendes Blättern; Seitenzahl und Zoomanzeige korrekt |
| F-004 | M | Seitenminiaturen, Seitennavigation und Lesezeichenanzeige | Seiten direkt anklickbar; vorhandene Lesezeichen lesbar |
| F-005 | M | Text suchen, Treffer hervorheben; Text auswählen und kopieren | Textbasierte PDFs korrekt durchsuchbar |
| F-006 | M | PDF-Seiten rotieren, löschen und umsortieren | Export entspricht der angezeigten Seitenreihenfolge |
| F-007 | M | PDF zusammenführen und Seitenbereiche extrahieren/aufteilen | Ergebnis öffnet in externen PDF-Viewern |
| F-008 | M | Overlay-Text, Formen und Bilder ergänzen | Position und Erscheinung im Export erhalten |
| F-009 | M | Markieren, Unterstreichen und Notizkommentare | Kommentare sichtbar; editierbare Projekt-Repräsentation |
| F-010 | M | Undo/Redo für alle R1-eigenen Änderungen | Mindestens 20 Bearbeitungsschritte ohne Projektbeschädigung |
| F-011 | M | „Save PDF As…“ und „Save Project As…“ | Standard-PDF kompatibel; .ppdf zur verlustarmen Weiterbearbeitung |
| F-012 | M | Druck und Export einzelner Seiten als PNG | Korrekte Seitengröße und Blattreihenfolge |
| F-013 | M | OCR Englisch und Deutsch, vollständig offline | Suchbare OCR-Kopie eines gescannten Dokuments |
| F-014 | M | PDF aus JPG/PNG-Bildern erstellen | Reihenfolge und Bildausrichtung wählbar |
| F-015 | M | Unsaved-Changes-Warnung, Wiederherstellungsentwurf | Keine stillen Datenverluste bei Schließen/Absturz |
| F-016 | M | Sichtbarer verlinkter Copyright-Vermerk | Text im Hauptfenster und „About“ immer auffindbar |
| F-017 | M | Englisch als Standardsprache, Ressourcenstruktur für Deutsch | Kein ungeklärter deutscher Text in EN-R1 |
| F-018 | M | Fehlerdiagnostik und Protokollierung lokal | Aussagekräftige Meldung; kein ungefragter Upload |
| F-019 | S | Kennwortschutz beim Export | Wenn implementiert: mit unabhängigem Viewer prüfbar |
| F-020 | S | Metadaten (Titel/Autor/Stichwörter) einsehen/bearbeiten | Nach Export erneut auslesbar |

**Fachliche Einschränkung R1:** An vorhandenen PDF-Inhalten werden zunächst Seitenstrukturen sowie neu hinzugefügte Elemente bearbeitet. Beliebiger alter PDF-Text wird nicht als „direkt editierbar“ beworben. Ein weißes Rechteck plus neuer Text gilt **nicht** als echte Textlöschung oder sichere Schwärzung. Das Projektformat `.ppdf` bewahrt die ursprüngliche PDF und die Bearbeitungsebene separat; ein exportiertes `.pdf` enthält die für die Kompatibilität angewendeten Änderungen, muss jedoch nicht alle Elemente erneut als PerplexPDF-Objekt editierbar halten.

## 6. Erweiterte funktionale Anforderungen – R2 bis R4

| ID | Release | Prio | Erweiterung / Voraussetzung |
|---|---|---|---|
| F-101 | R2 | M | Bestehende PDF-Texte und Bilder objektbasiert ersetzen/verschieben, soweit technisch möglich; komplexe PDFs mit Warnung oder Fallback |
| F-102 | R2 | M | Ausfüllbare AcroForm-Felder erkennen, bearbeiten und persistieren |
| F-103 | R2 | S | Einfache interaktive Formulare neu erstellen, Pflichtfelder, Dropdowns, Checkboxen |
| F-104 | R2 | M | Echte Schwärzung einschließlich Entfernung zugrunde liegender Daten; automatisierter Negativtest |
| F-105 | R2 | S | PDF-Kennwortverschlüsselung, Berechtigungsmanagement und Metadatenbereinigung |
| F-106 | R2 | S | Kopf-/Fußzeilen, Seitenzahlen, Wasserzeichen und Stempelvorlagen |
| F-107 | R2 | S | PDF-Ausgabeoptimierung und Kompression mit transparenter Qualitätsauswahl |
| F-108 | R2 | K | Bildbasierte und textbasierte Dokumentvergleiche |
| F-201 | R3 | S | Kryptografische PDF-Signatur und unabhängige Verifikation, Zertifikatsverwaltung |
| F-202 | R3 | S | Signaturfelder, Signaturprüfbericht; E-Signatur-Cloudworkflow nur mit geeignetem Dienst |
| F-203 | R3 | S | PDF/A-Konvertierung und Validierung, optional PDF/X/Preflight |
| F-204 | R3 | S | Accessibility: Tags, Lesereihenfolge, Alternativtexte, Prüfbericht |
| F-205 | R3 | S | Automatisierte Stapelaktionen, wiederholbare Workflows und Bates-Nummerierung |
| F-206 | R3 | K | PDF in DOCX/XLSX/PPTX konvertieren: gesonderte Qualitätsspezifikation, ggf. lizenzierter Konverter |
| F-207 | R3 | K | PDF-Portfolios, fortgeschrittene Ebenen und erweiterte Formularberechnungen |
| F-301 | R4 | K | Dokumentenzusammenfassung, Chat und KI-Befehle; Opt-in, externe Dienste nur mit Einwilligung |
| F-302 | R4 | K | Cloud-Speicherung, Zusammenarbeit, elektronische Unterschriftsdienste via Provider |

**Abhängigkeitsregel:** R2-F-101 darf nicht allein mit Overlay-Maskierung als fertig erklärt werden. R2-F-104 darf nicht mit schwarzer Zeichenfläche auf Originaltext abgenommen werden. R3-F-201 erfordert echte Kryptografie und unabhängige Validierung.

## 7. Nichtfunktionale Anforderungen

| ID | Priorität | Vorgabe und messbarer Maßstab |
|---|---|---|
| N-001 | M | **Windows:** R1 verpflichtend Windows 11 x64; Windows 10 ausschließlich auf noch durch Microsoft/.NET unterstützten Enterprise-/LTSC-Konfigurationen optional testen |
| N-002 | M | **Keine Endnutzer-Installationen:** .NET-Runtime, PDF-/OCR-Binaries und EN/DE-OCR-Daten werden im Download mitgeliefert |
| N-003 | M | **Offline:** Öffnen, Anzeige, Speichern, Seitenoperationen, Anmerkungen und OCR ohne Netzwerk nutzbar |
| N-004 | M | **Datenschutz:** Dokumente bleiben lokal, Telemetrie standardmäßig aus, kein stiller Datei-Upload |
| N-005 | M | **Sicherheit:** Keine aktive PDF-JavaScript-Ausführung, gehärtete Pfad-/Dateiverarbeitung, signalisierte Fehler bei beschädigter PDF |
| N-006 | M | **Integrität:** Original nie still überschreiben; vor Export sichere temporäre Datei, Prüfung, dann atomarer Austausch soweit möglich |
| N-007 | M | **Testbarkeit:** Automatisierte Unit-, Integrations- und Windows-Smoke-Tests; Golden-PDF-Testkorpus |
| N-008 | M | **Lizenzen:** Nur freigegebene und für die geplante Verbreitung geeignete Abhängigkeiten; Third-Party-Notices im Paket |
| N-009 | M | **Ergonomie:** Tastaturbedienung, eindeutige Tooltips, dunkle/helle Darstellung optional; keine Acrobat-UI-Kopie |
| N-010 | S | **Leistung:** Auf Referenzgerät Win11, 4 Kerne, 16 GB RAM, SSD: Start < 8 s, 50-seitige 10-MB-PDF laden < 5 s, typische Seite bei 150 dpi < 2 s |
| N-011 | S | **Robustheit:** R1-Testumfang bis 200 Seiten bzw. 50 MB; größere Dateien werden nicht still beschädigt, ggf. Hinweismeldung |
| N-012 | S | **Zugänglichkeit:** Tastaturnavigation, ausreichende Kontraste und sinnvolle UI-Automation-Namen |
| N-013 | M | **Urhebervermerk:** Im Hauptfenster dauerhaft lesbar, zusätzlich im About-Dialog als anklickbarer HTTPS-Link |
| N-014 | S | **Wartbarkeit:** Strikte Trennung UI, Domäne, PDF-Engines und Verpackung; austauschbare Adapter |

Leistungswerte sind R1-Abnahmesollwerte und müssen später auf dem definierten Referenzgerät gemessen werden, nicht bloß vermutet werden.

## 8. Benutzerführung und Produktidentität

**Sprache:** Alle sichtbaren Menütexte, Fehlermeldungen, Shortcuts-Hinweise und Dialoge in R1 auf Englisch. Die Architektur muss `en-US` und später `de-DE` per Ressourcen ermöglichen. Spracheinstellung unter **Settings > Language**; ein deutscher Übersetzungssatz darf unvollständig sein, dann wird englischer Fallback verwendet.

**Primäre Navigation:** `File`, `Edit`, `View`, `Pages`, `Annotate`, `Tools`, `Help`. Linke Leiste: `Thumbnails`, `Bookmarks`. Mitte: Dokumentseiten. Rechte Leiste: `Properties`, `Comments`. Obere Werkzeugleiste: `Open`, `Save PDF`, `Save Project`, `Undo`, `Redo`, `Search`, `Zoom`, `Add Text`, `Image`, `Highlight`, `OCR`. Statuszeile: `Page N / M`, Zoom, Änderungsstatus und durchgehend der Copyright-Link.

**Markenregeln:** Eigenes Logo, eigenständige Icons, Schrift- und Farbsysteme. Keine Nutzung von Adobe-Logos, Adobe-Schriftarten oder übernommenen Screenshots im Produkt. Referenzen dienen nur der Funktionsanalyse. Für `PerplexPDF` und `perplex.click` sind Namens- und Markenrechte vor Veröffentlichung separat zu prüfen.

## 9. Daten-, Sicherheits- und Betriebsgrenzen

- Unterstütztes R1-Dokumentformat: PDF 1.x und moderne PDF-Varianten, soweit die ausgewählte Engine sie öffnen kann; defekte/XFA-/3D-/signierte Spezial-PDFs ggf. als „unsupported“ kennzeichnen.
- Internes Projektformat: `.ppdf` (ZIP-Container mit `manifest.json`, `source.pdf`, `overlays.json`); keine ungesicherten absoluten Dateipfade im Archiv.
- Ausgabe: Standard-PDF und PNG. OCR erfasst primär gedruckte Texte, nicht verlässlich Handschrift.
- R1 speichert nicht ungefragt in die Cloud; externer Weblink nur nach aktivem Nutzerklick.
- R1 darf fremde digitale Signaturen nicht stillschweigend als gültig weiterführen, wenn die Exportoperation sie technisch ungültig macht.
- Lizenzhinweise und Copyrights der eingebundenen Open-Source-Komponenten werden getrennt vom Produkt-Copyright mitgeliefert.

## 10. Annahmen, Risiken und offene Entscheidungen

| ID | Thema | Risiko / Entscheidungsvorschlag |
|---|---|---|
| RSK-01 | Acrobat-Pro-Parität | Hoch: komplexe Funktionen benötigen eigene PDF-Engine oder kommerzielle SDKs; Phasenansatz verbindlich |
| RSK-02 | Lizenzierung | Hoch: PDFsharp ab 6.5 laut Hersteller AGPL/kommerziell; ohne geklärte Lizenz nicht als Standardabhängigkeit einplanen |
| RSK-03 | PDF-Eingabequalität | Hoch: fehlerhafte Fonts, Scans und kaputte PDF-Objekte; Warnungen/Regressionstests |
| RSK-04 | Native Auslieferung | Mittel: PDFium, qpdf, Tesseract und Schrift-/OCR-Daten müssen gebündelt und getestet werden |
| RSK-05 | Signaturen | Hoch: Verifikation und Erhalt nach Export komplex; erst R3 |
| RSK-06 | Sichere Schwärzung | Hoch: Überdecken ist nicht Entfernen; R2 nur nach Sicherheitsnachweis |
| RSK-07 | Archivierung/Barrierefreiheit | Mittel/Hoch: PDF/A und getaggte PDFs brauchen Validierungswerkzeuge; erst R3 |
| RSK-08 | Windows-10-Lebenszyklus | Mittel: Windows-10-Consumer-Versionen sind nicht mehr aktueller Haupt-Supportpfad; Win11 als Garantieziel |

**Für den Start getroffene Projektannahmen:** proprietärer Vertrieb soll möglich sein; keine AGPL-Pflicht für PerplexPDF eingehen; R1 wird ohne Konto, Abonnement, Backend oder verpflichtende Cloud gebaut. Quellcode, Tests und Releases sollen später dem Projektinhaber übergeben werden. Diese Annahmen können formal geändert werden, dürfen nicht still überschrieben werden.

## 11. Abnahmekriterien auf Produktebene

- **A-01:** Ein frisch aufgesetzter, unterstützter Windows-11-x64-Rechner ohne installierte .NET- oder OCR-Werkzeuge startet PerplexPDF aus dem entpackten Download.
- **A-02:** Hauptfenster ist englischsprachig. Dauerhaft sichtbar und anklickbar: `© by www.perplex.click - Michael Lappenbusch`; derselbe Hinweis erscheint unter Help > About PerplexPDF.
- **A-03:** Muster-PDF öffnen, durchsuchen, 2 Seiten umsortieren, ein Textfeld und eine Markierung hinzufügen, als PDF exportieren und mit einem anderen PDF-Viewer kontrollieren.
- **A-04:** Zwei PDFs zusammenführen, Teilseiten extrahieren und neue PDFs validieren.
- **A-05:** Ein gescannter Beispieltext ist nach offline OCR in der ausgegebenen PDF suchbar; Vorher-Nachher-Test dokumentiert.
- **A-06:** Projekt `.ppdf` speichern, schließen, neu öffnen, Overlay-Text erneut verschieben und PDF neu exportieren.
- **A-07:** Kein Netzverkehr für Kernfunktionen; kein erzwungener Download fehlender Abhängigkeiten.
- **A-08:** Git-Repository mit Build-Skript, Tests, Lizenzen, README und auslieferbarem Windows-Artefakt vorhanden.
- **A-09:** Eingrenzungen (insbesondere keine native Alttext-Bearbeitung und keine sichere Schwärzung in R1) sind in UI und Dokumentation korrekt ausgewiesen.

## 12. Projektabgrenzung und Abhängigkeit zum Pflichtenheft

Die **Anforderungsanalyse** beschreibt das *Was und Warum*. Das gesonderte Dokument **PP-SPEC-001: Pflichtenheft** definiert das *Wie*: Architektur, konkrete Dateiformate, Implementierungspfade, Schnittstellen, Testfälle, Releaseprozesse und den Übergabeauftrag für ChatGPT Work. Bei Widersprüchen gilt: Sicherheits- und Lizenzregeln zuerst; Featureumfang folgt der zugeordneten Releasephase; neue Entscheidungen werden per Versionsänderung festgehalten.

## 13. Quellen und technische Prüfhinweise

- Microsoft, *Publish .NET applications / single-file deployments*: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/
- Microsoft, *.NET on Windows – supported versions*: https://learn.microsoft.com/en-us/dotnet/core/install/windows
- PDFium, Lizenz: https://github.com/chromium/pdfium/blob/main/LICENSE
- qpdf, Möglichkeiten und Lizenz: https://qpdf.sourceforge.io/ und https://github.com/qpdf/qpdf/blob/main/manual/cli.rst
- Tesseract OCR, Installation und Apache 2.0: https://tesseract-ocr.github.io/tessdoc/Installation
- PdfPig, Apache 2.0 und Lesefunktionen: https://uglytoad.github.io/PdfPig/
- SkiaSharp, Lizenz: https://github.com/mono/SkiaSharp/blob/main/LICENSE.txt
- PDFsharp, aktuelle Lizenzbedingungen (ab 6.5 AGPL/kommerziell): https://docs.pdfsharp.net/General/License/License.html

*Alle Produkt- und Bibliotheksdaten sind am 08.11.2026 recherchierte Ausgangswerte. Vor Implementierung und Auslieferung sind tatsächliche Versionen, Lizenzen, Security Advisories und Nutzungsrechte nochmals zu verifizieren.*
