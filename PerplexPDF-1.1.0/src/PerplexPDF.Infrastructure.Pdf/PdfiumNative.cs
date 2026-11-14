using System.Reflection;
using System.Runtime.InteropServices;

namespace PerplexPDF.Infrastructure.Pdf;

internal static partial class PdfiumNative
{
    private const string Library = "perplexpdf-pdfium";
    private static nint _library;
    private static string? _path;
    internal static readonly object Sync = new();
    internal static Func<string, nint>? ResolveDesktopLibrary { private get; set; }

    static PdfiumNative() => NativeLibrary.SetDllImportResolver(
        typeof(PdfiumNative).Assembly, ResolveLibrary);

    internal static void Initialize(string path)
    {
        path = Path.GetFullPath(path);
        if (_library != 0)
        {
            if (!StringComparer.Ordinal.Equals(_path, path))
                throw new InvalidOperationException("Only one PDFium build may be loaded in a process.");
            return;
        }
        _library = OperatingSystem.IsWindows()
            ? NativeLibrary.Load(path, typeof(PdfiumNative).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32)
            : NativeLibrary.Load(path);
        _path = path;
        FPDF_InitLibrary();
    }

    private static nint ResolveLibrary(string name, Assembly assembly, DllImportSearchPath? searchPath) =>
        name == Library ? _library : ResolveDesktopLibrary?.Invoke(name) ?? 0;

    [DllImport(Library)] private static extern void FPDF_InitLibrary();
    [DllImport(Library)] internal static extern nint FPDF_LoadMemDocument64(nint data, nuint size,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);
    [DllImport(Library)] internal static extern uint FPDF_GetLastError();
    [DllImport(Library)] internal static extern void FPDF_CloseDocument(nint document);
    [DllImport(Library)] internal static extern int FPDF_GetPageCount(nint document);
    [DllImport(Library)] internal static extern int FPDF_GetPageSizeByIndex(nint document, int index, out double width, out double height);
    [DllImport(Library)] internal static extern nint FPDF_CreateNewDocument();
    [DllImport(Library)] internal static extern nint FPDFPage_New(nint document, int index, double width, double height);
    [DllImport(Library)] internal static extern int FPDFPage_GetRotation(nint page);
    [DllImport(Library)] internal static extern void FPDFPage_SetRotation(nint page, int rotation);
    [DllImport(Library)] internal static extern nint FPDF_LoadPage(nint document, int index);
    [DllImport(Library)] internal static extern void FPDF_ClosePage(nint page);
    [DllImport(Library)] internal static extern float FPDF_GetPageWidthF(nint page);
    [DllImport(Library)] internal static extern float FPDF_GetPageHeightF(nint page);
    [DllImport(Library)] internal static extern nint FPDFBitmap_CreateEx(int width, int height, int format, nint buffer, int stride);
    [DllImport(Library)] internal static extern void FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Library)] internal static extern void FPDFBitmap_Destroy(nint bitmap);
    [DllImport(Library)] internal static extern void FPDF_RenderPageBitmap(nint bitmap, nint page,
        int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
    [DllImport(Library)] internal static extern int FPDF_PageToDevice(nint page, int startX, int startY, int sizeX, int sizeY,
        int rotate, double pageX, double pageY, out int x, out int y);
    [DllImport(Library)] internal static extern int FPDF_DeviceToPage(nint page, int startX, int startY, int sizeX, int sizeY,
        int rotate, int x, int y, out double pageX, out double pageY);

    [DllImport(Library)] internal static extern nint FPDFText_LoadPage(nint page);
    [DllImport(Library)] internal static extern void FPDFText_ClosePage(nint textPage);
    [DllImport(Library)] internal static extern nint FPDFText_FindStart(nint textPage, nint text, uint flags, int startIndex);
    [DllImport(Library)] internal static extern int FPDFText_FindNext(nint search);
    [DllImport(Library)] internal static extern void FPDFText_FindClose(nint search);
    [DllImport(Library)] internal static extern int FPDFText_GetSchResultIndex(nint search);
    [DllImport(Library)] internal static extern int FPDFText_GetSchCount(nint search);
    [DllImport(Library)] internal static extern int FPDFText_CountRects(nint textPage, int start, int count);
    [DllImport(Library)] internal static extern int FPDFText_GetRect(nint textPage, int index,
        out double left, out double top, out double right, out double bottom);
    [DllImport(Library)] internal static extern int FPDFText_GetBoundedText(nint textPage, double left, double top,
        double right, double bottom, nint buffer, int bufferLength);

    [DllImport(Library)] internal static extern nint FPDFBookmark_GetFirstChild(nint document, nint bookmark);
    [DllImport(Library)] internal static extern nint FPDFBookmark_GetNextSibling(nint document, nint bookmark);
    [DllImport(Library)] internal static extern nuint FPDFBookmark_GetTitle(nint bookmark, [Out] byte[]? buffer, nuint length);
    [DllImport(Library)] internal static extern nint FPDFBookmark_GetDest(nint document, nint bookmark);
    [DllImport(Library)] internal static extern nint FPDFBookmark_GetAction(nint bookmark);
    [DllImport(Library)] internal static extern uint FPDFAction_GetType(nint action);
    [DllImport(Library)] internal static extern nint FPDFAction_GetDest(nint document, nint action);
    [DllImport(Library)] internal static extern int FPDFDest_GetDestPageIndex(nint document, nint dest);
    [DllImport(Library)] internal static extern nuint FPDF_GetMetaText(nint document,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string tag, [Out] byte[]? buffer, nuint length);
    [DllImport(Library)] internal static extern int FPDF_GetSignatureCount(nint document);
}
