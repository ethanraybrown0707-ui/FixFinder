"""Draws the icons of FixFinder and FixFinder Learn. FixFinder's is its logo on a black rounded tile: the logo as it was
given, in logo-given.png - a light-blue and a deep-blue stroke that meet at a peak, crossed low down by a mid-blue sweep.
FixFinder Learn's is a white open book on a rounded tile blended from FixFinder's blue, lightened with white, to its blue
darkened with black.

    python make-icon.py

Writes FixFinder.ico and logo.png beside itself, and FixFinderLearn.ico and logo.png into FixFinder.Learn's Assets.
Every size is made at its own scale rather than shrunk from one large icon, so the logo stays the logo at 16px: the small
sizes get a larger logo. At every size the tile fills its square to the edge, as other programs' icons do - one with a
margin of its own looks smaller than theirs beside it on the desktop.
"""
import io
import os
import struct
from PIL import Image, ImageChops, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
PNG_FROM = 64
SUPERSAMPLE = 8

# The same stops as Styles.xaml's LogoTileBrush: FixFinder's blue with 15% white, the blue, and the blue with 55% black.
BLEND = [
    (0.00, (0x32, 0x83, 0xEC)),
    (0.45, (0x0E, 0x6C, 0xE9)),
    (1.00, (0x07, 0x30, 0x69)),
]

WHITE = (255, 255, 255, 255)
BLACK = (0, 0, 0, 255)

# FixFinder's logo as it was given: 1254 by 1254, its mark on black. Its black is not one black: 1,246,505 of its pixels
# are greys of 1 to 4 out of 255, and 4,620 more are faint greys up to 18. Every grey up to 24 is made black, so the
# tile is one black. A pixel is grey when its three channels are within 10 of each other - the mark's own edges,
# blended into the black, keep their blue's lean and are left as they are.
GIVEN_LOGO = "logo-given.png"
FAINTEST_GREY = 24
GREY_SPREAD = 10

# How opaque the tile must be wherever the mark is painted: all of it, or the last of its smoothed rounded edge.
MOST_OPAQUE = 250

# A pixel brighter than this in any channel is the mark's, for finding where the mark is in the picture.
MARK_EDGE = 40

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


def draw_book(image, scale, size):
    pen = ImageDraw.Draw(image)
    for page in (LEFT_PAGE, RIGHT_PAGE):
        pen.polygon([(x * scale, y * scale) for x, y in page], fill=WHITE)


def draw_nothing(image, scale, size):
    pass


# Each tile and what is drawn on it: the book on its blend, and the black tile the logo's mark is set on.
TILES = {"book": (blended_tile, draw_book), "black": (black_tile, draw_nothing)}


def draw(size, tile_name):
    """A rounded tile at a size, with what is drawn on it, drawn eight times as large and shrunk so its edges are smooth."""
    pixels = size * SUPERSAMPLE
    scale = pixels / 100
    tile, mark = TILES[tile_name]

    corner = 22 if size <= 20 else 23 if size <= 40 else 24

    mask = Image.new("L", (pixels, pixels), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, pixels - 1, pixels - 1], radius=corner * scale, fill=255)

    image = Image.new("RGBA", (pixels, pixels), (0, 0, 0, 0))
    image.paste(tile(pixels), (0, 0), mask)

    mark(image, scale, size)

    return image.resize((size, size), Image.LANCZOS)


def brightest_channel(picture):
    red, green, blue = picture.split()
    return ImageChops.lighter(ImageChops.lighter(red, green), blue)


def given_mark(folder):
    """The logo's mark, cut from the picture as it was given, on black, with the black's faint grey marks made black."""
    picture = Image.open(os.path.join(folder, GIVEN_LOGO)).convert("RGB")
    colours = picture.load()
    width, height = picture.size
    made_black = 0
    for y in range(height):
        for x in range(width):
            red, green, blue = colours[x, y]
            if 0 < max(red, green, blue) <= FAINTEST_GREY and max(red, green, blue) - min(red, green, blue) < GREY_SPREAD:
                colours[x, y] = (0, 0, 0)
                made_black += 1

    where = brightest_channel(picture).point(lambda value: 255 if value > MARK_EDGE else 0).getbbox()
    print(f"the given logo: {made_black} grey pixels of its black made black; its mark is {where[2] - where[0]} by {where[3] - where[1]}")
    return picture.crop(where)


def mark_width(size):
    """How much of the tile's width the mark takes at a size: more at the small sizes, so it can still be made out."""
    return 0.83 if size <= 20 else 0.74 if size <= 40 else 0.66


def draw_logo(size, mark):
    """FixFinder's icon at a size: the mark shrunk straight from the given picture onto the black tile, in its middle."""
    icon = draw(size, "black")

    width = round(size * mark_width(size))
    height = round(width * mark.height / mark.width)
    placed = mark.resize((width, height), Image.LANCZOS)
    corner = ((size - width) // 2, (size - height) // 2)

    # Only the mark's own pixels are painted - its black is the tile's already - and only its colours: the tile keeps its
    # own shape. Every one of them is where the tile is at least MOST_OPAQUE opaque, none out on its rounded edge.
    ink = brightest_channel(placed).point(lambda value: 255 if value > 0 else 0)
    tile_alpha = icon.getchannel("A")
    too_clear = tile_alpha.crop((corner[0], corner[1], corner[0] + width, corner[1] + height)).point(
        lambda value: 255 if value < MOST_OPAQUE else 0)
    if ImageChops.multiply(ink, too_clear).getbbox() is not None:
        raise ValueError(f"at {size}px the mark reaches the tile's rounded edge")

    colours = icon.convert("RGB")
    colours.paste(placed, corner, ink)
    painted = colours.convert("RGBA")
    painted.putalpha(tile_alpha)
    return painted


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


def build(folder, icon_name, draw_at):
    entries = [(size, png_entry(draw_at(size)) if size >= PNG_FROM else bmp_entry(draw_at(size))) for size in SIZES]

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

    draw_at(256).save(os.path.join(folder, "logo.png"))
    print(f"wrote {icon_name} ({len(entries)} sizes) and logo.png in {folder}")


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    mark = given_mark(here)
    build(here, "FixFinder.ico", lambda size: draw_logo(size, mark))
    build(os.path.join(here, "..", "..", "FixFinder.Learn", "Assets"), "FixFinderLearn.ico", lambda size: draw(size, "book"))
