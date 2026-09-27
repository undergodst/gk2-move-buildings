"""Workshop cover: 128x128 pixel art in the style of the game's blueprint build icons, scaled 8x to 1024x1024."""
from PIL import Image

S = 128
im = Image.new("RGBA", (S, S), (0, 0, 0, 255))


def P(x, y, c):
    if 0 <= x < S and 0 <= y < S:
        im.putpixel((x, y), c + (255,))


# Palette taken from the game's blue blueprint icons.
BASE = (55, 100, 171)
WASH = (61, 117, 186)
WASH2 = (58, 112, 182)
HLINE = (48, 88, 151)
HLINE_HI = (58, 97, 155)
VLINE = (89, 126, 185)
VLINE_HI = (107, 140, 193)
B_TOP = (153, 183, 219)
B_LEFT = (151, 174, 211)
B_BOTTOM = (120, 159, 207)
B_RIGHT = (91, 136, 195)
OUTER = (22, 34, 58)
FILL = (46, 90, 154)
LINE = (198, 210, 233)
LINE_HI = (247, 250, 255)
SHADOW = (37, 71, 120)
CREAM = (243, 230, 196)
CREAM_SHADE = (214, 196, 150)
INK = (22, 34, 58)

# Dark outer frame, light bevel border, blueprint paper with brick grid.
x0, y0, x1, y1 = 3, 3, S - 4, S - 4
for y in range(y0, y1 + 1):
    for x in range(x0, x1 + 1):
        d = (x - x0) + (y - y0)
        P(x, y, WASH if d < 24 else WASH2 if d < 40 else BASE)
rows = list(range(y0 + 12, y1, 13))
for y in rows:
    for x in range(x0 + 2, x1 - 1):
        P(x, y, HLINE_HI if x % 11 == 4 else HLINE)
bands = [y0 + 2] + [r + 1 for r in rows]
ends = [r - 1 for r in rows] + [y1 - 2]
for i, (ya, yb) in enumerate(zip(bands, ends)):
    start = x0 + 14 if i % 2 == 0 else x0 + 7
    for x in range(start, x1 - 2, 14):
        for y in range(ya, yb + 1):
            P(x, y, VLINE_HI if y == ya else VLINE)
for x in range(x0, x1 + 1):
    P(x, y0, B_TOP); P(x, y0 + 1, B_TOP); P(x, y1, B_BOTTOM); P(x, y1 - 1, B_BOTTOM)
for y in range(y0, y1 + 1):
    P(x0, y, B_LEFT); P(x0 + 1, y, B_LEFT); P(x1, y, B_RIGHT); P(x1 - 1, y, B_RIGHT)
for i in range(3):
    for x in range(S):
        P(x, i, OUTER); P(x, S - 1 - i, OUTER)
    for y in range(S):
        P(i, y, OUTER); P(S - 1 - i, y, OUTER)


def shape(inside):
    return {(x, y) for y in range(S) for x in range(S) if inside(x, y)}


def ring(mask, width=1):
    out = set(mask)
    for _ in range(width):
        out |= {(x + dx, y + dy) for (x, y) in out for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))}
    return out - mask


# Four-way move arrow, same construction as the tile icon.
cx, cy = 63.5, 45.5
reach, shaft, head_len, head_half = 29.5, 3.0, 12, 10.5


def arrow(x, y):
    dx, dy = abs(x - cx), abs(y - cy)
    along, across = max(dx, dy), min(dx, dy)
    if along > reach:
        return False
    if along >= reach - head_len:
        return across <= head_half * (reach - along) / head_len + 0.01
    return across <= shaft


mask = shape(arrow)
edge = ring(mask)
outer_edge = ring(mask | edge)
for (x, y) in ring(mask | edge | outer_edge):
    if (x - 2, y - 2) in mask | edge:
        P(x, y, SHADOW)
for (x, y) in outer_edge:
    P(x, y, INK)
for (x, y) in edge:
    lit = (x + 1, y) in mask or (x, y + 1) in mask
    P(x, y, LINE_HI if lit else LINE)
for (x, y) in mask:
    P(x, y, FILL)

# 5x7 pixel font, only the glyphs used here.
FONT = {
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "V": ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    "U": ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
    "I": ["11111", "00100", "00100", "00100", "00100", "00100", "11111"],
    "L": ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
    "D": ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
    "N": ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
    "G": ["01110", "10001", "10000", "10111", "10001", "10001", "01110"],
    "S": ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
}


def text_mask(text, scale, top):
    width = len(text) * 6 * scale - scale
    left = (S - width) // 2
    pixels = set()
    for i, ch in enumerate(text):
        glyph = FONT[ch]
        for gy, row in enumerate(glyph):
            for gx, bit in enumerate(row):
                if bit == "1":
                    for sy in range(scale):
                        for sx in range(scale):
                            pixels.add((left + i * 6 * scale + gx * scale + sx, top + gy * scale + sy))
    return pixels


def draw_text(text, scale, top):
    m = text_mask(text, scale, top)
    border = ring(m)
    for (x, y) in {(x + 1, y + 1) for (x, y) in m | border} - m - border:
        P(x, y, SHADOW)
    for (x, y) in border:
        P(x, y, INK)
    for (x, y) in m:
        # lower third of each glyph a shade darker, like the game's title lettering
        P(x, y, CREAM_SHADE if (y - top) >= 5 * scale else CREAM)


draw_text("MOVE", 3, 80)
draw_text("BUILDINGS", 2, 105)

im.convert("RGB").resize((S * 8, S * 8), Image.NEAREST).save("workshop_cover.png", optimize=True)
print("saved workshop_cover.png")
