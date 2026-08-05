# -*- coding: utf-8 -*-
"""Build the single reference PDF from the same rules that generate the tiles."""
import os, sys
from PIL import Image
from reportlab.pdfgen import canvas as rl_canvas
from reportlab.lib.pagesizes import A4
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

from _rules import (render, ordered, name, S, CORNERS,
                    EDGE, DARK, LIGHT, FLAT, SH1, SH2)
import _showcase

HERE = os.path.dirname(os.path.abspath(__file__))
pdfmetrics.registerFont(TTFont('UI', 'C:/Windows/Fonts/segoeui.ttf'))
pdfmetrics.registerFont(TTFont('UIB', 'C:/Windows/Fonts/segoeuib.ttf'))
pdfmetrics.registerFont(TTFont('Mono', 'C:/Windows/Fonts/consola.ttf'))

W, H = A4
M = 42
INK, MUTED, RED, GREEN = (0.12, 0.11, 0.16), (0.47, 0.45, 0.51), (0.72, 0.18, 0.18), (0.16, 0.51, 0.31)
RULE = (0.86, 0.85, 0.88)

c = rl_canvas.Canvas(os.path.join(HERE, 'roof-tileset.pdf'), pagesize=A4)
c.setTitle('Крыши зданий — тайлсет')
c.setAuthor('RealmWeaver')

page_no = [0]

def sharp(img, factor=8):
    """Blow the pixel art up with nearest neighbour so PDF viewers cannot blur it."""
    return ImageReader(img.resize((img.width * factor, img.height * factor), Image.NEAREST))

def new_page(title=None, sub=None):
    if page_no[0]:
        c.setFont('UI', 8); c.setFillColorRGB(*MUTED)
        c.drawRightString(W - M, 24, str(page_no[0]))
        c.showPage()
    page_no[0] += 1
    y = H - M
    if title:
        c.setFont('UIB', 19); c.setFillColorRGB(*INK)
        c.drawString(M, y - 14, title); y -= 30
        if sub:
            c.setFont('UI', 10); c.setFillColorRGB(*MUTED)
            for line in sub.split('\n'):
                c.drawString(M, y - 4, line); y -= 13
            y -= 4
        c.setStrokeColorRGB(*RULE); c.setLineWidth(0.7)
        c.line(M, y, W - M, y); y -= 18
    return y

def para(y, lines, font='UI', size=10, lead=14, colour=INK, x=M):
    c.setFillColorRGB(*colour)
    for ln in lines:
        f, s = (('UIB', size) if ln.startswith('**') else (font, size))
        c.setFont(f, s)
        c.drawString(x, y, ln.lstrip('*'))
        y -= lead
    return y

def bullets(y, items, size=10, lead=14.5, x=M):
    merged = []                      # an item with an empty head continues the one before it
    for it in items:
        if it[0] or not merged:
            merged.append(list(it))
        else:
            merged[-1].extend(l for l in it if l)
    for it in merged:
        c.setFillColorRGB(*MUTED); c.setFont('UI', size)
        c.drawString(x, y, '•')
        c.setFillColorRGB(*INK)
        for i, ln in enumerate(it):
            c.setFont('UIB' if i == 0 else 'UI', size)
            c.drawString(x + 12, y, ln); y -= lead
        y -= 4
    return y

# ---------------------------------------------------------------- neighbour diagram
def diagram(voids, notches, px=9):
    im = Image.new('RGB', (px * 3, px * 3), (255, 255, 255))
    from PIL import ImageDraw
    d = ImageDraw.Draw(im)
    pos = {'NW': (0, 0), 'N': (1, 0), 'NE': (2, 0), 'W': (0, 1), 'E': (2, 1),
           'SW': (0, 2), 'S': (1, 2), 'SE': (2, 2)}
    FILL, VOID, ANY = (70, 66, 90), (243, 242, 246), (196, 194, 203)
    for k, (cx, cy) in pos.items():
        if k in ('N', 'E', 'S', 'W'):
            col = VOID if k in voids else FILL
        else:
            a, b = CORNERS[k]
            col = ANY if (a in voids or b in voids) else VOID if k in notches else FILL
        d.rectangle([cx * px, cy * px, cx * px + px - 1, cy * px + px - 1], fill=col,
                    outline=(205, 203, 211))
    d.rectangle([px, px, px * 2 - 1, px * 2 - 1], fill=(188, 58, 58), outline=(150, 40, 40))
    return im

RU_SIDE = {'N': 'С', 'E': 'В', 'S': 'Ю', 'W': 'З'}
RU_CORN = {'NE': 'СВ', 'SE': 'ЮВ', 'SW': 'ЮЗ', 'NW': 'СЗ'}

def human(voids, notches):
    a = ('пусто: ' + ' '.join(RU_SIDE[s] for s in ('N', 'E', 'S', 'W') if s in voids)) if voids \
        else 'дом со всех сторон'
    b = ('вырез: ' + ' '.join(RU_CORN[k] for k in ('NE', 'SE', 'SW', 'NW') if k in notches)) \
        if notches else ''
    return a, b

tiles = []
for key, title, group in ordered():
    for v, nt in group:
        tiles.append((key, title, v, nt, name(v, nt), render(set(v), set(nt))))

# ================================================================ p1  титул
y = new_page('Крыши зданий — тайлсет для города',
             'Разбор TileSetScheme.aseprite и собранный по нему полный набор из 47 тайлов.\n'
             'Правило не придумано, а восстановлено из твоего файла и проверено на нём же.')
y = para(y, ['Что в этом документе'], font='UIB', size=12); y -= 4
y = bullets(y, [
    ('Правило', 'палитра, геометрия, контур, тень — всё, по чему собран набор.'),
    ('Вогнутый угол', 'самая тонкая часть правила. Ты её сделал верно, и её легко «починить» в баг.'),
    ('Каталог 47 тайлов', 'каждый подписан, под ним схема соседства 3×3. Номер = позиция в roof-atlas.png.'),
    ('Разбор твоего файла', 'что верно, где второй вариант света, где брак, чего не хватало.'),
    ('Проверка', 'город, собранный из набора автоматически, и счёт по стыкам.'),
    ('Что решить дальше', 'четыре открытых вопроса, из них один блокирует финальные цвета.'),
])
y -= 8
c.setStrokeColorRGB(*RULE); c.line(M, y, W - M, y); y -= 22
y = para(y, ['Коротко: что нашлось'], font='UIB', size=12); y -= 4
y = bullets(y, [
    ('Хребет набора у тебя целый.', 'Все 16 базовых случаев (без вогнутых углов) есть, и все верные.'),
    ('Покрыто 42 случая из 47.', 'Не хватало пяти — дорисованы.'),
    ('23 тайла — второй вариант света.', 'Геометрия верная, но светлое и тёмное поменяны местами:'),
    ('', 'это свет с юго-востока. Не дрожь руки — попиксельная копия с заменой цвета.'),
    ('Брак: три тайла.', '#14 и #67 — по 42 сквозные дыры, #22 — пустой с четырьмя пикселями.'),
    ('Диагонали выкинуты.', 'Тайлы #10 #11 #64 #80 — дом под 45°, в ортогональной сетке им нет места.'),
    ('84 рисунка, повторно использованы три.', 'Один и тот же случай рисовался заново много раз —'),
    ('', 'отсюда весь разброс. Набор ниже собран так, что случай встречается ровно один раз.'),
])

# ================================================================ p2  правило
y = new_page('Правило', 'Всё ниже вычитано из твоего файла. Семь твоих тайлов генератор повторяет\n'
                        'ноль в ноль; остальные — с расхождением меньше 1.5%.')

y = para(y, ['Палитра — ровно твоя, шесть цветов'], font='UIB', size=12); y -= 16
sw = [(EDGE, '#110e2b', 'контур'), (LIGHT, '#383557', 'скат смотрит на север или запад — на свету'),
      (DARK, '#222034', 'скат смотрит на юг или восток — в тени'),
      (FLAT, '#847e87', 'плато: плоский верх крыши'),
      (SH2, '#3c3341', 'тень на плато, ближние 2 px'), (SH1, '#534b57', 'тень на плато, дальний 1 px')]
for col, hexs, what in sw:
    c.setFillColorRGB(col[0] / 255, col[1] / 255, col[2] / 255)
    c.rect(M, y - 3, 26, 13, fill=1, stroke=0)
    c.setFont('Mono', 9); c.setFillColorRGB(*INK); c.drawString(M + 34, y, hexs)
    c.setFont('UI', 10); c.setFillColorRGB(*MUTED); c.drawString(M + 100, y, what)
    y -= 17
y -= 12

y = para(y, ['Геометрия'], font='UIB', size=12); y -= 6
big = render({'N'}, set())
size = 150
c.drawImage(sharp(big), M, y - size, size, size)
ax = M + size + 26
lines = [
    ('Скат 32 px', 'С каждой стороны, за которой пусто, внутрь идёт скат'),
    ('', 'шириной ровно в половину клетки.'),
    ('Плато', 'Всё, что дальше 32 px от любого открытого края, — плоский'),
    ('', 'верх крыши. У дома толщиной в одну клетку плато нет вовсе:'),
    ('', 'два ската сходятся в гребень.'),
    ('Контур 2 px', 'По наружным сторонам и по всем стыкам поверхностей.'),
    ('', 'Между двумя плато контура НЕТ — иначе на стыке соседних'),
    ('', 'домов линия удвоится: рендер рисует объёмные тайлы'),
    ('', 'на 4% крупнее клетки.'),
    ('Тень 2+1 px', 'На плато, только от северного и западного ската.'),
]
ty = y - 6
for head, txt in lines:
    if head:
        c.setFont('UIB', 9.5); c.setFillColorRGB(*INK); c.drawString(ax, ty, head)
    c.setFont('UI', 9.5); c.setFillColorRGB(*MUTED); c.drawString(ax + 68, ty, txt)
    ty -= 13
y -= size + 22

c.setFillColorRGB(*MUTED); c.setFont('UI', 9.5)
c.drawString(M, y, 'Два ската сходятся под 45°. Ниже — все формы, которые из этого следуют:')
y -= 14
row = [({'N'}, set()), ({'N', 'W'}, set()), ({'N', 'S'}, set()), ({'N', 'E', 'W'}, set()),
       ({'N', 'E', 'S', 'W'}, set()), (set(), {'NE'})]
cap = ['одна сторона', 'внешний угол', 'гребень', 'торец', 'пирамида 1×1', 'вогнутый угол']
for i, ((v, n), t) in enumerate(zip(row, cap)):
    x = M + i * 86
    c.drawImage(sharp(render(v, n)), x, y - 62, 62, 62)
    c.setFont('UI', 8); c.setFillColorRGB(*MUTED); c.drawString(x, y - 74, t)

# ================================================================ p3  вогнутый угол
y = new_page('Вогнутый угол — самая тонкая часть',
             'Здесь легко «исправить» правильное в неправильное, поэтому объясняю подробно.')
y = para(y, [
    'Вогнутый угол — это когда по диагонали от клетки пусто, а обе смежные стороны заняты.',
    'В этом углу клетки появляется клин, и крыша уходит вниз к самой точке угла.',
    '',
], size=10.5)
y -= 4
y = para(y, ['**Направления в клине меняются местами.'], size=11)
y = para(y, [
    'Грань клина, лежащая вдоль СЕВЕРНОЙ стороны, смотрит на ВОСТОК — а не на север.',
    'И наоборот: грань вдоль восточной стороны смотрит на север.',
], size=10.5)
y -= 10

demo = Image.new('RGBA', (S * 3, S * 3), (0, 0, 0, 0))
plan = {(0, 1): ({'N', 'W'}, set()), (1, 1): ({'N'}, set()),
        (0, 2): ({'W'}, set()), (1, 2): (set(), {'NE'}), (2, 2): ({'N', 'E'}, set()),
        (2, 1): (set(), set())}
plan = {}
grid = [[1, 1, 0], [1, 1, 1], [1, 1, 1]]
def filled(x, y_):
    return 0 <= x < 3 and 0 <= y_ < 3 and grid[y_][x]
for gy in range(3):
    for gx in range(3):
        if not filled(gx, gy):
            continue
        d = {'N': (0, -1), 'S': (0, 1), 'W': (-1, 0), 'E': (1, 0)}
        v = {s for s, (dx, dy) in d.items() if not filled(gx + dx, gy + dy)}
        nt = set()
        for k, (a, b) in CORNERS.items():
            if a in v or b in v:
                continue
            if not filled(gx + (1 if 'E' in k else -1), gy + (1 if 'S' in k else -1)):
                nt.add(k)
        demo.alpha_composite(render(v, nt), (gx * S, gy * S))
bgd = Image.new('RGBA', demo.size, (238, 236, 232, 255)); bgd.alpha_composite(demo)
dsz = 230
c.drawImage(sharp(bgd.convert('RGB'), 4), M, y - dsz, dsz, dsz)

ax = M + dsz + 20
ty = y - 16
c.setFont('UI', 9.5); c.setFillColorRGB(*MUTED)
for ln in ['Слева — дом 3×3 без одной клетки в правом верхнем',
           'углу. Вогнутый угол — у центральной клетки.',
           '',
           'У клетки НАД ней пусто справа, значит её крыша',
           'уходит вниз на ВОСТОК — это тёмный скат.',
           '',
           'У клетки СПРАВА от неё пусто сверху, значит её крыша',
           'уходит вниз на СЕВЕР — это светлый скат.',
           '',
           'Клин обязан продолжить оба ската. Поэтому его грань',
           'вдоль северной стороны смотрит на восток и красится',
           'тёмным, а грань вдоль восточной — на север, светлым.',
           '',
           'Если красить «как кажется», по стороне, вдоль которой',
           'грань лежит, скаты соседей не сойдутся и на стыке',
           'будет видна ступенька.']:
    c.drawString(ax, ty, ln); ty -= 13
ty -= 6
c.setFont('UIB', 9.5); c.setFillColorRGB(*GREEN)
c.drawString(ax, ty, 'Твой тайл #15 сделан именно так — он верный.')
ty -= 13
c.setFillColorRGB(*RED)
c.drawString(ax, ty, 'Тайл #27 — тот же случай с перепутанным тоном.')
y = min(y - dsz, ty) - 26

c.setFont('UI', 9.5); c.setFillColorRGB(*MUTED)
c.drawString(M, y, 'Четыре вогнутых угла по отдельности. Обрати внимание: у СЗ обе грани светлые, '
                   'у ЮВ обе тёмные —')
y -= 12
c.drawString(M, y, 'а у СВ и ЮЗ одна светлая и одна тёмная, потому что там встречаются свет и тень.')
y -= 16
for i, k in enumerate(['NW', 'NE', 'SW', 'SE']):
    x = M + i * 118
    c.drawImage(sharp(render(set(), {k})), x, y - 74, 74, 74)
    c.setFont('UIB', 9); c.setFillColorRGB(*INK); c.drawString(x, y - 86, RU_CORN[k])
    c.setFont('UI', 8.5); c.setFillColorRGB(*MUTED); c.drawString(x + 26, y - 86, f'(-/{k})')

# ================================================================ каталог
CO, RO = 4, 3
TS = 104
CELLW = (W - 2 * M) / CO
CELLH = 176
i = 0
for key, title, group in ordered():
    gt = [t for t in tiles if t[0] == key]
    y = new_page(f'Каталог · {title}',
                 f'{len(gt)} шт. · номер — позиция в roof-atlas.png (слева направо, сверху вниз)\n'
                 'схема 3×3: красная — сам тайл, тёмная — там дом, белая — там пусто, серая — не важно')
    for k, t in enumerate(gt):
        if k and k % (CO * RO) == 0:
            y = new_page(f'Каталог · {title} (продолжение)')
        kk = k % (CO * RO)
        x = M + (kk % CO) * CELLW
        yy = y - (kk // CO) * CELLH
        c.drawImage(sharp(t[5]), x, yy - TS, TS, TS)
        c.setStrokeColorRGB(*RULE); c.setLineWidth(0.5)
        c.rect(x, yy - TS, TS, TS, fill=0, stroke=1)
        c.drawImage(ImageReader(diagram(t[2], t[3]).resize((108, 108), Image.NEAREST)),
                    x, yy - TS - 34, 30, 30)
        c.setFont('UIB', 11); c.setFillColorRGB(*INK)
        c.drawString(x + 36, yy - TS - 13, f'#{i}')
        c.setFont('Mono', 8); c.setFillColorRGB(*MUTED)
        c.drawString(x + 36 + 22, yy - TS - 13, t[4])
        a, b = human(t[2], t[3])
        c.setFont('UI', 8.5); c.setFillColorRGB(*MUTED)
        c.drawString(x + 36, yy - TS - 25, a)
        if b:
            c.drawString(x + 36, yy - TS - 35, b)
        i += 1

# ================================================================ дом со всеми тайлами
y = new_page('Дом, в котором есть все 47 тайлов',
             'Проверка от обратного: одна постройка, где каждый тайл набора встречается хотя бы раз.\n'
             'Цифра на клетке — номер тайла из каталога, отмечена первая клетка, где он появляется.')

show_img, first = _showcase.build()
assert len(first) == 47, first
sc_w = W - 2 * M
sc_h = sc_w * show_img.height / show_img.width
bgs = Image.new('RGBA', show_img.size, (238, 236, 232, 255)); bgs.alpha_composite(show_img)
c.drawImage(sharp(bgs.convert('RGB'), 2), M, y - sc_h, sc_w, sc_h)
k = sc_w / show_img.width
for idx, (cx, cy) in sorted(first.items()):
    px_ = M + (cx * S + S / 2) * k
    py_ = y - sc_h + (show_img.height - (cy * S + S / 2)) * k
    lbl = str(idx)
    tw = c.stringWidth(lbl, 'UIB', 7) + 6
    c.setFillColorRGB(1, 1, 1); c.setStrokeColorRGB(0.55, 0.53, 0.6); c.setLineWidth(0.4)
    c.roundRect(px_ - tw / 2, py_ - 5, tw, 10, 2, fill=1, stroke=1)
    c.setFillColorRGB(*INK); c.setFont('UIB', 7)
    c.drawCentredString(px_, py_ - 2.5, lbl)
y -= sc_h + 20
y = bullets(y, [
    ('72 клетки, 47 тайлов, ни один не пропущен.',
     'Найдено перебором с отжигом: сначала максимум охвата, потом минимум клеток и',
     'периметра — иначе решение рассыпается на полтора десятка отдельных избушек.'),
    ('Домов два, и второй иначе не получается.',
     'Пирамида — это крыша дома ровно в одну клетку, у которой пусто со всех четырёх сторон.',
     'В связной постройке такой клетки быть не может — отсюда сарай 1×1 справа внизу.'),
    ('Двор — это тоже проверка.',
     'Дырки внутри дома дают вогнутые углы, а узкие крылья в одну клетку — гребни и торцы.',
     'Без них половина набора не встретилась бы.'),
])

# ================================================================ разбор
canon = {t[4]: t[5] for t in tiles}
src = Image.open(os.path.join(HERE, '_source-tiles.png')).convert('RGBA')
def usr(n): return src.crop((0, n * S, S, (n + 1) * S))

INVERTED = [(36, 'N'), (46, 'E'), (45, 'S'), (35, 'W'), (12, 'NW'), (13, 'NE'), (25, 'SW'), (26, 'ES'),
            (67, '-/NW'), (68, '-/NE'), (27, '-/SW'), (81, '-/SE'), (66, '-/NE+SW'), (71, '-/SE+SW'),
            (72, '-/NE+SE'), (39, 'W/NE+SE'), (40, 'N/SE+SW'), (49, 'S/NE+NW'), (50, 'E/SW+NW'),
            (69, 'NW/SE'), (70, 'NE/SW'), (82, 'SW/NE'), (83, 'ES/NW')]
MISSING = ['-/SE+NW', '-/NE+SE+NW', '-/NE+SE+SW', '-/NE+SW+NW', '-/SE+SW+NW']

y = new_page('Разбор твоего файла · второй вариант света',
             '23 тайла. Геометрия у них верная, но светлое и тёмное поменяны местами —\n'
             'это свет с юго-востока. Слева твой, справа — тот, что совпадает с твоим описанием.')
PS = 56
PCO = 3
PCELLW = (W - 2 * M) / PCO
PCELLH = 100
for k, (ui, cn) in enumerate(INVERTED):
    if k and k % (PCO * 5) == 0:
        y = new_page('Второй вариант света — продолжение')
    kk = k % (PCO * 5)
    x = M + (kk % PCO) * PCELLW
    yy = y - (kk // PCO) * PCELLH
    c.drawImage(sharp(usr(ui)), x, yy - PS, PS, PS)
    c.setStrokeColorRGB(*RED); c.setLineWidth(0.8); c.rect(x, yy - PS, PS, PS, fill=0, stroke=1)
    c.drawImage(sharp(canon[cn]), x + PS + 20, yy - PS, PS, PS)
    c.setStrokeColorRGB(*GREEN); c.rect(x + PS + 20, yy - PS, PS, PS, fill=0, stroke=1)
    c.setFont('UI', 13); c.setFillColorRGB(*MUTED); c.drawString(x + PS + 5, yy - PS / 2 - 4, '→')
    c.setFont('UIB', 9.5); c.setFillColorRGB(*RED); c.drawString(x, yy - PS - 13, f'#{ui}')
    c.setFont('Mono', 8); c.setFillColorRGB(*MUTED); c.drawString(x + 24, yy - PS - 13, cn)

y = new_page('Разбор твоего файла · брак и пропуски')
y = para(y, ['Брак в пикселях'], font='UIB', size=12); y -= 12
for k, (ui, cap) in enumerate([(14, '42 сквозные дыры'), (67, '42 сквозные дыры'),
                               (22, 'пустой тайл, 4 случайных пикселя')]):
    x = M + k * 168
    im = usr(ui).convert('RGB'); px = im.load(); u = usr(ui).load()
    for yy2 in range(S):
        for xx in range(S):
            if u[xx, yy2][3] == 0:
                px[xx, yy2] = (255, 0, 0)
    c.drawImage(sharp(im), x, y - 84, 84, 84)
    c.setStrokeColorRGB(*RED); c.rect(x, y - 84, 84, 84, fill=0, stroke=1)
    c.setFont('UIB', 9.5); c.setFillColorRGB(*RED); c.drawString(x, y - 97, f'#{ui}')
    c.setFont('UI', 8.5); c.setFillColorRGB(*MUTED); c.drawString(x + 24, y - 97, cap)
y -= 118

y = para(y, ['Диагональные куски крыши — выкинуты'], font='UIB', size=12); y -= 6
y = para(y, ['Тайлы #10 #11 #64 #80 рисуют дом под 45°. Клетки города ортогональные, '
             'диагональных стен нет —', 'вставить их некуда, и семейство под них не достраивалось.'],
         size=9.5, colour=MUTED); y -= 6
for k, ui in enumerate([10, 11, 64, 80]):
    c.drawImage(sharp(usr(ui)), M + k * 74, y - 66, 66, 66)
y -= 88

y = para(y, ['Не хватало — 5 случаев из 47, все дорисованы'], font='UIB', size=12); y -= 6
y = para(y, ['Один — с двумя вырезами по диагонали, и все четыре случая с тремя вырезами.'],
         size=9.5, colour=MUTED); y -= 10
for k, cn in enumerate(MISSING):
    x = M + k * 100
    c.drawImage(sharp(canon[cn]), x, y - 78, 78, 78)
    c.setStrokeColorRGB(*GREEN); c.rect(x, y - 78, 78, 78, fill=0, stroke=1)
    c.setFont('Mono', 8); c.setFillColorRGB(*GREEN); c.drawString(x, y - 90, cn)

# ================================================================ проверка
y = new_page('Проверка', 'Набор проверялся не на глаз.')
y = bullets(y, [
    ('Семь твоих тайлов повторены ноль в ноль.', 'Плато, все четыре «одна сторона наружу» и оба'),
    ('', 'гребня совпали попиксельно. Остальные — расхождение меньше 1.5%, это дрожь руки'),
    ('', 'на диагоналях, а не разница правил.'),
    ('476 стыков, 0 расхождений цвета.', 'Город собран из случайной застройки, для каждой пары'),
    ('', 'соседних клеток сверены все 64 пикселя вдоль общей границы. Задействованы'),
    ('', 'все 47 случаев из 47 — то есть проверен каждый тайл набора, а не только частые.'),
    ('roof-atlas.aseprite перечитан обратно', 'и совпал с PNG пиксель в пиксель. Оговорка: читал'),
    ('', 'мой же парсер — сам Aseprite этот файл ещё не открывал. Проверь двойным щелчком.'),
])
y -= 10
demo_img = Image.open(os.path.join(HERE, 'roof-demo.png')).convert('RGB')
dw = W - 2 * M
dh = dw * demo_img.height / demo_img.width
c.drawImage(ImageReader(demo_img), M, y - dh, dw, dh)

# ================================================================ дальше
y = new_page('Что решить дальше', 'Четыре открытых вопроса. Второй блокирует финальные цвета всего набора.')
y = bullets(y, [
    ('1 · 64 → 128 — это перерисовка, а не увеличение.', 'docs/tileset-authoring.md советует 128×128.'),
    ('', 'При масштабе ×2 контур станет 4 px и весь набор огрубеет. Схему держи на 64,'),
    ('', 'финальный арт рисуй заново на 128.'),
    ('2 · Развилка из docs/tileset-authoring.md до сих пор не выбрана.', 'Рендер ЗАМЕНЯЕТ цвет'),
    ('', 'картинкой или ДОМНОЖАЕТ её на цвет. Если домножает — весь набор надо рисовать почти'),
    ('', 'в сером, иначе затенёнка ляжет дважды и всё уйдёт в грязь. От этого зависят'),
    ('', 'финальные цвета, поэтому вопрос стоит закрыть до того, как поверх ляжет черепица.'),
    ('3 · Контраст света слабый.', '#383557 и #222034 отличаются примерно на 20 уровней яркости.'),
    ('', 'На картинке города видно, что направление света читается с трудом. Для финального'),
    ('', 'арта светлый скат стоит поднять заметно выше.'),
    ('4 · Потребителя пока нет.', 'В рендере один слот buildingGround на все дома; выбор тайла'),
    ('', 'по соседям — это подпроект D, и слоты SettlementTileSprites ещё не проведены в сцену.'),
])

c.setFont('UI', 8); c.setFillColorRGB(*MUTED)
c.drawRightString(W - M, 24, str(page_no[0]))
c.showPage()
c.save()
print('roof-tileset.pdf', page_no[0], 'страниц')
