"""Draws the icons of FixFinder and FixFinder Learn: a rounded tile blended from blue through green and yellow to orange,
with a white tick that sweeps up and across FixFinder's, and a white open book on FixFinder Learn's.

    python make-icon.py

Writes FixFinder.ico and logo.png beside itself, and FixFinderLearn.ico and logo.png into FixFinder.Learn's Assets.
Every size is drawn at its own scale rather than shrunk from one large picture, so the tick stays a tick at 16px: the
small sizes get a heavier stroke and a tile that fills more of the square.
"""
import io
import os
import struct
from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
PNG_FROM = 64
SUPERSAMPLE = 8

BLEND = [
    (0.00, (0x1A, 0x6D, 0xFF)),
    (0.30, (0x00, 0xB3, 0xC7)),
    (0.52, (0x2F, 0xCB, 0x6B)),
    (0.72, (0xFF, 0xC9, 0x1A)),
    (0.92, (0xFF, 0x7A, 0x1A)),
    (1.00, (0xFF, 0x7A, 0x1A)),
]

TICK = [(24, 54), (30, 58), (36, 64), (42, 72), (52, 52), (64, 36), (80, 26)]

# An open book seen from above: two pages leaning away from the spine, with a gap down it, on the same 100-unit square.
LEFT_PAGE = [(17, 33), (47, 39), (47, 75), (17, 69)]
RIGHT_PAGE = [(53, 39), (83, 33), (83, 69), (53, 75)]


def blend_at(t):
    for (start, low), (end, high) in zip(BLEND, BLEND[1:]):
        if t <= end:
            share = (t - start) / (end - start)
            return tuple(round(a + (b - a) * share) for a, b in zip(low, high))
    return BLEND[-1][1]


def cubic(p0, p1, p2, p3, steps=400):
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        yield (u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
               u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1])


def tick_points():
    return list(cubic(*TICK[0:4])) + list(cubic(*TICK[3:7]))[1:]


def draw_tick(pen, scale, stroke):
    radius = stroke * scale / 2
    for x, y in tick_points():
        pen.ellipse([x * scale - radius, y * scale - radius, x * scale + radius, y * scale + radius], fill=(255, 255, 255, 255))


def draw_book(pen, scale, stroke):
    for page in (LEFT_PAGE, RIGHT_PAGE):
        pen.polygon([(x * scale, y * scale) for x, y in page], fill=(255, 255, 255, 255))


MARKS = {"tick": draw_tick, "book": draw_book}


def draw(size, mark):
    pixels = size * SUPERSAMPLE
    scale = pixels / 100

    inset, corner, stroke = (1, 22, 15) if size <= 20 else (2, 23, 13) if size <= 40 else (4, 24, 11)

    gradient = Image.new("RGBA", (pixels, pixels))
    shade = gradient.load()
    for y in range(pixels):
        for x in range(pixels):
            shade[x, y] = blend_at((x + y) / (2 * (pixels - 1))) + (255,)

    mask = Image.new("L", (pixels, pixels), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [inset * scale, inset * scale, (100 - inset) * scale, (100 - inset) * scale], radius=corner * scale, fill=255)

    image = Image.new("RGBA", (pixels, pixels), (0, 0, 0, 0))
    image.paste(gradient, (0, 0), mask)

    MARKS[mark](ImageDraw.Draw(image), scale, stroke)

    return image.resize((size, size), Image.LANCZOS)


def bmp_entry(image):
    width, height = image.size
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    rows = []
    pixels = image.load()
    for y in range(height - 1, -1, -1):
        rows.append(bytes(b for x in range(width) for b in (pixels[x, y][2], pixels[x, y][1], pixels[x, y][0], pixels[x, y][3])))
    mask = bytes(((width + 31) // 32) * 4 * height)
    return header + b"".join(rows) + mask


def png_entry(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def build(folder, icon_name, mark):
    entries = [(size, png_entry(draw(size, mark)) if size >= PNG_FROM else bmp_entry(draw(size, mark))) for size in SIZES]

    offset = 6 + 16 * len(entries)
    directory = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    payload = bytearray()

    for size, data in entries:
        side = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", side, side, 0, 0, 1, 32, len(data), offset)
        payload += data
        offset += len(data)

    os.makedirs(folder, exist_ok=True)
    with open(os.path.join(folder, icon_name), "wb") as handle:
        handle.write(bytes(directory) + bytes(payload))

    draw(256, mark).save(os.path.join(folder, "logo.png"))
    print(f"wrote {icon_name} ({len(entries)} sizes) and logo.png in {folder}")


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    build(here, "FixFinder.ico", "tick")
    build(os.path.join(here, "..", "..", "FixFinder.Learn", "Assets"), "FixFinderLearn.ico", "book")
