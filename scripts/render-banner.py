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
RED = (255, 90, 90)
WHITE = (245, 248, 252)
MUTED = (154, 168, 186)
HAIR = (48, 64, 86)
PAD = 64


def fnt(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    name = "LiberationSans-Bold.ttf" if bold else "LiberationSans-Regular.ttf"
    return ImageFont.truetype(f"/usr/share/fonts/truetype/liberation/{name}", size)


def rr(draw, box, r, fill, outline=None, width=2):
    draw.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def jitter_series(n=64, seed=4):
    rng = random.Random(seed)
    out, v = [], 0.62
    for i in range(n):
        v += rng.choice([-1, 1]) * rng.uniform(0.05, 0.17)
        v += 0.05 * math.sin(i * 0.85)
        v = max(0.30, min(0.90, v))
        out.append(v)
    return out


def stable_series(n=64, seed=9):
    rng = random.Random(seed)
    # 0–1000 µs scale: hold ~480–560 µs → ~0.50 of chart
    return [0.50 + 0.04 * math.sin(i * 0.55) + rng.uniform(-0.018, 0.018) for i in range(n)]


def draw_wave(d, box, values, color, fill_a, width=7, zone=0.0):
    x0, y0, x1, y1 = box
    if zone > 0:
        zy = y1 - (y1 - y0) * zone
        d.rectangle((x0, zy, x1, y1), fill=(*color, 42))
    pts = []
    n = len(values)
    for i, v in enumerate(values):
        x = x0 + (x1 - x0) * i / (n - 1)
        y = y1 - (y1 - y0) * v
        pts.append((x, y))
    d.polygon([(x0, y1)] + pts + [(x1, y1)], fill=(*color, fill_a))
    d.line(pts, fill=color + (255,), width=width, joint="curve")
    r = 8
    for p in (pts[0], pts[-1]):
        d.ellipse((p[0] - r, p[1] - r, p[0] + r, p[1] + r), fill=color)


def chart_card(base, box, values, accent, title, pill, y_labels, fill_a, width=8, zone=0.0):
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    rr(d, box, 28, (17, 26, 42, 240), outline=(*accent, 120), width=3)

    x0, y0, x1, y1 = box
    d.ellipse((x0 + 28, y0 + 28, x0 + 50, y0 + 50), fill=accent)
    d.text((x0 + 62, y0 + 24), title, font=fnt(28, True), fill=WHITE)

    tw = d.textlength(pill, font=fnt(22, True))
    px1, py0, py1 = x1 - 24, y0 + 18, y0 + 60
    px0 = px1 - tw - 36
    rr(d, (px0, py0, px1, py1), 16, (*accent, 36), outline=(*accent, 170), width=2)
    d.text((px0 + 18, py0 + 8), pill, font=fnt(22, True), fill=accent)

    gx0, gy0 = x0 + 86, y0 + 88
    gx1, gy1 = x1 - 32, y1 - 44
    for i, lab in enumerate(y_labels):
        yy = gy0 + (gy1 - gy0) * i / (len(y_labels) - 1)
        d.line((gx0, yy, gx1, yy), fill=(*HAIR, 150), width=2)
        d.text((x0 + 18, yy - 14), lab, font=fnt(22), fill=MUTED)
    d.text((x0 + 18, gy0 - 28), "µs", font=fnt(18, True), fill=MUTED)
    d.text((gx1 - 70, gy1 + 10), "TIME", font=fnt(18, True), fill=MUTED)

    draw_wave(d, (gx0, gy0, gx1, gy1), values, accent, fill_a, width=width, zone=zone)
    base.alpha_composite(layer)


def ico_clock(d, c, s, col):
    x, y = c
    d.ellipse((x - s, y - s, x + s, y + s), outline=col, width=6)
    d.line((x, y, x, y - s * 0.52), fill=col, width=6)
    d.line((x, y, x + s * 0.42, y + 4), fill=col, width=6)


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
        width=6,
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
        width=6,
    )
    d.line((x - s * 0.28, y + 4, x - 2, y + s * 0.4), fill=col, width=6)
    d.line((x - 2, y + s * 0.4, x + s * 0.36, y - s * 0.22), fill=col, width=6)


def tile(layer, box, icon, title, line1, line2):
    d = ImageDraw.Draw(layer)
    rr(d, box, 26, (18, 28, 46, 236), outline=(52, 72, 98, 220), width=2)
    x0, y0, x1, y1 = box
    d.rectangle((x0, y0 + 26, x0 + 8, y1 - 26), fill=(*GREEN, 220))
    cx, cy = x0 + 90, (y0 + y1) / 2
    d.ellipse((cx - 48, cy - 48, cx + 48, cy + 48), fill=(45, 226, 160, 30), outline=(*GREEN, 200), width=3)
    icon(d, (cx, cy), 22, GREEN)
    ty = cy - 52
    d.text((x0 + 168, ty), title, font=fnt(32, True), fill=WHITE)
    d.text((x0 + 168, ty + 48), line1, font=fnt(24), fill=MUTED)
    d.text((x0 + 168, ty + 82), line2, font=fnt(24), fill=MUTED)


def render() -> Image.Image:
    img = Image.new("RGB", (W, H))
    pix = img.load()
    for y in range(H):
        c = lerp(BG_TOP, BG_BOT, y / H)
        for x in range(W):
            pix[x, y] = c

    glow = Image.new("RGB", (W, H), (0, 0, 0))
    g = ImageDraw.Draw(glow)
    g.ellipse((-260, -280, 1100, 920), fill=(16, 64, 48))
    g.ellipse((1400, -160, 2920, 900), fill=(64, 18, 24))
    glow = glow.filter(ImageFilter.GaussianBlur(200))
    img = Image.blend(img, glow, 0.30).convert("RGBA")

    ui = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(ui)
    for x in range(0, W, 48):
        d.line((x, 0, x, H), fill=(255, 255, 255, 7))
    for y in range(0, H, 48):
        d.line((0, y, W, y), fill=(255, 255, 255, 7))

    foot_h = 108
    foot_y = H - PAD - foot_h
    content_b = foot_y - 20
    left_r = 1232
    right_l = 1256
    right_r = W - PAD

    logo = Image.open(LOGO).convert("RGBA").resize((148, 148), Image.Resampling.LANCZOS)
    ui.alpha_composite(logo, (PAD + 8, PAD + 10))
    d.text((PAD + 176, PAD + 22), "AntiLag Next", font=fnt(72, True), fill=WHITE)
    d.text((PAD + 176, PAD + 108), "github.com/swd3k/antilag-next", font=fnt(28), fill=MUTED)

    chip = "v1.4.0"
    cw = d.textlength(chip, font=fnt(22, True))
    cx0, cy0 = left_r - cw - 48, PAD + 36
    rr(d, (cx0, cy0, cx0 + cw + 32, cy0 + 44), 14, (45, 226, 160, 28), outline=(*GREEN, 180), width=2)
    d.text((cx0 + 16, cy0 + 8), chip, font=fnt(22, True), fill=GREEN)

    d.text((PAD + 8, PAD + 172), "Windows 10 / 11  ·  scheduling latency, not ping", font=fnt(26), fill=MUTED)

    specs = [
        (ico_clock, "TIMER RESOLUTION", "Global hold on Win11 22H2+", "Games inherit after reboot"),
        (ico_bolt, "POWER PLAN", "AC-only min/max CPU & ASPM", "Battery indexes never written"),
        (ico_pulse, "HEALTH / DRIFT", "Audit after Windows Update", "Fix recommended, one click"),
        (ico_shield, "SAFE UNDO", "JSON backup · Reset all", "No game inject, MIT"),
    ]
    grid_t = PAD + 220
    gap = 20
    cols, rows = 1, 4
    tw = (left_r - PAD - gap) / cols
    th = (content_b - grid_t - gap) / rows
    for i, (ico, title, a, b) in enumerate(specs):
        col, row = i % cols, i // cols
        x = PAD + col * (tw + gap)
        y = grid_t + row * (th + gap)
        tile(ui, (x, y, x + tw, y + th), ico, title, a, b)

    img.alpha_composite(ui)

    mid = grid_t + (content_b - grid_t - gap) / 2
    chart_card(
        img,
        (right_l, PAD, right_r, mid),
        jitter_series(),
        RED,
        "SCHEDULING LATENCY",
        "BEFORE",
        ["4000", "2000", "0"],
        75,
        width=8,
    )
    chart_card(
        img,
        (right_l, mid + gap, right_r, content_b),
        stable_series(),
        GREEN,
        "SCHEDULING LATENCY",
        "AFTER",
        ["1000", "500", "0"],
        155,
        width=10,
        zone=0.62,
    )

    foot = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    fd = ImageDraw.Draw(foot)
    rr(fd, (PAD, foot_y, W - PAD, foot_y + foot_h), 28, (16, 22, 34, 245), outline=(52, 68, 90, 220), width=2)

    items = [
        ("NO GAME INJECT", RED, "ban"),
        ("MIT LICENSE", GREEN, None),
        ("ADMINISTRATOR", MUTED, "shield"),
        ("OPEN SOURCE  ·  swd3k", WHITE, None),
    ]
    inner_l, inner_r = PAD + 28, W - PAD - 28
    slot = (inner_r - inner_l) / len(items)
    cy = foot_y + foot_h / 2
    for i, (label, col, kind) in enumerate(items):
        cx = inner_l + slot * i + slot / 2
        if i:
            dx = inner_l + slot * i
            fd.line((dx, foot_y + 28, dx, foot_y + foot_h - 28), fill=(70, 86, 108, 220), width=2)
        # icon + label centered in slot
        text_w = fd.textlength(label, font=fnt(26, True))
        icon_w = 44 if kind else 0
        total = icon_w + text_w
        x0 = cx - total / 2
        if kind == "ban":
            r = 16
            ix, iy = x0 + 18, cy
            fd.ellipse((ix - r, iy - r, ix + r, iy + r), outline=col, width=4)
            fd.line((ix - 11, iy + 11, ix + 11, iy - 11), fill=col, width=4)
        elif kind == "mit":
            fd.text((x0, cy - 16), "MIT", font=fnt(22, True), fill=col)
        elif kind == "shield":
            ico_shield(fd, (x0 + 16, cy), 16, col)
        tx = x0 + icon_w
        fd.text((tx, cy - 18), label, font=fnt(26, True), fill=WHITE)

    img.alpha_composite(foot)
    return img.convert("RGB")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hi = render()
    banner = hi.resize((1280, 720), Image.Resampling.LANCZOS)
    banner.save(OUT / "banner.jpg", "JPEG", quality=93, optimize=True, progressive=True)
    og = hi.crop((0, 24, 2560, 1304)).resize((1280, 640), Image.Resampling.LANCZOS)
    og.save(OUT / "og.jpg", "JPEG", quality=91, optimize=True)
    print("banner.jpg", (OUT / "banner.jpg").stat().st_size)
    print("og.jpg", (OUT / "og.jpg").stat().st_size)


if __name__ == "__main__":
    main()
