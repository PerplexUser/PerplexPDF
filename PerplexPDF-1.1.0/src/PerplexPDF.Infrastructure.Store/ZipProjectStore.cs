using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PerplexPDF.Application;
using PerplexPDF.Domain;

namespace PerplexPDF.Infrastructure.Store;

public sealed class ZipProjectStore(IWorkDirectories directories) : IProjectStore
{
    private const int JsonLimit = 32 * 1024 * 1024;
    private const long ArchiveLimit = DocumentProject.MaxSourceBytes + DocumentProject.MaxResourceBytes + JsonLimit + 4 * 1024 * 1024;
    private const string ManifestName = "manifest.json";
    private const string PdfName = "source.pdf";
    private const string OverlaysName = "overlays.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private sealed record Manifest(string SchemaVersion, string Product, Guid ProjectId, string SourceSha256,
        string DocumentFile, string OverlaysFile, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc,
        ImmutableArray<PageReference> PageOrder, string CreatedWith);

    public async Task SaveAsync(DocumentProject project, string source, string target, CancellationToken ct)
    {
        project.Validate();
        using var work = directories.Create();
        var staged = work.File("project.ppdf");
        await using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, true))
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            await using (var manifest = archive.CreateEntry(ManifestName, CompressionLevel.NoCompression).Open())
            {
                await JsonSerializer.SerializeAsync(manifest, new Manifest(DocumentProject.SchemaVersion, "PerplexPDF",
                    project.Id, project.SourceHash, PdfName, OverlaysName, project.CreatedUtc, project.UpdatedUtc,
                    project.Pages, "1.0.0"), JsonOptions, ct);
            }
            await using (var overlays = archive.CreateEntry(OverlaysName, CompressionLevel.NoCompression).Open())
                await JsonSerializer.SerializeAsync(overlays, project.Overlays, JsonOptions, ct);

            // Source and images are typically already compressed. Store them without recompression.
            var verified = await SafeFiles.CopyPdfAsync(source, work.File("verified.pdf"), ct);
            if (!StringComparer.Ordinal.Equals(project.SourceHash, verified))
                throw new AppFailure("SourceChanged");
            await using (var pdf = archive.CreateEntry(PdfName, CompressionLevel.NoCompression).Open())
            await using (var original = new FileStream(work.File("verified.pdf"), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
                await original.CopyToAsync(pdf, ct);

            foreach (var hash in project.Overlays.Select(overlay => overlay.ResourceHash).OfType<string>().Distinct())
            {
                var resource = project.Resources[hash];
                if (!StringComparer.Ordinal.Equals(hash, Hash(resource.AsSpan())))
                    throw new AppFailure("InvalidProject");
                await using var stream = archive.CreateEntry($"resources/{hash}.png", CompressionLevel.NoCompression).Open();
                await stream.WriteAsync(resource.AsMemory(), ct);
            }
        }
        // Refuse a manifest that could not be read back under the same limits.
        await using (var check = File.OpenRead(staged))
        using (var archive = new ZipArchive(check, ZipArchiveMode.Read))
        {
            SafeFiles.CheckLength(check.Length, ArchiveLimit);
            if (archive.GetEntry(OverlaysName)!.Length > JsonLimit)
                throw new AppFailure("SizeLimit");
        }
        await SafeFiles.CommitFileAsync(staged, target, ct);
    }

    public async Task<DocumentProject> LoadAsync(string path, string sourceTarget, CancellationToken ct)
    {
        try
        {
            return await LoadCoreAsync(path, sourceTarget, ct);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or OverflowException or ArgumentException)
        {
            throw new AppFailure("InvalidProject", error);
        }
    }

    private static async Task<DocumentProject> LoadCoreAsync(string path, string sourceTarget, CancellationToken ct)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        SafeFiles.CheckLength(input.Length, ArchiveLimit);
        // Inspect the end record before ZipArchive allocates one object per central-directory entry.
        await ValidateEndRecordAsync(input, ct);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count is < 3 or > DocumentProject.MaxOverlays + 3)
            throw new InvalidDataException();

        var names = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var limit = entry.FullName switch
            {
                ManifestName => 2 * 1024 * 1024,
                OverlaysName => JsonLimit,
                PdfName => DocumentProject.MaxSourceBytes,
                var name when IsResource(name) => DocumentProject.MaxImageBytes,
                _ => 0
            };
            var mode = (entry.ExternalAttributes >> 16) & 0xf000;
            if (mode is not (0 or 0x8000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                limit == 0 || entry.Length < 1 || entry.Length > limit ||
                entry.CompressedLength < 1 || entry.Length / (double)entry.CompressedLength > 200 ||
                !names.TryAdd(entry.FullName, entry))
                throw new InvalidDataException();
            total += entry.Length;
            if (total > ArchiveLimit)
                throw new InvalidDataException();
        }
        if (!names.TryGetValue(ManifestName, out var manifestEntry) ||
            !names.TryGetValue(OverlaysName, out var overlayEntry) || !names.TryGetValue(PdfName, out var sourceEntry))
            throw new InvalidDataException();

        var manifest = JsonSerializer.Deserialize<Manifest>(await ReadEntryAsync(manifestEntry, ct), JsonOptions);
        if (manifest is null || manifest.SchemaVersion != DocumentProject.SchemaVersion)
            throw new AppFailure("ProjectVersion");
        if (manifest.Product != "PerplexPDF" || manifest.DocumentFile != PdfName || manifest.OverlaysFile != OverlaysName)
            throw new InvalidDataException();

        var overlays = JsonSerializer.Deserialize<ImmutableArray<OverlayObject>>(
            await ReadEntryAsync(overlayEntry, ct), JsonOptions);
        if (overlays.IsDefault || overlays.Length > DocumentProject.MaxOverlays || overlays.Any(item => item is null))
            throw new InvalidDataException();

        var resourceHashes = overlays.Select(overlay => overlay.ResourceHash).OfType<string>().ToHashSet();
        if (resourceHashes.Count != names.Count - 3)
            throw new InvalidDataException();
        var resources = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        long resourceBytes = 0;
        foreach (var hash in resourceHashes)
        {
            if (!DocumentProject.IsHash(hash) || !names.TryGetValue($"resources/{hash}.png", out var entry) ||
                (resourceBytes += entry.Length) > DocumentProject.MaxResourceBytes)
                throw new InvalidDataException();
            var bytes = await ReadEntryAsync(entry, ct);
            if (!StringComparer.Ordinal.Equals(Hash(bytes), hash))
                throw new InvalidDataException();
            resources.Add(hash, ImmutableCollectionsMarshal.AsImmutableArray(bytes));
        }

        var project = new DocumentProject(manifest.ProjectId, manifest.SourceSha256, manifest.CreatedUtc,
            manifest.UpdatedUtc, manifest.PageOrder, overlays, resources.ToImmutable());
        project.Validate();
        using var contentHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = sourceEntry.Open();
        await using (var target = new FileStream(sourceTarget, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buffer = new byte[65536];
            long copied = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) != 0)
            {
                copied += read;
                if (copied > sourceEntry.Length)
                    throw new InvalidDataException();
                contentHash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (copied != sourceEntry.Length ||
                !StringComparer.Ordinal.Equals(Convert.ToHexStringLower(contentHash.GetHashAndReset()), project.SourceHash))
                throw new InvalidDataException();
        }
        await using var extracted = File.OpenRead(sourceTarget);
        await SafeFiles.CheckPdfHeaderAsync(extracted, ct);
        return project;
    }

    private static async Task ValidateEndRecordAsync(FileStream input, CancellationToken ct)
    {
        var length = (int)Math.Min(input.Length, 65_557);
        var tail = new byte[length];
        input.Position = input.Length - length;
        await input.ReadExactlyAsync(tail, ct);
        for (var offset = length - 22; offset >= 0; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(offset)) != 0x06054b50 ||
                BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(offset + 20)) != length - offset - 22)
                continue;
            var onDisk = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(offset + 8));
            var count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(offset + 10));
            var centralSize = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(offset + 12));
            var centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(offset + 16));
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(offset + 4)) != 0 ||
                count != onDisk || count is < 3 or > DocumentProject.MaxOverlays + 3 ||
                centralSize > 2 * 1024 * 1024 ||
                (long)centralSize + centralOffset != input.Length - length + offset)
                throw new InvalidDataException();
            input.Position = 0;
            return;
        }
        throw new InvalidDataException();
    }

    private static bool IsResource(string value) => value.Length == 78 &&
        value.StartsWith("resources/", StringComparison.Ordinal) && value.EndsWith(".png", StringComparison.Ordinal) &&
        DocumentProject.IsHash(value[10..^4]);

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)entry.Length));
        await using var stream = entry.Open();
        await stream.ReadExactlyAsync(bytes, ct);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException();
        return bytes;
    }
}
