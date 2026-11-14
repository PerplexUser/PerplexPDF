"""Regenerate original test material. Requires ReportLab, pypdf and Pillow."""
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from pypdf import PdfReader, PdfWriter
from pypdf.generic import RectangleObject
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

here = Path(__file__).parent
fonts = here.parent.parent / "assets" / "fonts"
font = str(fonts / "DejaVuSans.ttf")

memory = BytesIO()
pdf = canvas.Canvas(memory, pagesize=(612, 792), invariant=1)
pdf.setTitle("PerplexPDF example")
pdf.setAuthor("PerplexPDF fixtures")
pdfmetrics.registerFont(TTFont("FixtureSans", font))
for number, (width, height) in enumerate([(612, 792), (720, 480), (450, 600), (612, 792)], 1):
    pdf.setPageSize((width, height))
    pdf.setFillColorRGB(.09, .23, .49)
    pdf.rect(38, height - 87, width - 76, 48, fill=1, stroke=0)
    pdf.setFillColorRGB(1, 1, 1)
    pdf.setFont("Helvetica-Bold", 20)
    pdf.drawString(54, height - 70, f"PerplexPDF example / Page {number}")
    pdf.setFillColorRGB(.12, .20, .32)
    pdf.setFont("Helvetica", 14)
    pdf.drawString(56, height - 130, f"Sample page {number}: the quick brown fox.")
    pdf.drawString(56, height - 156, "Use this PDF to test search, page order, and overlays.")
    if number == 4:
        pdf.setFont("FixtureSans", 14)
        pdf.drawString(56, height - 185, "Unicode test: Grüße, naïve café.")
    pdf.showPage()
pdf.save()
reader = PdfReader(memory)
writer = PdfWriter(clone_from=reader)
writer.pages[1].cropbox = RectangleObject((24, 22, 695, 459))
writer.pages[1].rotate(90)
writer.add_outline_item("Start here", 0)
writer.add_outline_item("Rotated and cropped page", 1)
writer.add_outline_item("Different page size", 2)
writer.write(here / "example.pdf")
writer.encrypt("perplex-test", "perplex-owner", algorithm="AES-256")
writer.write(here / "protected.pdf")

scan = canvas.Canvas(str(here / "scanned-en-de.pdf"), pagesize=(612, 360), invariant=1)
for lines in (
    ["ENGLISH SCAN", "The quick brown fox jumps over the lazy dog.", "Recognition should find the word lighthouse."],
    ["DEUTSCHER SCAN", "Franz jagt im komplett verwahrlosten Taxi quer durch Bayern.", "Die automatische Erkennung findet das Wort Leuchtturm."],
):
    image = Image.new("RGB", (1700, 1000), "white")
    draw = ImageDraw.Draw(image)
    for i, line in enumerate(lines):
        draw.text((110, 165 + i * 110), line, fill="black", font=ImageFont.truetype(font, 46))
    scan.drawImage(ImageReader(image), 0, 0, width=612, height=360)
    scan.showPage()
scan.save()

image = Image.new("RGBA", (310, 140), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((2, 2, 308, 138), radius=18, fill=(23, 77, 187, 225))
draw.text((30, 42), "PerplexPDF", fill="white", font=ImageFont.truetype(font, 39))
image.save(here / "logo.png")
image.convert("RGB").rotate(90, expand=True).save(here / "portrait.jpg", quality=92)

active = PdfWriter(clone_from=here / "example.pdf")
active.add_js("app.alert('PerplexPDF safety fixture: no alert should run');")
active.add_uri(0, "https://example.com/perplexpdf-fixture", (50, 600, 300, 622))
active.write(here / "javascript.pdf")
(here / "corrupt.pdf").write_bytes(b"%PDF-1.7\nThis fixture deliberately has no PDF objects.\n")
large = PdfWriter()
source = PdfReader(here / "example.pdf")
for index in range(200):
    large.add_page(source.pages[index % 4])
large.write(here / "200-pages.pdf")
