"""List files in an Unreal .pak (legacy index, v3..v11). Usage: pak_list.py file.pak [filter]"""
import struct, sys, os

MAGIC = 0x5A6F12E1


def rstr(b, o):
    n = struct.unpack_from("<i", b, o)[0]; o += 4
    if n == 0:
        return "", o
    if n < 0:
        s = b[o:o - n * 2].decode("utf-16-le").rstrip("\0"); return s, o - n * 2
    return b[o:o + n].decode("latin-1").rstrip("\0"), o + n


def footer(f):
    size = os.fstat(f.fileno()).st_size
    for flen in (221, 205, 189, 61, 53, 45, 44):  # various footer sizes
        if size < flen:
            continue
        f.seek(size - flen); d = f.read(flen)
        for off in range(0, flen - 4):
            if struct.unpack_from("<I", d, off)[0] == MAGIC:
                ver, idx_off, idx_size = struct.unpack_from("<iqq", d, off + 4)
                enc = d[off - 1] if off >= 1 else 0
                if 1 <= ver <= 12 and 0 < idx_off < size:
                    return ver, idx_off, idx_size, enc
    raise SystemExit("footer not found")


def main():
    path = sys.argv[1]; flt = sys.argv[2].lower() if len(sys.argv) > 2 else ""
    with open(path, "rb") as f:
        ver, io, isz, enc = footer(f)
        print(f"version={ver} index_off={io} index_size={isz} encrypted_index={enc}")
        if enc:
            return
        f.seek(io); b = f.read(isz)
    mount, o = rstr(b, 0)
    count = struct.unpack_from("<i", b, o)[0]; o += 4
    print(f"mount={mount} entries={count}")
    if ver >= 10:
        print("v10+ path hash index: use repak/FModel"); return
    for _ in range(count):
        name, o = rstr(b, o)
        off, csize, usize, comp = struct.unpack_from("<qqqi", b, o); o += 28
        o += 20  # sha1
        if comp:
            nb = struct.unpack_from("<i", b, o)[0]; o += 4 + nb * 16
        if ver >= 3:
            o += 5  # encrypted flag + block size
        if flt in name.lower():
            print(f"{usize:>12} {name}")


main()
