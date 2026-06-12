#!/usr/bin/env python3
"""Add a footer (running title + 'Seite X von Y') to every page except the cover."""
import io, sys
from pypdf import PdfReader, PdfWriter
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import A4

src, dst, title = sys.argv[1], sys.argv[2], sys.argv[3]

reader = PdfReader(src)
n = len(reader.pages)
numbered = n - 1  # cover (page 0) stays unnumbered

W, H = A4
buf = io.BytesIO()
c = canvas.Canvas(buf, pagesize=A4)
for i in range(n):
    if i >= 1:  # skip cover
        c.setStrokeColorRGB(0.79, 0.84, 0.90)
        c.setLineWidth(0.5)
        c.line(56, 46, W - 56, 46)
        c.setFont("Helvetica", 8)
        c.setFillColorRGB(0.42, 0.42, 0.42)
        c.drawString(56, 34, title)
        c.drawRightString(W - 56, 34, f"Seite {i} von {numbered}")
    c.showPage()
c.save()
buf.seek(0)

overlay = PdfReader(buf)
writer = PdfWriter()
for i, page in enumerate(reader.pages):
    if i < len(overlay.pages):
        page.merge_page(overlay.pages[i])
    writer.add_page(page)

with open(dst, "wb") as f:
    writer.write(f)
print(f"OK  {dst}  ({n} Seiten, davon {numbered} nummeriert)")
