"""Extract radio line subtitles from Volta.locres (namespaces DialogueScript_*).

Usage: locres_dialogue.py <Volta.locres> <out.json>
Output: { "LHamilton": { "013046_DR_LH": "text", ... }, ... }
"""
import json
import struct
import sys

MAGIC = bytes.fromhex("0E14747567 4A03FC 4A15909DC3377F1B".replace(" ", ""))


class R:
    def __init__(self, b):
        self.b, self.o = b, 0

    def u(self, fmt):
        v = struct.unpack_from("<" + fmt, self.b, self.o)
        self.o += struct.calcsize("<" + fmt)
        return v[0]

    def fstr(self):
        n = self.u("i")
        if n == 0:
            return ""
        if n < 0:
            s = self.b[self.o:self.o - 2 * n].decode("utf-16-le")
            self.o -= 2 * n
        else:
            s = self.b[self.o:self.o + n].decode("latin-1")
            self.o += n
        return s.rstrip("\0")


def read_locres(path):
    r = R(open(path, "rb").read())
    if r.b[:16] != MAGIC:
        raise SystemExit("unsupported locres (no magic)")
    r.o = 16
    ver = r.u("B")
    str_off = r.u("q")
    if ver >= 2:
        r.u("I")  # entries count
    save = r.o
    r.o = str_off
    strings = []
    for _ in range(r.u("i")):
        strings.append(r.fstr())
        if ver >= 2:
            r.u("i")  # ref count
    r.o = save
    out = {}
    for _ in range(r.u("I")):
        if ver >= 2:
            r.u("I")
        ns = r.fstr()
        keys = {}
        for _ in range(r.u("I")):
            if ver >= 2:
                r.u("I")
            key = r.fstr()
            r.u("I")  # source hash
            keys[key] = strings[r.u("i")]
        out[ns] = keys
    return out


def main():
    data = read_locres(sys.argv[1])
    lines = {}
    for ns, keys in data.items():
        if not ns.startswith("DialogueScript_"):
            continue
        speaker = ns[len("DialogueScript_"):]
        lines[speaker] = {k[:-len("_Subtitle")]: v for k, v in keys.items() if k.endswith("_Subtitle")}
    json.dump(lines, open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False, indent=1, sort_keys=True)
    total = sum(len(v) for v in lines.values())
    print(f"{len(lines)} speakers, {total} lines")
    for s, v in sorted(lines.items(), key=lambda kv: -len(kv[1])):
        tags = sorted({k.split("_", 1)[1] if "_" in k else "?" for k in v})
        print(f"  {s:<22} {len(v):>5}  {','.join(tags)[:60]}")


if __name__ == "__main__":
    main()
