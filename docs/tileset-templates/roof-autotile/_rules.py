"""Generate the canonical 47-tile roof autotile set from the rules
reverse-engineered from TileSetScheme.aseprite."""
import itertools, os, sys
from PIL import Image

S = 64
EDGE  = (17, 14, 43, 255)
DARK  = (34, 32, 52, 255)    # slopes facing S or E
LIGHT = (56, 53, 87, 255)    # slopes facing N or W
FLAT  = (132, 126, 135, 255)
SH2   = (60, 51, 65, 255)    # contact shadow, near
SH1   = (83, 75, 87, 255)    # contact shadow, far
PLATEAU = S / 2          # the slope reaches this far in before the flat top starts

SIDES = ('N', 'E', 'S', 'W')
CORNERS = {'NE': ('N', 'E'), 'SE': ('S', 'E'), 'SW': ('S', 'W'), 'NW': ('N', 'W')}
TONE = {'N': LIGHT, 'W': LIGHT, 'S': DARK, 'E': DARK}


def regions(voids, notches):
    """Per-pixel region map: 'P' for the flat top, else the direction the slope faces."""
    reg = [[None] * S for _ in range(S)]
    for y in range(S):
        for x in range(S):
            d = {'N': y + 0.5, 'S': S - 0.5 - y, 'W': x + 0.5, 'E': S - 0.5 - x}
            best_h, best_f = PLATEAU, 'P'
            for v in SIDES:                     # convex: roof descends to the void edge
                if v in voids and d[v] < best_h: # fixed order: a 45 deg tie must break the same way
                    best_h, best_f = d[v], v     # every run, or the art is not reproducible
            for c in ('NE', 'SE', 'SW', 'NW'):   # concave: the two facets ridge up to the corner
                if c not in notches:
                    continue
                a, b = CORNERS[c]
                h, f = (d[a], a) if d[a] >= d[b] else (d[b], b)
                if h < best_h:
                    best_h, best_f = h, f
            reg[y][x] = best_f
    return reg


def render(voids, notches):
    reg = regions(voids, notches)
    img = Image.new('RGBA', (S, S))
    px = img.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = FLAT if reg[y][x] == 'P' else TONE[reg[y][x]]

    # contact shadow: a N-facing slope drops it southward, a W-facing one eastward
    for y in range(S):
        for x in range(S):
            if reg[y][x] != 'P':
                continue
            d = 99
            for k in range(1, 5):
                if y - k >= 0 and reg[y - k][x] != 'P':
                    if reg[y - k][x] == 'N':
                        d = min(d, k)
                    break
            for k in range(1, 5):
                if x - k >= 0 and reg[y][x - k] != 'P':
                    if reg[y][x - k] == 'W':
                        d = min(d, k)
                    break
            if 2 <= d <= 3:
                px[x, y] = SH2
            elif d == 4:
                px[x, y] = SH1

    # outline: 2 px on every seam between regions
    seam = set()
    for y in range(S):
        for x in range(S):
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < S and 0 <= ny < S and reg[ny][nx] != reg[y][x]:
                    seam.add((x, y))
                    break
    # outline: 2 px along every edge that faces the outside
    for v in voids:
        for i in range(S):
            for k in (0, 1):
                seam.add((i, k) if v == 'N' else (i, S - 1 - k) if v == 'S'
                         else (k, i) if v == 'W' else (S - 1 - k, i))
    for x, y in seam:
        px[x, y] = EDGE
    return img


def configs():
    """The 47 distinct neighbourhoods. A corner only counts when both its sides are filled."""
    out = []
    for k in range(5):
        for voids in itertools.combinations(SIDES, k):
            free = [c for c, (a, b) in CORNERS.items() if a not in voids and b not in voids]
            for r in range(len(free) + 1):
                for notch in itertools.combinations(free, r):
                    out.append((frozenset(voids), frozenset(notch)))
    return out


FAMILY = [
    ('plateau',  'Плато: соседи со всех сторон',            lambda v: len(v) == 0),
    ('side',     'Одна сторона наружу',                      lambda v: len(v) == 1),
    ('corner',   'Внешний угол: две смежные стороны наружу', lambda v: len(v) == 2 and not opposite(v)),
    ('ridge',    'Гребень: дом толщиной в одну клетку',      lambda v: len(v) == 2 and opposite(v)),
    ('cap',      'Торец: конец полосы в одну клетку',        lambda v: len(v) == 3),
    ('pyramid',  'Пирамида: дом 1x1',                        lambda v: len(v) == 4),
]


def opposite(v):
    return v == {'N', 'S'} or v == {'E', 'W'}


def ordered():
    cfgs = configs()
    out = []
    for key, title, test in FAMILY:
        group = [c for c in cfgs if test(c[0])]
        group.sort(key=lambda c: (len(c[1]),
                                  tuple(SIDES.index(s) for s in sorted(c[0], key=SIDES.index)),
                                  tuple(sorted(c[1]))))
        out.append((key, title, group))
    return out


def name(voids, notches):
    v = ''.join(s for s in SIDES if s in voids) or '-'
    n = '+'.join(c for c in ('NE', 'SE', 'SW', 'NW') if c in notches)
    return v + ('/' + n if n else '')


if __name__ == '__main__':
    total = sum(len(g) for _, _, g in ordered())
    print('configs:', total)
    for key, title, group in ordered():
        print(f'  {key:8s} {len(group):3d}  {title}')
