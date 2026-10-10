"""Generate the MSIX logo assets (same look as the tray icon). Run: python packaging/make_assets.py"""
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Assets")
os.makedirs(OUT, exist_ok=True)

BACK = (0x20, 0x21, 0x24, 255)
BLUE = (0x4F, 0x8E, 0xF7, 255)


def draw(size: int) -> Image.Image:
    scale = 8  # draw large, then shrink for smooth edges
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = s * 0.04
    d.rounded_rectangle([pad, pad, s - pad, s - pad], radius=s * 0.22, fill=BACK)
    w = max(int(s * 0.09), 2)
    a, b = s * 0.26, s * 0.42   # corner arm start / length
    lo, hi = s * 0.26, s * 0.74
    arm = s * 0.18
    # top-left bracket
    d.line([(lo, lo + arm), (lo, lo), (lo + arm, lo)], fill=BLUE, width=w, joint="curve")
    # bottom-right bracket
    d.line([(hi, hi - arm), (hi, hi), (hi - arm, hi)], fill=BLUE, width=w, joint="curve")
    return img.resize((size, size), Image.LANCZOS)


for name, size in (("StoreLogo.png", 50), ("Square44x44Logo.png", 44), ("Square150x150Logo.png", 150)):
    draw(size).save(os.path.join(OUT, name))
    print("wrote", name, size)
