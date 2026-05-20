"""
Stdlib-only icons for Snapdesk: multi-size ``.ico`` + PPM for ``PhotoImage``.

``ensure_app_assets`` writes into ``assets/`` when files are missing (first run).
Regenerate explicitly with ``python scripts/build_app_assets.py``.
"""

from __future__ import annotations

import struct
from pathlib import Path


def _blend(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def _draw_motif_rgba(size: int, *, bg_rgb: tuple[int, int, int]) -> bytes:
    """Top-down RGBA8888 (row major, origin top-left)."""
    w = h = size
    px = bytearray(w * h * 4)
    br, bg, bb = bg_rgb

    def set_px(x: int, y: int, r: int, g: int, b: int, a: int = 255) -> None:
        if 0 <= x < w and 0 <= y < h:
            j = (y * w + x) * 4
            px[j : j + 4] = bytes((r, g, b, a))

    for y in range(h):
        for x in range(w):
            set_px(x, y, br, bg, bb, 255)

    primary = (0x1A, 0x73, 0xE8)
    deep = (0x08, 0x4B, 0xA0)
    face_hi = _blend(deep, (0xE8, 0xF0, 0xFF), 0.55)
    face_mid = _blend(deep, primary, 0.45)
    face_lo = _blend(deep, (0x02, 0x06, 0x12), 0.35)
    outline = (0xD0, 0xE4, 0xFF)

    def win(layer: int) -> None:
        o = layer * max(1, size // 10)
        x0 = 2 + o
        y0 = 3 + o
        x1 = size - 3 - o // 2
        y1 = size - 2 - o
        if x1 <= x0 + 2 or y1 <= y0 + 4:
            return
        tb_h = max(2, (y1 - y0) // 5)
        for yy in range(y0, y1 + 1):
            for xx in range(x0, x1 + 1):
                edge = xx <= x0 or xx >= x1 or yy <= y0 or yy >= y1
                if not edge:
                    continue
                set_px(xx, yy, *outline, 255)
        for yy in range(y0 + 1, min(y0 + tb_h, y1)):
            for xx in range(x0 + 1, x1):
                set_px(xx, yy, *face_lo, 255)
        for yy in range(y0 + tb_h, y1):
            for xx in range(x0 + 1, x1):
                t = (xx - x0) / max(1, (x1 - x0))
                u = (yy - (y0 + tb_h)) / max(1, (y1 - (y0 + tb_h)))
                c = _blend(face_mid, face_hi, 0.35 + 0.35 * t + 0.25 * u)
                set_px(xx, yy, int(c[0]), int(c[1]), int(c[2]), 255)

    for L in (2, 1, 0):
        win(L)

    return bytes(px)


def _rgba_top_to_bgra_bottom(rgba: bytes, w: int, h: int) -> bytes:
    out = bytearray(w * h * 4)
    for y in range(h):
        src_row = y * w * 4
        dst_row = (h - 1 - y) * w * 4
        for x in range(w):
            si = src_row + x * 4
            di = dst_row + x * 4
            r, g, b, a = rgba[si : si + 4]
            out[di : di + 4] = bytes((b, g, r, a))
    return bytes(out)


def _dib32_icon(size: int, rgba_top: bytes) -> bytes:
    w = h = size
    bgra = _rgba_top_to_bgra_bottom(rgba_top, w, h)
    row_b = ((w + 31) // 32) * 4
    and_mask = bytes(row_b * h)
    header = struct.pack(
        "<IIIHHIIIIII",
        40,
        w,
        h * 2,
        1,
        32,
        0,
        len(bgra),
        0,
        0,
        0,
        0,
    )
    return header + bgra + and_mask


def _build_multi_ico(sizes: tuple[int, ...]) -> bytes:
    images: list[bytes] = []
    for sz in sizes:
        rgba = _draw_motif_rgba(sz, bg_rgb=(0x12, 0x18, 0x22))
        images.append(_dib32_icon(sz, rgba))
    count = len(images)
    hdr = struct.pack("<HHH", 0, 1, count)
    dir_size = 6 + count * 16
    offset = dir_size
    entries = bytearray()
    blobs = bytearray()
    for sz, blob in zip(sizes, images, strict=True):
        wbyte = sz if sz < 256 else 0
        hbyte = sz if sz < 256 else 0
        entries.extend(
            struct.pack(
                "<BBBBHHII",
                wbyte,
                hbyte,
                0,
                0,
                1,
                32,
                len(blob),
                offset,
            )
        )
        blobs.extend(blob)
        offset += len(blob)
    return hdr + bytes(entries) + bytes(blobs)


def _write_ppm(path: Path, rgba: bytes, w: int, h: int) -> None:
    rgb = bytearray(w * h * 3)
    di = 0
    for y in range(h):
        for x in range(w):
            si = (y * w + x) * 4
            r, g, b, _a = rgba[si : si + 4]
            rgb[di] = r
            rgb[di + 1] = g
            rgb[di + 2] = b
            di += 3
    header = f"P6\n{w} {h}\n255\n".encode("ascii")
    path.write_bytes(header + bytes(rgb))


def build_all(assets_dir: Path, *, ppm_size: int = 64) -> None:
    """Write or overwrite ``app_icon.ico`` and PPM photo variants."""
    assets_dir.mkdir(parents=True, exist_ok=True)
    dark_bg = (0x2D, 0x2D, 0x2D)
    light_bg = (0xF6, 0xF7, 0xF9)
    rgba_d = _draw_motif_rgba(ppm_size, bg_rgb=dark_bg)
    rgba_l = _draw_motif_rgba(ppm_size, bg_rgb=light_bg)
    _write_ppm(assets_dir / "app_icon_dark.ppm", rgba_d, ppm_size, ppm_size)
    _write_ppm(assets_dir / "app_icon_light.ppm", rgba_l, ppm_size, ppm_size)
    (assets_dir / "app_icon.ico").write_bytes(_build_multi_ico((16, 32)))


def ensure_app_assets(assets_dir: Path) -> None:
    """Create ``assets/`` icons on first launch if they are not present."""
    need = ("app_icon.ico", "app_icon_dark.ppm", "app_icon_light.ppm")
    if all((assets_dir / n).is_file() for n in need):
        return
    build_all(assets_dir)


if __name__ == "__main__":
    build_all(Path(__file__).resolve().parents[1] / "assets")
