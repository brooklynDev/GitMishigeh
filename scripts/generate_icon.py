#!/usr/bin/env python3

import math
import os
import struct
import subprocess
import zlib
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "GitMishigeh" / "Assets"


def clamp(value, low=0.0, high=1.0):
    return max(low, min(high, value))


def smoothstep(edge0, edge1, x):
    if edge0 == edge1:
        return 0.0 if x < edge0 else 1.0
    t = clamp((x - edge0) / (edge1 - edge0))
    return t * t * (3.0 - 2.0 * t)


def mix(a, b, t):
    return tuple(a[i] * (1.0 - t) + b[i] * t for i in range(4))


def alpha_blend(dst, src):
    sr, sg, sb, sa = src
    dr, dg, db, da = dst
    out_a = sa + da * (1.0 - sa)
    if out_a <= 1e-6:
        return (0.0, 0.0, 0.0, 0.0)

    out_r = (sr * sa + dr * da * (1.0 - sa)) / out_a
    out_g = (sg * sa + dg * da * (1.0 - sa)) / out_a
    out_b = (sb * sa + db * da * (1.0 - sa)) / out_a
    return (out_r, out_g, out_b, out_a)


def rounded_rect_alpha(x, y, left, top, right, bottom, radius, aa):
    cx = clamp(x, left + radius, right - radius)
    cy = clamp(y, top + radius, bottom - radius)
    distance = math.hypot(x - cx, y - cy) - radius
    return 1.0 - smoothstep(-aa, aa, distance)


def segment_distance(px, py, ax, ay, bx, by):
    abx = bx - ax
    aby = by - ay
    apx = px - ax
    apy = py - ay
    denom = abx * abx + aby * aby
    t = 0.0 if denom == 0.0 else clamp((apx * abx + apy * aby) / denom)
    qx = ax + abx * t
    qy = ay + aby * t
    return math.hypot(px - qx, py - qy)


def stroke_alpha(distance, radius, aa):
    return 1.0 - smoothstep(radius - aa, radius + aa, distance)


def ellipse_ring_alpha(x, y, cx, cy, rx, ry, thickness, angle, aa):
    s = math.sin(angle)
    c = math.cos(angle)
    dx = x - cx
    dy = y - cy
    xr = dx * c + dy * s
    yr = -dx * s + dy * c
    q = math.sqrt((xr * xr) / (rx * rx) + (yr * yr) / (ry * ry))
    return 1.0 - smoothstep((thickness / max(rx, ry)) - aa, (thickness / max(rx, ry)) + aa, abs(q - 1.0))


def star_alpha(x, y, cx, cy, size, angle, aa):
    dx = x - cx
    dy = y - cy
    s = math.sin(angle)
    c = math.cos(angle)
    xr = dx * c + dy * s
    yr = -dx * s + dy * c
    d1 = max(abs(xr), abs(yr * 2.8))
    d2 = max(abs(yr), abs(xr * 2.8))
    return max(
        1.0 - smoothstep(size - aa, size + aa, d1),
        1.0 - smoothstep(size - aa, size + aa, d2),
    )


def render_icon(size):
    aa = 1.5 / size
    pixels = [(0.0, 0.0, 0.0, 0.0)] * (size * size)

    bg_a = (8 / 255, 13 / 255, 25 / 255, 1.0)
    bg_b = (24 / 255, 33 / 255, 53 / 255, 1.0)
    bg_c = (43 / 255, 18 / 255, 58 / 255, 1.0)
    border = (79 / 255, 103 / 255, 145 / 255, 0.45)
    orange = (1.0, 132 / 255, 72 / 255, 1.0)
    gold = (1.0, 187 / 255, 92 / 255, 1.0)
    cyan = (110 / 255, 231 / 255, 255 / 255, 1.0)
    cyan_soft = (110 / 255, 231 / 255, 255 / 255, 0.26)
    white = (248 / 255, 250 / 255, 252 / 255, 1.0)

    nodes = [
        (0.28, 0.78),
        (0.41, 0.58),
        (0.69, 0.33),
        (0.72, 0.68),
        (0.25, 0.34),
    ]
    branches = [(0, 1), (1, 2), (1, 3), (1, 4)]

    for py in range(size):
        y = (py + 0.5) / size
        for px in range(size):
            x = (px + 0.5) / size
            index = py * size + px
            pixel = (0.0, 0.0, 0.0, 0.0)

            panel_alpha = rounded_rect_alpha(x, y, 0.08, 0.08, 0.92, 0.92, 0.19, aa)
            if panel_alpha > 0.0:
                linear = mix(bg_a, bg_b, clamp((x * 0.55) + (y * 0.45)))
                radial = mix(linear, bg_c, clamp(1.15 - math.hypot(x - 0.32, y - 0.24) * 1.6))
                pixel = alpha_blend(pixel, (radial[0], radial[1], radial[2], panel_alpha))

                border_outer = rounded_rect_alpha(x, y, 0.08, 0.08, 0.92, 0.92, 0.19, aa)
                border_inner = rounded_rect_alpha(x, y, 0.095, 0.095, 0.905, 0.905, 0.175, aa)
                border_alpha = clamp(border_outer - border_inner)
                if border_alpha > 0.0:
                    pixel = alpha_blend(pixel, (border[0], border[1], border[2], border_alpha))

            glow = 0.0
            for ax, ay, bx, by in [(nodes[a][0], nodes[a][1], nodes[b][0], nodes[b][1]) for a, b in branches]:
                glow = max(glow, stroke_alpha(segment_distance(x, y, ax, ay, bx, by), 0.09, aa * 2.2))
            if glow > 0.0:
                pixel = alpha_blend(pixel, (orange[0], orange[1], orange[2], glow * 0.18))

            for ax, ay, bx, by in [(nodes[a][0], nodes[a][1], nodes[b][0], nodes[b][1]) for a, b in branches]:
                distance = segment_distance(x, y, ax, ay, bx, by)
                alpha = stroke_alpha(distance, 0.046, aa * 1.6)
                if alpha > 0.0:
                    branch_tint = mix(orange, gold, clamp((1.0 - y) * 0.85 + (x * 0.15)))
                    pixel = alpha_blend(pixel, (branch_tint[0], branch_tint[1], branch_tint[2], alpha))

            for nx, ny in nodes:
                node_glow = stroke_alpha(math.hypot(x - nx, y - ny), 0.085, aa * 2.3)
                if node_glow > 0.0:
                    pixel = alpha_blend(pixel, (orange[0], orange[1], orange[2], node_glow * 0.22))

                node_alpha = stroke_alpha(math.hypot(x - nx, y - ny), 0.062, aa * 1.5)
                if node_alpha > 0.0:
                    node_color = mix(orange, gold, clamp(1.0 - ny))
                    pixel = alpha_blend(pixel, (node_color[0], node_color[1], node_color[2], node_alpha))

                highlight_alpha = stroke_alpha(math.hypot(x - (nx - 0.018), y - (ny - 0.02)), 0.016, aa)
                if highlight_alpha > 0.0:
                    pixel = alpha_blend(pixel, (white[0], white[1], white[2], highlight_alpha * 0.55))

            orbit = ellipse_ring_alpha(x, y, 0.47, 0.48, 0.15, 0.095, 0.025, -0.55, aa * 1.5)
            orbit2 = ellipse_ring_alpha(x, y, 0.49, 0.46, 0.16, 0.085, 0.014, -0.4, aa * 1.5)
            if orbit > 0.0:
                pixel = alpha_blend(pixel, (cyan[0], cyan[1], cyan[2], orbit * 0.65))
            if orbit2 > 0.0:
                pixel = alpha_blend(pixel, (cyan_soft[0], cyan_soft[1], cyan_soft[2], orbit2))

            for sx, sy, ss, sa in [(0.79, 0.24, 0.028, 0.85), (0.22, 0.22, 0.022, 0.72), (0.82, 0.57, 0.018, 0.58)]:
                spark = star_alpha(x, y, sx, sy, ss, 0.32, aa * 1.3)
                if spark > 0.0:
                    pixel = alpha_blend(pixel, (cyan[0], cyan[1], cyan[2], spark * sa))

            pixels[index] = pixel

    return pixels


def encode_png(size, pixels):
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        for x in range(size):
            r, g, b, a = pixels[y * size + x]
            raw.extend(
                (
                    int(clamp(r) * 255 + 0.5),
                    int(clamp(g) * 255 + 0.5),
                    int(clamp(b) * 255 + 0.5),
                    int(clamp(a) * 255 + 0.5),
                )
            )

    def chunk(tag, data):
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    png = bytearray(b"\x89PNG\r\n\x1a\n")
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    return bytes(png)


def write_icon_files():
    ASSETS.mkdir(parents=True, exist_ok=True)

    master_size = 1024
    master_png = ASSETS / "git-mishigeh-1024.png"
    master_png.write_bytes(encode_png(master_size, render_icon(master_size)))

    for size in (512, 256, 128, 64, 48, 32, 24, 16):
        subprocess.run(
            [
                "sips",
                "-z",
                str(size),
                str(size),
                str(master_png),
                "--out",
                str(ASSETS / f"git-mishigeh-{size}.png"),
            ],
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )

    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    ico_images = []
    for size in ico_sizes:
        ico_images.append((size, (ASSETS / f"git-mishigeh-{size}.png").read_bytes()))

    header = struct.pack("<HHH", 0, 1, len(ico_images))
    directory = bytearray()
    offset = 6 + (16 * len(ico_images))
    image_data = bytearray()

    for size, image in ico_images:
        width = 0 if size == 256 else size
        height = 0 if size == 256 else size
        directory += struct.pack("<BBBBHHII", width, height, 0, 0, 1, 32, len(image), offset)
        image_data += image
        offset += len(image)

    (ASSETS / "git-mishigeh.ico").write_bytes(header + directory + image_data)


if __name__ == "__main__":
    write_icon_files()
