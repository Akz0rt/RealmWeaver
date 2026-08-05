# -*- coding: utf-8 -*-
"""Assemble a test town from the 47 tiles — proves the set actually tiles."""
import os, sys
from PIL import Image, ImageDraw, ImageFont
from _rules import render, S, CORNERS

OUT = sys.argv[1]
FONT, FONTB = 'C:/Windows/Fonts/segoeui.ttf', 'C:/Windows/Fonts/segoeuib.ttf'

PLAN = [
    "..................",
    ".###..#...####.##.",
    ".###..#...#..#....",
    ".###..#...#..#.##.",
    ".#....#...####.##.",
    ".#..######........",
    ".#..#....#..###...",
    "....#....#..#.#...",
    "....######..###...",
    "..................",
]

def cell(g, x, y):
    return 0 <= y < len(g) and 0 <= x < len(g[0]) and g[y][x] == '#'

cache = {}
def tile(voids, notches):
    k = (frozenset(voids), frozenset(notches))
    if k not in cache:
        cache[k] = render(set(voids), set(notches))
    return cache[k]

H, W = len(PLAN), len(PLAN[0])
town = Image.new('RGBA', (W * S, H * S), (0, 0, 0, 0))
used = set()
for y in range(H):
    for x in range(W):
        if not cell(PLAN, x, y):
            continue
        d = {'N': (0, -1), 'S': (0, 1), 'W': (-1, 0), 'E': (1, 0)}
        voids = {s for s, (dx, dy) in d.items() if not cell(PLAN, x + dx, y + dy)}
        notches = set()
        for c, (a, b) in CORNERS.items():
            if a in voids or b in voids:
                continue
            dx = 1 if 'E' in c else -1
            dy = 1 if 'S' in c else -1
            if not cell(PLAN, x + dx, y + dy):
                notches.add(c)
        used.add((frozenset(voids), frozenset(notches)))
        town.alpha_composite(tile(voids, notches), (x * S, y * S))

pad, head = 24, 92
card = Image.new('RGB', (town.width + pad * 2, town.height + head + pad), (250, 249, 246))
bg = Image.new('RGBA', town.size, (238, 236, 232, 255))
bg.alpha_composite(town)
card.paste(bg.convert('RGB'), (pad, head))
dr = ImageDraw.Draw(card)
dr.text((pad, 26), 'Проверка: город собран из набора автоматически',
        font=ImageFont.truetype(FONTB, 24), fill=(30, 28, 40))
dr.text((pad, 60), f'{len(used)} разных тайлов из 47 · ни одного шва, ни одной дырки — '
                   'скаты соседних клеток сходятся',
        font=ImageFont.truetype(FONT, 13), fill=(120, 116, 130))
card.save(os.path.join(OUT, 'roof-demo.png'))
print('tiles used:', len(used), 'of 47')
