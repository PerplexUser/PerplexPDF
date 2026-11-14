namespace PerplexPDF.Domain;

// Source-frame coordinates are crop-relative PDF points, with a bottom-left origin.
// The imported PDF's own rotation is already reflected in Source.
public readonly record struct PageTransform(PageGeometry Source, int Rotation)
{
    public PageGeometry Display => Rotation is 90 or 270
        ? new PageGeometry(Source.Height, Source.Width) : Source;

    public PdfPoint ToDisplay(PdfPoint source) => Rotation switch
    {
        0 => new(source.X, Source.Height - source.Y),
        90 => new(source.Y, source.X),
        180 => new(Source.Width - source.X, source.Y),
        270 => new(Source.Height - source.Y, Source.Width - source.X),
        _ => throw new ArgumentOutOfRangeException(nameof(Rotation))
    };

    public PdfPoint ToSource(PdfPoint display) => Rotation switch
    {
        0 => new(display.X, Source.Height - display.Y),
        90 => new(display.Y, display.X),
        180 => new(Source.Width - display.X, display.Y),
        270 => new(Source.Width - display.Y, Source.Height - display.X),
        _ => throw new ArgumentOutOfRangeException(nameof(Rotation))
    };

    public PdfRect ToDisplay(PdfRect source) => PdfRect.Between(
        ToDisplay(new PdfPoint(source.X, source.Y)),
        ToDisplay(new PdfPoint(source.X + source.Width, source.Y + source.Height)));

    public PdfRect ToSource(PdfRect display) => PdfRect.Between(
        ToSource(new PdfPoint(display.X, display.Y)),
        ToSource(new PdfPoint(display.X + display.Width, display.Y + display.Height)));

    public PdfRect ToUnrotatedTopLeft(PdfRect source) =>
        new PageTransform(Source, 0).ToDisplay(source);
}
