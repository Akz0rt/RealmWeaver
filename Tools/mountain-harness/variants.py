# -*- coding: utf-8 -*-
"""
Лист вариантов вида гор: подставляет наборы констант, пересобирает стенд, рисует и клеит
всё на один лист. Нужен затем, чтобы ДМ ВЫБИРАЛ ГЛАЗАМИ, а не по описанию.

Числа вида — постоянные в коде (ползунков у гор нет намеренно), поэтому «покрутить и
посмотреть» иначе не выходит: каждый вариант требует пересборки. Четыре варианта — около
полуминуты.

    python variants.py

Правится один список VARIANTS ниже: имя варианта и что в нём поменять. Имена констант
ищутся по «public [const] float ИМЯ = ...» в MountainInk.cs (числа туши) и
MountainSettings.cs (геометрия). Исходники восстанавливаются в конце в любом случае.

Требуется Chrome (SVG → PNG) и Pillow.
"""
import io, os, re, subprocess
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
OUT = os.path.join(ROOT, 'Tools', 'mountain-harness')
EXE = os.path.join(OUT, 'bin', 'Release', 'net8.0', 'mountainharness.exe')
HERE = os.path.join(os.environ.get('TEMP', '.'), 'mountain-variants')
os.makedirs(HERE, exist_ok=True)
CHROME = r'C:\Program Files\Google\Chrome\Application\chrome.exe'
INK = os.path.join(ROOT, 'Assets', 'WorldGen', 'Generation', 'Mountains', 'MountainInk.cs')
SET = os.path.join(ROOT, 'Assets', 'WorldGen', 'Generation', 'Mountains', 'MountainSettings.cs')

ZOOM = '1.3'

# imya -> {konstanta: znachenie}
VARIANTS = [
    ('I sharp0.66 h2.2', {'PenWidth': '0.20', 'Radius': '7'}),
    ('J sharp0.85 h2.2', {'PenWidth': '0.20', 'Radius': '7', 'Sharp': '0.85'}),
    ('K sharp0.66 h2.8', {'PenWidth': '0.20', 'Radius': '7', 'HeightFactor': '2.8'}),
    ('L sharp0.85 h2.8', {'PenWidth': '0.20', 'Radius': '7', 'Sharp': '0.85', 'HeightFactor': '2.8'}),
]

INK_KEYS = {'PenWidth', 'PenTaper', 'Grit', 'GritFall', 'ShadeThreshold'}


def patch(values):
    for path, keys in ((INK, INK_KEYS), (SET, None)):
        src = io.open(path, encoding='utf-8').read()
        out = src
        for k, v in values.items():
            if keys is not None and k not in keys:
                continue
            if keys is None and k in INK_KEYS:
                continue
            out = re.sub(r'(public (?:const )?float %s = )[0-9.]+f;' % k, r'\g<1>%sf;' % v, out)
        io.open(path, 'w', encoding='utf-8').write(out)
    return None


def snapshot():
    return io.open(INK, encoding='utf-8').read(), io.open(SET, encoding='utf-8').read()


def restore(state):
    io.open(INK, 'w', encoding='utf-8').write(state[0])
    io.open(SET, 'w', encoding='utf-8').write(state[1])


base = snapshot()
tiles = []
for name, values in VARIANTS:
    restore(base)
    patch(values)
    subprocess.run(['dotnet', 'build', '-c', 'Release', '-v', 'q', '--nologo'], cwd=OUT,
                   capture_output=True)
    subprocess.run([EXE, 'pen', ZOOM], cwd=OUT, capture_output=True)
    png = os.path.join(HERE, 'v_%d.png' % len(tiles))
    subprocess.run([CHROME, '--headless', '--disable-gpu', '--hide-scrollbars',
                    '--screenshot=' + png, '--window-size=760,760',
                    '--virtual-time-budget=4000',
                    'file:///D:/D&D/Tools/mountain-harness/pen.svg'], capture_output=True)
    tiles.append((name, png))

restore(base)
subprocess.run(['dotnet', 'build', '-c', 'Release', '-v', 'q', '--nologo'], cwd=OUT,
               capture_output=True)

CROP = (150, 120, 610, 500)
w, h = CROP[2] - CROP[0], CROP[3] - CROP[1]
sheet = Image.new('RGB', (w * 2 + 12, (h + 22) * 2 + 12), 'white')
d = ImageDraw.Draw(sheet)
for i, (name, png) in enumerate(tiles):
    im = Image.open(png).convert('RGB').crop(CROP)
    x = (i % 2) * (w + 12)
    y = (i // 2) * (h + 22 + 12)
    d.text((x + 4, y + 4), name, fill='black')
    sheet.paste(im, (x, y + 20))
sheet.save(os.path.join(HERE, 'variants.png'))
print('gotovo')
