# -*- coding: utf-8 -*-
"""Пилот: крыши города. Третий заход — настоящий пиксель-арт, сказочный дом.

Что это значит на деле, в отличие от прошлого захода:
  * холст 64x64, как в твоей схеме. В игру уходит 128 — ровное удвоение по ближайшему соседу,
    то есть «пиксель» в игре крупный и честный, а не размытая гладь;
  * ПАЛИТРА ФИКСИРОВАНА: девять яркостей, и ничего между ними. Ни одного градиента: там, где
    раньше был плавный переход, теперь ступень или дизеринг 4x4;
  * фактура задаётся не множителем, а СМЕЩЕНИЕМ ПО ПАЛИТРЕ (-3…+1 ступени) — так пиксель-арт
    и рисуют, и так его потом легко перекрашивать: меняешь девять чисел, меняется весь набор;
  * конёк и скаты гуляют: линия чуть виляет, у черепицы неровные ряды. Ровная крыша — это
    ангар, а нам нужен сказочный дом.

Арт чёрно-белый: выбран вариант «рендер домножает картинку на цвет» (docs/tileset-authoring.md).
Цвет города приходит из рендера — buildingColor / wallColor / roadColor.
"""
import os, sys, math, struct, zlib
from PIL import Image

OUT = os.path.dirname(os.path.abspath(__file__))
sys.stdout.reconfigure(encoding='utf-8')

# ────────────────────────────── крутилки ──────────────────────────────

S       = 64            # холст, на котором всё рисуется
UPSCALE = 2             # во столько раз он растёт при экспорте: 64 -> 128
OUTLINE = 2             # силуэт, в пикселях холста

# Палитра. Индекс 0 — прозрачность, дальше девять яркостей от тёмной к светлой.
# Это ЕДИНСТВЕННОЕ место, где живёт тон: всё остальное оперирует номерами ступеней.
# Ступени подняты вверх: рендер домножает арт на buildingColor (0.66, 0.38, 0.24), и всё, что
# нарисовано ниже середины, после умножения превращается в грязь. Тёмные ступени оставлены под
# линии толщиной в пиксель, а не под целые плоскости.
RAMP = [56, 92, 124, 150, 172, 194, 214, 234, 252]
I_EDGE, I_DEEP, I_DARK, I_S, I_E, I_W, I_N, I_FLAT, I_CAP = range(9)
I_MID = I_E

FACE_I = {'S': I_S, 'E': I_E, 'W': I_W, 'N': I_N, 'P': I_FLAT}

# Глубина ската с каждой стороны, В КЛЕТКАХ (может быть больше единицы: скат тогда покрывает
# клетку целиком и продолжается в соседнюю). Сумма противоположных = 2.0 — тогда:
#   дом толщиной в 1 клетку → скаты сходятся коньком внутри клетки;
#   дом толщиной в 2 клетки → северная клетка вся скат, южная вся скат, конёк ровно на их стыке.
# 0.8 против 1.2 — это перспектива: камера смотрит с юга, дальний скат ракурсом сжат.
DEPTH_HIP      = {'N': 0.80, 'S': 1.20, 'E': 1.00, 'W': 1.00}
DEPTH_GABLE_EW = {'N': 0.80, 'S': 1.20}
DEPTH_GABLE_NS = {'E': 1.00, 'W': 1.00}

STYLES = [
    ('hip',      'Вальмовая',              DEPTH_HIP),
    ('gable-ew', 'Двускатная, конёк В-З',  DEPTH_GABLE_EW),
    ('gable-ns', 'Двускатная, конёк С-Ю',  DEPTH_GABLE_NS),
]
DEPTHS = dict((k, d) for k, _, d in STYLES)
DEPTHS['legacy'] = {'N': 0.50, 'S': 0.50, 'E': 0.50, 'W': 0.50}   # для панели «было»

WOBBLE = 1.5            # на сколько пикселей виляет линия ската. 0 — идеально ровно, «ангар»
WOB_PERIOD = 32         # период виляния; обязан делить S, иначе линия рвётся на шве клетки
EAVE_PX = 5             # карнизная доска: сколько пикселей у нижнего края ската
CAP_W = 2               # коньковая накладка
VERGE_W = 3             # причелина на фронтоне двускатной крыши

SIDES   = ('N', 'E', 'S', 'W')
CORNERS = {'NE': ('N', 'E'), 'SE': ('S', 'E'), 'SW': ('S', 'W'), 'NW': ('N', 'W')}

BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]
def dither(x, y, t):
    """Ступень между двумя тонами вместо градиента. t=0 — весь тёмный, t=1 — весь светлый."""
    return BAYER[y & 3][x & 3] < t * 16

def _h(a, b):
    v = (a * 374761393 + b * 668265263) & 0xFFFFFFFF
    v = (v ^ (v >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((v ^ (v >> 16)) & 0xFFFF) / 65535.0

# ────────────────────────────── геометрия скатов ──────────────────────────────

def _wob(t):
    """Виляние линии ската. Период делит S, поэтому у соседней клетки линия продолжается."""
    return WOBBLE * math.sin(2 * math.pi * (t % WOB_PERIOD) / WOB_PERIOD)

def regions(voids, notches, style='hip'):
    """Карта граней и нормированной высоты.

    u = (расстояние до края) / (глубина ската). Скат живёт там, где u < 1; при равных глубинах
    это ровно старое правило из ../roof-autotile, при разных — тот же прямой стык, но не под 45°.
    """
    dep = DEPTHS[style]
    reg = [[None] * S for _ in range(S)]
    nrm = [[1.0] * S for _ in range(S)]
    for y in range(S):
        for x in range(S):
            d = {'N': y + 0.5 + _wob(x), 'S': S - 0.5 - y + _wob(x + 11),
                 'W': x + 0.5 + _wob(y + 5), 'E': S - 0.5 - x + _wob(y + 19)}
            u = {k: d[k] / (dep[k] * S) for k in dep}
            best, face = 1.0, 'P'
            for v in SIDES:                        # выпуклый случай: крыша сходит к пустой стороне
                if v in voids and v in u and u[v] < best:
                    best, face = u[v], v           # порядок перебора фиксирован — набор обязан
            if len(dep) == 4:                      # быть одинаковым от запуска к запуску
                for c in ('NE', 'SE', 'SW', 'NW'): # вогнутый угол: две грани гребнем к углу
                    if c not in notches:
                        continue
                    a, b = CORNERS[c]
                    # берётся БОЛЬШЕЕ нормированное — тогда грань вдоль северной стороны смотрит
                    # на восток, а не на север. Это решение ДМа, и его легко «починить» в баг.
                    h, f = (u[a], a) if u[a] >= u[b] else (u[b], b)
                    if h < best:
                        best, face = h, f
            reg[y][x], nrm[y][x] = face, best
    return reg, nrm

# ────────────────────────────── фактуры ──────────────────────────────
# Фактура возвращает СМЕЩЕНИЕ ПО ПАЛИТРЕ, а не множитель. Зависит только от координат внутри
# тайла, и все периоды делят 64 — иначе ряды соседних клеток разъедутся по фазе.

def axes(facet, x, y):
    """u — вдоль ската (поперёк рядов), v — вдоль ряда."""
    return (x, y) if facet in ('E', 'W') else (y, x)

SCALE_ARC = (3, 2, 1, 1, 1, 1, 2, 3)      # чешуйка круглится: отступ края от низа ячейки 8x8

def m_scale(u, v):
    """Чешуя — сказочная крыша. Ряды по 8, чешуйки по 8 вразбежку, край круглый."""
    row = u // 8
    off = v + (4 if row % 2 else 0)
    sx, sy = off % 8, u % 8
    jag = 1 if _h(off // 8 % 8, row % 8) > 0.72 else 0      # каждый седьмой ряд чуть просел
    edge = 8 - SCALE_ARC[sx] - jag
    if sy >= edge:      return -3                            # щель под чешуйкой
    if sy == edge - 1:  return +1                            # блик по краю
    if sy < 2:          return -1                            # тень от чешуйки сверху
    return 0

def m_shingle(u, v):
    """Дранка: ряды по 8, дощечки по 16 вразбежку, у каждой свой тон."""
    row, in_row = u // 8, u % 8
    off = v + (8 if row % 2 else 0)
    col, in_col = off // 16, off % 16
    if in_row == 7:     return -3                            # торец дощечки
    if in_col == 0:     return -2                            # стык дощечек
    k = _h(col % 4, row % 8)
    return (-1 if k < 0.34 else (1 if k > 0.88 else 0))      # разнотон дерева

def m_thatch(u, v):
    """Солома: снопы по 16, край гребёнкой, вдоль ската — соломины."""
    arc = (2, 1, 0, 0, 1, 2, 3, 3)
    row, in_row = u // 16, u % 16
    edge = 16 - arc[v % 8] - (1 if _h(row % 4, (v // 8) % 8) > 0.6 else 0)
    if in_row >= edge:  return -3
    if in_row == 0:     return -2                            # тень под снопом сверху
    if in_row < 4:      return -1
    if v % 4 == 0:      return -1                            # соломины
    return 1 if in_row >= edge - 2 else 0

MATERIALS = [
    ('scale',   'Чешуя',   m_scale),
    ('shingle', 'Дранка',  m_shingle),
    ('thatch',  'Солома',  m_thatch),
]
MAT = dict((k, f) for k, _, f in MATERIALS)

# ────────────────────────────── тайл крыши ──────────────────────────────

def _clamp(i):
    return max(I_EDGE + 1, min(I_CAP, i))

def roof(voids, notches, mat, style='hip', perspective=True, legacy=False, deco=None, layers=False):
    """Тайл крыши 64x64 в индексах палитры. legacy=True — первый заход, для панели «было»."""
    if legacy:
        style, perspective = 'legacy', False
    reg, nrm = regions(voids, notches, style)
    dep = DEPTHS[style]
    idx = [[FACE_I[reg[y][x]] for x in range(S)] for y in range(S)]
    ink = [[False] * S for _ in range(S)]

    for y in range(S):
        for x in range(S):
            f = reg[y][x]
            if perspective and f != 'P':
                run = dep[f] * S
                px_from_eave = nrm[y][x] * run                # в пикселях, а не в долях
                # кромка карниза гуляет вместе с фактурой: ровная линия — это ангар, а не изба
                if px_from_eave < 2 + (1 if axes(f, x, y)[1] % 8 in (3, 4) else 0):
                    idx[y][x] = I_DEEP
                elif px_from_eave < EAVE_PX:
                    idx[y][x] = _clamp(idx[y][x] - 2)
                elif px_from_eave < EAVE_PX + 4 and not dither(x, y, (px_from_eave - EAVE_PX) / 4):
                    idx[y][x] = _clamp(idx[y][x] - 1)         # ступень с дизерингом, не градиент
            if f != 'P' or not perspective:
                idx[y][x] = _clamp(idx[y][x] + mat(*axes(f, x, y)))
            else:
                idx[y][x] = _clamp(idx[y][x] + max(-1, mat(*axes(f, x, y))))

    # контактная тень на плато — от северного и западного ската (решение ДМа, оставлено как есть)
    for y in range(S):
        for x in range(S):
            if reg[y][x] != 'P':
                continue
            d = 99
            for k in range(1, 6):
                if y - k >= 0 and reg[y - k][x] != 'P':
                    if reg[y - k][x] == 'N':
                        d = min(d, k)
                    break
            for k in range(1, 6):
                if x - k >= 0 and reg[y][x - k] != 'P':
                    if reg[y][x - k] == 'W':
                        d = min(d, k)
                    break
            if 2 <= d <= 4:
                idx[y][x] = _clamp(idx[y][x] - 1)

    if perspective:
        # Конёк — только там, где сходятся ДВА СКАТА. Стык ската с плато — это верхний край
        # ската: там нужна тёмная линия, а не светлая накладка, иначе крыша обрастает светлым
        # каркасом по всем швам и перестаёт читаться как объём.
        cap, brow = set(), set()
        for y in range(S):
            for x in range(S):
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if not (0 <= nx < S and 0 <= ny < S) or reg[ny][nx] == reg[y][x]:
                        continue
                    if reg[y][x] != 'P' and reg[ny][nx] != 'P':
                        cap.add((x, y))
                    elif reg[y][x] != 'P':
                        brow.add((x, y))
        for _ in range((CAP_W - 1) // 2):
            cap |= {(x + dx, y + dy) for x, y in cap for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))
                    if 0 <= x + dx < S and 0 <= y + dy < S and reg[y + dy][x + dx] != 'P'}
        for x, y in cap:
            idx[y][x] = I_FLAT          # конёк светлее скатов, но не белая проволока
        for x, y in brow:
            idx[y][x] = _clamp(idx[y][x] - 2)

        for v in voids:                                       # причелина на фронтоне
            if v in dep:
                continue
            for i in range(S):
                for k in range(OUTLINE, OUTLINE + VERGE_W):
                    x, y = ((i, k) if v == 'N' else (i, S - 1 - k) if v == 'S'
                            else (k, i) if v == 'W' else (S - 1 - k, i))
                    idx[y][x] = I_FLAT if k < OUTLINE + VERGE_W - 1 else I_DEEP

    # силуэт по внешним сторонам; внутренние стыки силуэтом не обводятся
    if not perspective:
        for y in range(S):
            for x in range(S):
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < S and 0 <= ny < S and reg[ny][nx] != reg[y][x]:
                        ink[y][x] = True
                        break
    for v in voids:
        for i in range(S):
            for k in range(OUTLINE):
                x, y = ((i, k) if v == 'N' else (i, S - 1 - k) if v == 'S'
                        else (k, i) if v == 'W' else (S - 1 - k, i))
                ink[y][x] = True

    if deco:
        deco(idx, ink, reg, nrm, voids, style)

    art = Image.new('RGBA', (S, S))
    out = Image.new('RGBA', (S, S))
    pa, po = art.load(), out.load()
    for y in range(S):
        for x in range(S):
            pa[x, y] = (RAMP[idx[y][x]],) * 3 + (255,)
            if ink[y][x]:
                po[x, y] = (RAMP[I_EDGE],) * 3 + (255,)
    flat = art.copy()
    flat.alpha_composite(out)
    return (flat, [('roof', 0, art), ('outline', 0, out)]) if layers else flat

# ────────────────────────────── украшения (отдельные тайлы-варианты) ──────────────────────────────

def _put(idx, ink, x, y, i, edge=False):
    if 0 <= x < S and 0 <= y < S:
        ink[y][x] = edge
        if not edge:
            idx[y][x] = i

def _box(idx, ink, x0, y0, w, h, face_h, top, front, east, lean=0):
    """Объёмчик поверх крыши: верх, южная грань, восточная полоска, тень вправо-вниз.
    lean — во сколько пикселей завал влево кверху. Ровная труба — скучная труба."""
    for y in range(y0 + h, y0 + h + 4):
        for x in range(x0 + 3, x0 + w + 3):
            if 0 <= x < S and 0 <= y < S and not ink[y][x]:
                idx[y][x] = _clamp(idx[y][x] - 3)
    for y in range(y0, y0 + h):
        sh = int(lean * (y0 + h - y) / max(1, h))
        for x in range(x0 + sh, x0 + w + sh):
            border = x in (x0 + sh, x0 + w + sh - 1) or y in (y0, y0 + h - 1)
            i = top if y < y0 + h - face_h else (east if x >= x0 + w + sh - 3 else front)
            _put(idx, ink, x, y, i, edge=border)

def deco_chimney(idx, ink, reg, nrm, voids, style):
    """Труба, слегка кривая: стоит на коньке, видна и на плато, и на двускатной крыше."""
    _box(idx, ink, 36, 14, 12, 24, 15, I_FLAT, I_W, I_S, lean=2)
    _box(idx, ink, 34, 10, 18, 5, 2, I_CAP, I_W, I_E)              # оголовок пошире ствола
    for x in range(38, 48):
        for y in range(11, 13):
            _put(idx, ink, x, y, I_DEEP)                           # устье

def deco_dormer(idx, ink, reg, nrm, voids, style):
    """Слуховое окно: сидит на южном скате, где его видно с камеры."""
    _box(idx, ink, 20, 30, 24, 20, 11, I_FLAT, I_W, I_E)
    for x in range(20, 44):                                        # свой конёк домиком
        k = min(x - 20, 43 - x)
        for y in range(30, 39):
            if y - 30 < 8 - k:
                _put(idx, ink, x, y, I_MID if x < 32 else I_E)
    for x in range(27, 37):                                        # проём
        for y in range(42, 48):
            _put(idx, ink, x, y, I_EDGE + 1)

def deco_attic(idx, ink, reg, nrm, voids, style):
    """Второй ярус: перепад высоты ВНУТРИ постройки. Рендер знает одну высоту на дом, поэтому
    надстройка живёт внутри клетки — зато и тень бросает на свою же крышу."""
    _box(idx, ink, 13, 10, 38, 36, 16, I_FLAT, I_W, I_S)
    for x in range(13, 51):                                        # конёк надстройки
        _put(idx, ink, x, 18, I_CAP)
    for x in range(26, 38):                                        # окошко под коньком
        for y in range(33, 41):
            _put(idx, ink, x, y, I_EDGE + 1)

DECOS = [('chimney', 'Кривая труба', deco_chimney), ('dormer', 'Слуховое окно', deco_dormer),
         ('attic', 'Второй ярус', deco_attic)]

# ────────────────────────────── плоские тайлы и грань ──────────────────────────────

def _img(fn):
    im = Image.new('RGBA', (S, S))
    p = im.load()
    for y in range(S):
        for x in range(S):
            p[x, y] = (RAMP[_clamp(fn(x, y))],) * 3 + (255,)
    return im

def wall_face():
    """wallSprite: бесшовная кладка. Тянется по высоте как угодно, левый край сходится с правым.
    Сверху — тень от свеса крыши: она в ДОЛЯХ спрайта, поэтому на низком доме это тонкая линия,
    а на стене 1.25 — широкая полоса. Так и задумано: чем выше стена, тем глубже свес."""
    def f(x, y):
        row, in_row = y // 8, y % 8
        off = x + (8 if row % 2 else 0)
        i = I_W if _h((off // 16) % 4, row % 8) > 0.55 else I_MID
        if in_row == 0:            i -= 2                    # горизонтальный шов
        if off % 16 == 0:          i -= 2                    # вертикальный шов
        t = y / (S - 1.0)
        if t < 0.10:               i -= 3                    # тень от свеса крыши
        elif t < 0.20 and not dither(x, y, (t - 0.10) / 0.10):
            i -= 2
        if t > 0.86:               i -= 1                    # притемнение у земли
        return i
    return _img(f)

def road():
    """roadGround: булыжник."""
    def f(x, y):
        gx, gy = (x // 8) % 8, (y // 8) % 8
        jx, jy = int(_h(gx, gy) * 3) - 1, int(_h(gy, gx) * 3) - 1
        ix, iy = (x - jx) % 8, (y - jy) % 8
        d = max(abs(ix - 3.5), abs(iy - 3.5))
        if d > 3.0:                return I_S                # щель между камнями
        i = I_W if _h(gx * 7, gy * 3) > 0.5 else I_MID
        return i + 1 if d < 1.5 else i
    return _img(f)

def yard():
    """voidGround: земля двора. Не булыжник — иначе двор и улица сливаются в один ковёр.
    Ровный тон плюс редкая крапина: сплошной дизеринг на большой площади рябит в глазах."""
    def f(x, y):
        i = I_W
        if _h((x // 2) % 32, (y // 2) % 32) > 0.86:          # проплешины
            i = I_E
        gx, gy = (x // 16) % 4, (y // 16) % 4
        sx, sy = 4 + int(_h(gx, gy) * 8), 4 + int(_h(gy, gx + 5) * 8)
        if abs(x % 16 - sx) < 2 and abs(y % 16 - sy) < 1:
            i = I_N if _h(gx + 3, gy) > 0.5 else I_S         # редкий камешек
        return i
    return _img(f)

# ────────────────────────────── экспорт ──────────────────────────────

def up(img):
    """64 -> 128 по ближайшему соседу. Пиксель остаётся пикселем."""
    return img.resize((img.width * UPSCALE, img.height * UPSCALE), Image.NEAREST)

def write_aseprite(path, size, layers, grid=S):
    """RGBA-файл + палитра в свотчах, чтобы девять яркостей были под рукой при правках."""
    w, h = size
    body = b''
    pal = b''.join(struct.pack('<H', 0) + bytes((v, v, v, 255)) for v in RAMP)
    pal = struct.pack('<III8x', len(RAMP), 0, len(RAMP) - 1) + pal
    body += struct.pack('<IH', len(pal) + 6, 0x2019) + pal
    for nmz, blend, img in layers:
        nm = nmz.encode('utf8')
        lay = struct.pack('<HHHHHHB3x', 1, 0, 0, 0, 0, blend, 255) + struct.pack('<H', len(nm)) + nm
        body += struct.pack('<IH', len(lay) + 6, 0x2004) + lay
    for i, (nmz, blend, img) in enumerate(layers):
        raw = zlib.compress(img.convert('RGBA').tobytes())
        cel = struct.pack('<HhhBHh5x', i, 0, 0, 255, 2, 0) + struct.pack('<HH', w, h) + raw
        body += struct.pack('<IH', len(cel) + 6, 0x2005) + cel
    n = 2 * len(layers) + 1
    frame = struct.pack('<IHHH2xI', 16 + len(body), 0xF1FA, n, 100, n) + body
    head = struct.pack('<IHHHHHIH8xB3xHBBhhHH84x',
                       128 + len(frame), 0xA5E0, 1, w, h, 32, 1, 100,
                       0, len(RAMP), 1, 1, 0, 0, grid, grid)
    open(path, 'wb').write(head + frame)

# ────────────────────────────── соседство ──────────────────────────────

def neighbourhood(g, x, y, mark='#'):
    def f(i, j):
        return 0 <= j < len(g) and 0 <= i < len(g[0]) and g[j][i] == mark
    d = {'N': (0, -1), 'S': (0, 1), 'W': (-1, 0), 'E': (1, 0)}
    voids = {s for s, (dx, dy) in d.items() if not f(x + dx, y + dy)}
    notches = set()
    for c, (a, b) in CORNERS.items():
        if a in voids or b in voids:
            continue
        if not f(x + (1 if 'E' in c else -1), y + (1 if 'S' in c else -1)):
            notches.add(c)
    return frozenset(voids), frozenset(notches)

def tint(img, col):
    """Что сделает рендер: img.color = col, шейдер умножает."""
    r, g, b = col
    out = Image.new('RGBA', img.size)
    src, dst = img.convert('RGBA').load(), out.load()
    for y in range(img.size[1]):
        for x in range(img.size[0]):
            p = src[x, y]
            dst[x, y] = (p[0] * r // 255, p[1] * g // 255, p[2] * b // 255, p[3])
    return out
