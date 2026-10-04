"""Build a recording script for a custom voice pack based on a donor voice.

Every donor line (e.g. 150048_EN_AB) must exist in the pack. Lines sharing the
same text are recorded once: the script lists unique texts, and lines.txt maps
each game line to the recorded file.

Usage:
  voicepack_script.py <dialogue_en.json> <DonorFolder> <outdir> [--rename Zhou=Lewis ...]

Writes:
  <outdir>/script.csv   file;text;uses   (one row per sentence to record, UTF-8)
  <outdir>/lines.txt    <line>=<file>     (pack alias map read by F1VoiceMapper)
"""
import csv
import json
import os
import re
import sys


def norm(text):
    t = text.replace("’", "'").replace("�", "'")
    return re.sub(r"\s+", " ", t).strip()


def key(text):
    return re.sub(r"[^a-z0-9]+", " ", text.lower()).strip()


def main():
    args = sys.argv[1:]
    renames = []
    if "--rename" in args:
        i = args.index("--rename")
        renames = [r.split("=", 1) for r in args[i + 1:]]
        args = args[:i]
    src, donor, out = args
    lines = json.load(open(src, encoding="utf-8"))[donor]
    os.makedirs(out, exist_ok=True)

    groups = {}  # normalized key -> [display text, [line ids]]
    for line_id in sorted(lines):
        text = norm(lines[line_id])
        for old, new in renames:
            text = re.sub(rf"\b{re.escape(old)}\b", new, text)
        g = groups.setdefault(key(text), [text, []])
        g[1].append(line_id)

    ordered = sorted(groups.values(), key=lambda g: (-len(g[1]), g[0].lower()))
    with open(os.path.join(out, "script.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["file", "text", "uses"])
        with open(os.path.join(out, "lines.txt"), "w", encoding="utf-8") as m:
            m.write(f"# Voice pack based on {donor}: <game line>=<recorded file>\n")
            for n, (text, ids) in enumerate(ordered, 1):
                name = f"{n:04d}.wem"
                w.writerow([f"{n:04d}.wav", text, len(ids)])
                for line_id in ids:
                    m.write(f"{line_id}.wem={name}\n")
    print(f"{donor}: {len(lines)} game lines -> {len(ordered)} sentences to record ({out})")


if __name__ == "__main__":
    main()
