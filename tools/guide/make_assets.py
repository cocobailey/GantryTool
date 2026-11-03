"""Bakes guide sign fonts and shields into the small binary files the mod embeds (it has no font or SVG engine).

Fonts: tools/guide/fonts/*.ttf -> Resources/Guide/us/<name>.gbf
Shields: tools/guide/*.svg -> Resources/Guide/us/<name>.gim

Run from the repository root: python tools/guide/make_assets.py (needs Pillow).

.gbf (little endian): "GBF1", int emPx, int glyphCount, then per glyph ushort char, short width, short height,
short left, short top (above the baseline), float advance (px), int dataLength; int kernCount, then per pair
ushort a, ushort b, float kern (px); then every glyph's data in order. Glyph data is run-length coded by row:
a byte = high nibble coverage level (0-15), low nibble run length - 1.

.gim: "GIM1", int width, int height, then runs of (byte length - 1, byte r, g, b, a), top row first.
"""
import glob
import os
import re
import struct

from PIL import Image, ImageChops, ImageDraw, ImageFont

OUT = os.path.join("Resources", "Guide", "us")
EM = 112  # px per em: signs bake at about this size, so letters are rarely scaled up
CHARS = [chr(c) for c in range(32, 127)]


def glyph_runs(mask, w, h):
    px = mask.load()
    out = bytearray()
    for y in range(h):
        x = 0
        while x < w:
            level = px[x, y] >> 4
            run = 1
            while x + run < w and run < 16 and (px[x + run, y] >> 4) == level:
                run += 1
            out.append((level << 4) | (run - 1))
            x += run
    return bytes(out)


def bake_font(path):
    font = ImageFont.truetype(path, EM)
    glyphs, data = [], []
    for ch in CHARS:
        l, t, r, b = font.getbbox(ch, anchor="ls")
        w, h = max(0, r - l), max(0, b - t)
        runs = b""
        if w > 0 and h > 0 and ch != " ":
            img = Image.new("L", (w, h), 0)
            ImageDraw.Draw(img).text((-l, -t), ch, font=font, fill=255, anchor="ls")
            runs = glyph_runs(img, w, h)
        else:
            w = h = 0
        glyphs.append((ord(ch), w, h, l, -t, font.getlength(ch), len(runs)))
        data.append(runs)

    kerns = []
    letters = [c for c in CHARS if c != " "]
    single = {c: font.getlength(c) for c in CHARS}
    for a in letters:
        for b in letters:
            k = font.getlength(a + b) - single[a] - single[b]
            if abs(k) >= 0.5:
                kerns.append((ord(a), ord(b), k))

    name = os.path.splitext(os.path.basename(path))[0]
    with open(os.path.join(OUT, name + ".gbf"), "wb") as f:
        f.write(b"GBF1" + struct.pack("<ii", EM, len(glyphs)))
        for g in glyphs:
            f.write(struct.pack("<HhhhhfI", *g))
        f.write(struct.pack("<i", len(kerns)))
        for k in kerns:
            f.write(struct.pack("<HHf", *k))
        for d in data:
            f.write(d)
    print(name, sum(len(d) for d in data), "glyph bytes,", len(kerns), "kern pairs")


# ---------------------------------------------------------------- SVG (only the path features the shields use)
TOKEN = re.compile(r"[MmLlHhVvCcZz]|-?\d*\.?\d+(?:[eE][-+]?\d+)?")


def flatten(d):
    """A path's subpaths as point lists (curves flattened)."""
    toks = TOKEN.findall(d)
    subs, cur = [], []
    i, x, y, sx, sy, cmd = 0, 0.0, 0.0, 0.0, 0.0, None

    def num():
        nonlocal i
        v = float(toks[i])
        i += 1
        return v

    while i < len(toks):
        if re.match(r"[A-Za-z]", toks[i]):
            cmd = toks[i]
            i += 1
            if cmd in "Zz":
                x, y = sx, sy
                cur.append((x, y))
                subs.append(cur)
                cur = []
                continue
        rel = cmd.islower()
        c = cmd.upper()
        if c == "M":
            nx, ny = num(), num()
            x, y = (x + nx, y + ny) if rel else (nx, ny)
            if cur:
                subs.append(cur)
            cur = [(x, y)]
            sx, sy = x, y
            cmd = "l" if rel else "L"
        elif c == "L":
            nx, ny = num(), num()
            x, y = (x + nx, y + ny) if rel else (nx, ny)
            cur.append((x, y))
        elif c == "H":
            n = num()
            x = x + n if rel else n
            cur.append((x, y))
        elif c == "V":
            n = num()
            y = y + n if rel else n
            cur.append((x, y))
        elif c == "C":
            pts = [num() for _ in range(6)]
            if rel:
                pts = [p + (x if k % 2 == 0 else y) for k, p in enumerate(pts)]
            x0, y0 = x, y
            for s in range(1, 25):
                t = s / 24.0
                u = 1 - t
                px = u**3 * x0 + 3 * u * u * t * pts[0] + 3 * u * t * t * pts[2] + t**3 * pts[4]
                py = u**3 * y0 + 3 * u * u * t * pts[1] + 3 * u * t * t * pts[3] + t**3 * pts[5]
                cur.append((px, py))
            x, y = pts[4], pts[5]
    if cur:
        subs.append(cur)
    return subs


def colour(s):
    named = {"#fff": (255, 255, 255), "#000": (0, 0, 0), "white": (255, 255, 255), "black": (0, 0, 0)}
    if s in named:
        return named[s]
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


def bake_shield(path, size=256, ss=4):
    text = open(path, encoding="utf-8").read()
    vb = re.search(r'viewBox="([\d. -]+)"', text)
    box = [float(v) for v in vb.group(1).split()] if vb else [0, 0, 20, 20]
    scale = size * ss / box[3]
    img = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
    for m in re.finditer(r"<path\b([^>]*)>", text, re.S):
        attrs = m.group(1)
        props = dict(re.findall(r'(fill|stroke|stroke-width)="([^"]*)"', attrs))
        style = re.search(r'style="([^"]*)"', attrs)
        if style:
            for part in style.group(1).split(";"):
                if ":" in part:
                    k, v = part.split(":", 1)
                    props[k.strip()] = v.strip()
        subs = flatten(re.search(r'\bd="([^"]*)"', attrs).group(1))
        pts = [[((x - box[0]) * scale, (y - box[1]) * scale) for x, y in s] for s in subs]
        fill = props.get("fill", "#000")
        if fill != "none":
            mask = Image.new("1", img.size, 0)
            for s in pts:  # even-odd, so inner outlines punch holes
                one = Image.new("1", img.size, 0)
                ImageDraw.Draw(one).polygon(s, fill=1)
                mask = ImageChops.logical_xor(mask, one)
            img.paste(colour(fill) + (255,), (0, 0), mask.convert("L"))
        stroke = props.get("stroke", "none")
        if stroke != "none":
            w = float(props.get("stroke-width", "1")) * scale
            d = ImageDraw.Draw(img)
            for s in pts:
                d.line(s, fill=colour(stroke) + (255,), width=int(round(w)), joint="curve")
                r = w / 2
                for x, y in s:
                    d.ellipse([x - r, y - r, x + r, y + r], fill=colour(stroke) + (255,))
    img = img.resize((size, size), Image.LANCZOS)
    data = img.load()
    name = os.path.splitext(os.path.basename(path))[0]
    with open(os.path.join(OUT, name + ".gim"), "wb") as f:
        f.write(b"GIM1" + struct.pack("<ii", size, size))
        n = 0
        for y in range(size):
            x = 0
            while x < size:
                p = data[x, y]
                run = 1
                while x + run < size and run < 256 and data[x + run, y] == p:
                    run += 1
                f.write(struct.pack("<BBBBB", run - 1, *p))
                n += 5
                x += run
    print(name, n, "bytes")


if __name__ == "__main__":
    for ttf in sorted(glob.glob(os.path.join("tools", "guide", "fonts", "*.ttf"))):
        bake_font(ttf)
    for svg in sorted(glob.glob(os.path.join("tools", "guide", "*.svg"))):
        bake_shield(svg)
