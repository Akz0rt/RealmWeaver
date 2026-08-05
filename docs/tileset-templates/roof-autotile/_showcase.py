# -*- coding: utf-8 -*-
"""One building that uses every one of the 47 tiles.

Found by simulated annealing over a grid, scored on tiles covered, then on
fewest cells and shortest perimeter so the result stays one compact building
instead of scattered huts. The pyramid tile is the roof of a 1x1 house, so it
cannot appear in a connected shape — hence the single detached shed.
"""
import os
from PIL import Image
from _rules import render, ordered, name, S, CORNERS

PLAN = [
    "....#....#...",
    "...###..###..",
    ".###.#######.",
    "###########..",
    ".#.#.###.###.",
    "..###########",
    "...###.##.#..",
    "..##.#..####.",
    "...######.#..",
    "....##.#...#.",
]

GW, GH = len(PLAN[0]), len(PLAN)
ORDER = [name(v, n) for _, _, g in ordered() for v, n in g]


def at(x, y):
    return 0 <= x < GW and 0 <= y < GH and PLAN[y][x] == '#'


def config(x, y):
    v = {s for s, (dx, dy) in {'N': (0, -1), 'S': (0, 1), 'W': (-1, 0), 'E': (1, 0)}.items()
         if not at(x + dx, y + dy)}
    n = set()
    for k, (a, b) in CORNERS.items():
        if a in v or b in v:
            continue
        if not at(x + (1 if 'E' in k else -1), y + (1 if 'S' in k else -1)):
            n.add(k)
    return v, n


def build():
    """Return (image, {tile index: (cell x, cell y)}) — one label per tile."""
    im = Image.new('RGBA', (GW * S, GH * S), (0, 0, 0, 0))
    first = {}
    for y in range(GH):
        for x in range(GW):
            if not at(x, y):
                continue
            v, n = config(x, y)
            im.alpha_composite(render(v, n), (x * S, y * S))
            first.setdefault(ORDER.index(name(v, n)), (x, y))
    return im, first


if __name__ == '__main__':
    here = os.path.dirname(os.path.abspath(__file__))
    im, first = build()
    missing = [i for i in range(47) if i not in first]
    cells = sum(r.count('#') for r in PLAN)
    assert not missing, f'не хватает тайлов: {missing}'
    bg = Image.new('RGBA', im.size, (238, 236, 232, 255))
    bg.alpha_composite(im)
    bg.convert('RGB').save(os.path.join(here, 'roof-showcase.png'))
    print(f'roof-showcase.png · {cells} клеток · задействовано {len(first)}/47 тайлов')
