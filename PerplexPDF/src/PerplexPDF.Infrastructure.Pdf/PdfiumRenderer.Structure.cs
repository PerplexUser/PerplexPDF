using System.Runtime.InteropServices;
using PerplexPDF.Application;
using static PerplexPDF.Infrastructure.Pdf.PdfiumNative;

namespace PerplexPDF.Infrastructure.Pdf;

public sealed partial class PdfiumRenderer
{
    private sealed partial class PdfiumDocument
    {
        private static (bool Tagged, Dictionary<int, PdfHeading> Headings) ReadHeadings(nint page, CancellationToken ct)
        {
            var headings = new Dictionary<int, PdfHeading>();
            var tree = FPDF_StructTree_GetForPage(page);
            if (tree == 0)
                return (false, headings);
            var seen = new HashSet<nint>();
            var type = new byte[6];
            var group = 0;
            var budget = MaxPageObjects * 3;
            try
            {
                var count = Math.Min(MaxPageObjects, FPDF_StructTree_CountChildren(tree));
                for (var index = 0; index < count && budget > 0; index++)
                    Visit(FPDF_StructTree_GetChildAtIndex(tree, index), default, 0);
                return (true, headings);
            }
            finally { FPDF_StructTree_Close(tree); }

            void Visit(nint item, PdfHeading heading, int depth)
            {
                ct.ThrowIfCancellationRequested();
                if (--budget < 0 || item == 0 || depth > 64 || !seen.Add(item))
                    return;
                if (FPDF_StructElement_GetType(item, type, new CULong((uint)type.Length)).Value == 6 &&
                    type[0] == 'H' && type[1] == 0 && type[3] == 0 && type[2] is >= (byte)'1' and <= (byte)'6')
                    heading = new(type[2] - '0', ++group);

                if (heading.Level != 0)
                {
                    var marks = Math.Min(MaxPageObjects, FPDF_StructElement_GetMarkedContentIdCount(item));
                    for (var index = 0; index < marks && budget-- > 0; index++)
                        Mark(FPDF_StructElement_GetMarkedContentIdAtIndex(item, index), heading);
                }
                var children = Math.Min(MaxPageObjects, FPDF_StructElement_CountChildren(item));
                for (var index = 0; index < children && budget > 0; index++)
                {
                    if (heading.Level != 0)
                        Mark(FPDF_StructElement_GetChildMarkedContentID(item, index), heading);
                    Visit(FPDF_StructElement_GetChildAtIndex(item, index), heading, depth + 1);
                }
            }

            void Mark(int id, PdfHeading heading)
            {
                if (id >= 0 && headings.Count < MaxPageObjects)
                    headings[id] = heading;
            }
        }
    }
}
