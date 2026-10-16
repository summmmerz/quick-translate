# -*- coding: utf-8 -*-
"""生成 GitHub 仓库的社交预览图（Settings → Social preview）。

    python tools/make_social.py

输出 assets/social-preview.png，**1280×640**——这是 GitHub 推荐的尺寸比例
（官方建议 1280×640，最大 1 MB，PNG/JPG/GIF）。直接把方形 icon.png 传上去会被裁得很难看。

风格与 assets/icon.ico 一致：深色底 + 曼城蓝 (#6CABDD) + 四角角标。
"""
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(HERE, '..', 'assets'))
FONTS = r'C:\Windows\Fonts'
YAHEI_B = os.path.join(FONTS, 'msyhbd.ttc')
YAHEI = os.path.join(FONTS, 'msyh.ttc')
SEGOE = os.path.join(FONTS, 'segoeui.ttf')

W, H = 1280, 640
BG_TOP, BG_BOT = (21, 27, 34), (10, 13, 18)
ACCENT = (108, 171, 221)
WHITE = (238, 243, 248)
MUTED = (135, 148, 162)
FAINT = (86, 97, 110)
ICON = 208
ICON_X, ICON_Y = 96, (H - ICON) // 2


def font(path, size):
    return ImageFont.truetype(path, size)


def draw_text(d, xy, text, fnt, fill):
    d.text(xy, text, font=fnt, fill=fill)


def corner_marks(d, m=34, ln=52, w=3):
    """四角角标：只画 L 形，不画整框。"""
    for cx, cy, dx, dy in ((m, m, 1, 1), (W - m, m, -1, 1),
                           (m, H - m, 1, -1), (W - m, H - m, -1, -1)):
        d.line([(cx, cy), (cx + dx * ln, cy)], fill=ACCENT + (150,), width=w)
        d.line([(cx, cy), (cx, cy + dy * ln)], fill=ACCENT + (150,), width=w)


def main():
    img = Image.new('RGB', (W, H), BG_BOT)
    grad = Image.new('RGB', (1, H))
    gd = ImageDraw.Draw(grad)
    for y in range(H):
        t = y / (H - 1)
        gd.point((0, y), fill=tuple(int(BG_TOP[i] + (BG_BOT[i] - BG_TOP[i]) * t) for i in range(3)))
    img.paste(grad.resize((W, H)))
    d = ImageDraw.Draw(img, 'RGBA')

    # 右下角的斜纹（技术图形层，压得很淡）
    for x in range(W - 330, W + 320, 26):
        d.line([(x, H), (x + 300, H - 300)], fill=ACCENT + (14,), width=9)

    corner_marks(d)

    # 图标
    icon = Image.open(os.path.join(ASSETS, 'icon.png')).convert('RGBA').resize((ICON, ICON), Image.LANCZOS)
    img.paste(icon, (ICON_X, ICON_Y), icon)

    # 竖分隔条
    bar_x = ICON_X + ICON + 66
    d.rounded_rectangle([bar_x, ICON_Y + 6, bar_x + 7, ICON_Y + ICON - 6], radius=4, fill=ACCENT + (255,))

    tx = bar_x + 48
    draw_text(d, (tx, ICON_Y + 12), '轻译', font(YAHEI_B, 104), WHITE)
    draw_text(d, (tx + 8, ICON_Y + 134), 'QuickTranslate', font(SEGOE, 46), ACCENT)
    draw_text(d, (tx + 8, ICON_Y + 196), 'Windows 划词翻译 · ECDICT 离线词典', font(YAHEI, 30), MUTED)
    draw_text(d, (tx + 8, ICON_Y + 242), 'C# + WPF · 零 NuGet 依赖', font(YAHEI, 30), MUTED)

    d.line([(tx, H - 92), (tx + 560, H - 92)], fill=FAINT + (120,), width=2)
    draw_text(d, (tx, H - 74), 'github.com/summmmerz/quick-translate', font(SEGOE, 26), FAINT)

    out = os.path.join(ASSETS, 'social-preview.png')
    img.save(out)
    print('saved :', out)
    print('size  : %dx%d, %d bytes' % (W, H, os.path.getsize(out)))


if __name__ == '__main__':
    main()
