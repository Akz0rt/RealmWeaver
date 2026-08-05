# -*- coding: utf-8 -*-
"""Проверки, которые глазом не делаются.

1. Фактура обязана быть 64-периодичной по обеим осям — иначе ряды соседних клеток
   разъезжаются по фазе и на большом доме проступает сетка.
2. Та же проверка, но на собранной постройке: градиент на швах клеток не должен
   выделяться среди градиента внутри клетки.
3. Проверка 1 прогоняется на ЗАВЕДОМО СЛОМАННОЙ фактуре (период 3 не делит 64) —
   если мутант её проходит, проверка ничего не стоит.
4. Толщина контура — измеряется, а не декларируется.
"""
import sys, math
from PIL import Image
from _pilot import (S, OUTLINE, RAMP, I_EDGE, CAP_W, MATERIALS, STYLES, DEPTHS, regions,
                    roof, wall_face, road, yard, _h, neighbourhood)

EDGE = RAMP[I_EDGE]

sys.stdout.reconfigure(encoding='utf-8')
fails = []

def check(name, ok, detail=''):
    print(('  OK   ' if ok else '  ПРОВАЛ ') + name + (('  — ' + detail) if detail else ''))
    if not ok:
        fails.append(name)

# ── 1. периодичность самой маски ──────────────────────────────────────────────
print('1. Маска периодична по 64 в обе стороны')

def periodic(fn):
    bad = []
    for u in range(0, S, 3):
        for v in range(0, S, 3):
            if abs(fn(u, v) - fn(u + S, v)) > 1e-12:
                bad.append(('u', u, v))
            if abs(fn(u, v) - fn(u, v + S)) > 1e-12:
                bad.append(('v', u, v))
    return bad

for key, title, fn in MATERIALS:
    bad = periodic(fn)
    check(f'{title:9s}', not bad, f'{len(bad)} точек расходятся, напр. {bad[:2]}' if bad else '')

def m_thatch_broken(u, v):
    """МУТАНТ: солома, у которой соломины идут через 3 пикселя, а 3 не делит 64."""
    arc = (2, 1, 0, 0, 1, 2, 3, 3)
    row, in_row = u // 16, u % 16
    edge = 16 - arc[v % 8] - (1 if _h(row % 4, (v // 8) % 8) > 0.6 else 0)
    if in_row >= edge:  return -3
    if in_row == 0:     return -2
    if in_row < 4:      return -1
    if v % 3 == 0:      return -1
    return 1 if in_row >= edge - 2 else 0

check('мутант (солома с периодом 3) проверкой ловится', bool(periodic(m_thatch_broken)),
      f'{len(periodic(m_thatch_broken))} расхождений')

# ── 2. на собранной постройке шов не выделяется ───────────────────────────────
# Грубая страховка: ловит только явный разрыв. Сдвиг фазы на пиксель ей не ловится
# (мутант выше её проходит) — его ловит проверка 1, и она тут главная.
print('\n2. На собранной постройке швы клеток не выделяются градиентом (грубая страховка)')

PLAN = ["......", ".####.", ".####.", ".####.", ".####.", "......"]

def assemble(fn):
    g = [list(r) for r in PLAN]
    im = Image.new('RGBA', (len(g[0]) * S, len(g) * S), (0, 0, 0, 0))
    cache = {}
    for j in range(len(g)):
        for i in range(len(g[0])):
            if g[j][i] != '#':
                continue
            k = neighbourhood(g, i, j)
            if k not in cache:
                cache[k] = roof(set(k[0]), set(k[1]), fn)
            im.alpha_composite(cache[k], (i * S, j * S))
    return im.convert('L')

def seam_ratio(im):
    """Перепад яркости на шве клетки против такого же перепада внутри клетки.

    Меряется ТОЛЬКО в глубине плато (клетки 2-3 застройки 4x4), где по обе стороны шва одна и та
    же грань. На краю постройки перепад теперь законный: там сходятся два ската, и шов клетки —
    это конёк. Раньше здесь меряли всю постройку, и после переделки геометрии проверка честно
    падала на собственном коньке."""
    p = im.load()
    y0, y1 = 2 * S + 8, 4 * S - 8
    def col_grad(x):
        return sum(abs(p[x, y] - p[x - 1, y]) for y in range(y0, y1)) / (y1 - y0)
    inner = sorted(col_grad(x) for x in range(2 * S + 4, 4 * S - 4) if x % S not in (0, 1, S - 1))
    return col_grad(3 * S), inner[int(len(inner) * 0.98)]

for key, title, fn in MATERIALS:
    s, w = seam_ratio(assemble(fn))
    check(f'{title:9s}', s <= w, f'шов {s:.1f} против {w:.1f} худшего внутри клетки')

# ── 2b. вогнутый угол: направления обязаны меняться местами ───────────────────
# Решение ДМа, которое легче всего «починить» в баг. С несимметричными глубинами скатов
# max(u[a], u[b]) уже НЕ то же самое, что max(d[a], d[b]) — значит проверять надо заново.
print('\n2b. Вогнутый угол: грань вдоль стороны смотрит в сторону соседней (свап)')
SWAP = [                        # (вырез, точка у стороны, какая грань обязана быть)
    ('NE', (S - 24, 3),       'E'), ('NE', (S - 4, 24),      'N'),
    ('SE', (S - 24, S - 4),   'E'), ('SE', (S - 4, S - 24),  'S'),
    ('SW', (24, S - 4),       'W'), ('SW', (3, S - 24),      'S'),
    ('NW', (24, 3),           'W'), ('NW', (3, 24),          'N'),
]
reg_cache = {}
for corner, (x, y), want in SWAP:
    if corner not in reg_cache:
        reg_cache[corner], _ = regions(set(), {corner}, 'hip')
    got = reg_cache[corner][y][x]
    check(f'вырез {corner} в точке ({x},{y})', got == want, f'грань {got}, ожидалась {want}')

# ── 3. wallSprite: левый край сходится с правым ───────────────────────────────
# Порог не выдуман: стык обязан выглядеть не хуже, чем любое другое место той же фактуры.
# Тёмный шов кладки на границе — это шов, а не дефект, поэтому сравниваем с перепадом ВНУТРИ тайла.
print('\n3. wallSprite и дорога замыкаются сами на себя')
def wrap(im, axis):
    p = im.convert('L').load()
    if axis == 'x':
        edge = max(abs(p[0, y] - p[S - 1, y]) for y in range(S))
        inner = max(abs(p[x, y] - p[x - 1, y]) for y in range(S) for x in range(1, S))
    else:
        edge = max(abs(p[x, 0] - p[x, S - 1]) for x in range(S))
        inner = max(abs(p[x, y] - p[x, y - 1]) for x in range(S) for y in range(1, S))
    return edge, inner

for nm, im, axes_ in (('кладка', wall_face(), 'x'),          # по высоте её тянут, а не повторяют
                      ('булыжник', road(), 'xy'),
                      ('земля', yard(), 'xy')):
    for a in axes_:
        e, i = wrap(im, a)
        check(f'{nm:9s} по {a.upper()}', e <= i * 1.1,
              f'на стыке {e}, внутри тайла бывает {i} (порог — не заметнее самого '
              f'контрастного места фактуры больше чем на 10%)')

# ── 4. силуэт и коньковая накладка ────────────────────────────────────────────
# Силуэт и конёк — разные вещи и меряются отдельно. Внутренние стыки силуэтом больше НЕ
# обводятся: конёк читается светлой накладкой, а верхний край ската — тонкой тёмной линией.
print('\n4. Силуэт и конёк')
p = roof({'N'}, set(), MATERIALS[0][2]).convert('L').load()
col = [p[S // 2, y] for y in range(S)]
outer = 0
while outer < S and col[outer] == EDGE:
    outer += 1
check('силуэт по внешнему краю', outer == OUTLINE, f'{outer} px')
clean = EDGE not in col[outer + 1:]
check('внутри тайла силуэта нет', clean, '' if clean else 'тёмная линия там, где должен быть конёк')

# конёк дома толщиной в клетку: два ската сходятся, накладка ложится на обе стороны стыка
want = 2 + 2 * ((CAP_W - 1) // 2)
reg, _ = regions({'N', 'S'}, set(), 'gable-ew')
ridge = next(y for y in range(1, S) if reg[y][S // 2] != reg[y - 1][S // 2])
p = roof({'N', 'S'}, set(), MATERIALS[0][2], 'gable-ew').convert('L').load()
plain = roof({'N', 'S'}, set(), MATERIALS[0][2], 'gable-ew', perspective=False).convert('L').load()
run = sum(1 for y in range(ridge - 6, ridge + 6) if p[S // 2, y] > plain[S // 2, y] + 6)
check('ширина коньковой накладки', run == want, f'{run} px, по формуле должно быть {want}')

# ── 5. дисциплина палитры ─────────────────────────────────────────────────────
# Главное отличие пиксель-арта от гладкого рендера: промежуточных яркостей НЕ БЫВАЕТ.
# Один случайно оставленный градиент — и весь набор перестаёт быть пиксель-артом.
print('\n5. Во всём наборе только девять яркостей палитры')
seen = set()
for key, title, fn in MATERIALS:
    for skey, stitle, dep in STYLES:
        for v, n in (({'N', 'S', 'E', 'W'}, set()), ({'N', 'W'}, set()), ({'N'}, set()),
                     (set(), {'NE'}), (set(), set())):
            seen |= {p[0] for p in roof(v, n, fn, skey).convert('RGB').getdata()}
for im in (wall_face(), road(), yard()):
    seen |= {p[0] for p in im.convert('RGB').getdata()}
check('лишних яркостей нет', seen <= set(RAMP), f'{len(seen)} тонов, вне палитры: {sorted(seen - set(RAMP))}')

print('\nИТОГ:', 'всё зелёное' if not fails else f'ПРОВАЛОВ {len(fails)}: {fails}')
sys.exit(1 if fails else 0)
