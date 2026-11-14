using System.Runtime.InteropServices;

namespace PerplexPDF.Infrastructure.Pdf;

internal static partial class PdfiumNative
{
    [DllImport(Library)] internal static extern nint FPDF_StructTree_GetForPage(nint page);
    [DllImport(Library)] internal static extern void FPDF_StructTree_Close(nint tree);
    [DllImport(Library)] internal static extern int FPDF_StructTree_CountChildren(nint tree);
    [DllImport(Library)] internal static extern nint FPDF_StructTree_GetChildAtIndex(nint tree, int index);
    [DllImport(Library)] internal static extern int FPDF_StructElement_CountChildren(nint element);
    [DllImport(Library)] internal static extern nint FPDF_StructElement_GetChildAtIndex(nint element, int index);
    [DllImport(Library)] internal static extern int FPDF_StructElement_GetChildMarkedContentID(nint element, int index);
    [DllImport(Library)] internal static extern int FPDF_StructElement_GetMarkedContentIdCount(nint element);
    [DllImport(Library)] internal static extern int FPDF_StructElement_GetMarkedContentIdAtIndex(nint element, int index);
    [DllImport(Library)] internal static extern CULong FPDF_StructElement_GetType(nint element, [Out] byte[] type, CULong length);
    [DllImport(Library)] internal static extern int FPDFPageObj_GetMarkedContentID(nint item);
}
