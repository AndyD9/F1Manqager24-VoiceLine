"""Inspect / extract a Wwise .pck (AKPK) file.

Usage:
  akpk.py info    <file.pck>
  akpk.py extract <file.pck> <outdir> [max]
"""
import os, struct, sys


def parse(path):
    f = open(path, "rb"); h = f.read(28)
    assert h[:4] == b"AKPK", "not an AKPK file"
    hsize, ver, lang_sz, bnk_sz, stm_sz, ext_sz = struct.unpack_from("<6I", h, 4)
    f.seek(28); hdr = f.read(hsize - 20)
    o = lang_sz
    def table(o, size, wide):
        n = struct.unpack_from("<I", hdr, o)[0]; o += 4; out = []
        for _ in range(n):
            if wide:
                fid, blk, sz, off, lang = struct.unpack_from("<QIIII", hdr, o); o += 24
            else:
                fid, blk, sz, off, lang = struct.unpack_from("<5I", hdr, o); o += 20
            out.append(dict(id=fid, size=sz, off=off * blk, lang=lang))
        return out
    banks = table(o, bnk_sz, False); o += bnk_sz
    streams = table(o, stm_sz, False); o += stm_sz
    ext = table(o, ext_sz, True) if ext_sz else []
    return f, ver, banks, streams, ext


def main():
    cmd, path = sys.argv[1], sys.argv[2]
    f, ver, banks, streams, ext = parse(path)
    print(f"AKPK v{ver}: {len(banks)} banks, {len(streams)} streamed wem, {len(ext)} external")
    if cmd == "info":
        tot = sum(s["size"] for s in streams)
        print(f"wem total {tot/1e6:.1f} MB, avg {tot/max(1,len(streams))/1e3:.1f} KB")
        for b in banks[:20]:
            print(f"  bank id={b['id']} size={b['size']}")
    elif cmd == "extract":
        out = sys.argv[3]; mx = int(sys.argv[4]) if len(sys.argv) > 4 else 10**9
        os.makedirs(out, exist_ok=True)
        for kind, items, ext_ in (("bnk", banks, "bnk"), ("wem", streams, "wem"), ("ext", ext, "wem")):
            for it in items[:mx]:
                f.seek(it["off"]); open(os.path.join(out, f"{it['id']}.{ext_}"), "wb").write(f.read(it["size"]))


main()
