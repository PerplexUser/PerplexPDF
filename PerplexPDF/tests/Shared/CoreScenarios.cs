using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using PerplexPDF.Infrastructure.Store;

namespace PerplexPDF.Tests;

internal static class CoreScenarios
{
    public static void Rotation()
    {
        var geometry = new PageGeometry(612, 792);
        foreach (var angle in new[] { 0, 90, 180, 270 })
        {
            var transform = new PageTransform(geometry, angle);
            var rect = new PdfRect(45, 90, 100, 70);
            Check.That(transform.ToSource(transform.ToDisplay(rect)) == rect, $"Rotation {angle} lost geometry.");
        }
        var rotated = new PageTransform(geometry, 90);
        Check.That(rotated.Display == new PageGeometry(792, 612) &&
                   rotated.ToDisplay(new PdfPoint(0, 0)) == new PdfPoint(0, 0), "90° is not clockwise.");
    }

    public static void UndoRedo()
    {
        var project = DocumentProject.Create(new string('a', 64), 4);
        var history = new ProjectHistory(project);
        var page = project.Pages[2].Id;
        for (var index = 0; index < 30; index++)
            history.Execute("F-006", PageEdits.Rotate(history.Current, page, 90));
        var final = history.Current;
        Check.That(history.IsDirty && final.Pages[2].Rotation == 180, "Dirty or final rotation is wrong.");
        for (var index = 0; index < 30; index++)
            history.Undo();
        Check.That(ReferenceEquals(history.Current, project) && !history.IsDirty && !history.CanUndo, "Undo lost saved state.");
        for (var index = 0; index < 30; index++)
            history.Redo();
        Check.That(ReferenceEquals(history.Current, final) && history.IsDirty && !history.CanRedo, "Redo lost final state.");
        history.MarkSaved(final);
        history.Undo();
        Check.That(history.IsDirty, "Undo away from the save point must be dirty.");
        history.Execute("F-006", PageEdits.Move(history.Current, page, 0));
        Check.That(!history.CanRedo && history.Current.Pages[0].Id == page, "Branching edit left obsolete redo state.");
    }

    public static void Ranges()
    {
        Check.That(PageRanges.Parse("4, 1-3", 4).SequenceEqual(new[] { 3, 0, 1, 2 }), "Range order changed.");
        foreach (var input in new[] { "0", "5", "3-2", "1,1", "1-2-3", "", "1," })
        {
            try { PageRanges.Parse(input, 4); }
            catch (FormatException) { continue; }
            throw new InvalidOperationException("Invalid range accepted.");
        }
    }

    public static async Task ProjectRoundTrip()
    {
        using var test = new Scratch();
        using var folder = test.Workspace.Create();
        var source = folder.File("in.pdf");
        var bytes = "%PDF-1.7\nA project-store-only fixture.\n"u8.ToArray();
        await File.WriteAllBytesAsync(source, bytes);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var project = DocumentProject.Create(hash, 2);
        var note = new OverlayObject(Guid.NewGuid(), project.Pages[1].Id, OverlayKind.Note, new(20, 70, 150, 50),
            0xff121212, "Wieder öffnen", 12);
        project = project with { Overlays = [note] };
        project = PageEdits.Rotate(PageEdits.Move(project, project.Pages[1].Id, 0), project.Pages[1].Id, 90);
        var store = new ZipProjectStore(test.Workspace);
        var package = folder.File("work.ppdf");
        await store.SaveAsync(project, source, package, default);
        using (var archive = ZipFile.OpenRead(package))
        using (var stream = archive.GetEntry("overlays.json")!.Open())
        using (var json = await JsonDocument.ParseAsync(stream))
        {
            var regular = json.RootElement[0];
            Check.That(!regular.TryGetProperty("bold", out _) && !regular.TryGetProperty("italic", out _) &&
                       !regular.TryGetProperty("underlined", out _), "Regular text should retain the original project representation.");
        }
        var extracted = folder.File("out.pdf");
        var reopened = await store.LoadAsync(package, extracted, default);
        Check.That(reopened.Pages.SequenceEqual(project.Pages) && reopened.Overlays.Single() == note &&
                   File.ReadAllBytes(extracted).SequenceEqual(bytes), "Project lost edits or original bytes.");
        var edited = note with
        {
            Bounds = note.Bounds with { X = 40 }, Font = OverlayFont.Serif, FontSize = 24.5,
            SystemFont = "Arial", Color = 0xff387abc,
            Bold = true, Italic = true, Underlined = true
        };
        await store.SaveAsync(reopened with { Overlays = [edited] }, extracted, package, default);
        var second = await store.LoadAsync(package, folder.File("second.pdf"), default);
        Check.That(second.Overlays.Single() == edited, "Editing the reopened object duplicated it or lost its text style.");
    }

    public static async Task HostileArchive()
    {
        using var test = new Scratch();
        using var folder = test.Workspace.Create();
        foreach (var kind in new[] { "traversal", "absolute", "symlink", "bomb" })
        {
            var path = folder.File(kind + ".ppdf");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var manifest = archive.CreateEntry("manifest.json");
                using (var writer = new StreamWriter(manifest.Open()))
                    writer.Write(kind == "bomb" ? new string('0', 500_000) : "{}");
                if (kind == "symlink")
                    manifest.ExternalAttributes = unchecked((int)0xa1ff0000);
                Add("source.pdf");
                Add("overlays.json");
                if (kind == "traversal")
                    Add("../escaped.txt");
                if (kind == "absolute")
                    Add("/escaped.txt");

                void Add(string name)
                {
                    using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                    writer.Write("{}");
                }
            }
            var extract = folder.File(kind + ".pdf");
            await Check.FailsAsync<AppFailure>(() => new ZipProjectStore(test.Workspace).LoadAsync(path, extract, default),
                error => error.Code == "InvalidProject");
            Check.That(!File.Exists(extract), $"Hostile ZIP {kind} left a source on disk.");
        }
    }

    public static async Task AtomicCommit()
    {
        using var test = new Scratch();
        using var folder = test.Workspace.Create();
        var target = folder.File("target.pdf");
        var staged = folder.File("stage.pdf");
        await File.WriteAllTextAsync(target, "original");
        await File.WriteAllTextAsync(staged, "replacement");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Check.FailsAsync<OperationCanceledException>(() => SafeFiles.CommitFileAsync(staged, target, canceled.Token));
        Check.That(await File.ReadAllTextAsync(target) == "original", "Canceled replacement changed target.");
        await Check.FailsAsync<IOException>(() => SafeFiles.CommitFileAsync(folder.File("missing.pdf"), target, default));
        Check.That(await File.ReadAllTextAsync(target) == "original", "Failed replacement changed target.");
        await SafeFiles.CommitFileAsync(staged, target, default);
        Check.That(await File.ReadAllTextAsync(target) == "replacement", "Completed replacement is missing.");
    }

    public static void Recovery()
    {
        using var test = new Scratch();
        using var active = test.Workspace.CreateRecoverySession();
        var name = Guid.NewGuid().ToString("N") + ".ppdf";
        File.WriteAllText(active.File(name), "active");
        Check.That(test.Workspace.FindDrafts().Count == 0, "An active process's draft was offered for recovery.");
        var folder = Path.Combine(test.Workspace.Recovery, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ".active"), "");
        var orphan = Path.Combine(folder, name);
        File.WriteAllText(orphan, "orphan");
        Check.That(test.Workspace.FindDrafts().Single() == orphan, "An abandoned draft was not recovered.");
        LocalWorkspace.RemoveDraft(orphan);
        Check.That(!File.Exists(orphan), "Recovered draft was not removed.");
    }

    public sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "perplexpdf-check-" + Guid.NewGuid().ToString("N"));
        public LocalWorkspace Workspace { get; }
        public Scratch() => Workspace = new LocalWorkspace(_root);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
