"""Recover radio line names from TeamComms*.pck external-source IDs.

The game plays radio lines as Wwise external sources named
    TeamComms\\<Drivers|Engineers>\\<Folder>\\<NNNNNN>_<DR|EN>_<Tag>.wem
and the .pck stores FNV-1 64 of the lowercased path. For a candidate
(folder, tag) we hash all 1,000,000 line numbers at once and keep hits.

Usage:
  teamcomms_index.py <pck> <Drivers|Engineers> <Folder> <Tag> [<Folder> <Tag> ...]
"""
import sys

import numpy as np

sys.path.insert(0, __file__.rsplit("\\", 1)[0].rsplit("/", 1)[0])
from akpk_lib import read_ids  # noqa: E402

PRIME = np.uint64(0x100000001B3)
BASIS = 0xCBF29CE484222325


def fnv_prefix(text):
    h = BASIS
    for c in text.encode("ascii"):
        h = (h * 0x100000001B3) & 0xFFFFFFFFFFFFFFFF
        h ^= c
    return h


def all_numbers(prefix, suffix):
    """FNV-1 64 of prefix + %06d + suffix for every number, vectorised."""
    n = np.arange(1_000_000, dtype=np.uint64)
    h = np.full(n.shape, np.uint64(fnv_prefix(prefix)), dtype=np.uint64)
    div = 100_000
    for _ in range(6):
        digit = (n // np.uint64(div)) % np.uint64(10) + np.uint64(ord("0"))
        h = (h * PRIME) ^ digit
        div //= 10
    for c in suffix.encode("ascii"):
        h = (h * PRIME) ^ np.uint64(c)
    return h


def find_lines(ids_sorted, role, folder, tag):
    kind = "dr" if role.lower() == "drivers" else "en"
    prefix = f"teamcomms\\{role.lower()}\\{folder.lower()}\\"
    h = all_numbers(prefix, f"_{kind}_{tag.lower()}.wem")
    idx = np.searchsorted(ids_sorted, h)
    idx[idx >= len(ids_sorted)] = 0
    return np.nonzero(ids_sorted[idx] == h)[0]


def main():
    with np.errstate(over="ignore"):
        ids = np.array(sorted(read_ids(sys.argv[1])), dtype=np.uint64)
        role = sys.argv[2]
        args = sys.argv[3:]
        for folder, tag in zip(args[::2], args[1::2]):
            lines = find_lines(ids, role, folder, tag)
            rng = f"{lines.min():06d}..{lines.max():06d}" if len(lines) else "-"
            print(f"{folder:<16} {tag:<6} {len(lines):>5} lines  {rng}")


if __name__ == "__main__":
    main()
