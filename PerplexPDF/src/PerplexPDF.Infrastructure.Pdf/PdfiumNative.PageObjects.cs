using System.Runtime.InteropServices;

namespace PerplexPDF.Infrastructure.Pdf;

internal static partial class PdfiumNative
{
    internal const int TextObject = 1;
    internal const int PathObject = 2;
    internal const int ImageObject = 3;
    internal const int ShadingObject = 4;
    internal const int FormObject = 5;

    [DllImport(Library)] internal static extern int FPDFPage_CountObjects(nint page);
    [DllImport(Library)] internal static extern int FPDF_GetPageBoundingBox(nint page, out FloatRect bounds);
    [DllImport(Library)] internal static extern nint FPDFPage_GetObject(nint page, int index);
    [DllImport(Library)] internal static extern int FPDFPageObj_GetType(nint pageObject);
    [DllImport(Library)] internal static extern int FPDFPageObj_GetIsActive(nint pageObject, out int active);
    [DllImport(Library)] internal static extern int FPDFPageObj_SetIsActive(nint pageObject, int active);
    [DllImport(Library)] internal static extern int FPDFPageObj_GetMatrix(nint pageObject, out FloatMatrix matrix);
    [DllImport(Library)] internal static extern int FPDFFormObj_CountObjects(nint form);
    [DllImport(Library)] internal static extern nint FPDFFormObj_GetObject(nint form, uint index);
    [DllImport(Library)] internal static extern int FPDFPageObj_GetBounds(nint pageObject,
        out float left, out float bottom, out float right, out float top);
    [DllImport(Library)] internal static extern int FPDFPageObj_GetFillColor(nint pageObject,
        out uint red, out uint green, out uint blue, out uint alpha);
    [DllImport(Library)] internal static extern int FPDFPageObj_GetStrokeColor(nint pageObject,
        out uint red, out uint green, out uint blue, out uint alpha);
    [DllImport(Library)] internal static extern int FPDFPath_GetDrawMode(nint path, out int fillMode, out int stroke);
    [DllImport(Library)] internal static extern int FPDFPath_CountSegments(nint path);
    [DllImport(Library)] internal static extern nint FPDFPath_GetPathSegment(nint path, int index);
    [DllImport(Library)] internal static extern int FPDFPathSegment_GetType(nint segment);
    [DllImport(Library)] internal static extern int FPDFPathSegment_GetPoint(nint segment, out float x, out float y);
    [DllImport(Library)] internal static extern int FPDFTextObj_GetTextRenderMode(nint textObject);
    [DllImport(Library)] internal static extern int FPDFTextObj_GetFontSize(nint textObject, out float size);
    [DllImport(Library)] internal static extern nint FPDFTextObj_GetFont(nint textObject);
    [DllImport(Library)] internal static extern uint FPDFFont_GetBaseFontName(nint font, byte[] buffer, uint length);
    [DllImport(Library)] internal static extern int FPDFFont_GetFlags(nint font);
    [DllImport(Library)] internal static extern int FPDFFont_GetWeight(nint font);
    [DllImport(Library)] internal static extern uint FPDFTextObj_GetText(nint textObject, nint textPage, byte[]? buffer, uint length);
    [DllImport(Library)] internal static extern int FPDFText_SetText(nint textObject, [MarshalAs(UnmanagedType.LPWStr)] string text);
    [DllImport(Library)] internal static extern int FPDFPage_GenerateContent(nint page);
    [DllImport(Library)] internal static extern int FPDF_SaveAsCopy(nint document, ref FileWriter file, uint flags);
    [DllImport(Library)] internal static extern int FPDF_GetSecurityHandlerRevision(nint document);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int WriteBlock(nint file, nint data, uint length);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileWriter
    {
        internal int Version;
        internal nint Callback;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FloatRect { internal float Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FloatMatrix { internal float A, B, C, D, E, F; }
}
