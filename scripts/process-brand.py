#!/usr/bin/env python3
"""Import authored banner + chip logo: strip editor cursor, emit repo brand files."""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
def _src(*cands):
    for p in cands:
        if p.exists():
            return p
    raise FileNotFoundError(cands)

SRC_BANNER = _src(Path("/workspace/attachments/image.png"), Path("/home/workdir/attachments/image.png"))
SRC_LOGO = _src(Path("/workspace/attachments/logo.png"), Path("/home/workdir/attachments/logo.png"))
ASSETS = ROOT / "docs" / "assets"
UI = ROOT / "AntiLagNext" / "src" / "AntiLagNext.Ui"


def clean_banner(src: Path) -> Image.Image:
    im = Image.open(src).convert("RGB")
    a = np.array(im)
    h, w = a.shape[:2]
    cx, cy, r = 669.4, 583.5, 44
    yy, xx = np.ogrid[:h, :w]
    dist = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    rcol, gcol, bcol = a[:, :, 0].astype(int), a[:, :, 1].astype(int), a[:, :, 2].astype(int)
    purple = (bcol > 130) & (rcol > 60) & (gcol < 180) & (bcol > gcol + 25) & (dist < 56)
    white = (rcol > 170) & (gcol > 170) & (bcol > 190) & (dist < 32)
    mask = (dist <= r) | purple | white
    from numpy.lib.stride_tricks import sliding_window_view

    padded = np.pad(mask, 3, mode="constant")
    mask = sliding_window_view(padded, (7, 7)).any(axis=(-1, -2))
    a[mask] = np.array([3, 10, 26], dtype=np.uint8)
    out = Image.fromarray(a, "RGB")

    d = ImageDraw.Draw(out)
    cyan = (0, 229, 204)
    x, y, s = 652, 562, 12
    d.line([(x + s, y), (x, y + s), (x + s, y + 2 * s)], fill=cyan, width=3, joint="curve")
    d.line([(x + s + 8, y + 2 * s - 1), (x + s + 17, y + 1)], fill=cyan, width=3)
    d.line(
        [(x + s + 24, y), (x + s + 24 + s, y + s), (x + s + 24, y + 2 * s)],
        fill=cyan,
        width=3,
        joint="curve",
    )
    return out


def clean_logo(src: Path) -> Image.Image:
    im = Image.open(src).convert("RGBA")
    a = np.array(im)
    alpha = a[:, :, 3]
    # drop almost-transparent white fringe
    faint = alpha < 12
    a[:, :, 3][faint] = 0
    # crush near-black RGB where fully transparent
    a[:, :, 0][a[:, :, 3] == 0] = 0
    a[:, :, 1][a[:, :, 3] == 0] = 0
    a[:, :, 2][a[:, :, 3] == 0] = 0
    im = Image.fromarray(a, "RGBA")
    bbox = im.getbbox() or (0, 0, im.width, im.height)
    # keep a little pin padding
    pad = 8
    bbox = (
        max(0, bbox[0] - pad),
        max(0, bbox[1] - pad),
        min(im.width, bbox[2] + pad),
        min(im.height, bbox[3] + pad),
    )
    im = im.crop(bbox)
    # square canvas
    side = max(im.size)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(im, ((side - im.size[0]) // 2, (side - im.size[1]) // 2), im)
    return canvas.resize((512, 512), Image.Resampling.LANCZOS)


def save_ico(img: Image.Image, path: Path, sizes=(16, 20, 24, 32, 40, 48, 64, 128, 256)):
    """PNG-in-ICO (Vista+). Pillow's ICO writer drops extra sizes here."""
    import io
    import struct

    blobs = []
    for s in sizes:
        buf = io.BytesIO()
        img.resize((s, s), Image.Resampling.LANCZOS).save(buf, format="PNG")
        blobs.append((s, buf.getvalue()))
    count = len(blobs)
    offset = 6 + 16 * count
    header = struct.pack("<HHH", 0, 1, count)
    directory = b""
    payload = b""
    for s, png in blobs:
        w = 0 if s >= 256 else s
        h = 0 if s >= 256 else s
        directory += struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(png), offset + len(payload))
        payload += png
    path.write_bytes(header + directory + payload)


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    banner = clean_banner(SRC_BANNER)
    # native 1118x627 ≈ 16:9; emit README 1280x720 + OG 1280x640
    b1280 = banner.resize((1280, 720), Image.Resampling.LANCZOS)
    b1280.save(ASSETS / "banner.jpg", "JPEG", quality=93, optimize=True, progressive=True)
    og = banner.resize((1280, 640), Image.Resampling.LANCZOS)
    og.save(ASSETS / "og.jpg", "JPEG", quality=91, optimize=True)

    logo = clean_logo(SRC_LOGO)
    logo.save(ROOT / "logo.png", "PNG", optimize=True)
    ui_logo = logo.resize((256, 256), Image.Resampling.LANCZOS)
    ui_logo.save(UI / "wwwroot" / "logo.png", "PNG", optimize=True)

    ico_dir = UI / "Assets"
    ico_dir.mkdir(parents=True, exist_ok=True)
    save_ico(logo, ico_dir / "app.ico")
    save_ico(logo, ROOT / "logo.ico")
    save_ico(logo, ROOT / "logo-app.ico", sizes=(16, 24, 32, 48, 64, 256))

    print("banner.jpg", (ASSETS / "banner.jpg").stat().st_size)
    print("og.jpg", (ASSETS / "og.jpg").stat().st_size)
    print("logo.png", (ROOT / "logo.png").stat().st_size)
    print("wwwroot/logo.png", (UI / "wwwroot" / "logo.png").stat().st_size)
    print("app.ico", (ico_dir / "app.ico").stat().st_size)


if __name__ == "__main__":
    main()
