"""Draws the GIF Wallpaper icon and exports every size the Android and Windows apps need.
Run from the repo root:  python3 art/make_icons.py
"""
import os
from PIL import Image, ImageDraw

TOP = (123, 92, 255)      # violet
BOTTOM = (255, 92, 158)   # pink
SS = 4                    # supersampling for smooth edges
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def gradient(size):
    """Diagonal violet -> pink gradient."""
    small = Image.new("RGB", (256, 256))
    px = small.load()
    for y in range(256):
        for x in range(256):
            t = (x + y) / 510
            px[x, y] = tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM))
    return small.resize((size, size), Image.BICUBIC)


def glyph(size):
    """The white frame + landscape + play badge, transparent background, filling `size`."""
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    u = S / 100  # work in a 100x100 grid
    white = (255, 255, 255, 255)

    # Picture frame
    fx0, fy0, fx1, fy1 = 6 * u, 16 * u, 86 * u, 76 * u
    d.rounded_rectangle((fx0, fy0, fx1, fy1), radius=11 * u, outline=white, width=round(7 * u))

    # Landscape inside the frame (clipped to the frame's inner area)
    inner = Image.new("L", (S, S), 0)
    ImageDraw.Draw(inner).rounded_rectangle(
        (fx0 + 7 * u, fy0 + 7 * u, fx1 - 7 * u, fy1 - 7 * u), radius=5 * u, fill=255)
    scene = Image.new("L", (S, S), 0)
    sd = ImageDraw.Draw(scene)
    sd.ellipse((21 * u, 28 * u, 33 * u, 40 * u), fill=255)                       # sun
    sd.polygon([(10 * u, 72 * u), (36 * u, 44 * u), (56 * u, 72 * u)], fill=255)  # left peak
    sd.polygon([(34 * u, 72 * u), (60 * u, 38 * u), (86 * u, 72 * u)], fill=255)  # right peak
    scene = Image.composite(scene, Image.new("L", (S, S), 0), inner)
    img.paste(white, mask=scene)

    # Play badge overlapping the bottom-right corner
    cx, cy, r = 78 * u, 74 * u, 19 * u
    ring = 4.5 * u
    # transparent gap around the badge so it reads as separate from the frame
    gap = Image.new("L", (S, S), 0)
    ImageDraw.Draw(gap).ellipse((cx - r - ring, cy - r - ring, cx + r + ring, cy + r + ring), fill=255)
    img.putalpha(Image.composite(Image.new("L", (S, S), 0), img.getchannel("A"), gap))
    d = ImageDraw.Draw(img)
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=white)
    t = 9 * u
    tri = [(cx - t * 0.7, cy - t), (cx - t * 0.7, cy + t), (cx + t * 1.05, cy)]
    tri_mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(tri_mask).polygon(tri, fill=255)
    img.paste(Image.alpha_composite(
        Image.new("RGBA", (S, S), (0, 0, 0, 0)),
        gradient(S).convert("RGBA")), mask=tri_mask)

    return img.resize((size, size), Image.LANCZOS)


def full_icon(size, corner=0.22):
    """Rounded-square app icon (Windows, wallpaper thumbnail, README)."""
    S = size * SS
    bg = gradient(S).convert("RGBA")
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=corner * S, fill=255)
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(bg, mask=mask)
    g = glyph(round(size * 0.74)).resize((round(S * 0.74),) * 2, Image.LANCZOS)
    off = (S - g.width) // 2
    out.alpha_composite(g, (off, off + round(S * 0.02)))
    return out.resize((size, size), Image.LANCZOS)


def adaptive_foreground(size):
    """Android adaptive icon foreground: glyph inside the 66/108 safe zone."""
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    g = glyph(round(size * 0.52))
    off = (size - g.width) // 2
    out.alpha_composite(g, (off, off + round(size * 0.01)))
    return out


def main():
    res = os.path.join(ROOT, "app", "src", "main", "res")
    for density, scale in {"mdpi": 1, "hdpi": 1.5, "xhdpi": 2, "xxhdpi": 3, "xxxhdpi": 4}.items():
        folder = os.path.join(res, f"mipmap-{density}")
        os.makedirs(folder, exist_ok=True)
        px = round(108 * scale)
        adaptive_foreground(px).save(os.path.join(folder, "ic_launcher_foreground.png"))
        gradient(px).save(os.path.join(folder, "ic_launcher_background.png"))

    drawable = os.path.join(res, "drawable-nodpi")
    os.makedirs(drawable, exist_ok=True)
    full_icon(512).save(os.path.join(drawable, "wallpaper_thumb.png"))

    win = os.path.join(ROOT, "windows", "GifWallpaper")
    big = full_icon(256)
    big.save(os.path.join(win, "app.ico"),
             sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])

    full_icon(512).save(os.path.join(ROOT, "art", "icon.png"))
    print("done")


if __name__ == "__main__":
    main()
