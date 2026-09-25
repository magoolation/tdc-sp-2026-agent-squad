"""Shared look and layout helpers for the Agent Squad deck.

The palette is the one the project's own dashboard uses, so the slides and the
live demo look like the same product rather than two different things. Type is
deliberately large: this is read from the back of a conference room, not from a
laptop.
"""

from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import MSO_ANCHOR, PP_ALIGN
from pptx.util import Emu, Inches, Pt

# --- palette ---------------------------------------------------------------

BG = RGBColor(0x0D, 0x11, 0x17)
BG_RAISED = RGBColor(0x16, 0x1B, 0x22)
BG_SUNKEN = RGBColor(0x01, 0x04, 0x09)
BORDER = RGBColor(0x30, 0x36, 0x3D)

TEXT = RGBColor(0xE6, 0xED, 0xF3)
MUTED = RGBColor(0x91, 0x98, 0xA1)
DIM = RGBColor(0x6E, 0x76, 0x81)

ACCENT = RGBColor(0x58, 0xA6, 0xFF)
ACCENT_DEEP = RGBColor(0x1F, 0x6F, 0xEB)
SUCCESS = RGBColor(0x3F, 0xB9, 0x50)
WARNING = RGBColor(0xD2, 0x99, 0x22)
DANGER = RGBColor(0xF8, 0x51, 0x49)
VIOLET = RGBColor(0xBC, 0x8C, 0xFF)

WHITE = RGBColor(0xFF, 0xFF, 0xFF)

FONT = "Segoe UI"
FONT_LIGHT = "Segoe UI Light"
MONO = "Consolas"

# --- geometry --------------------------------------------------------------

W = Inches(13.333)
H = Inches(7.5)
MARGIN = Inches(0.85)
CONTENT_W = W - 2 * MARGIN


def blank(prs):
    """Adds a slide with the blank layout and paints the background."""
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    bg = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, 0, 0, W, H)
    bg.fill.solid()
    bg.fill.fore_color.rgb = BG
    bg.line.fill.background()
    bg.shadow.inherit = False
    return slide


def textbox(slide, left, top, width, height):
    """Adds a text box with sane defaults: no autofit surprises, no wrap-off."""
    box = slide.shapes.add_textbox(left, top, width, height)
    frame = box.text_frame
    frame.word_wrap = True
    frame.margin_left = 0
    frame.margin_right = 0
    frame.margin_top = 0
    frame.margin_bottom = 0
    return frame


def write(
    frame,
    text,
    size=20,
    color=TEXT,
    bold=False,
    font=FONT,
    align=PP_ALIGN.LEFT,
    space_after=8,
    space_before=0,
    line=1.25,
    first=False,
):
    """Appends a paragraph. `first` reuses the frame's initial empty paragraph."""
    para = frame.paragraphs[0] if first else frame.add_paragraph()
    para.alignment = align
    para.space_after = Pt(space_after)
    para.space_before = Pt(space_before)
    para.line_spacing = line

    run = para.add_run()
    run.text = text
    run.font.size = Pt(size)
    run.font.color.rgb = color
    run.font.bold = bold
    run.font.name = font
    return para


def rich(frame, parts, size=20, align=PP_ALIGN.LEFT, space_after=8, line=1.25, first=False):
    """Appends a paragraph built from (text, color, bold, font) tuples."""
    para = frame.paragraphs[0] if first else frame.add_paragraph()
    para.alignment = align
    para.space_after = Pt(space_after)
    para.line_spacing = line

    for part in parts:
        text, color = part[0], part[1]
        bold = part[2] if len(part) > 2 else False
        font = part[3] if len(part) > 3 else FONT

        run = para.add_run()
        run.text = text
        run.font.size = Pt(size)
        run.font.color.rgb = color
        run.font.bold = bold
        run.font.name = font
    return para


def box(slide, left, top, width, height, fill=BG_RAISED, outline=BORDER, radius=True):
    """Adds a filled panel."""
    shape_type = MSO_SHAPE.ROUNDED_RECTANGLE if radius else MSO_SHAPE.RECTANGLE
    shape = slide.shapes.add_shape(shape_type, left, top, width, height)
    shape.fill.solid()
    shape.fill.fore_color.rgb = fill
    shape.shadow.inherit = False

    if outline is None:
        shape.line.fill.background()
    else:
        shape.line.color.rgb = outline
        shape.line.width = Pt(1)

    if radius:
        try:
            shape.adjustments[0] = 0.08
        except (IndexError, ValueError):
            pass

    shape.text_frame.word_wrap = True
    return shape


def rule(slide, top, color=BORDER, left=MARGIN, width=None, thickness=1.25):
    """Adds a horizontal rule."""
    width = CONTENT_W if width is None else width
    line = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, left, top, width, Pt(thickness))
    line.fill.solid()
    line.fill.fore_color.rgb = color
    line.line.fill.background()
    line.shadow.inherit = False
    return line


def heading(slide, title, kicker=None, color=TEXT):
    """Standard slide header: optional kicker, title, accent rule."""
    top = Inches(0.55)

    if kicker:
        frame = textbox(slide, MARGIN, top, CONTENT_W, Inches(0.35))
        write(frame, kicker.upper(), size=13, color=ACCENT, bold=True, space_after=0, first=True)
        top = top + Inches(0.38)

    frame = textbox(slide, MARGIN, top, CONTENT_W, Inches(0.8))
    write(frame, title, size=34, color=color, bold=True, space_after=0, first=True)

    rule(slide, top + Inches(0.78), color=BORDER)
    return top + Inches(1.05)


def footer(slide, page, total, label="Agent Squad · TDC São Paulo 2026"):
    """Adds the page footer."""
    frame = textbox(slide, MARGIN, H - Inches(0.62), CONTENT_W * 0.7, Inches(0.3))
    write(frame, label, size=11, color=DIM, space_after=0, first=True)

    frame = textbox(slide, W - MARGIN - Inches(1.2), H - Inches(0.62), Inches(1.2), Inches(0.3))
    write(frame, f"{page}/{total}", size=11, color=DIM, align=PP_ALIGN.RIGHT, space_after=0, first=True)


def code_panel(slide, left, top, width, lines, size=15, height=None):
    """Adds a terminal-style panel with monospaced lines.

    `lines` items are either a string or a (text, color) pair.
    """
    if height is None:
        height = Inches(0.34) * len(lines) + Inches(0.42)

    panel = box(slide, left, top, width, height, fill=BG_SUNKEN, outline=BORDER)
    frame = panel.text_frame
    frame.margin_left = Inches(0.28)
    frame.margin_right = Inches(0.2)
    frame.margin_top = Inches(0.2)
    frame.margin_bottom = Inches(0.18)
    frame.vertical_anchor = MSO_ANCHOR.TOP

    for index, item in enumerate(lines):
        text, color = (item, MUTED) if isinstance(item, str) else item
        write(frame, text, size=size, color=color, font=MONO, space_after=3, line=1.15, first=(index == 0))

    return panel


def bullets(slide, top, items, size=19, gap=15, left=MARGIN, width=None, marker="—"):
    """Adds a bulleted list. Items are strings or (bold_lead, rest) pairs."""
    width = CONTENT_W if width is None else width
    frame = textbox(slide, left, top, width, H - top - Inches(0.9))

    for index, item in enumerate(items):
        if isinstance(item, str):
            parts = [(f"{marker}  ", ACCENT, True), (item, TEXT)]
        else:
            parts = [(f"{marker}  ", ACCENT, True), (item[0], TEXT, True), (item[1], MUTED)]

        rich(frame, parts, size=size, space_after=gap, first=(index == 0))

    return frame


def metric(slide, left, top, width, value, label, color=ACCENT, height=Inches(1.5)):
    """Adds a big-number tile."""
    panel = box(slide, left, top, width, height)
    frame = panel.text_frame
    frame.margin_top = Inches(0.18)
    frame.vertical_anchor = MSO_ANCHOR.MIDDLE

    write(frame, value, size=40, color=color, bold=True, align=PP_ALIGN.CENTER, space_after=2, first=True)
    write(frame, label, size=13, color=MUTED, align=PP_ALIGN.CENTER, space_after=0)
    return panel


def chip(slide, left, top, text, color=ACCENT, width=None, size=13):
    """Adds a small labelled pill."""
    width = Inches(0.24) + Inches(0.085) * len(text) if width is None else width
    shape = box(slide, left, top, width, Inches(0.34), fill=BG_SUNKEN, outline=color)

    frame = shape.text_frame
    frame.margin_left = 0
    frame.margin_right = 0
    frame.vertical_anchor = MSO_ANCHOR.MIDDLE
    write(frame, text, size=size, color=color, bold=True, align=PP_ALIGN.CENTER, space_after=0, first=True)
    return shape


def notes(slide, text):
    """Sets the speaker notes."""
    slide.notes_slide.notes_text_frame.text = text
