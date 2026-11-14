using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace PerplexPDF.Domain;

public sealed record PageReference(Guid Id, int SourcePage, int Rotation = 0);
public sealed record PageGeometry(double Width, double Height);
public readonly record struct PdfPoint(double X, double Y);

public readonly record struct PdfRect(double X, double Y, double Width, double Height)
{
    public bool Contains(PdfPoint point) =>
        point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;

    public static PdfRect Between(PdfPoint first, PdfPoint second) => new(
        Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
        Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));

    public PdfRect Clamp(PageGeometry page)
    {
        var width = Math.Min(Width, page.Width);
        var height = Math.Min(Height, page.Height);
        return new(Math.Clamp(X, 0, page.Width - width), Math.Clamp(Y, 0, page.Height - height), width, height);
    }
}

public enum OverlayKind { Text, Image, Rectangle, Ellipse, Highlight, Underline, Note }
public enum OverlayFont { Sans, Serif, Mono }

public sealed record OverlayObject(
    Guid Id, Guid PageId, OverlayKind Kind, PdfRect Bounds,
    uint Color, string Text = "", double FontSize = 16, OverlayFont Font = OverlayFont.Sans,
    string? ResourceHash = null, int CreationRotation = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Bold = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Italic = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Underlined = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SystemFont { get; init; }
}

public sealed record DocumentProject(
    Guid Id, string SourceHash, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc,
    ImmutableArray<PageReference> Pages, ImmutableArray<OverlayObject> Overlays,
    ImmutableDictionary<string, ImmutableArray<byte>> Resources)
{
    public const string SchemaVersion = "1.0";
    public const int MaxPages = 2000;
    public const long MaxSourceBytes = 256L * 1024 * 1024;
    public const int MaxOverlays = 10_000;
    public const int MaxImageBytes = 16 * 1024 * 1024;
    public const int MaxResourceBytes = 64 * 1024 * 1024;

    public static DocumentProject Create(string sourceHash, int count)
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), sourceHash, now, now,
            Enumerable.Range(0, count).Select(index => new PageReference(Guid.NewGuid(), index)).ToImmutableArray(),
            [], ImmutableDictionary<string, ImmutableArray<byte>>.Empty);
    }

    public void Validate(IReadOnlyList<PageGeometry>? sourcePages = null)
    {
        if (Id == Guid.Empty || !IsHash(SourceHash) || Pages.IsDefaultOrEmpty || Pages.Length > MaxPages ||
            Overlays.IsDefault || Overlays.Length > MaxOverlays || Resources is null ||
            Resources.Count > MaxOverlays || Resources.Values.Sum(value => (long)value.Length) > MaxResourceBytes)
            throw new InvalidDataException("Invalid project limits or manifest.");

        var pages = new Dictionary<Guid, PageReference>();
        foreach (var page in Pages)
        {
            if (page is null || page.Id == Guid.Empty || !pages.TryAdd(page.Id, page) ||
                page.SourcePage < 0 || page.SourcePage >= (sourcePages?.Count ?? MaxPages) ||
                page.Rotation is not (0 or 90 or 180 or 270))
                throw new InvalidDataException("Invalid project page.");
        }

        foreach (var (hash, bytes) in Resources)
        {
            if (!IsHash(hash) || bytes.IsDefaultOrEmpty || bytes.Length > MaxImageBytes)
                throw new InvalidDataException("Invalid project resource.");
        }

        var objects = new HashSet<Guid>();
        foreach (var overlay in Overlays)
        {
            if (overlay is null || overlay.Id == Guid.Empty || !objects.Add(overlay.Id) ||
                !pages.TryGetValue(overlay.PageId, out var page) ||
                !Enum.IsDefined(overlay.Kind) || !Enum.IsDefined(overlay.Font) ||
                overlay.SystemFont is { } family && !IsFontFamily(family) ||
                overlay.CreationRotation is not (0 or 90 or 180 or 270) ||
                overlay.Text is null || overlay.Text.Length > 4000 ||
                !double.IsFinite(overlay.FontSize) || overlay.FontSize is < 4 or > 144)
                throw new InvalidDataException("Invalid project overlay.");

            var (x, y, width, height) = overlay.Bounds;
            var geometry = sourcePages?[page.SourcePage] ?? new PageGeometry(20_000, 20_000);
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) ||
                x < 0 || y < 0 || width <= 0 || height <= 0 ||
                x + width > geometry.Width + .01 || y + height > geometry.Height + .01)
                throw new InvalidDataException("Overlay is outside its page.");

            if (overlay.Kind == OverlayKind.Image)
            {
                if (overlay.ResourceHash is null || !Resources.ContainsKey(overlay.ResourceHash))
                    throw new InvalidDataException("Missing overlay image.");
            }
            else if (overlay.ResourceHash is not null)
                throw new InvalidDataException("Unexpected overlay resource.");
        }
    }

    public static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool IsFontFamily(string value) =>
        value.Length is > 0 and <= 128 && !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
}

public sealed class ProjectHistory(DocumentProject initial, bool isNew = false)
{
    private const int Capacity = 100;
    private readonly List<ProjectEdit> _undo = [];
    private readonly Stack<ProjectEdit> _redo = [];
    private DocumentProject? _saved = isNew ? null : initial;

    public DocumentProject Current { get; private set; } = initial;
    public bool IsDirty => !ReferenceEquals(_saved, Current);
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;

    public void Execute(string requirement, DocumentProject next)
    {
        if (ReferenceEquals(next, Current))
            return;
        next = next with { UpdatedUtc = DateTimeOffset.UtcNow };
        var edit = new ProjectEdit(requirement, Current, next);
        _undo.Add(edit);
        if (_undo.Count > Capacity)
            _undo.RemoveAt(0);
        _redo.Clear();
        Current = edit.After;
    }

    public void Undo()
    {
        if (!CanUndo)
            return;
        var edit = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Push(edit);
        Current = edit.Before;
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var edit))
            return;
        _undo.Add(edit);
        Current = edit.After;
    }

    public void MarkSaved(DocumentProject snapshot) => _saved = snapshot;

    private sealed record ProjectEdit(string Requirement, DocumentProject Before, DocumentProject After);
}

public static class PageEdits
{
    public static DocumentProject Rotate(DocumentProject project, Guid pageId, int degrees)
    {
        var index = IndexOf(project, pageId);
        var page = project.Pages[index];
        return project with { Pages = project.Pages.SetItem(index, page with { Rotation = (page.Rotation + degrees + 360) % 360 }) };
    }

    public static DocumentProject Move(DocumentProject project, Guid pageId, int targetIndex)
    {
        var index = IndexOf(project, pageId);
        if (index == targetIndex || targetIndex < 0 || targetIndex >= project.Pages.Length)
            return project;
        return project with { Pages = project.Pages.RemoveAt(index).Insert(targetIndex, project.Pages[index]) };
    }

    public static DocumentProject Delete(DocumentProject project, Guid pageId)
    {
        if (project.Pages.Length == 1)
            return project;
        return project with
        {
            Pages = project.Pages.RemoveAt(IndexOf(project, pageId)),
            Overlays = project.Overlays.RemoveAll(overlay => overlay.PageId == pageId)
        };
    }

    public static DocumentProject Duplicate(DocumentProject project, Guid pageId)
    {
        if (project.Pages.Length >= DocumentProject.MaxPages)
            return project;
        var index = IndexOf(project, pageId);
        var duplicate = project.Pages[index] with { Id = Guid.NewGuid() };
        var overlays = project.Overlays.Where(overlay => overlay.PageId == pageId)
            .Select(overlay => overlay with { Id = Guid.NewGuid(), PageId = duplicate.Id });
        return project with
        {
            Pages = project.Pages.Insert(index + 1, duplicate),
            Overlays = project.Overlays.AddRange(overlays)
        };
    }

    private static int IndexOf(DocumentProject project, Guid pageId)
    {
        for (var index = 0; index < project.Pages.Length; index++)
            if (project.Pages[index].Id == pageId)
                return index;
        throw new ArgumentException("Page does not belong to the project.", nameof(pageId));
    }
}

public static class PageRanges
{
    // User-facing ranges are one-based and retain the requested order.
    public static int[] Parse(string value, int pageCount)
    {
        if (value.Length > 16_000 || pageCount is < 1 or > DocumentProject.MaxPages)
            throw new FormatException();
        var pages = new List<int>();
        var used = new HashSet<int>();
        foreach (var part in value.Split(','))
        {
            var endpoints = part.Split('-', StringSplitOptions.TrimEntries);
            if (endpoints.Length is < 1 or > 2 || !ReadPage(endpoints[0], out var first))
                throw new FormatException();
            var last = first;
            if (endpoints.Length == 2 && (!ReadPage(endpoints[1], out last) || last < first))
                throw new FormatException();
            for (var page = first; page <= last; page++)
            {
                if (!used.Add(page))
                    throw new FormatException();
                pages.Add(page - 1);
            }
        }
        return pages.ToArray();

        bool ReadPage(string input, out int page) =>
            int.TryParse(input, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out page) && page >= 1 && page <= pageCount;
    }
}
