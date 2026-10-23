"""Draws the icons of FixFinder and FixFinder Learn. FixFinder's is its logo on a black rounded tile: a white chevron either
side of a diamond, its upper half blue and its lower half white, with a band of black between them. FixFinder Learn's is a
white open book on a rounded tile blended from the logo's blue, lightened with white, to its blue darkened with black.

    python make-icon.py

Writes FixFinder.ico and logo.png beside itself, and FixFinderLearn.ico and logo.png into FixFinder.Learn's Assets.
Every size is drawn at its own scale rather than shrunk from one large picture, so the logo stays the logo at 16px: the
small sizes get a tile that fills more of the square, and FixFinder's a larger logo with heavier chevrons.
"""
import io
import os
import struct
from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
PNG_FROM = 64
SUPERSAMPLE = 8

# The same stops as Styles.xaml's LogoTileBrush: the logo's blue with 15% white, the blue, and the blue with 55% black.
BLEND = [
    (0.00, (0x5E, 0x80, 0xE6)),
    (0.45, (0x42, 0x69, 0xE2)),
    (1.00, (0x1E, 0x2F, 0x66)),
]

WHITE = (255, 255, 255, 255)
BLACK = (0, 0, 0, 255)

# FixFinder's logo, measured from the logo as it was given (2000 by 2000) on the same 100-unit square: each chevron is one
# stroke with round ends and a round corner, and the diamond is the blue half above and the white half below the band.
LOGO_BLUE = (0x42, 0x69, 0xE2, 255)
LEFT_CHEVRON = [(27.30, 37.12), (18.55, 49.95), (27.30, 62.78)]
RIGHT_CHEVRON = [(100 - x, y) for x, y in LEFT_CHEVRON]
CHEVRON_STROKE = 4.65
DIAMOND_TOP, DIAMOND_BOTTOM = (50, 28.40), (50, 71.55)
DIAMOND_LEFT, DIAMOND_RIGHT = (38.55, 50), (61.45, 50)
BAND_TOP, BAND_BOTTOM = (50, 44.90), (50, 55.10)
BLUE_HALF = [DIAMOND_TOP, DIAMOND_RIGHT, BAND_TOP, DIAMOND_LEFT]
WHITE_HALF = [DIAMOND_LEFT, BAND_BOTTOM, DIAMOND_RIGHT, DIAMOND_BOTTOM]

# An open book seen from above: two pages leaning away from the spine, with a gap down it, on the same 100-unit square.
LEFT_PAGE = [(17, 33), (47, 39), (47, 75), (17, 69)]
RIGHT_PAGE = [(53, 39), (83, 33), (83, 69), (53, 75)]


def blend_at(t):
    for (start, low), (end, high) in zip(BLEND, BLEND[1:]):
        if t <= end:
            share = (t - start) / (end - start)
            return tuple(round(a + (b - a) * share) for a, b in zip(low, high))
    return BLEND[-1][1]


def blended_tile(pixels):
    gradient = Image.new("RGBA", (pixels, pixels))
    shade = gradient.load()
    for y in range(pixels):
        for x in range(pixels):
            shade[x, y] = blend_at((x + y) / (2 * (pixels - 1))) + (255,)
    return gradient


def black_tile(pixels):
    return Image.new("RGBA", (pixels, pixels), BLACK)


def logo_scale(size):
    """How much larger than as given the logo is drawn at a size, about the tile's centre, and its chevrons' stroke there."""
    return (1.32, 8.0) if size <= 20 else (1.18, 6.2) if size <= 40 else (1.0, CHEVRON_STROKE)


def draw_logo(pen, scale, size):
    enlarge, stroke = logo_scale(size)

    def placed(point):
        x, y = point
        return ((50 + (x - 50) * enlarge) * scale, (50 + (y - 50) * enlarge) * scale)

    width = stroke * enlarge * scale
    for chevron in (LEFT_CHEVRON, RIGHT_CHEVRON):
        corners = [placed(point) for point in chevron]
        pen.line(corners, fill=WHITE, width=round(width), joint="curve")
        for x, y in corners:
            pen.ellipse([x - width / 2, y - width / 2, x + width / 2, y + width / 2], fill=WHITE)

    pen.polygon([placed(point) for point in BLUE_HALF], fill=LOGO_BLUE)
    pen.polygon([placed(point) for point in WHITE_HALF], fill=WHITE)


def draw_book(pen, scale, size):
    for page in (LEFT_PAGE, RIGHT_PAGE):
        pen.polygon([(x * scale, y * scale) for x, y in page], fill=WHITE)


# Each icon: the tile under its mark, and the mark.
ICONS = {"logo": (black_tile, draw_logo), "book": (blended_tile, draw_book)}


def draw(size, icon):
    pixels = size * SUPERSAMPLE
    scale = pixels / 100
    tile, mark = ICONS[icon]

    inset, corner = (1, 22) if size <= 20 else (2, 23) if size <= 40 else (4, 24)

    mask = Image.new("L", (pixels, pixels), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [inset * scale, inset * scale, (100 - inset) * scale, (100 - inset) * scale], radius=corner * scale, fill=255)

    image = Image.new("RGBA", (pixels, pixels), (0, 0, 0, 0))
    image.paste(tile(pixels), (0, 0), mask)

    mark(ImageDraw.Draw(image), scale, size)

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


def build(folder, icon_name, icon):
    entries = [(size, png_entry(draw(size, icon)) if size >= PNG_FROM else bmp_entry(draw(size, icon))) for size in SIZES]

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

    draw(256, icon).save(os.path.join(folder, "logo.png"))
    print(f"wrote {icon_name} ({len(entries)} sizes) and logo.png in {folder}")


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    build(here, "FixFinder.ico", "logo")
    build(os.path.join(here, "..", "..", "FixFinder.Learn", "Assets"), "FixFinderLearn.ico", "book")
