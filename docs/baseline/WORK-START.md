# PerplexPDF – Startauftrag für ChatGPT Work

**Basisdokumente:**
- `PerplexPDF_Anforderungsanalyse.md` (PP-REQ-001, Version 1.0)
- `PerplexPDF_Pflichtenheft.md` (PP-SPEC-001, Version 1.0)

## Direkt verwendbarer Work-Auftrag

> Entwickle **PerplexPDF 1.0** als reale, kompilierte Windows-x64-Desktopanwendung auf Grundlage der beiden beigefügten PerplexPDF-Dokumente. Lies beide vollständig und übernimm ihre Releasegrenzen, IDs, Sicherheits- und Lizenzregeln verbindlich. Die Anwendungssprache ist Englisch, Deutsch optional. Verwende bevorzugt C# / .NET 10 / WPF und die im Pflichtenheft spezifizierte modulare Architektur, sofern keine nachgewiesene bessere Alternative besteht.
>
> Arbeite nach den Meilensteinen **M0 bis M5**. Implementiere zuerst eine startbare Shell mit dem immer sichtbaren, anklickbaren Copyrighttext **© by www.perplex.click - Michael Lappenbusch**, der auf `https://www.perplex.click/` führt. Baue dann PDF-Viewer, Seitenoperationen, editierbare Overlays/Projekte, OCR und Export. Setze die Acceptance-Tests um und führe sie aus. Behaupte keine erfolgreiche Umsetzung nicht vorhandener oder ungetesteter Features.
>
> Erstelle zum Schluss die kompilierten Windows-Artefakte (`PerplexPDF.exe` und `PerplexPDF-1.0.0-win-x64-portable.zip`), eine SHA-256-Prüfsumme, Quellcode, Release Notes, Lizenzinventar und Benutzeranleitung. Auf dem Zielrechner dürfen keine zusätzlichen Laufzeitpakete installiert werden müssen. Verifiziere den Download auf einer sauberen unterstützten Windows-VM. Wenn die Build-Umgebung nicht Windows ist oder ein benötigtes SDK fehlt, dokumentiere die konkrete Blockade und liefere zunächst den vollständig reproduzierbaren Code-/Buildstand; gib keine ungetestete Binärdatei als fertig aus.
>
> Für R2/R3 nur Schnittstellen und späteren Backlog vorbereiten. Insbesondere darf das Verdecken von Inhalt nicht als echte Textlöschung/Schwärzung verkauft werden und eine gezeichnete Unterschrift nicht als kryptografische Signatur. Achte auf die **AGPL-/kommerzielle Lizenzfrage bei neuerem PDFsharp** und nimm keine unfreiwilligen Lizenzverpflichtungen in Kauf.

## Erwarteter Endzustand

Das Repository enthält Anwendung, Tests, technische und lizenzrechtliche Dokumentation, Build-/Release-Skript und ein nachweisbar unter Windows startbares Portable-Paket. Bei jeder nicht erfüllten Muss-Anforderung ist ein explizites Restpunkt-Protokoll zu führen.

## Abgrenzung

Dieses Startdokument **beauftragt eine spätere Work-Entwicklung**, erstellt aber selbst keinen Quellcode oder Windows-Installer. Die aktuelle Lieferung enthält ausschließlich die Planungsunterlagen.
