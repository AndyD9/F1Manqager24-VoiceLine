"""Read UE PAK v11 (encrypted index) - list and extract files.

Usage:
  pak11.py list    <pak> [filter]
  pak11.py extract <pak> <filter> <outdir>
Key: env F1M_AES (hex) or default F1 Manager community key.
"""
import os, struct, sys, zlib
from Crypto.Cipher import AES

KEY = bytes.fromhex(os.environ.get("F1M_AES",
    "43A5CEC244E89A3E109E14EDE787569E79C9CDECBB09857C6FEF080F14898EF9"))
MAGIC = 0x5A6F12E1


def dec(data):
    return AES.new(KEY, AES.MODE_ECB).decrypt(data)


class R:
    def __init__(s, b, o=0): s.b, s.o = b, o
    def u(s, fmt):
        v = struct.unpack_from("<" + fmt, s.b, s.o); s.o += struct.calcsize("<" + fmt)
        return v if len(v) > 1 else v[0]
    def raw(s, n): v = s.b[s.o:s.o + n]; s.o += n; return v
    def fstr(s):
        n = s.u("i")
        if n == 0: return ""
        if n < 0: return s.raw(-n * 2).decode("utf-16-le").rstrip("\0")
        return s.raw(n).decode("utf-8", "replace").rstrip("\0")


_oo = None


def oodle(raw, rawlen):
    """Decompress with a locally installed oo2core_9_win64.dll (env OODLE_DLL)."""
    global _oo
    import ctypes
    if _oo is None:
        _oo = ctypes.WinDLL(os.environ.get("OODLE_DLL", "oo2core_9_win64.dll"))
        _oo.OodleLZ_Decompress.restype = ctypes.c_ssize_t
        _oo.OodleLZ_Decompress.argtypes = [ctypes.c_char_p, ctypes.c_ssize_t, ctypes.c_void_p,
            ctypes.c_ssize_t] + [ctypes.c_int] * 3 + [ctypes.c_void_p, ctypes.c_ssize_t,
            ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ssize_t, ctypes.c_int]
    buf = ctypes.create_string_buffer(rawlen)
    n = _oo.OodleLZ_Decompress(raw, len(raw), buf, rawlen, 1, 0, 0, None, 0, None, None, None, 0, 3)
    if n != rawlen:
        raise SystemExit(f"oodle failed ({n} != {rawlen})")
    return buf.raw


def read_aligned(f, off, size):
    f.seek(off); return dec(f.read((size + 15) & ~15))[:size]


def open_pak(path):
    f = open(path, "rb"); size = os.fstat(f.fileno()).st_size
    f.seek(size - 221); d = f.read(221)
    p = d.find(struct.pack("<I", MAGIC))
    enc = d[p - 1]; ver, io, isz = struct.unpack_from("<iqq", d, p + 4)
    comp_names = [d[p + 44 + i * 32:p + 44 + (i + 1) * 32].split(b"\0")[0].decode() for i in range(5)]
    idx = R(read_aligned(f, io, isz) if enc else (f.seek(io), f.read(isz))[1])
    mount = idx.fstr()
    if not mount.startswith("../"):
        raise SystemExit("bad AES key (index did not decrypt)")
    n = idx.u("i"); idx.u("Q")
    if idx.u("I"): idx.u("qq"); idx.raw(20)
    assert idx.u("I"), "no full directory index"
    fdo, fds = idx.u("qq"); idx.raw(20)
    enc_size = idx.u("i"); encoded = idx.raw(enc_size)
    fdi = R(read_aligned(f, fdo, fds) if enc else (f.seek(fdo), f.read(fds))[1])
    files = []
    for _ in range(fdi.u("i")):
        dname = fdi.fstr()
        for _ in range(fdi.u("i")):
            fname = fdi.fstr(); eo = fdi.u("i")
            files.append((mount + dname + fname, eo))
    return f, mount, encoded, files, comp_names


def entry(encoded, eo):
    r = R(encoded, eo); bits = r.u("I")
    cbs = bits & 0x3F
    if cbs == 0x3F: cbs = r.u("I")
    else: cbs <<= 11
    comp = (bits >> 23) & 0x3F; encr = bool(bits & (1 << 22)); nblk = (bits >> 6) & 0xFFFF
    off = r.u("I") if bits & (1 << 31) else r.u("Q")
    usz = r.u("I") if bits & (1 << 30) else r.u("Q")
    csz = (r.u("I") if bits & (1 << 29) else r.u("Q")) if comp else usz
    blocks = []
    if comp and nblk:
        if nblk == 1 and not encr:
            blocks = [csz]
        else:
            blocks = [r.u("I") for _ in range(nblk)]
    return dict(off=off, usz=usz, csz=csz, comp=comp, encr=encr, blocks=blocks, cbs=cbs)


def header_size(ver_entry_blocks, comp):
    # FPakEntry serialized header before data (v11): offset, csize, usize, comp(u32), sha1, [blocks], flags(u8), cbs(u32)
    return 8 + 8 + 8 + 4 + 20 + (4 + 16 * ver_entry_blocks if comp else 0) + 1 + 4


def read_file(f, e, comp_names):
    hs = header_size(len(e["blocks"]), e["comp"])
    pos = e["off"] + hs
    if not e["comp"]:
        f.seek(pos); data = f.read((e["usz"] + 15) & ~15 if e["encr"] else e["usz"])
        return (dec(data) if e["encr"] else data)[:e["usz"]]
    out = bytearray(); method = comp_names[e["comp"] - 1].lower()
    for bs in e["blocks"]:
        f.seek(pos); raw = f.read((bs + 15) & ~15 if e["encr"] else bs)
        pos += (bs + 15) & ~15 if e["encr"] else bs
        if e["encr"]: raw = dec(raw)[:bs]
        if method == "zlib": out += zlib.decompress(raw)
        elif method == "oodle": out += oodle(raw, min(e["cbs"], e["usz"] - len(out)))
        else: raise SystemExit("compression " + method)
    return bytes(out[:e["usz"]])


def main():
    cmd, path = sys.argv[1], sys.argv[2]
    flt = sys.argv[3].lower() if len(sys.argv) > 3 else ""
    f, mount, encoded, files, comps = open_pak(path)
    if cmd == "list":
        print(f"# mount={mount} files={len(files)} compression={comps}", file=sys.stderr)
        for name, eo in files:
            if flt in name.lower():
                e = entry(encoded, eo)
                print(f"{e['usz']:>11} c{e['comp']} {name}")
    elif cmd == "extract":
        outdir = sys.argv[4]
        for name, eo in files:
            if flt in name.lower():
                data = read_file(f, entry(encoded, eo), comps)
                dst = os.path.join(outdir, name.replace("../", ""))
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                open(dst, "wb").write(data); print(len(data), dst)


main()
