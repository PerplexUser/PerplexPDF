from pathlib import Path

from reportlab.pdfgen import canvas

root = Path(__file__).parent
pdf = canvas.Canvas(str(root / "word-layout.pdf"), pagesize=(612, 792), invariant=1)
pdf.setTitle("Word conversion structure")
pdf.setAuthor("PerplexPDF fixtures")
pdf.setFillColor("#1757AD")
pdf.rect(54, 728, 504, 42, fill=1, stroke=0)
pdf.setFillColor("#FFFFFF")
pdf.setFont("Helvetica-Bold", 21)
pdf.drawString(64, 744, "Project overview")
pdf.setFillColor("#000000")
pdf.setFont("Helvetica", 11)
pdf.drawString(54, 712, "This paragraph starts here and")
pdf.drawString(54, 698, "continues on a second PDF line.")
pdf.setFont("Helvetica-Oblique", 11)
pdf.drawString(54, 674, "Reviewed with care.")
pdf.setFont("Helvetica", 11)
pdf.setFillColor("#A82746")
pdf.drawString(54, 657, "Red accent")
pdf.setFillColor("#177044")
pdf.drawString(112, 657, "and green text")
pdf.setFillColor("#000000")
pdf.drawString(58, 640, "• First editable bullet")
pdf.drawString(58, 624, "• Second editable bullet")
pdf.drawString(58, 608, "7. Original list marker")
for index, row in enumerate([("City", "Quarter", "Amount"), ("Berlin", "Q1", "12 EUR"), ("Bonn", "Q2", "15 EUR")]):
    pdf.setFont("Helvetica-Bold" if index == 0 else "Helvetica", 11)
    for left, cell in zip((54, 225, 410), row):
        pdf.drawString(left, 557 - index * 17, cell)
pdf.drawImage(str(root / "logo.png"), 54, 368, width=155, height=70, mask="auto")
pdf.setStrokeColor("#A82746")
pdf.setLineWidth(3)
pdf.line(410, 423, 558, 368)
pdf.drawString(422, 391, "Chart note")
pdf.setFont("Helvetica", 9)
pdf.drawCentredString(306, 35, "Page 1 of 2")
pdf.showPage()

pdf.setFont("Helvetica-Bold", 20)
pdf.drawString(54, 746, "Inhaltsverzeichnis")
pdf.setFont("Helvetica", 11)
for index, (left, right) in enumerate(zip(("Intro", "Methods", "Results"), ("Appendix", "Sources", "Index"))):
    y = 705 - 20 * index
    pdf.drawString(54, y, left + " ........")
    pdf.drawString(246, y, str(index + 1))
    pdf.drawString(330, y, right + " ........")
    pdf.drawString(543, y, str(index + 4))
pdf.setFont("Helvetica", 9)
pdf.drawCentredString(306, 35, "Page 2 of 2")
pdf.save()
