# -*- coding: utf-8 -*-
# Пекём КРИСП битмап-атлас PixCyrillic (без анти-алиаса, порогом) + метрики для рантайм-Font в Unity.
import sys
for s in (sys.stdout, sys.stderr):
    if hasattr(s, "reconfigure"): s.reconfigure(encoding="utf-8", errors="backslashreplace")
from PIL import Image, ImageFont, ImageDraw

SC = r"C:\Users\user\AppData\Local\Temp\claude\C--Users-user\2a4bdbf6-80cd-46b4-b6f6-327dd24fa9f5\scratchpad"
TTF = SC + r"\PixCyrillic.ttf"
SIZE = 32           # размер рендера (тюним под игру)
THRESHOLD = 100     # порог альфы -> 1-битный крисп (0/255), убирает серую кайму AA
GAP = 1             # зазор между ячейками в атласе (анти-блид UV)
MAXW = 512

chars = (" !\"#$%&'()*+,-./0123456789:;<=>?@"
         "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`"
         "abcdefghijklmnopqrstuvwxyz{|}~"
         "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ"
         "абвгдеёжзийклмнопрстуфхцчшщъыьэюя"
         "«»—–…№°×")
chars = "".join(dict.fromkeys(chars))  # дедуп, порядок

font = ImageFont.truetype(TTF, SIZE)
ascent, descent = font.getmetrics()
lineH = ascent + descent

# строчная «а» в PixCyrillic — почти идеальный кружок (=«о»): «Программы»->«Прогроммы». Рисуем КАСТОМНУЮ
# single-story «а»: прямой правый стем + ножка вправо у baseline -> однозначно отлична от «о». (по ревью ТБ)
# Сетка 16x32, ascent=24 (baseline на y=24). Выбран кандидат A после рендер-сравнения в слове.
def build_a(adv, lineH):
    img = Image.new("L", (adv, lineH), 0)
    px = img.load()
    def row(r, cols):
        for c in cols:
            if 0 <= c < adv: px[c, r] = 255
    row(8, range(4, 12)); row(9, range(2, 14))
    for r in range(10, 19): row(r, list(range(2, 6)) + list(range(10, 14)))
    row(19, range(2, 14)); row(20, range(2, 14))
    row(21, list(range(2, 6)) + list(range(10, 14)))
    row(22, list(range(2, 6)) + list(range(10, 15)))
    row(23, range(4, 15))
    return img

# рендер каждого глифа в ячейку (ширина = advance), порог -> 1 бит
glyphs = []  # (code, adv, mask_image)
for ch in chars:
    adv = int(round(font.getlength(ch)))
    if adv < 1: adv = 1
    if ch == "а":                          # кастомная «а» (см. build_a), TTF-форму не используем
        img = build_a(adv, lineH)
    else:
        img = Image.new("L", (adv, lineH), 0)
        d = ImageDraw.Draw(img)
        d.text((0, 0), ch, fill=255, font=font, anchor="la")  # ascender у y=0, baseline на y=ascent
        img = img.point(lambda p: 255 if p >= THRESHOLD else 0)
    glyphs.append((ord(ch), adv, img))

# пакуем рядами
x = y = 0; atlasW = 0
placed = []  # (code, adv, x, y, w, h)
for code, adv, img in glyphs:
    if x + adv > MAXW:
        x = 0; y += lineH + GAP
    placed.append((code, adv, x, y, adv, lineH))
    x += adv + GAP
    atlasW = max(atlasW, x)
atlasH = y + lineH

atlas = Image.new("RGBA", (atlasW, atlasH), (255, 255, 255, 0))
for (code, adv, img), (_, _, px, py, _, _) in zip(glyphs, placed):
    rgba = Image.new("RGBA", img.size, (255, 255, 255, 0))
    rgba.putalpha(img)               # белый глиф, альфа = маска
    atlas.paste(rgba, (px, py))
atlas.save(SC + r"\pixcyr_atlas.png")

# метрики (компактный текст): META size ascent descent lineH atlasW atlasH n ; затем n строк: code adv x y w h
lines = ["META %d %d %d %d %d %d %d" % (SIZE, ascent, descent, lineH, atlasW, atlasH, len(placed))]
for code, adv, px, py, w, h in placed:
    lines.append("%d %d %d %d %d %d" % (code, adv, px, py, w, h))
open(SC + r"\pixcyr.meta", "w", encoding="utf-8", newline="\n").write("\n".join(lines))
print("atlas %dx%d, glyphs=%d, ascent=%d descent=%d lineH=%d" % (atlasW, atlasH, len(placed), ascent, descent, lineH))
