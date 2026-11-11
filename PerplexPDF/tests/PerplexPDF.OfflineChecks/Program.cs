using PerplexPDF.Tests;

var failed = 0;
await Run("T-003 coordinate transforms", Sync(CoreScenarios.Rotation));
await Run("T-010 thirty undo and redo steps", Sync(CoreScenarios.UndoRedo));
await Run("T-007 page range parser", Sync(CoreScenarios.Ranges));
await Run("T-011 .ppdf round-trip and edit", CoreScenarios.ProjectRoundTrip);
await Run("T-015 recovery ownership", Sync(CoreScenarios.Recovery));
await Run("T-020 atomic output preservation", CoreScenarios.AtomicCommit);
await Run("T-025 archive traversal, symlink and bomb", CoreScenarios.HostileArchive);

if (args.Length == 5)
{
    var native = new NativeScenarios(args[0], args[1], args[2], args[3], args[4]);
    await Run("T-001/004/005 PDFium open, bookmarks, rotated search and password", native.OpenSearchAndPassword);
    await Run("T-006/007 qpdf reorder, rotation, extraction, merge", native.Structure);
    await Run("PDF to Word and EPUB with text, page images and cancellation", native.ConvertDocuments);
    await Run("F-101 replace an existing PDF text object", native.ReplaceOriginalPdfText);
    await Run("Structured Word export with headings, tables, lists, images, contents and footer", native.StructuredWord);
    await Run("T-013 English OCR and unselected-page preservation", () => native.Ocr(german: false));
    await Run("T-018 OCR cancellation", native.CancelOcr);
    if (File.Exists(Path.Combine(args[3], "deu.traineddata")))
        await Run("T-013 bilingual OCR", () => native.Ocr(german: true));
    else
        Console.WriteLine("SKIP T-013 German OCR: no deu.traineddata in the supplied local tessdata directory.");
}
else
{
    Console.WriteLine("Native checks require 5 paths: PDFium library, qpdf, tesseract, tessdata, fixtures.");
    if (args.Length != 0)
        return 2;
}

return failed == 0 ? 0 : 1;

async Task Run(string title, Func<Task> run)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {title}");
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {title}\n{error}");
    }
}

static Func<Task> Sync(Action action) => () => { action(); return Task.CompletedTask; };
