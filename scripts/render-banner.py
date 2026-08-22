#!/usr/bin/env python3
"""Render AntiLag Next README banner (1280x720) and OG image (1280x640)."""
from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "assets"
LOGO = ROOT / "logo.png"

W, H = 2560, 1440
BG_TOP = (10, 16, 28)
BG_BOT = (8, 12, 22)
GREEN = (45, 226, 160)
GREEN_DK = (22, 120, 88)
RED = (255, 90, 90)
WHITE = (245, 248, 252)
MUTED = (154, 168, 186)
HAIR = (48, 64, 86)


def fnt(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    name = "LiberationSans-Bold.ttf" if bold else "LiberationSans-Regular.ttf"
    return ImageFont.truetype(f"/usr/share/fonts/truetype/liberation/{name}", size)


def rr(draw, box, r, fill, outline=None, width=2):
    draw.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def jitter_series(n=56, seed=4):
    rng = random.Random(seed)
    out = []
    v = 0.62
    for i in range(n):
        v += rng.choice([-1, 1]) * rng.uniform(0.04, 0.16)
        v += 0.04 * math.sin(i * 0.9)
        v = max(0.28, min(0.88, v))
        out.append(v)
    return out


def stable_series(n=56, seed=9):
    rng = random.Random(seed)
    return [0.22 + 0.02 * math.sin(i * 0.65) + rng.uniform(-0.01, 0.01) for i in range(n)]


def draw_wave(d: ImageDraw.ImageDraw, box, values, color, fill_a: int, width=7, zone=0.0):
    x0, y0, x1, y1 = box
    if zone > 0:
        zy = y1 - (y1 - y0) * zone
        d.rectangle((x0, zy, x1, y1), fill=(*color, 38))
    n = len(values)
    pts = []
    for i, v in enumerate(values):
        x = x0 + (x1 - x0) * i / (n - 1)
        y = y1 - (y1 - y0) * v
        pts.append((x, y))
    poly = [(x0, y1)] + pts + [(x1, y1)]
    d.polygon(poly, fill=(*color, fill_a))
    d.line(pts, fill=color + (255,), width=width, joint="curve")
    r = 8
    for p in (pts[0], pts[-1]):
        d.ellipse((p[0] - r, p[1] - r, p[0] + r, p[1] + r), fill=color)


def chart_card(base, box, values, accent, title, pill, fill_a, width=7, zone=0.0):
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    rr(d, box, 32, (17, 26, 42, 236), outline=(*accent, 110), width=3)

    x0, y0, x1, y1 = box
    d.ellipse((x0 + 32, y0 + 30, x0 + 52, y0 + 50), fill=accent)
    d.text((x0 + 64, y0 + 24), title, font=fnt(26, True), fill=WHITE)

    tw = d.textlength(pill, font=fnt(20, True))
    px1, py0, py1 = x1 - 28, y0 + 20, y0 + 58
    px0 = px1 - tw - 32
    rr(d, (px0, py0, px1, py1), 14, (*accent, 32), outline=(*accent, 160), width=2)
    d.text((px0 + 16, py0 + 8), pill, font=fnt(20, True), fill=accent)

    gx0, gy0 = x0 + 78, y0 + 86
    gx1, gy1 = x1 - 36, y1 - 48
    for i, lab in enumerate(["4000", "2000", "0"]):
        yy = gy0 + (gy1 - gy0) * i / 2.0
        d.line((gx0, yy, gx1, yy), fill=(*HAIR, 150), width=2)
        d.text((x0 + 20, yy - 12), lab, font=fnt(20), fill=MUTED)
    d.text((x0 + 20, gy0 - 26), "µs", font=fnt(18, True), fill=MUTED)
    d.text((gx1 - 64, gy1 + 12), "TIME", font=fnt(18, True), fill=MUTED)

    draw_wave(d, (gx0, gy0, gx1, gy1), values, accent, fill_a, width=width, zone=zone)
    base.alpha_composite(layer)


def ico_clock(d, c, s, col):
    x, y = c
    d.ellipse((x - s, y - s, x + s, y + s), outline=col, width=5)
    d.line((x, y, x, y - s * 0.52), fill=col, width=5)
    d.line((x, y, x + s * 0.42, y + 4), fill=col, width=5)


def ico_bolt(d, c, s, col):
    x, y = c
    d.polygon(
        [
            (x - 2, y - s),
            (x + s * 0.55, y - s * 0.12),
            (x + 4, y - s * 0.12),
            (x + 8, y + s),
            (x - s * 0.55, y + s * 0.08),
            (x - 2, y + s * 0.08),
        ],
        fill=col,
    )


def ico_pulse(d, c, s, col):
    x, y = c
    d.line(
        [
            (x - s, y),
            (x - s * 0.4, y),
            (x - s * 0.18, y + s * 0.62),
            (x + 4, y - s * 0.72),
            (x + s * 0.38, y),
            (x + s, y),
        ],
        fill=col,
        width=5,
        joint="curve",
    )


def ico_shield(d, c, s, col):
    x, y = c
    d.polygon(
        [
            (x, y - s),
            (x + s * 0.82, y - s * 0.5),
            (x + s * 0.7, y + s * 0.32),
            (x, y + s),
            (x - s * 0.7, y + s * 0.32),
            (x - s * 0.82, y - s * 0.5),
        ],
        outline=col,
        width=5,
    )
    d.line((x - s * 0.28, y + 4, x - 2, y + s * 0.4), fill=col, width=5)
    d.line((x - 2, y + s * 0.4, x + s * 0.36, y - s * 0.22), fill=col, width=5)


def tile(layer, box, icon, title, sub):
    d = ImageDraw.Draw(layer)
    rr(d, box, 24, (18, 28, 46, 230), outline=(52, 72, 98, 210), width=2)
    cx = box[0] + 62
    cy = (box[1] + box[3]) / 2
    d.ellipse((cx - 36, cy - 36, cx + 36, cy + 36), fill=(45, 226, 160, 28), outline=(*GREEN, 190), width=3)
    icon(d, (cx, cy), 17, GREEN)
    d.text((box[0] + 116, box[1] + 28), title, font=fnt(26, True), fill=WHITE)
    d.text((box[0] + 116, box[1] + 68), sub, font=fnt(20), fill=MUTED)


def footer_pill(layer, x, y, w, h, label, color, icon=None):
    d = ImageDraw.Draw(layer)
    rr(d, (x, y, x + w, y + h), h / 2, (20, 28, 44, 245), outline=(58, 74, 98, 220), width=2)
    tx = x + 36
    if icon == "ban":
        cx, cy, r = x + 40, y + h / 2, 14
        d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=color, width=4)
        d.line((cx - 10, cy + 10, cx + 10, cy - 10), fill=color, width=4)
        tx = x + 66
    elif icon == "mit":
        d.text((x + 28, y + 18), "MIT", font=fnt(22, True), fill=color)
        tx = x + 92
    elif icon == "shield":
        ico_shield(d, (x + 40, y + h / 2), 14, color)
        tx = x + 68
    d.text((tx, y + 18), label, font=fnt(26, True), fill=WHITE)


def render() -> Image.Image:
    img = Image.new("RGB", (W, H))
    pix = img.load()
    for y in range(H):
        c = lerp(BG_TOP, BG_BOT, y / H)
        for x in range(W):
            pix[x, y] = c

    glow = Image.new("RGB", (W, H), (0, 0, 0))
    g = ImageDraw.Draw(glow)
    g.ellipse((-280, -320, 980, 860), fill=(16, 64, 48))
    g.ellipse((1480, -180, 2900, 820), fill=(64, 18, 24))
    glow = glow.filter(ImageFilter.GaussianBlur(200))
    img = Image.blend(img, glow, 0.32).convert("RGBA")

    ui = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ui)
    for x in range(0, W, 56):
        d.line((x, 0, x, H), fill=(255, 255, 255, 7))
    for y in range(0, H, 56):
        d.line((0, y, W, y), fill=(255, 255, 255, 7))

    logo = Image.open(LOGO).convert("RGBA").resize((176, 176), Image.Resampling.LANCZOS)
    ui.alpha_composite(logo, (88, 86))
    d.text((292, 104), "AntiLag Next", font=fnt(82, True), fill=WHITE)
    d.text((292, 202), "github.com/swd3k/antilag-next", font=fnt(30), fill=MUTED)
    d.text((96, 292), "Windows 10 / 11  ·  scheduling latency, not network ping", font=fnt(28), fill=MUTED)

    specs = [
        (ico_clock, "TIMER RESOLUTION", "Global hold on Win11 22H2+"),
        (ico_bolt, "POWER PLAN", "AC-only, laptop-safe"),
        (ico_pulse, "HEALTH / DRIFT", "Audit + Fix recommended"),
        (ico_shield, "SAFE UNDO", "JSON backup · Reset all"),
    ]
    tw, th, gap = 560, 118, 22
    ox, oy = 96, 360
    for i, (ico, title, sub) in enumerate(specs):
        col, row = i % 2, i // 2
        x = ox + col * (tw + gap)
        y = oy + row * (th + gap)
        tile(ui, (x, y, x + tw, y + th), ico, title, sub)

    d.text((96, 640), "Before:", font=fnt(30, True), fill=RED)
    d.text((228, 640), "high scheduling jitter.", font=fnt(30), fill=WHITE)
    d.text((96, 688), "After:", font=fnt(30, True), fill=GREEN)
    d.text((210, 688), "held timer, stable µs.", font=fnt(30), fill=WHITE)

    img.alpha_composite(ui)

    chart_card(img, (1268, 80, 2472, 640), jitter_series(), RED, "SCHEDULING LATENCY", "BEFORE", 70, width=8)
    chart_card(
        img,
        (1268, 668, 2472, 1228),
        stable_series(),
        GREEN,
        "SCHEDULING LATENCY",
        "AFTER",
        150,
        width=10,
        zone=0.28,
    )

    foot = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    pills = [
        (120, 520, "NO GAME INJECT", RED, "ban"),
        (668, 430, "MIT LICENSE", GREEN, "mit"),
        (1126, 470, "ADMINISTRATOR", MUTED, "shield"),
        (1624, 760, "swd3k  ·  OPEN SOURCE", WHITE, None),
    ]
    y, h = 1284, 72
    for x, w, label, col, icon in pills:
        footer_pill(foot, x, y, w, h, label, col, icon)
    img.alpha_composite(foot)
    return img.convert("RGB")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hi = render()
    banner = hi.resize((1280, 720), Image.Resampling.LANCZOS)
    banner.save(OUT / "banner.jpg", "JPEG", quality=93, optimize=True, progressive=True)
    banner.save(OUT / "banner.png", "PNG", optimize=True)
    # GitHub social preview 1280x640
    og = hi.crop((0, 40, 2560, 1320)).resize((1280, 640), Image.Resampling.LANCZOS)
    og.save(OUT / "og.jpg", "JPEG", quality=91, optimize=True)
    print("banner.jpg", (OUT / "banner.jpg").stat().st_size)
    print("banner.png", (OUT / "banner.png").stat().st_size)
    print("og.jpg", (OUT / "og.jpg").stat().st_size)


if __name__ == "__main__":
    main()
