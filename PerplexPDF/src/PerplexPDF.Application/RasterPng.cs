using System.Buffers.Binary;
using System.IO.Compression;

namespace PerplexPDF.Application;

public static class RasterPng
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static async Task WriteAsync(RasterPage raster, int dpi, string target, CancellationToken ct)
    {
        await using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        await WriteAsync(raster, dpi, file, ct);
    }

    internal static async Task WriteAsync(RasterPage raster, double dpi, Stream file, CancellationToken ct)
    {
        await file.WriteAsync(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, ct);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, raster.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), raster.Height);
        header[8] = 8;
        header[9] = 2;
        await ChunkAsync(file, "IHDR"u8.ToArray(), header, ct);

        var resolution = new byte[9];
        var pixelsPerMeter = (int)Math.Round(dpi / .0254);
        BinaryPrimitives.WriteInt32BigEndian(resolution, pixelsPerMeter);
        BinaryPrimitives.WriteInt32BigEndian(resolution.AsSpan(4), pixelsPerMeter);
        resolution[8] = 1;
        await ChunkAsync(file, "pHYs"u8.ToArray(), resolution, ct);

        using var compressed = new MemoryStream();
        await using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[raster.Width * 3 + 1];
            for (var y = 0; y < raster.Height; y++)
            {
                ct.ThrowIfCancellationRequested();
                var input = y * raster.Width * 4;
                var output = 1;
                for (var x = 0; x < raster.Width; x++, input += 4)
                {
                    row[output++] = raster.Bgra[input + 2];
                    row[output++] = raster.Bgra[input + 1];
                    row[output++] = raster.Bgra[input];
                }
                await deflate.WriteAsync(row, ct);
            }
        }
        await ChunkAsync(file, "IDAT"u8.ToArray(), compressed.GetBuffer().AsMemory(0, (int)compressed.Length), ct);
        await ChunkAsync(file, "IEND"u8.ToArray(), ReadOnlyMemory<byte>.Empty, ct);
    }

    private static async Task ChunkAsync(Stream stream, byte[] type, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var value = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(value, data.Length);
        await stream.WriteAsync(value, ct);
        await stream.WriteAsync(type, ct);
        await stream.WriteAsync(data, ct);
        var crc = Checksum(Checksum(uint.MaxValue, type), data.Span);
        BinaryPrimitives.WriteUInt32BigEndian(value, ~crc);
        await stream.WriteAsync(value, ct);
    }

    private static uint Checksum(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var crc = index;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : 0xedb88320U ^ (crc >> 1);
            table[index] = crc;
        }
        return table;
    }
}
