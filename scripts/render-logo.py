#!/usr/bin/env python3
"""Render the Pegged app icons.

Draws a rounded Fluent tile with a sagging clothesline and a wooden
clothespin hanging from it. Produces:

  assets/logo.png    256x256 (tray icon, README, showcase)
  assets/logo.svg    the vector source of the same mark (hand-authored)
  assets/app.ico     multi-size Windows icon (16/32/48/256)

Run with the project venv:
  python scripts/render-logo.py
"""
import math
import os

from PIL import Image, ImageDraw

S = 1024          # supersample canvas
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets")


def lerp(a, b, t):
    return a + (b - a) * t


def mix(c1, c2, t):
    return tuple(int(round(lerp(c1[i], c2[i], t))) for i in range(4))


def vgrad(size, top, bottom):
    """Vertical linear gradient image."""
    img = Image.new("RGBA", (1, size))
    d = ImageDraw.Draw(img)
    for y in range(size):
        d.point((0, y), fill=mix(top, bottom, y / max(1, size - 1)))
    return img.resize((size, size))


def diagonal_tile(size, c1, c2):
    """A diagonal gradient used for the tile background."""
    img = Image.new("RGBA", (size, size))
    px = img.load()
    for y in range(size):
        for x in range(size):
            t = (x / size * 0.55 + y / size * 0.45)
            px[x, y] = mix(c1, c2, t)
    return img


def rounded_mask(size, radius):
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return m


def wood_gradient(w, h, left, mid, right):
    img = Image.new("RGBA", (w, h))
    px = img.load()
    for x in range(w):
        t = x / max(1, w - 1)
        c = mix(left, mid, t / 0.55) if t < 0.55 else mix(mid, right, (t - 0.55) / 0.45)
        for y in range(h):
            px[x, y] = c
    return img


def bezier(p0, p1, p2, n=160):
    pts = []
    for i in range(n + 1):
        t = i / n
        mt = 1 - t
        x = mt * mt * p0[0] + 2 * mt * t * p1[0] + t * t * p2[0]
        y = mt * mt * p0[1] + 2 * mt * t * p1[1] + t * t * p2[1]
        pts.append((x, y))
    return pts


def draw_rope(layer, cx):
    """A thin rope sagging from edge to edge of the tile."""
    d = ImageDraw.Draw(layer)
    x0, x1 = -int(0.06 * S), int(1.06 * S)
    y = 0.335 * S
    sag = 0.052 * S
    p0, p1, p2 = (x0, y), ((x0 + x1) / 2, y + 2 * sag), (x1, y)
    pts = bezier(p0, p1, p2)
    core = [(120, 126, 138, 255), (74, 82, 96, 255), (222, 228, 238, 255)]
    for i, pts_, color, wdt in ((0, pts, core[0], int(0.030 * S)),
                                (1, [(x, yy - 0.006 * S) for x, yy in pts], core[2], int(0.009 * S))):
        d.line(pts_, fill=color, width=wdt, joint="curve")
    # shadow of the rope onto the tile
    sh = [(x, yy + 0.012 * S) for x, yy in pts]
    d.line(sh, fill=(20, 30, 60, 70), width=int(0.034 * S), joint="curve")


def rope_y_at(x):
    x0, x1 = 0.0, 1.0
    y = 0.335
    sag = 0.052
    t = (x - x0) / (x1 - x0)
    mt = 1 - t
    return (mt * mt * y + 2 * mt * t * (y + 2 * sag) + t * t * y) * S


def draw_clothespin(layer, cx):
    """A wooden clothespin hanging from the rope, seen from the front."""
    pin_w = 0.145 * S
    pin_h = 0.300 * S
    top_y = rope_y_at(0.5) - 0.004 * S
    left, right = cx - pin_w / 2, cx + pin_w / 2
    bottom = top_y + pin_h

    wood = wood_gradient(int(pin_w), int(pin_h),
                         (238, 196, 152, 255), (214, 158, 100, 255), (150, 96, 52, 255))
    mask = Image.new("L", wood.size, 0)
    md = ImageDraw.Draw(mask)
    r = int(0.045 * S)
    md.rounded_rectangle([0, 0, pin_w - 1, pin_h - 1], radius=r, fill=255)
    # the pin tapers slightly towards the bottom
    md.polygon([(0, 0), (pin_w, 0), (right - 0.012 * S, pin_h), (left + 0.012 * S, pin_h)], fill=255)
    layer.paste(wood, (int(left), int(top_y)), mask)
    d = ImageDraw.Draw(layer)
    # slot where the rope passes
    sw, sh_ = 0.052 * S, 0.014 * S
    d.rounded_rectangle([cx - sw / 2, top_y + 0.055 * S, cx + sw / 2, top_y + 0.055 * S + sh_],
                        radius=sh_ / 2, fill=(52, 38, 24, 210))
    # the split between the two wooden halves
    d.line([(cx, top_y + 0.075 * S), (cx, bottom - 0.030 * S)],
           fill=(96, 60, 30, 190), width=max(2, int(0.006 * S)))
    # wooden highlight on the left edge
    d.line([(left + 0.014 * S, top_y + 0.05 * S), (left + 0.014 * S, bottom - 0.028 * S)],
           fill=(255, 240, 220, 150), width=max(2, int(0.008 * S)))
    # the metal spring band gripping the middle
    bw, bh = 0.052 * S, 0.052 * S
    band = vgrad(int(bw), (226, 232, 240, 255), (148, 158, 172, 255)).rotate(90, expand=True)
    band = band.resize((int(bw), int(bh)))
    layer.paste(band, (int(cx - bw / 2), int(top_y + 0.135 * S)))
    d.rounded_rectangle([cx - bw / 2, top_y + 0.135 * S, cx + bw / 2, top_y + 0.135 * S + bh],
                        radius=int(0.010 * S), outline=(92, 100, 116, 235),
                        width=max(2, int(0.006 * S)))
    # contact shadow under the pin
    d.ellipse([cx - 0.075 * S, bottom - 0.010 * S, cx + 0.075 * S, bottom + 0.022 * S],
              fill=(20, 30, 60, 80))


def make_icon():
    tile = diagonal_tile(S, (61, 141, 255, 255), (140, 96, 255, 255))
    # soft light from the top-left
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for i in range(int(0.5 * S), 0, -1):
        a = int(40 * (1 - i / (0.5 * S)))
        gd.ellipse([-0.28 * S - i, -0.30 * S - i, 0.22 * S + i, 0.24 * S + i],
                   fill=(255, 255, 255, a))
    tile = Image.alpha_composite(tile, glow)

    art = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    draw_rope(art, S)
    draw_clothespin(art, 0.5 * S)

    icon = art
    # clip to the rounded tile
    icon = Image.composite(icon, Image.new("RGBA", (S, S), (0, 0, 0, 0)),
                           rounded_mask(S, int(0.115 * S)))
    # tile under the art
    final = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    final.paste(tile, (0, 0), rounded_mask(S, int(0.115 * S)))
    final = Image.alpha_composite(final, icon)

    # outer ring stroke, the way Fluent tiles read
    ring = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(ring).rounded_rectangle(
        [2, 2, S - 3, S - 3], radius=int(0.115 * S),
        outline=(255, 255, 255, 60), width=int(0.006 * S))
    final = Image.alpha_composite(final, ring)
    return final


def main():
    os.makedirs(OUT, exist_ok=True)
    icon = make_icon()
    logo = icon.resize((256, 256), Image.LANCZOS)
    logo.save(os.path.join(OUT, "logo.png"))
    logo.save(os.path.join(OUT, "app.ico"),
              sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
    print("wrote", os.path.join(OUT, "logo.png"))
    print("wrote", os.path.join(OUT, "app.ico"))


if __name__ == "__main__":
    main()
