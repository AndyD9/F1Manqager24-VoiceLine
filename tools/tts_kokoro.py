"""Generate WAV files with Kokoro (local, offline AI TTS, 24 kHz mono).

Usage:
  tts_kokoro.py --voices                          list available voices
  tts_kokoro.py <list.tsv> [--voice bm_george] [--speed 1.0] [--lang en-gb]
<list.tsv>: UTF-8, one "<out.wav><TAB><text>" per line.
Model files: voicepacks/models/kokoro-v1.0.onnx and voices-v1.0.bin.
"""
import os
import struct
import sys

import numpy as np
from kokoro_onnx import Kokoro

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "voicepacks", "models")


def write_wav(path, samples, rate):
    pcm = (np.clip(samples, -1, 1) * 32767).astype("<i2").tobytes()
    hdr = b"RIFF" + struct.pack("<I", 36 + len(pcm)) + b"WAVEfmt " + struct.pack(
        "<IHHIIHH", 16, 1, 1, rate, rate * 2, 2, 16) + b"data" + struct.pack("<I", len(pcm))
    open(path, "wb").write(hdr + pcm)


def opt(args, name, default):
    if name in args:
        i = args.index(name)
        v = args[i + 1]
        del args[i:i + 2]
        return v
    return default


def main():
    args = sys.argv[1:]
    k = Kokoro(os.path.join(MODELS, "kokoro-v1.0.onnx"), os.path.join(MODELS, "voices-v1.0.bin"))
    if args and args[0] == "--voices":
        print(" ".join(sorted(k.get_voices())))
        return
    voice = opt(args, "--voice", "bm_george")
    speed = float(opt(args, "--speed", "1.0"))
    lang = opt(args, "--lang", "en-gb" if voice.startswith("b") else "en-us")
    rows = [l.rstrip("\n").split("\t", 1) for l in open(args[0], encoding="utf-8") if "\t" in l]
    for n, (out, text) in enumerate(rows, 1):
        samples, rate = k.create(text, voice=voice, speed=speed, lang=lang)
        write_wav(out, samples, rate)
        if n % 50 == 0 or n == len(rows):
            print(f"  {n}/{len(rows)}", flush=True)


if __name__ == "__main__":
    main()
