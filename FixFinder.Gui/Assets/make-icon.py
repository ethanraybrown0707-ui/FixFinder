"""Draws FixFinder.ico.

Kept in the repository next to the icon it produces, because a committed .ico is an opaque
binary that nobody can adjust - the design lives here instead, as something readable.

    python make-icon.py

Two things about it are deliberate:

* Every size is drawn at its own scale rather than produced by shrinking one large image.
  Downscaling a 256px drawing to 16px turns a 2px ring into grey mush; drawing at 16px with
  proportionally heavier strokes keeps it a ring. The small sizes are the ones people actually
  see - taskbar, title bar, Alt+Tab - so they get the attention.

* Sizes up to 48px are stored as 32-bit BMP and the large ones as PNG. Windows has accepted
  PNG entries at any size since Vista, but parts of the shell and plenty of third-party tools
  still expect BMP for the small ones, and a missing 16px icon shows up as a generic page.

The mark is a magnifying glass with a tick in the lens: find, then confirm. The colours are the
window's own - the blue it uses for headings and the green it uses for a change that worked.
"""
import io
import struct
from PIL import Image, ImageDraw

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
PNG_FROM = 64          # sizes at or above this are stored as PNG

GLASS = (13, 71, 161, 255)      # #0D47A1 - the blue the window uses for headings
LENS = (46, 125, 50, 255)       # #2E7D32 - the green used for a change that worked
TICK = (255, 255, 255, 255)     # the tick is punched out of the lens

# The tick is white on green rather than green on white, which is the opposite of the obvious
# way round. It was chosen by drawing both and looking at them at 16px: green on white turns
# into a grey-green smudge, because the tick and its background are close in luminance once
# there are only a few pixels to say it with. White on green keeps the full contrast range and
# is still a tick at the smallest size in the file.

SS = 8                          # supersample factor, for anti-aliasing


# Geometry per size tier, as fractions of the icon's width.
#
# Three tiers rather than one formula, because the problem changes with the size rather than
# just scaling. At 16px a ring drawn at the proportions that look elegant at 256px is barely a
# pixel wide and reads as a grey smudge, so the small tiers get heavier strokes, a bigger lens
# and a stubbier handle - the mark fills its box instead of floating in the middle of it.
TIERS = {
    "tiny": dict(                       # 16-20px: taskbar and title bar
        radius=0.360, cx=0.380, cy=0.360,
        ring_w=0.130, handle_w=0.180, tick_w=0.135,
        handle_end=0.930, tick_scale=1.28,
    ),
    "small": dict(                      # 24-40px: Alt+Tab, small list views
        radius=0.335, cx=0.390, cy=0.370,
        ring_w=0.105, handle_w=0.150, tick_w=0.115,
        handle_end=0.930, tick_scale=1.28,
    ),
    "large": dict(                      # 48px and up: Explorer tiles, properties, the Desktop
        radius=0.315, cx=0.395, cy=0.375,
        ring_w=0.085, handle_w=0.125, tick_w=0.100,
        handle_end=0.930, tick_scale=1.28,
    ),
}


def tier_for(size: int) -> dict:
    if size <= 20:
        return TIERS["tiny"]
    if size <= 40:
        return TIERS["small"]

    return TIERS["large"]


def draw_icon(size: int) -> Image.Image:
    """Renders one size, drawn large and reduced once for clean edges."""
    px = size * SS
    image = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    tier = tier_for(size)

    ring_w = tier["ring_w"] * px
    handle_w = tier["handle_w"] * px
    tick_w = tier["tick_w"] * px

    cx, cy = tier["cx"] * px, tier["cy"] * px
    radius = tier["radius"] * px

    def circle(x, y, r, **kwargs):
        draw.ellipse([x - r, y - r, x + r, y + r], **kwargs)

    def stroke(a, b, width, colour):
        """A line with round caps, which ImageDraw does not provide on its own."""
        draw.line([a, b], fill=colour, width=int(round(width)))
        circle(a[0], a[1], width / 2, fill=colour)
        circle(b[0], b[1], width / 2, fill=colour)

    # Handle first, so the lens covers where it meets the ring.
    diagonal = 0.7071
    handle_start = (cx + diagonal * radius * 0.9, cy + diagonal * radius * 0.9)
    handle_end = (tier["handle_end"] * px, tier["handle_end"] * px)
    stroke(handle_start, handle_end, handle_w, GLASS)

    # Lens, then the tick inside it, then the ring on top of both.
    circle(cx, cy, radius, fill=LENS)

    # The tick is sized against the lens rather than the icon, so it keeps the same margin
    # inside the glass whatever the tier does to the radius.
    t = tier["tick_scale"] * radius
    p1 = (cx - 0.52 * t, cy + 0.02 * t)
    p2 = (cx - 0.16 * t, cy + 0.38 * t)
    p3 = (cx + 0.54 * t, cy - 0.42 * t)
    stroke(p1, p2, tick_w, TICK)
    stroke(p2, p3, tick_w, TICK)

    circle(cx, cy, radius, outline=GLASS, width=int(round(ring_w)))

    reduced = image.resize((size, size), Image.LANCZOS)

    # LANCZOS leaves the smallest sizes slightly soft. A light contrast lift on the alpha
    # channel alone sharpens the silhouette without touching the colours.
    if size <= 24:
        r, g, b, a = reduced.split()
        a = a.point(lambda v: 0 if v < 24 else min(255, int(v * 1.35)))
        reduced = Image.merge("RGBA", (r, g, b, a))

    return reduced


def bmp_entry(image: Image.Image) -> bytes:
    """A 32-bit BMP icon entry: BITMAPINFOHEADER, BGRA rows bottom-up, then the AND mask."""
    w, h = image.size

    header = struct.pack(
        "<IiiHHIIiiII",
        40,        # biSize
        w,
        h * 2,     # biHeight covers the colour rows and the mask rows
        1,         # biPlanes
        32,        # biBitCount
        0,         # biCompression (BI_RGB)
        0, 0, 0, 0, 0,
    )

    pixels = image.load()
    rows = []

    for y in range(h - 1, -1, -1):          # bottom-up, as BMP requires
        row = bytearray()
        for x in range(w):
            r, g, b, a = pixels[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))

    # The 1-bit AND mask is legacy; the alpha channel does the real work, so it is all zeros.
    mask_row = ((w + 31) // 32) * 4
    mask = bytes(mask_row * h)

    return header + b"".join(rows) + mask


def png_entry(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def build(path: str) -> None:
    entries = []

    for size in SIZES:
        image = draw_icon(size)
        data = png_entry(image) if size >= PNG_FROM else bmp_entry(image)
        entries.append((size, data))

    offset = 6 + 16 * len(entries)
    directory = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    payload = bytearray()

    for size, data in entries:
        directory += struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,     # 0 means 256 in the icon directory
            0 if size >= 256 else size,
            0,                              # palette entries, 0 for 32-bit
            0,                              # reserved
            1,                              # colour planes
            32,                             # bits per pixel
            len(data),
            offset,
        )

        payload += data
        offset += len(data)

    with open(path, "wb") as handle:
        handle.write(bytes(directory) + bytes(payload))

    print(f"wrote {path}: {len(entries)} sizes, {len(directory) + len(payload):,} bytes")


if __name__ == "__main__":
    import os

    build(os.path.join(os.path.dirname(os.path.abspath(__file__)), "FixFinder.ico"))
