#!/usr/bin/env python3
"""Regénère localconvert.ico à partir des PNG de ce dossier (nécessite Pillow).

Format le plus compatible avec l'Explorateur Windows :
  - 16, 24, 32, 48, 64 et 128 px en bitmap 32 bits (BMP + masque), lus partout ;
  - 256 px en PNG (seule taille où Windows attend du PNG).
Un .ico entièrement en PNG s'affiche mal dans certaines vues de l'Explorateur
(icône générique à la place de la nôtre).

Usage : python3 assets/icon/make-ico.py
"""
import io
import struct
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
SOURCE = HERE / "localconvert-512.png"
SIZES = [16, 24, 32, 48, 64, 128, 256]


def frame(size: int) -> Image.Image:
    exact = HERE / f"localconvert-{size}.png"
    img = Image.open(exact if exact.exists() else SOURCE).convert("RGBA")
    if img.size != (size, size):
        img = img.resize((size, size), Image.LANCZOS)
    return img


def bmp_entry(img: Image.Image) -> bytes:
    w, h = img.size
    # En-tête BITMAPINFOHEADER : hauteur doublée (image XOR + masque AND).
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    px = img.tobytes("raw", "BGRA")
    stride = w * 4
    xor = b"".join(px[y * stride:(y + 1) * stride] for y in range(h - 1, -1, -1))
    mask_stride = ((w + 31) // 32) * 4
    alpha = img.getchannel("A").tobytes()
    mask = bytearray()
    for y in range(h - 1, -1, -1):
        row = bytearray(mask_stride)
        for x in range(w):
            if alpha[y * w + x] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        mask += row
    return header + xor + bytes(mask)


def png_entry(img: Image.Image) -> bytes:
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def main() -> None:
    entries = []
    for size in SIZES:
        img = frame(size)
        entries.append((size, png_entry(img) if size >= 256 else bmp_entry(img)))
    out = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    offset = 6 + 16 * len(entries)
    for size, data in entries:
        dim = 0 if size >= 256 else size
        out += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in entries:
        out += data
    target = HERE / "localconvert.ico"
    target.write_bytes(out)
    print(f"{target} : {len(out)} octets, tailles {SIZES}")


if __name__ == "__main__":
    main()
