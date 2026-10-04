"""Minimal Wwise .pck (AKPK) reader shared by the tools."""
import struct


def parse(path):
    """Returns (file, version, banks, streams, externals); entries are dicts id/size/off/lang."""
    f = open(path, "rb")
    h = f.read(28)
    if h[:4] != b"AKPK":
        raise ValueError("not an AKPK file")
    hsize, ver, lang_sz, bnk_sz, stm_sz, ext_sz = struct.unpack_from("<6I", h, 4)
    hdr = f.read(hsize - 20)

    def table(o, wide):
        n = struct.unpack_from("<I", hdr, o)[0]
        o += 4
        out = []
        for _ in range(n):
            if wide:
                fid, blk, sz, off, lang = struct.unpack_from("<QIIII", hdr, o)
                o += 24
            else:
                fid, blk, sz, off, lang = struct.unpack_from("<5I", hdr, o)
                o += 20
            out.append(dict(id=fid, size=sz, off=off * blk, lang=lang))
        return out

    o = lang_sz
    banks = table(o, False); o += bnk_sz
    streams = table(o, False); o += stm_sz
    ext = table(o, True) if ext_sz else []
    return f, ver, banks, streams, ext


def read_ids(path):
    return [e["id"] for e in parse(path)[4]]
