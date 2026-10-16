# -*- coding: utf-8 -*-
"""生成 QuickTranslate 的应用图标。

    python tools/make_icon.py

输出到 <repo>/assets/：
    icon.ico  多尺寸（16/24/32/48/64/128/256），供 csproj 的 ApplicationIcon 使用
    icon.png  256×256，供 README / 仓库社交预览使用

设计：曼城蓝 (#6CABDD) 圆角方块 + 深色「译」字，与界面强调色一致（Theme.xaml 的 Accent）。
"""
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, '..', 'assets'))
FONT = r'C:\Windows\Fonts\msyhbd.ttc'          # Microsoft YaHei Bold
S = 1024                                        # 先按 1024 绘制再降采样，保证边缘平滑
RADIUS = int(S * 0.22)                          # 圆角比例（现代应用图标观感）
TOP = (122, 186, 232)                           # 曼城蓝偏亮
BOT = (74, 142, 199)                            # 曼城蓝偏深，做出纵向渐变
INK = (10, 20, 30)                              # 字色：近黑的深藏青
SIZES = [16, 24, 32, 48, 64, 128, 256]


def render():
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))

    grad = Image.new('RGB', (1, S))
    d = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        d.point((0, y), fill=tuple(int(TOP[i] + (BOT[i] - TOP[i]) * t) for i in range(3)))
    grad = grad.resize((S, S))

    mask = Image.new('L', (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=RADIUS, fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    fnt = ImageFont.truetype(FONT, 640)
    box = d.textbbox((0, 0), '译', font=fnt)
    d.text((S / 2 - (box[0] + box[2]) / 2, S / 2 - (box[1] + box[3]) / 2),
           '译', font=fnt, fill=INK + (255,))
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    big = render()

    ico = os.path.join(OUT, 'icon.ico')
    big.save(ico, format='ICO', sizes=[(s, s) for s in SIZES])
    png = os.path.join(OUT, 'icon.png')
    big.resize((256, 256), Image.LANCZOS).save(png)

    # 自检：确认 ico 里各尺寸都在、且非纯透明
    with Image.open(ico) as im:
        got = sorted({s[0] for s in im.ico.sizes()})
    print('ico sizes :', got)
    print('ico bytes :', os.path.getsize(ico))
    print('png bytes :', os.path.getsize(png))
    assert got == SIZES, 'sizes mismatch: %s' % got

    strip = Image.new('RGBA', (sum(SIZES) + 16 * len(SIZES), 256), (30, 34, 40, 255))
    x = 8
    for s in SIZES:
        t = big.resize((s, s), Image.LANCZOS)
        strip.paste(t, (x, 256 - s - 8), t)
        x += s + 16
    strip.save(os.path.join(OUT, 'preview-sizes.png'))
    print('preview   : assets/preview-sizes.png')


if __name__ == '__main__':
    main()
