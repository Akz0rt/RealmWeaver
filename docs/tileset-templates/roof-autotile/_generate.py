# -*- coding: utf-8 -*-
"""Build the deliverables: atlas, key sheet, error sheet, .aseprite."""
import os, sys, struct, zlib
from PIL import Image, ImageDraw, ImageFont
from _rules import render, ordered, name, S, LIGHT, DARK

sys.stdout.reconfigure(encoding='utf-8')
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

FONT = 'C:/Windows/Fonts/segoeui.ttf'
FONTB = 'C:/Windows/Fonts/segoeuib.ttf'
def f(sz, bold=False): return ImageFont.truetype(FONTB if bold else FONT, sz)

INK, MUTED, RED, PAPER = (30, 28, 40), (120, 116, 130), (190, 40, 40), (250, 249, 246)

# ---------------------------------------------------------------- the 47 tiles
tiles = []   # (family_key, family_title, voids, notches, label, image)
for key, title, group in ordered():
    for v, nt in group:
        tiles.append((key, title, v, nt, name(v, nt), render(set(v), set(nt))))
assert len(tiles) == 47

COLS = 8
ROWS = (len(tiles) + COLS - 1) // COLS

# ---------------------------------------------------------------- atlas
atlas = Image.new('RGBA', (COLS * S, ROWS * S), (0, 0, 0, 0))
for i, t in enumerate(tiles):
    atlas.alpha_composite(t[5], ((i % COLS) * S, (i // COLS) * S))
atlas.save(os.path.join(OUT, 'roof-atlas.png'))

# ---------------------------------------------------------------- .aseprite
def write_aseprite(img, path, grid=S):
    w, h = img.size
    raw = zlib.compress(img.tobytes())
    cel = struct.pack('<HhhBHh5x', 0, 0, 0, 255, 2, 0) + struct.pack('<HH', w, h) + raw
    cel = struct.pack('<IH', len(cel) + 6, 0x2005) + cel
    nm = 'roof'.encode('utf8')
    lay = struct.pack('<HHHHHHB3x', 1, 0, 0, 0, 0, 0, 255) + struct.pack('<H', len(nm)) + nm
    lay = struct.pack('<IH', len(lay) + 6, 0x2004) + lay
    frame = struct.pack('<IHHH2xI', 16 + len(lay) + len(cel), 0xF1FA, 2, 100, 2) + lay + cel
    head = struct.pack('<IHHHHHIH8xB3xHBBhhHH84x',
                       128 + len(frame), 0xA5E0, 1, w, h, 32, 1, 100,
                       0, 0, 1, 1, 0, 0, grid, grid)
    open(path, 'wb').write(head + frame)

write_aseprite(atlas, os.path.join(OUT, 'roof-atlas.aseprite'))

# ---------------------------------------------------------------- key sheet
def neighbours(voids, notches, px=12):
    """3x3 diagram: filled neighbours dark, outside light, notched corners hatched."""
    im = Image.new('RGB', (px * 3 + 2, px * 3 + 2), PAPER)
    d = ImageDraw.Draw(im)
    pos = {'NW': (0, 0), 'N': (1, 0), 'NE': (2, 0), 'W': (0, 1), 'E': (2, 1),
           'SW': (0, 2), 'S': (1, 2), 'SE': (2, 2)}
    FILL, VOID, ANY = (70, 66, 90), (240, 239, 243), (192, 190, 200)
    for k, (cx, cy) in pos.items():
        if k in ('N', 'E', 'S', 'W'):
            c = VOID if k in voids else FILL
        else:
            a, b = {'NE': ('N', 'E'), 'SE': ('S', 'E'), 'SW': ('S', 'W'), 'NW': ('N', 'W')}[k]
            c = ANY if (a in voids or b in voids) else VOID if k in notches else FILL
        d.rectangle([cx * px + 1, cy * px + 1, cx * px + px, cy * px + px], fill=c,
                    outline=(200, 198, 205))
    d.rectangle([px + 1, px + 1, px * 2, px * 2], fill=(190, 60, 60), outline=(150, 40, 40))
    return im

CW, CH = 150, 132
HDR = 46
groups = []
for key, title, group in ordered():
    groups.append((title, [t for t in tiles if t[0] == key]))

height = 112
for title, g in groups:
    height += HDR + ((len(g) + COLS - 1) // COLS) * CH + 14
sheet = Image.new('RGB', (COLS * CW + 48, height), PAPER)
d = ImageDraw.Draw(sheet)
d.text((24, 26), 'Крыши зданий — канонический набор, 47 тайлов 64×64',
       font=f(26, True), fill=INK)
d.text((24, 62), 'Свет сверху-слева · скат на N/W светлый, на S/E тёмный · ровное серое = плоский верх крыши',
       font=f(13), fill=MUTED)
d.text((24, 80), 'Схема 3×3 под тайлом — соседи клетки: красная — сам тайл, тёмная — там дом, '
                 'белая — там пусто, серая — не важно',
       font=f(13), fill=MUTED)

y = 112
idx = 0
for title, g in groups:
    d.rectangle([24, y + 8, COLS * CW + 24, y + 9], fill=(220, 218, 226))
    d.text((24, y + 18), f'{title}  ·  {len(g)} шт.', font=f(17, True), fill=INK)
    y += HDR
    for k, t in enumerate(g):
        cx = 24 + (k % COLS) * CW
        cy = y + (k // COLS) * CH
        sheet.paste(t[5].convert('RGB'), (cx + 8, cy))
        d.rectangle([cx + 7, cy - 1, cx + 8 + S, cy + S], outline=(210, 208, 216))
        sheet.paste(neighbours(t[2], t[3]), (cx + 8, cy + S + 10))
        d.text((cx + 58, cy + S + 10), f'#{idx}', font=f(15, True), fill=INK)
        d.text((cx + 58, cy + S + 28), t[4], font=f(12), fill=MUTED)
        idx += 1
    y += ((len(g) + COLS - 1) // COLS) * CH + 14
sheet.save(os.path.join(OUT, 'roof-key.png'))
print('key sheet', sheet.size, 'tiles', idx)
