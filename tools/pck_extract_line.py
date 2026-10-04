"""Extract one radio line from a TeamComms .pck by its game path.

Usage: pck_extract_line.py <pck> "TeamComms\\Drivers\\CLeclerc\\124144_DR_CL.wem" <out.wem>
"""
import sys

from akpk_lib import parse
from teamcomms_index import fnv_prefix


def main():
    f, _, _, _, ext = parse(sys.argv[1])
    want = fnv_prefix(sys.argv[2].lower())
    for e in ext:
        if e["id"] == want:
            f.seek(e["off"])
            open(sys.argv[3], "wb").write(f.read(e["size"]))
            print(f"{sys.argv[3]}: {e['size']} bytes")
            return
    raise SystemExit("line not found in pck")


if __name__ == "__main__":
    main()
