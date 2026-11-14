using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using static PerplexPDF.Infrastructure.Pdf.PdfiumNative;

namespace PerplexPDF.Infrastructure.Pdf;

public sealed partial class PdfiumRenderer
{
    private sealed partial class PdfiumDocument
    {
        private const int MaxEditTextLength = 4_000;

        public Task<PdfTextObject?> FindTextObjectAsync(PageReference page, PdfPoint point, CancellationToken ct) =>
            WithPageAsync<PdfTextObject?>(page, handle =>
            {
                var location = ToPdf(handle, Pages[page.SourcePage], point);
                var textPage = FPDFText_LoadPage(handle);
                if (textPage == 0)
                    return null;
                try
                {
                    for (var index = FPDFPage_CountObjects(handle) - 1; index >= 0; index--)
                    {
                        ct.ThrowIfCancellationRequested();
                        var item = FPDFPage_GetObject(handle, index);
                        if (!IsVisibleText(item) || FPDFPageObj_GetBounds(item, out var left, out var bottom, out var right, out var top) == 0 ||
                            location.X < left || location.X > right || location.Y < bottom || location.Y > top)
                            continue;
                        if (ReadObjectText(item, textPage) is { } text)
                            return new PdfTextObject(index, text);
                    }
                    return null;
                }
                finally { FPDFText_ClosePage(textPage); }
            }, ct);

        public Task<string?> ReadTextObjectAsync(PageReference page, int index, CancellationToken ct) =>
            WithPageAsync(page, handle => ReadObject(handle, FPDFPage_GetObject(handle, index)), ct);

        public Task RewriteTextObjectAsync(PageReference page, PdfTextObject original, string replacement, string output,
            CancellationToken ct) => WithPageAsync(page, handle =>
        {
            if (FPDF_GetSecurityHandlerRevision(_document) >= 0 || Metadata.Signed)
                throw new AppFailure("TextEditProtected");
            if (!IsSupportedEditText(replacement))
                throw new AppFailure("TextEditUnsupported");
            var item = FPDFPage_GetObject(handle, original.Index);
            if (!IsVisibleText(item) || !StringComparer.Ordinal.Equals(ReadObject(handle, item), original.Text) ||
                FPDFText_SetText(item, replacement) == 0 || !FitsVisiblePage(handle, item) ||
                FPDFPage_GenerateContent(handle) == 0)
                throw new AppFailure("TextEditUnsupported");

            SavePdf(_document, output, "TextEditUnsupported", ct);
            return true;
        }, ct);

        private static bool IsVisibleText(nint item)
        {
            if (item == 0 || FPDFPageObj_GetType(item) != TextObject)
                return false;
            var mode = FPDFTextObj_GetTextRenderMode(item);
            return (mode is 0 or 2 && FPDFPageObj_GetFillColor(item, out _, out _, out _, out var fill) != 0 && fill > 0) ||
                (mode is 1 or 2 && FPDFPageObj_GetStrokeColor(item, out _, out _, out _, out var stroke) != 0 && stroke > 0);
        }

        private static bool IsSupportedEditText(string text) =>
            text.Length <= MaxEditTextLength && !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl);

        private static bool FitsVisiblePage(nint page, nint item) =>
            FPDF_GetPageBoundingBox(page, out var crop) != 0 &&
            FPDFPageObj_GetBounds(item, out var left, out var bottom, out var right, out var top) != 0 &&
            left < right && bottom < top && left >= crop.Left - .5 && right <= crop.Right + .5 &&
            bottom >= crop.Bottom - .5 && top <= crop.Top + .5;

        private static string? ReadObject(nint page, nint item)
        {
            if (!IsVisibleText(item))
                return null;
            var textPage = FPDFText_LoadPage(page);
            if (textPage == 0)
                return null;
            try { return ReadObjectText(item, textPage); }
            finally { FPDFText_ClosePage(textPage); }
        }

        private static string? ReadObjectText(nint item, nint textPage)
        {
            var text = ReadObjectString(item, textPage, MaxEditTextLength);
            return text is not null && IsSupportedEditText(text) && !text.Contains('\uFFFD') ? text : null;
        }

        private static string? ReadObjectString(nint item, nint textPage, int maximum)
        {
            var bytes = FPDFTextObj_GetText(item, textPage, null, 0);
            if (bytes < 4 || bytes > (maximum + 1) * 2)
                return null;
            var buffer = new byte[bytes];
            if (FPDFTextObj_GetText(item, textPage, buffer, bytes) != bytes)
                return null;
            return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        }
    }

    private static unsafe void SavePdf(nint document, string output, string failureCode, CancellationToken ct)
    {
        const uint fullRewrite = 2;
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Exception? failure = null;
        WriteBlock callback = (_, data, length) =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                SafeFiles.CheckLength(file.Position + length, DocumentProject.MaxSourceBytes);
                file.Write(new ReadOnlySpan<byte>((void*)data, checked((int)length)));
                return 1;
            }
            catch (Exception error)
            {
                failure = error;
                return 0;
            }
        };
        var writer = new FileWriter { Version = 1, Callback = Marshal.GetFunctionPointerForDelegate(callback) };
        var success = FPDF_SaveAsCopy(document, ref writer, fullRewrite);
        GC.KeepAlive(callback);
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        if (success == 0)
            throw new AppFailure(failureCode);
    }
}
