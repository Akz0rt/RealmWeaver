# -*- coding: utf-8 -*-
"""Error sheet: what the review found in TileSetScheme.aseprite."""
import os, sys
from PIL import Image, ImageDraw, ImageFont
from _rules import render, ordered, name, S, LIGHT, DARK

sys.stdout.reconfigure(encoding='utf-8')
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
src = Image.open(os.path.join(os.path.dirname(__file__), '_source-tiles.png')).convert('RGBA')
def user(i): return src.crop((0, i * S, S, (i + 1) * S))

FONT, FONTB = 'C:/Windows/Fonts/segoeui.ttf', 'C:/Windows/Fonts/segoeuib.ttf'
def f(sz, bold=False): return ImageFont.truetype(FONTB if bold else FONT, sz)
INK, MUTED, RED, GREEN, PAPER = (30, 28, 40), (120, 116, 130), (185, 45, 45), (40, 130, 80), (250, 249, 246)

canon = {}
for key, title, group in ordered():
    for v, nt in group:
        canon[name(v, nt)] = render(set(v), set(nt))

# tone-inverted twins: your tile, the config it belongs to
INVERTED = [(36, 'N'), (46, 'E'), (45, 'S'), (35, 'W'),
            (12, 'NW'), (13, 'NE'), (25, 'SW'), (26, 'ES'),
            (67, '-/NW'), (68, '-/NE'), (27, '-/SW'), (81, '-/SE'),
            (66, '-/NE+SW'), (71, '-/SE+SW'), (72, '-/NE+SE'),
            (39, 'W/NE+SE'), (40, 'N/SE+SW'), (49, 'S/NE+NW'), (50, 'E/SW+NW'),
            (69, 'NW/SE'), (70, 'NE/SW'), (82, 'SW/NE'), (83, 'ES/NW')]
MISSING = ['-/SE+NW', '-/NE+SE+NW', '-/NE+SE+SW', '-/NE+SW+NW', '-/SE+SW+NW']
DEFECTS = [(14, '42 сквозные дыры'), (67, '42 сквозные дыры'), (22, 'пустой, 4 пикселя')]
DIAGONAL = [10, 11, 64, 80]

PW, PH = 196, 132          # pair cell
COLS = 6
rows_inv = (len(INVERTED) + COLS - 1) // COLS
H = 112 + 40 + rows_inv * PH + 60 + PH + 60 + PH + 40
W = COLS * PW + 48
sheet = Image.new('RGB', (W, H), PAPER)
d = ImageDraw.Draw(sheet)

d.text((24, 26), 'Разбор TileSetScheme.aseprite — что нашлось', font=f(26, True), fill=INK)
d.text((24, 62), '84 рисунка, 47 канонических случаев. Слева — твой тайл, справа — канонический.', font=f(13), fill=MUTED)

def pair(x, y, ui, cn, caption):
    a, b = user(ui).convert('RGB'), canon[cn].convert('RGB')
    sheet.paste(a, (x, y)); sheet.paste(b, (x + S + 26, y))
    d.rectangle([x - 1, y - 1, x + S, y + S], outline=RED)
    d.rectangle([x + S + 25, y - 1, x + S + 26 + S, y + S], outline=GREEN)
    d.text((x + S + 8, y + 24), '→', font=f(17, True), fill=MUTED)
    d.text((x, y + S + 6), f'#{ui}', font=f(13, True), fill=RED)
    d.text((x + 30, y + S + 7), caption, font=f(11), fill=MUTED)

y = 112
d.rectangle([24, y, W - 24, y + 1], fill=(220, 218, 226))
d.text((24, y + 8), f'Второй вариант света — {len(INVERTED)} тайла. Геометрия верная; свет падает с юго-востока, '
                    'а не с северо-запада. Справа — тот, что совпадает с твоим правилом.', font=f(16, True), fill=INK)
y += 40
for k, (ui, cn) in enumerate(INVERTED):
    pair(24 + (k % COLS) * PW, y + (k // COLS) * PH, ui, cn, cn)
y += rows_inv * PH + 20

d.rectangle([24, y, W - 24, y + 1], fill=(220, 218, 226))
d.text((24, y + 8), 'Брак в пикселях', font=f(16, True), fill=INK)
y += 40
for k, (ui, cap) in enumerate(DEFECTS):
    x = 24 + k * PW
    im = user(ui).convert('RGB'); px = im.load()
    u = user(ui).load()
    for yy in range(S):
        for xx in range(S):
            if u[xx, yy][3] == 0: px[xx, yy] = (255, 0, 0)
    sheet.paste(im, (x, y)); d.rectangle([x - 1, y - 1, x + S, y + S], outline=RED)
    d.text((x, y + S + 6), f'#{ui}', font=f(13, True), fill=RED)
    d.text((x + 30, y + S + 7), cap, font=f(11), fill=MUTED)
x = 24 + 3 * PW
d.text((x, y), 'Диагональные куски крыши', font=f(13, True), fill=INK)
d.text((x, y + 18), 'тайлы ' + ', '.join(f'#{i}' for i in DIAGONAL) + ' — дом под 45°.', font=f(11), fill=MUTED)
d.text((x, y + 34), 'В городе клетки ортогональные,', font=f(11), fill=MUTED)
d.text((x, y + 50), 'вставить их некуда — выкинуты.', font=f(11), fill=MUTED)
for k, i in enumerate(DIAGONAL):
    sheet.paste(user(i).convert('RGB').resize((44, 44)), (x + k * 50, y + 72))
y += PH + 20

d.rectangle([24, y, W - 24, y + 1], fill=(220, 218, 226))
d.text((24, y + 8), f'Не хватало — {len(MISSING)} случаев из 47. Все дорисованы.', font=f(16, True), fill=INK)
y += 40
for k, cn in enumerate(MISSING):
    x = 24 + k * PW
    sheet.paste(canon[cn].convert('RGB'), (x, y))
    d.rectangle([x - 1, y - 1, x + S, y + S], outline=GREEN)
    d.text((x, y + S + 6), cn, font=f(12, True), fill=GREEN)
sheet.save(os.path.join(OUT, 'roof-review.png'))
print('review sheet', sheet.size)
