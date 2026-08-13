# -*- coding: utf-8 -*-
"""
Собирает живое превью гор из шаблона и выгруженных звеньев.

    cd Tools/mountain-harness
    dotnet build -c Debug
    ./bin/Debug/net8.0/mountainharness.exe json
    python build-lab.py

Даёт два файла из одного шаблона:
  * lab-artifact.html  — без обёртки документа, для публикации страницей;
  * ../../docs/Горы-превью.html — самостоятельный файл, открывается с диска.

Звенья считает стенд НАСТОЯЩИМ кодом слоя (Export.cs) и кладёт в links.json; страница повторяет на
JS только дешёвую половину конвейера — долю, ярусы и краски. Поэтому радиус горы, длину звена и
сглаживание на превью крутить нельзя: они меняют сами звенья, и для них нужен новый links.json.
"""
import io
import os

here = os.path.dirname(os.path.abspath(__file__))
template = io.open(os.path.join(here, 'lab-template.html'), encoding='utf-8').read()
data = io.open(os.path.join(here, 'links.json'), encoding='utf-8').read()

page = template.replace('{{DATA}}', data)
assert '{{DATA}}' not in page, 'заполнитель данных не подставился'

io.open(os.path.join(here, 'lab-artifact.html'), 'w', encoding='utf-8').write(page)

standalone = (
    '<!doctype html>\n<html lang="ru">\n<head>\n<meta charset="utf-8">\n'
    '<meta name="viewport" content="width=device-width, initial-scale=1">\n'
    + page.replace('<title>', '<title>', 1)
)
# <title> и <style> из шаблона уже стоят первыми — закрываем голову перед разметкой.
standalone = standalone.replace('</style>\n', '</style>\n</head>\n<body>\n', 1) + '\n</body>\n</html>\n'

docs = os.path.join(here, '..', '..', 'docs')
if not os.path.isdir(docs):
    os.makedirs(docs)
out = os.path.join(docs, 'Горы-превью.html')
io.open(out, 'w', encoding='utf-8').write(standalone)

print('страница, КБ:', round(len(page.encode('utf-8')) / 1024))
print('на диск:', os.path.normpath(out))
