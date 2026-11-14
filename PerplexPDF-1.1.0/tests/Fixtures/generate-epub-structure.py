from io import BytesIO
from pathlib import Path

from pypdf import PdfReader, PdfWriter
from pypdf.generic import ArrayObject, BooleanObject, DictionaryObject, NameObject, NumberObject
from reportlab.pdfgen import canvas

directory = Path(__file__).parent
buffer = BytesIO()
pdf = canvas.Canvas(buffer, pagesize=(612, 792), invariant=1)

def text(role, index, y, value, size=11):
    pdf._code.append(f"/{role} <</MCID {index}>> BDC")
    pdf.setFont("Helvetica", size)
    pdf.drawString(50, y, value)
    pdf._code.append("EMC")

text("H1", 0, 742, "First chapter")
text("P", 1, 715, "An embedded picture follows.")
text("H2", 2, 675, "A larger subheading", 21)
pdf.drawImage(str(directory / "logo.png"), 50, 545, width=115, height=65, mask="auto")
text("H1", 3, 485, "Second chapter")
text("P", 4, 450, "Two chapters share the first PDF page.")
pdf.showPage()
text("P", 0, 742, "This page continues the second chapter.")
text("H1", 1, 685, "Third chapter")
text("P", 2, 652, "The final chapter has its own image.")
pdf.drawImage(str(directory / "logo.png"), 50, 540, width=90, height=50, mask="auto")
pdf.save()

writer = PdfWriter()
writer.append_pages_from_reader(PdfReader(buffer))
name = NameObject
def dictionary(**entries):
    return DictionaryObject({name("/" + key): value for key, value in entries.items()})
root = dictionary(Type=name("/StructTreeRoot"))
root_ref = writer._add_object(root)
document = dictionary(Type=name("/StructElem"), S=name("/Document"), P=root_ref, K=ArrayObject())
document_ref = writer._add_object(document)
numbers = ArrayObject()
for page_index, roles in enumerate((("H1", "P", "H2", "H1", "P"), ("P", "H1", "P"))):
    page = writer.pages[page_index]
    page[name("/StructParents")] = NumberObject(page_index)
    parents = ArrayObject()
    for content_id, role in enumerate(roles):
        tag = dictionary(Type=name("/StructElem"), S=name("/" + role), P=document_ref,
                         Pg=page.indirect_reference, K=NumberObject(content_id))
        reference = writer._add_object(tag)
        document[name("/K")].append(reference)
        parents.append(reference)
    numbers.extend((NumberObject(page_index), parents))
root[name("/K")] = ArrayObject([document_ref])
root[name("/ParentTree")] = writer._add_object(dictionary(Nums=numbers))
root[name("/ParentTreeNextKey")] = NumberObject(2)
writer.root_object[name("/StructTreeRoot")] = root_ref
writer.root_object[name("/MarkInfo")] = dictionary(Marked=BooleanObject(True))
with (directory / "epub-structure.pdf").open("wb") as output:
    writer.write(output)
