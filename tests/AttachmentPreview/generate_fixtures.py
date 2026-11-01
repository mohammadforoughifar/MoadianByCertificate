"""Deterministic, non-sensitive fixtures; pip install pillow python-docx python-pptx pymupdf openpyxl.
Run once; generated fixtures are included, so the .NET tests do not require Python."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
from io import BytesIO
from PIL import Image, ImageDraw
from pptx import Presentation
from pptx.util import Inches, Pt
import pymupdf as fitz
from docx import Document
from docx.shared import Inches as WordInches
import openpyxl
import json

root = Path(__file__).parent / 'Fixtures'
root.mkdir(exist_ok=True)
img = Image.new('RGB', (640, 420), '#eaf2ff')
draw = ImageDraw.Draw(img)
draw.rectangle((40, 40, 600, 380), fill='#2563eb')
draw.ellipse((180, 80, 460, 360), fill='#fde68a')
draw.text((50, 390), 'ACTUAL PREVIEW FIXTURE', fill='black')
for name, fmt in [('sample.png', 'PNG'), ('sample.jpg', 'JPEG'), ('sample.gif', 'GIF'), ('sample.webp', 'WEBP'), ('sample.bmp', 'BMP'), ('sample.tiff', 'TIFF')]:
    img.save(root / name, fmt)
(root / 'sample.svg').write_text('<svg xmlns="http://www.w3.org/2000/svg" width="640" height="420"><rect width="640" height="420" fill="#2563eb"/><circle cx="320" cy="210" r="110" fill="#fde68a"/></svg>')
for name, encrypted in [('sample.pdf', False), ('protected.pdf', True)]:
    pdf = fitz.open()
    for i in range(3):
        page = pdf.new_page()
        page.insert_text((50, 55), 'PDF PREVIEW PAGE %d' % (i + 1), fontsize=22)
        page.insert_image(fitz.Rect(70, 100, 500, 385), filename=str(root / 'sample.png'))
    options = dict(encryption=fitz.PDF_ENCRYPT_AES_256, owner_pw='fixture-owner', user_pw='pdf-fixture') if encrypted else {}
    pdf.save(root / name, deflate=True, **options)
    pdf.close()

w = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
a = 'http://schemas.openxmlformats.org/drawingml/2006/main'
r = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
relsns = 'http://schemas.openxmlformats.org/package/2006/relationships'
paragraph = '<w:p><w:r><w:t>Word content: متن فارسی</w:t></w:r></w:p>'
body = paragraph + '<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Bold heading</w:t></w:r></w:p>'
body += '<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Word table cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>'
body += '<w:p><w:r><w:drawing><a:blip r:embed="img1"/></w:drawing></w:r></w:p>'
body += '<w:p><w:r><w:t>&lt;script&gt;window.parent.PREVIEW_UNSAFE=1&lt;/script&gt;</w:t></w:r></w:p>'
document = f'<w:document xmlns:w="{w}" xmlns:a="{a}" xmlns:r="{r}"><w:body>{body}</w:body></w:document>'
doc = Document()
doc.add_paragraph('Word content: متن فارسی')
doc.add_paragraph('Bold heading').runs[0].bold = True
table = doc.add_table(rows=1, cols=2)
table.cell(0, 0).text = 'Word table cell'
table.cell(0, 1).text = 'مقدار'
doc.add_picture(str(root / 'sample.png'), width=WordInches(4))
doc.add_paragraph('<script>window.parent.PREVIEW_UNSAFE=1</script>')
doc.sections[0].header.paragraphs[0].text = 'Word header marker'
doc.sections[0].footer.paragraphs[0].text = 'Word footer marker'
doc.save(root / 'sample.docx')
with ZipFile(root / 'bad-xml.docx', 'w', ZIP_DEFLATED) as z:
    z.writestr('word/document.xml', '<!DOCTYPE test [<!ENTITY attack SYSTEM "file:///etc/passwd">]><test>&attack;</test>')
(root / 'legacy.doc').write_bytes(bytes.fromhex('D0CF11E0A1B11AE1') + b'legacy fixture')
(root / 'legacy.xls').write_bytes((root / 'legacy.doc').read_bytes())
(root / 'legacy.ppt').write_bytes((root / 'legacy.doc').read_bytes())
(root / 'corrupt.docx').write_bytes(b'PKnot a zip file')
(root / 'corrupt.pdf').write_bytes(b'not a pdf')
(root / 'corrupt.jpg').write_bytes(b'not an image')
(root / 'unsupported.heic').write_bytes(b'not a supported heic')
wb = openpyxl.Workbook()
ws = wb.active
ws.title = 'برگه فارسی'
ws.append(['Excel text', 'متن فارسی', 'Quote " value', '<script>unsafe</script>'])
ws.append([10, 20, '=A2+B2', True])
ws.append(['multi\nline', 'last cell'])
ws.add_image(openpyxl.drawing.image.Image(root / 'sample.png'), 'A5')
extra = wb.create_sheet('Second sheet')
extra.append(['Second sheet marker', 42])
wb.save(root / 'sample.xlsx')
longwb = openpyxl.Workbook()
for i in range(1, 271): longwb.active.cell(i, 1, 'row-%d' % i)
longwb.active.cell(1, 55, 'beyond-column-limit')
longwb.save(root / 'large.xlsx')
(root / 'sample.csv').write_text('name,note,value\r\n"Ali","quoted ""word""",12\r\n"متن فارسی","line one\nline two",13\r\n', encoding='utf-8-sig')
(root / 'windows-1256.csv').write_bytes('نام,مقدار\r\nسلام,۱۲'.encode('cp1256', errors='replace'))
(root / 'quotes.csv').write_text('name,"a;b;c;d",value\nAli,one,12\n')
(root / 'sample.txt').write_text('متن فارسی و Plain text preview\n<script>not executable</script>')
ppt = Presentation()
for i in range(2):
    slide = ppt.slides.add_slide(ppt.slide_layouts[6])
    text = slide.shapes.add_textbox(Inches(.5), Inches(.3), Inches(8), Inches(.8)).text_frame
    text.text = 'PowerPoint slide %d — متن فارسی' % (i + 1)
    text.paragraphs[0].runs[0].font.size = Pt(28)
    slide.shapes.add_picture(str(root / 'sample.png'), Inches(.5), Inches(1.3), width=Inches(4))
    cells = slide.shapes.add_table(2, 2, Inches(5.2), Inches(1.3), Inches(4), Inches(1.4)).table
    cells.cell(0, 0).text = 'Slide table cell'
    cells.cell(0, 1).text = '42'
    cells.cell(1, 0).text = '<script>not executed</script>'
    cells.cell(1, 1).text = 'پایان'
ppt.save(root / 'sample.pptx')
items = []
normal = ['sample.png', 'sample.jpg', 'sample.gif', 'sample.webp', 'sample.bmp', 'sample.tiff', 'sample.svg', 'sample.pdf', 'sample.docx', 'sample.xlsx', 'sample.pptx', 'sample.csv', 'sample.txt', 'protected.pdf', 'legacy.doc', 'legacy.xls', 'legacy.ppt', 'corrupt.docx', 'corrupt.pdf', 'corrupt.jpg', 'unsupported.heic', 'large.xlsx']
for i, name in enumerate(normal, 1): items.append(dict(id=i, file=name, version=101, storage='disk' if i % 2 else 'database'))
for i, name in enumerate(['sample.docx', 'sample.pdf', 'sample.png', 'sample.pptx'], 101): items.append(dict(id=i, file=name, version=102, storage='database'))
items.append(dict(id=200, file='sample.docx', version=103, storage='disk'))
(root / 'manifest.json').write_text(json.dumps(items, ensure_ascii=False, indent=2))
print('Generated', len(items), 'attachment records from', len(list(root.iterdir())), 'fixture files.')
