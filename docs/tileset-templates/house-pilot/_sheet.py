# -*- coding: utf-8 -*-
"""Собирает пилот: атласы, .aseprite, лист pilot.png и проверку швов."""
import os, sys, math
from PIL import Image, ImageDraw, ImageFont
from _pilot import (S, MATERIALS, MAT, STYLES, DECOS, roof, wall_face, road, yard, up,
                    write_aseprite, tint, neighbourhood, _h, OUT)

sys.stdout.reconfigure(encoding='utf-8')

SQUASH, OVERDRAW, EAST = 0.8, 1.04, 0.22
FRONT_SH, EAST_SH = 0.72, 0.52
SHOFF, SHSCALE = 0.14, 1.15
H_MIN, H_MAX = 0.55, 1.10          # реальный разброс высот дома в рендере, по хешу от id

# цвета из SettlementVolumeRenderer.cs, переведённые в 0..255
C_BLD  = (168, 97, 61)      # buildingColor — терракота
C_ROAD = (153, 135, 102)    # roadColor — утоптанный песок
C_VOID = (107, 92, 71)      # voidColor — земля двора
C_BLD_DARK, C_ROAD_DARK, C_VOID_DARK = (74, 69, 104), (78, 74, 86), (56, 53, 66)

INK, MUTE, PAPER, BLUE = (28, 26, 24), (128, 120, 108), (247, 243, 235), (40, 92, 160)
F  = r"C:\Windows\Fonts\segoeui.ttf"
FB = r"C:\Windows\Fonts\segoeuib.ttf"
def font(sz, bold=False): return ImageFont.truetype(FB if bold else F, sz)
def text(d, xy, s, sz=16, bold=False, fill=INK, anchor="la"):
    d.text(xy, s, font=font(sz, bold), fill=fill, anchor=anchor)

# ───────────────────────────── сцена ─────────────────────────────

def height_of(bid):
    """Как рендер: высота дома — хеш от id, в диапазоне 0.55…1.10 клетки."""
    return H_MIN + (H_MAX - H_MIN) * _h(ord(bid), 7)

class Scene:
    def __init__(self, rows, styles):
        self.g = [list(r) for r in rows]
        self.styles = styles
        self.H, self.W = len(self.g), len(self.g[0])
    def at(self, x, y):
        return self.g[y][x]
    def is_bld(self, x, y):
        return 0 <= x < self.W and 0 <= y < self.H and self.g[y][x].isalpha()

def render_scene(sc, mat, cw, cols, legacy=False, cache=None):
    ch = cw * SQUASH
    dw, dh = cw * OVERDRAW, ch * OVERDRAW
    ew = dw * EAST
    c_bld, c_road, c_void = cols
    top = int(H_MAX * cw) + 8
    img = Image.new('RGBA', (int(sc.W * cw), int(sc.H * ch) + top), (0, 0, 0, 0))
    cache = {} if cache is None else cache

    g_road = tint(road().resize((int(cw) - 1, int(ch) - 1), Image.NEAREST), c_road)
    g_void = tint(yard().resize((int(cw) - 1, int(ch) - 1), Image.NEAREST), c_void)
    for j in range(sc.H):
        for i in range(sc.W):
            img.alpha_composite(g_road if sc.at(i, j) == '=' else g_void,
                                (int(i * cw), int(top + j * ch)))

    def paste(src, box, shade):
        w, h = max(1, int(box[2])), max(1, int(box[3]))
        img.alpha_composite(tint(src.resize((w, h), Image.NEAREST),
                                 tuple(int(c * shade) for c in c_bld)), (int(box[0]), int(box[1])))

    wall = wall_face()
    for j in range(sc.H):
        for i in range(sc.W):
            if not sc.is_bld(i, j):
                continue
            bid = sc.at(i, j)
            hh = height_of(bid) * cw
            cx, cy = (i + 0.5) * cw, top + (j + 0.5) * ch
            sh = Image.new('RGBA', (int(dw * SHSCALE), int(dh * SHSCALE)), (0, 0, 0, 0))
            ImageDraw.Draw(sh).ellipse([0, 0, sh.width - 1, sh.height - 1], fill=(0, 0, 0, 70))
            img.alpha_composite(sh, (int(cx + cw * SHOFF - sh.width / 2),
                                     int(cy + ch * SHOFF - sh.height / 2)))
            if not (j + 1 < sc.H and sc.at(i, j + 1) == bid):
                paste(wall, (cx - dw / 2, cy + ch / 2 - hh, dw, hh), FRONT_SH)
            if not (i + 1 < sc.W and sc.at(i, j) == bid and sc.at(i + 1, j) == bid):
                paste(wall, (cx + dw / 2 - ew, cy + ch / 2 - hh, ew, hh), EAST_SH)
            st = sc.styles[bid]
            key = (neighbourhood(sc.g, i, j, bid), st, legacy, id(mat))
            if key not in cache:
                v, n = key[0]
                cache[key] = roof(set(v), set(n), mat, st, legacy=legacy)
            paste(cache[key], (cx - dw / 2, cy - ch / 2 - hh, dw, dh), 1.0)
    return img

STREET = Scene([
    "..........",
    ".AA..BB.C.",
    ".AA..BB.C.",
    "==========",
    ".DDD.EE...",
    ".DDD.EE.F.",
    "..........",
], {'A': 'hip', 'B': 'gable-ew', 'C': 'gable-ns', 'D': 'gable-ew', 'E': 'hip', 'F': 'hip'})

HOUSE = Scene(["....", ".AA.", ".AA.", ".A..", "...."], {'A': 'hip'})

def one_house(style, mat, cw, cols, legacy=False):
    sc = Scene(["....", ".AA.", ".AA.", ".A..", "...."], {'A': style})
    return render_scene(sc, mat, cw, cols, legacy)

# ───────────────────────────── файлы набора ─────────────────────────────

wall, rd, yd = wall_face(), road(), yard()
for nm, lname, im in (('wall-face', 'stone', wall), ('road', 'cobbles', rd), ('yard', 'earth', yd)):
    up(im).save(os.path.join(OUT, f'{nm}-128.png'))          # в игру
    write_aseprite(os.path.join(OUT, f'{nm}-64.aseprite'), (S, S), [(lname, 0, im)])   # править тут

cases = sorted({neighbourhood(STREET.g, i, j, STREET.at(i, j))
                for j in range(STREET.H) for i in range(STREET.W) if STREET.is_bld(i, j)},
               key=lambda k: (len(k[0]), sorted(k[0]), sorted(k[1])))
print('случаев соседства на улице:', len(cases))

for mkey, mtitle, mfn in MATERIALS:
    for skey, stitle, dep in STYLES:
        tiles, lay = [], None
        for c in cases:
            flat, ls = roof(set(c[0]), set(c[1]), mfn, skey, layers=True)
            tiles.append((flat, ls))
        atlas = Image.new('RGBA', (len(cases) * S, S))
        lays = {nm: Image.new('RGBA', (len(cases) * S, S)) for nm, _, _ in tiles[0][1]}
        order = [(nm, bl) for nm, bl, _ in tiles[0][1]]
        for i, (flat, ls) in enumerate(tiles):
            atlas.alpha_composite(flat, (i * S, 0))
            for nm, _, im in ls:
                lays[nm].paste(im, (i * S, 0))
        up(atlas).save(os.path.join(OUT, f'roof-{mkey}-{skey}-128.png'))
        write_aseprite(os.path.join(OUT, f'roof-{mkey}-{skey}-64.aseprite'),
                       (len(cases) * S, S), [(nm, bl, lays[nm]) for nm, bl in order])

# ───────────────────────────── лист ─────────────────────────────

CW = 78
PW, PH = render_scene(STREET, MAT['scale'], CW, (C_BLD, C_ROAD, C_VOID)).size
HW, HH = one_house('hip', MAT['scale'], CW, (C_BLD, C_ROAD, C_VOID)).size
TV = 256

W_SHEET = max(1560, PW + 96)
blocks = [130, HH + 120, HH + 120, 3 * (PH + 74) + 60, TV + 150, TV + 130]
sheet = Image.new('RGB', (W_SHEET, sum(blocks) + 80), PAPER)
d = ImageDraw.Draw(sheet)

def panel(img, x, y, cap):
    bg = Image.new('RGB', img.size, (238, 234, 226))
    bg.paste(img, (0, 0), img)
    sheet.paste(bg, (x, y))
    text(d, (x, y + img.size[1] + 8), cap, 15, fill=MUTE)

y = 40
text(d, (48, y), 'Пилот 3: пиксель-арт, сказочный дом', 34, True)
text(d, (48, y + 48), 'Пиксель-арт: холст 64×64, девять яркостей и ничего между ними, '
                      'ни одного градиента. В игру уходит 128 — ровное удвоение.', 18, fill=MUTE)
y += blocks[0]

# 1 — было / стало
text(d, (48, y - 40), '1. Откуда берётся объём', 26, True)
for k, (leg, cap) in enumerate(((True, 'было: скат по полклетки, плато посередине'),
                                (False, 'стало: 0.8 на север, 1.2 на юг + конёк и карниз'))):
    panel(one_house('hip', MAT['scale'], CW, (C_BLD, C_ROAD, C_VOID), leg),
          48 + k * (HW + 40), y, cap)
y += blocks[1]

# 2 — формы
text(d, (48, y - 40), '2. Формы: один и тот же дом', 26, True)
for k, (skey, stitle, dep) in enumerate(STYLES):
    panel(one_house(skey, MAT['scale'], CW, (C_BLD, C_ROAD, C_VOID)), 48 + k * (HW + 40), y, stitle)
y += blocks[2]

# 3 — улица
text(d, (48, y - 40), '3. Улица: шесть построек, высоты по хешу 0.55…1.10, формы вперемешку', 26, True)
for k, (mkey, mtitle, mfn) in enumerate(MATERIALS):
    panel(render_scene(STREET, mfn, CW, (C_BLD, C_ROAD, C_VOID)), 48, y + k * (PH + 74), mtitle)
y += blocks[3]

# 4 — украшения
text(d, (48, y - 40), '4. Детали: это уже ВАРИАНТЫ тайла, а не общий слот', 26, True)
for k, (dkey, dtitle, dfn) in enumerate(DECOS + [('plain', 'без детали — для сравнения', None)]):
    im = roof({'N', 'S'}, set(), MAT['scale'], 'gable-ew', deco=dfn)
    sheet.paste(tint(im.resize((TV, TV), Image.NEAREST), C_BLD).convert('RGB'), (48 + k * (TV + 40), y))
    text(d, (48 + k * (TV + 40), y + TV + 8), dtitle, 15, fill=MUTE)
text(d, (48, y + TV + 40), 'Слот buildingGround один на все дома: труба в тайле = труба на КАЖДОЙ '
                           'клетке города. Нужен канал вариантов (подпроект D).', 17, fill=BLUE)
y += blocks[4]

# 5 — плоские
text(d, (48, y - 40), '5. Плоские тайлы и вертикальная грань', 26, True)
for k, (im, cap) in enumerate(((rd, 'roadGround: булыжник'),
                               (yd, 'voidGround: земля двора'),
                               (wall, 'wallSprite: кладка, сверху тень от свеса крыши'))):
    sheet.paste(im.resize((TV, TV), Image.NEAREST).convert('RGB'), (48 + k * (TV + 40), y))
    text(d, (48 + k * (TV + 40), y + TV + 8), cap, 15, fill=MUTE)

sheet.save(os.path.join(OUT, 'pilot.png'))
print('pilot.png', sheet.size)

# крупный план: клетка 150 px вместо 78 — видно и накладку, и карниз, и фактуру
zoom = render_scene(Scene(["......", ".AA.B.", ".AA.B.", "======", ".CCC..", ".CCC.D", "......"],
                          {'A': 'hip', 'B': 'gable-ns', 'C': 'gable-ew', 'D': 'hip'}),
                    MAT['scale'], 150, (C_BLD, C_ROAD, C_VOID))
bg = Image.new('RGB', zoom.size, (238, 234, 226))
bg.paste(zoom, (0, 0), zoom)
bg.save(os.path.join(OUT, 'zoom.png'))

# ───────────────────────────── проверка швов ─────────────────────────────

SEAM = Scene(["........", ".AAAAAA.", ".AAAAAA.", ".AAA.AA.", ".A...AA.", "........"], {'A': 'hip'})
for mkey, mtitle, mfn in MATERIALS:
    im = Image.new('RGBA', (SEAM.W * S, SEAM.H * S), (0, 0, 0, 0))
    cache = {}
    for j in range(SEAM.H):
        for i in range(SEAM.W):
            if not SEAM.is_bld(i, j):
                continue
            k = neighbourhood(SEAM.g, i, j, 'A')
            if k not in cache:
                cache[k] = roof(set(k[0]), set(k[1]), mfn, 'hip')
            im.alpha_composite(cache[k], (i * S, j * S))
    im.resize((im.width // 2, im.height // 2), Image.NEAREST).save(
        os.path.join(OUT, f'seam-check-{mkey}.png'))
print('готово ->', OUT)
