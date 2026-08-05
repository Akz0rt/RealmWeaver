# -*- coding: utf-8 -*-
"""Generates the settlement tileset stencils + schematic sheets.
Every number here is READ FROM THE RENDERER, not invented:
  tiltSquash 0.8 | tileOverdraw 1.04 | eastFaceFraction 0.22 | gridThicknessPx 1
  ShadowScale 1.15 | ShadowOffsetCells 0.14 | frontShade 0.72 | eastShade 0.52
  BuildingHeightMin 0.55 | BuildingHeightMax 1.10 | WallHeight 1.25 | GateHeight 0.85
"""
import os, math
from PIL import Image, ImageDraw, ImageFont

OUT = r"D:\D&D\docs\tileset-templates"
os.makedirs(OUT, exist_ok=True)

SQUASH   = 0.8
OVERDRAW = 1.04
EAST     = 0.22
SHSCALE  = 1.15
SHOFF    = 0.14
FRONT_SH = 0.72
EAST_SH  = 0.52
H_MIN, H_MAX, H_WALL, H_GATE = 0.55, 1.10, 1.25, 0.85

C_VOID  = (107,  92,  71)
C_ROAD  = (153, 135, 102)
C_BLD   = (168,  97,  61)
C_DUMMY = (102,  94,  84)
C_WALL  = (138, 133, 120)
C_GATE  = (184, 143,  64)

INK   = (28, 26, 24)
PAPER = (247, 243, 235)
MUTE  = (128, 120, 108)
ACC   = (196,  46, 120)      # magenta guides
BLUE  = (40, 92, 160)

F  = r"C:\Windows\Fonts\segoeui.ttf"
FB = r"C:\Windows\Fonts\segoeuib.ttf"
FM = r"C:\Windows\Fonts\consola.ttf"
def font(sz, bold=False, mono=False):
    return ImageFont.truetype(FM if mono else (FB if bold else F), sz)

def shade(c, k):
    return (int(c[0]*k), int(c[1]*k), int(c[2]*k))

def text(d, xy, s, sz=16, bold=False, fill=INK, anchor="la", mono=False):
    d.text(xy, s, font=font(sz, bold, mono), fill=fill, anchor=anchor)

# ───────────────────────────── stencils (transparent overlays, 128×128) ─────────────────────────────

N = 128

def new_stencil():
    im = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)

def guides(d, alpha=90):
    g = ACC + (alpha,)
    for k in (0.25, 0.5, 0.75):
        p = int(N * k)
        d.line([(p, 0), (p, N - 1)], fill=g, width=1)
        d.line([(0, p), (N - 1, p)], fill=g, width=1)

def corners(d, inset, alpha=220, length=10):
    g = ACC + (alpha,)
    a, b = inset, N - 1 - inset
    for (x, y, dx, dy) in ((a, a, 1, 1), (b, a, -1, 1), (a, b, 1, -1), (b, b, -1, -1)):
        d.line([(x, y), (x + dx * length, y)], fill=g, width=1)
        d.line([(x, y), (x, y + dy * length)], fill=g, width=1)

# 1 — flat tile (roadGround / voidGround): drawn at cw − 1px, full bleed
im, d = new_stencil()
guides(d)
d.rectangle([0, 0, N - 1, N - 1], outline=ACC + (255,), width=1)
corners(d, 0)
im.save(os.path.join(OUT, "stencil-flat-128.png"))

# 2 — volume top (buildingGround / wallGround / gateGround): drawn at 1.04×, so the outer band overlaps
band = max(2, int(round(N * (1 - 1 / OVERDRAW) / 2)))   # ≈ 2 px per side crosses into the neighbour
im, d = new_stencil()
guides(d)
for k in range(band):                                    # the overlap band, painted solid
    d.rectangle([k, k, N - 1 - k, N - 1 - k], outline=ACC + (120,), width=1)
d.rectangle([band, band, N - 1 - band, N - 1 - band], outline=ACC + (255,), width=1)
corners(d, band)
im.save(os.path.join(OUT, "stencil-volume-top-128.png"))

# 3 — wallSprite: square file, stretched wildly; keep it horizontally banded and seamless L↔R
im, d = new_stencil()
for k in range(1, 8):
    y = int(N * k / 8)
    d.line([(0, y), (N - 1, y)], fill=ACC + (60,), width=1)
d.line([(0, 0), (0, N - 1)], fill=ACC + (255,), width=1)
d.line([(N - 1, 0), (N - 1, N - 1)], fill=ACC + (255,), width=1)
d.rectangle([0, 0, N - 1, N - 1], outline=ACC + (120,), width=1)
cy = N // 2
for x0, s in ((6, 1), (N - 7, -1)):                      # arrows: left edge must meet right edge
    d.line([(x0, cy), (x0 + s * 14, cy)], fill=ACC + (255,), width=1)
    d.polygon([(x0, cy), (x0 + s * 5, cy - 4), (x0 + s * 5, cy + 4)], fill=ACC + (255,))
im.save(os.path.join(OUT, "stencil-wallface-128.png"))

# ───────────────────────────── source tiles used by the stretch sheet ─────────────────────────────

def tile_facade():
    """What NOT to draw into wallSprite: a literal façade with a door and windows."""
    im = Image.new("RGB", (N, N), (150, 128, 104))
    d = ImageDraw.Draw(im)
    for y in range(0, N, 16):
        d.line([(0, y), (N, y)], fill=(128, 108, 88), width=2)
    d.rectangle([46, 62, 82, 127], fill=(92, 62, 40), outline=(60, 40, 26), width=2)   # door
    d.ellipse([74, 96, 79, 101], fill=(220, 200, 120))
    for x in (14, 92):                                                                  # windows
        d.rectangle([x, 24, x + 22, 48], fill=(60, 72, 88), outline=(48, 34, 24), width=2)
        d.line([(x + 11, 24), (x + 11, 48)], fill=(48, 34, 24), width=2)
    return im

def tile_band():
    """What TO draw: seamless horizontal courses, no feature that must stay square."""
    im = Image.new("RGB", (N, N), (146, 138, 124))
    d = ImageDraw.Draw(im)
    row = 0
    for y in range(0, N, 16):
        d.rectangle([0, y, N, y + 13], fill=(158, 150, 136) if row % 2 == 0 else (140, 132, 118))
        off = 0 if row % 2 == 0 else 16
        for x in range(-32, N + 32, 32):
            d.line([(x + off, y), (x + off, y + 13)], fill=(118, 110, 98), width=2)
        row += 1
    return im

# ───────────────────────────── sheet 1: the stretch test ─────────────────────────────

CW = 132.0                                   # a "cell" for the sheet, in px
CASES = [
    ("фасад дома, самый низкий", OVERDRAW, H_MIN),
    ("фасад ворот",              OVERDRAW, H_GATE),
    ("фасад дома, самый высокий",OVERDRAW, H_MAX),
    ("фасад стены",              OVERDRAW, H_WALL),
    ("вост. полоска, низкий дом",OVERDRAW * EAST, H_MIN),
    ("вост. полоска, стена",     OVERDRAW * EAST, H_WALL),
]

W1, H1 = 1500, 1000
sheet = Image.new("RGB", (W1, H1), PAPER)
d = ImageDraw.Draw(sheet)
text(d, (48, 40), "wallSprite: один файл — шесть очень разных прямоугольников", 34, True)
text(d, (48, 84), "Слот один на все вертикальные грани. Разброс пропорций ≈10× (от 1.89:1 до 0.18:1).", 19, fill=MUTE)
text(d, (48, 110), "Ниже — ОДИН И ТОТ ЖЕ тайл, растянутый рендером в реальные размеры.", 19, fill=MUTE)

rows = [("нарисован как фасад — плывёт", tile_facade(), 170),
        ("нарисован как бесшовная фактура — держится", tile_band(), 560)]
for title, src, y0 in rows:
    text(d, (48, y0), title, 22, True)
    x = 48
    for label, wf, hf in CASES:
        w, h = int(CW * wf), int(CW * hf)
        box = src.resize((max(w, 4), max(h, 4)), Image.LANCZOS)
        top = y0 + 46 + int(CW * H_WALL) - h
        sheet.paste(box, (x, top))
        d.rectangle([x, top, x + w - 1, top + h - 1], outline=(90, 82, 74), width=1)
        ar = wf / hf
        text(d, (x, y0 + 52 + int(CW * H_WALL) + 8), f"{ar:.2f} : 1", 17, True, mono=True)
        for i, line in enumerate(label.split(", ")):
            text(d, (x, y0 + 52 + int(CW * H_WALL) + 30 + i * 19), line, 15, fill=MUTE)
        x += max(w, 128) + 34

text(d, (48, 950), "Вывод: рисуй горизонтальную фактуру без деталей, которые обязаны остаться квадратными.", 19, True, fill=BLUE)
sheet.save(os.path.join(OUT, "sheet-wallsprite-stretch.png"))

# ───────────────────────────── sheet 2: geometry of one tile ─────────────────────────────

W2, H2 = 1500, 980
sh = Image.new("RGB", (W2, H2), PAPER)
d = ImageDraw.Draw(sh)
text(d, (48, 40), "Как квадратный тайл превращается в клетку города", 34, True)

# panel A: square → 5:4
ax, ay, a = 60, 130, 200
text(d, (ax, ay - 32), "1 — файл квадратный, экран сжимает по вертикали", 20, True)
d.rectangle([ax, ay, ax + a, ay + a], fill=(214, 206, 192), outline=INK, width=2)
text(d, (ax + a // 2, ay + a // 2), "128 × 128", 18, True, anchor="mm", mono=True)
text(d, (ax + a // 2, ay + a + 22), "в файле", 16, fill=MUTE, anchor="ma")
d.line([(ax + a + 30, ay + a // 2), (ax + a + 90, ay + a // 2)], fill=BLUE, width=3)
d.polygon([(ax + a + 96, ay + a // 2), (ax + a + 80, ay + a // 2 - 8), (ax + a + 80, ay + a // 2 + 8)], fill=BLUE)
bx = ax + a + 120
by = ay + int(a * (1 - SQUASH) / 2)
d.rectangle([bx, by, bx + a, by + int(a * SQUASH)], fill=(214, 206, 192), outline=INK, width=2)
text(d, (bx + a // 2, by + int(a * SQUASH) // 2), "5 : 4", 18, True, anchor="mm", mono=True)
text(d, (bx + a // 2, ay + a + 22), "на экране (tiltSquash 0.8)", 16, fill=MUTE, anchor="ma")

# panel B: anatomy of a volume tile
px, py = 60, 470
cw = 210.0
ch = cw * SQUASH
hh = 1.0 * cw                                  # a 1.00-cell-tall house, for the drawing
cx, cy = px + 300, py + 300
dw, dh = cw * OVERDRAW, ch * OVERDRAW
text(d, (px, py - 34), "2 — из чего состоит объёмный тайл (дом высотой 1.00 клетки)", 20, True)
# shadow
d.ellipse([cx + cw * SHOFF - dw * SHSCALE / 2, cy + ch * SHOFF - dh * SHSCALE / 2,
           cx + cw * SHOFF + dw * SHSCALE / 2, cy + ch * SHOFF + dh * SHSCALE / 2],
          fill=(206, 198, 184))
# front
d.rectangle([cx - dw / 2, cy + ch / 2 - hh, cx + dw / 2, cy + ch / 2], fill=shade(C_BLD, FRONT_SH))
# east strip
ew = dw * EAST
d.rectangle([cx + dw / 2 - ew, cy + ch / 2 - hh, cx + dw / 2, cy + ch / 2], fill=shade(C_BLD, EAST_SH))
# top
d.rectangle([cx - dw / 2, cy - ch / 2 - hh, cx + dw / 2, cy + ch / 2 - hh], fill=C_BLD, outline=(70, 40, 26))
# cell outline on the ground
d.rectangle([cx - cw / 2, cy - ch / 2, cx + cw / 2, cy + ch / 2], outline=ACC, width=2)

def callout(x0, y0, x1, y1, s, s2=None):
    d.line([(x0, y0), (x1, y1)], fill=INK, width=1)
    d.ellipse([x0 - 3, y0 - 3, x0 + 3, y0 + 3], fill=INK)
    text(d, (x1 + 8, y1 - 10), s, 17, True)
    if s2: text(d, (x1 + 8, y1 + 10), s2, 15, fill=MUTE)

callout(cx, cy - ch / 2 - hh + 24, cx + dw / 2 + 90, py + 30, "верх", "buildingGround / wallGround / gateGround")
callout(cx - dw / 4, cy + ch / 2 - hh / 2, cx + dw / 2 + 90, py + 150, "передняя грань", "wallSprite, ×0.72 по яркости")
callout(cx + dw / 2 - ew / 2, cy + ch / 2 - hh / 2, cx + dw / 2 + 90, py + 250, "восточная полоска 0.22", "wallSprite, ×0.52 — она делит дома")
callout(cx + cw * SHOFF, cy + ch * SHOFF + dh * SHSCALE / 2 - 14, cx + dw / 2 + 90, py + 350, "тень", "shadowSprite ×1.15, сдвиг 0.14")
callout(cx - cw / 2 + 12, cy, cx + dw / 2 + 90, py + 440, "клетка (розовая рамка)", "тайл рисуется на 4% крупнее неё")

# panel C: the height table
tx, ty = 1020, 130
text(d, (tx, ty - 34), "3 — высоты, в ширинах клетки", 20, True)
bars = [("дом, минимум", H_MIN, C_BLD), ("ворота", H_GATE, C_GATE),
        ("дом, максимум", H_MAX, C_BLD), ("стена", H_WALL, C_WALL)]
bw, gap, base = 74, 34, ty + 330
for i, (name, hv, col) in enumerate(bars):
    x = tx + i * (bw + gap)
    hpx = hv * 200
    d.rectangle([x, base - hpx, x + bw, base], fill=col, outline=INK)
    text(d, (x + bw / 2, base - hpx - 22), f"{hv:.2f}", 18, True, anchor="ma", mono=True)
    for k, line in enumerate(name.split(", ")):
        text(d, (x + bw / 2, base + 10 + k * 18), line, 14, fill=MUTE, anchor="ma")
d.line([(tx - 10, base), (tx + 4 * (bw + gap), base)], fill=INK, width=2)
text(d, (tx, ty + 400), "Высота дома берётся хешем от id — у каждого своя,\nв диапазоне 0.55…1.10. Стена всегда выше любого дома.", 16, fill=MUTE)

sh.save(os.path.join(OUT, "sheet-tile-geometry.png"))

# ───────────────────────────── sheet 3: a derived fragment of a town ─────────────────────────────

# cells: (i, j) -> (type, roomId, heightCells)
B, R, V, WL, G = "B", "R", "V", "W", "G"
cells = {}
COLS, ROWS = 8, 5
for j in range(ROWS):
    for i in range(COLS):
        cells[(i, j)] = (V, 0, 0.0)
for i in range(COLS):
    cells[(i, 0)] = (WL, 0, H_WALL)
cells[(3, 0)] = (G, 0, H_GATE)
for j in range(1, ROWS):
    cells[(3, j)] = (R, 0, 0.0)
for i in range(COLS):
    cells[(i, 3)] = (R, 0, 0.0)
for c in [(1, 1), (2, 1)]:
    cells[c] = (B, 11, 0.92)          # ONE house, two cells → no divider between them
cells[(5, 1)] = (B, 12, 0.68)
cells[(6, 1)] = (B, 13, 1.05)          # a different house flush against it → divider stays
cells[(1, 4)] = (B, 14, 0.80)
for c in [(5, 4), (6, 4)]:
    cells[c] = (B, 15, 0.60)

W3, H3 = 1500, 790
fr = Image.new("RGB", (W3, H3), PAPER)
d = ImageDraw.Draw(fr)
text(d, (48, 40), "Как это собирается: фрагмент города, построенный по формулам рендера", 34, True)
text(d, (48, 84), "Порядок отрисовки построчный: дальние ряды рисуются раньше, поэтому ближний тайл всегда перекрывает дальний.", 19, fill=MUTE)

cw = 96.0
ch = cw * SQUASH
ox, oy = 120, 330
dw, dh = cw * OVERDRAW, ch * OVERDRAW
ew = dw * EAST

COLOR = {B: C_BLD, R: C_ROAD, V: C_VOID, WL: C_WALL, G: C_GATE}

def same_room(rid, i, j):
    if rid == 0: return False
    c = cells.get((i, j))
    return c is not None and c[0] == B and c[1] == rid

for (i, j) in sorted(cells.keys(), key=lambda k: (k[1], k[0])):      # DrawOrder: row-major
    t, rid, hv = cells[(i, j)]
    cx = ox + (i + 0.5) * cw
    cy = oy + (j + 0.5) * ch
    col = COLOR[t]
    h = hv * cw
    volume = h > 0.5
    if volume:
        d.ellipse([cx + cw * SHOFF - dw * SHSCALE / 2, cy + ch * SHOFF - dh * SHSCALE / 2,
                   cx + cw * SHOFF + dw * SHSCALE / 2, cy + ch * SHOFF + dh * SHSCALE / 2],
                  fill=(214, 206, 192))
        if not same_room(rid, i, j + 1):
            d.rectangle([cx - dw / 2, cy + ch / 2 - h, cx + dw / 2, cy + ch / 2], fill=shade(col, FRONT_SH))
        if not same_room(rid, i + 1, j):
            d.rectangle([cx + dw / 2 - ew, cy + ch / 2 - h, cx + dw / 2, cy + ch / 2], fill=shade(col, EAST_SH))
        d.rectangle([cx - dw / 2, cy - ch / 2 - h, cx + dw / 2, cy + ch / 2 - h], fill=col)
    else:
        d.rectangle([cx - cw / 2 + 0.5, cy - ch / 2 + 0.5, cx + cw / 2 - 0.5, cy + ch / 2 - 0.5], fill=col)

MARKS = [
    (ox + 2.5 * cw, oy + 1.5 * ch - 0.92 * cw + 20,
     "один дом на две клетки — грани между ними нет"),
    (ox + 5.5 * cw + dw / 2 - ew / 2, oy + 1.5 * ch - 0.34 * cw,
     "два разных дома вплотную — тёмная полоска осталась"),
    (ox + 3.5 * cw, oy + 0.5 * ch - 0.85 * cw + 22, "ворота, высота 0.85 клетки"),
    (ox + 7.5 * cw, oy + 0.5 * ch - 1.25 * cw + 22, "стена, 1.25 — всегда выше любого дома"),
    (ox + 2.6 * cw, oy + 3.5 * ch, "улица — плоская клетка, объёма нет"),
    (ox + 1.5 * cw, oy + 2.5 * ch, "двор — тоже плоская"),
    (ox + 6.5 * cw + cw * SHOFF, oy + 4.5 * ch + ch * SHOFF + dh * SHSCALE / 2 - 12,
     "тень падает вправо-вниз"),
]
NUMS = "①②③④⑤⑥⑦"
LX = ox + COLS * cw + 52
for k, (mx, my, s) in enumerate(MARKS):
    d.ellipse([mx - 15, my - 15, mx + 15, my + 15], fill=(250, 250, 250), outline=BLUE, width=3)
    text(d, (mx, my + 1), str(k + 1), 20, True, fill=BLUE, anchor="mm")
    ly = 330 + k * 54
    d.ellipse([LX - 15, ly - 15, LX + 15, ly + 15], fill=(250, 250, 250), outline=BLUE, width=3)
    text(d, (LX, ly + 1), str(k + 1), 20, True, fill=BLUE, anchor="mm")
    for w, line in enumerate(s.split(" — ")):
        text(d, (LX + 28, ly - 11 + w * 21), line if w == 0 else "— " + line, 17,
             w == 0, fill=INK if w == 0 else MUTE)

fr.save(os.path.join(OUT, "sheet-town-fragment.png"))

print("done ->", OUT)
for f in sorted(os.listdir(OUT)):
    p = os.path.join(OUT, f)
    print(" ", f, Image.open(p).size, os.path.getsize(p), "bytes")
