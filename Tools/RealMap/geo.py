"""Hilfen fuer echte Karten: Koordinaten (WGS84 <-> UTM 32N), TIFF lesen (float32, unkomprimiert/Deflate/LZW)."""
import math, struct, zlib

def latlon_to_utm32(lat, lon):
    """WGS84 -> ETRS89/UTM Zone 32N (EPSG:25832), Meter."""
    a, f = 6378137.0, 1 / 298.257223563
    k0, lon0 = 0.9996, math.radians(9.0)
    e2 = f * (2 - f); ep2 = e2 / (1 - e2)
    phi, lam = math.radians(lat), math.radians(lon)
    N = a / math.sqrt(1 - e2 * math.sin(phi) ** 2)
    T = math.tan(phi) ** 2; C = ep2 * math.cos(phi) ** 2; A = math.cos(phi) * (lam - lon0)
    M = a * ((1 - e2 / 4 - 3 * e2 ** 2 / 64 - 5 * e2 ** 3 / 256) * phi - (3 * e2 / 8 + 3 * e2 ** 2 / 32 + 45 * e2 ** 3 / 1024) * math.sin(2 * phi)
             + (15 * e2 ** 2 / 256 + 45 * e2 ** 3 / 1024) * math.sin(4 * phi) - (35 * e2 ** 3 / 3072) * math.sin(6 * phi))
    x = k0 * N * (A + (1 - T + C) * A ** 3 / 6 + (5 - 18 * T + T * T + 72 * C - 58 * ep2) * A ** 5 / 120) + 500000.0
    y = k0 * (M + N * math.tan(phi) * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * A ** 4 / 24 + (61 - 58 * T + T * T + 600 * C - 330 * ep2) * A ** 6 / 720))
    return x, y

def _lzw_decode(data):
    out = bytearray(); table = [bytes([i]) for i in range(256)] + [b"", b""]
    bits, nbits, val, prev = 0, 9, 0, None
    pos = 0
    total = len(data) * 8
    while pos + nbits <= total:
        byte_i = pos >> 3
        chunk = int.from_bytes(data[byte_i:byte_i + 4].ljust(4, b"\0"), "big")
        code = (chunk >> (32 - (pos & 7) - nbits)) & ((1 << nbits) - 1)
        pos += nbits
        if code == 256: table = table[:258]; nbits = 9; prev = None; continue
        if code == 257: break
        if prev is None: entry = table[code]; out += entry; prev = entry; continue
        entry = table[code] if code < len(table) else prev + prev[:1]
        out += entry
        table.append(prev + entry[:1]); prev = entry
        if len(table) + 1 >= (1 << nbits) and nbits < 12: nbits += 1
    return bytes(out)

def read_tiff_float(path):
    """Einfacher TIFF-Leser fuer ein Band float32 (oder int16/uint16). Gibt (breite, hoehe, liste) zurueck."""
    d = open(path, "rb").read()
    le = d[:2] == b"II"; E = "<" if le else ">"
    off = struct.unpack(E + "I", d[4:8])[0]
    n = struct.unpack(E + "H", d[off:off + 2])[0]
    tags = {}
    for i in range(n):
        tag, typ, cnt, val = struct.unpack(E + "HHI4s", d[off + 2 + i * 12: off + 14 + i * 12])
        size = {1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 11: 4, 12: 8, 16: 8}.get(typ, 1)
        fmt = {1: "B", 2: "c", 3: "H", 4: "I", 11: "f", 12: "d", 16: "Q"}.get(typ, "B")
        raw = val if size * cnt <= 4 else d[struct.unpack(E + "I", val)[0]: struct.unpack(E + "I", val)[0] + size * cnt]
        if typ == 5: tags[tag] = raw; continue
        tags[tag] = list(struct.unpack(E + fmt * cnt, raw[:size * cnt]))
    W, H = tags[256][0], tags[257][0]
    comp = tags.get(259, [1])[0]; pred = tags.get(317, [1])[0]
    bps = tags.get(258, [32])[0]; sfmt = tags.get(339, [3])[0]
    vfmt = {(32, 3): "f", (16, 2): "h", (16, 1): "H", (32, 2): "i"}[(bps, sfmt)]
    bpp = bps // 8
    out = [0.0] * (W * H)
    def unpack(buf, w, h):
        if comp == 8 or comp == 32946: buf = zlib.decompress(buf)
        elif comp == 5: buf = _lzw_decode(buf)
        if pred == 3:   # Gleitkomma-Praediktor
            row = w * bpp; b = bytearray(buf)
            res = bytearray(len(b))
            for y in range(h):
                r = b[y * row:(y + 1) * row]
                for i in range(1, row): r[i] = (r[i] + r[i - 1]) & 255
                for x in range(w):
                    for k in range(bpp):
                        res[y * row + x * bpp + (k if not le else bpp - 1 - k)] = r[k * w + x]
            return struct.unpack((">" if True else E) + vfmt * (w * h), bytes(res[:w * h * bpp])) if not le else struct.unpack("<" + vfmt * (w * h), bytes(res[:w * h * bpp]))
        if pred == 2:
            vals = list(struct.unpack(E + vfmt * (len(buf) // bpp), buf[:len(buf) // bpp * bpp]))
            for y in range(h):
                for x in range(1, w): vals[y * w + x] += vals[y * w + x - 1]
            return vals
        return struct.unpack(E + vfmt * (len(buf) // bpp), buf[:len(buf) // bpp * bpp])
    if 322 in tags:   # Kacheln
        tw, th = tags[322][0], tags[323][0]
        offs, cnts = tags[324], tags[325]
        across = (W + tw - 1) // tw
        for t, (o, c) in enumerate(zip(offs, cnts)):
            vals = unpack(d[o:o + c], tw, th)
            tx, ty = (t % across) * tw, (t // across) * th
            for y in range(th):
                if ty + y >= H: break
                for x in range(tw):
                    if tx + x < W: out[(ty + y) * W + tx + x] = vals[y * tw + x]
    else:   # Streifen
        offs, cnts = tags[273], tags[279]
        rps = tags.get(278, [H])[0]
        for s, (o, c) in enumerate(zip(offs, cnts)):
            rows = min(rps, H - s * rps)
            vals = unpack(d[o:o + c], W, rows)
            for i in range(min(len(vals), rows * W)): out[s * rps * W + i] = vals[i]
    return W, H, out, comp
