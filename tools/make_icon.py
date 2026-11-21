"""Génère l'icône de PCDoc (écran + tracé de pouls) : python3 tools/make_icon.py src/PCDoc.App/Assets/pcdoc.ico"""
import sys
from PIL import Image, ImageDraw

def frame(size):
    s = size / 64
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([4 * s, 8 * s, 60 * s, 46 * s], radius=7 * s, fill=(0, 95, 184, 255))
    d.rounded_rectangle([22 * s, 50 * s, 42 * s, 56 * s], radius=3 * s, fill=(93, 93, 93, 255))
    pts = [(12, 28), (22, 28), (27, 18), (34, 38), (39, 28), (52, 28)]
    d.line([(x * s, y * s) for x, y in pts], fill=(255, 255, 255, 255), width=max(1, round(4 * s)), joint="curve")
    return img

sizes = [16, 24, 32, 48, 64, 128, 256]
frame(256).save(sys.argv[1], sizes=[(n, n) for n in sizes])
print("ok")
