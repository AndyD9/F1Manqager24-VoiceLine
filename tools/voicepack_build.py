"""Build a voice pack (packs\\<DonorFolder>) from recordings and/or placeholder TTS.

Usage:
  voicepack_build.py <scriptdir> <packdir> [--recordings DIR] [--tts kokoro:bm_george|sapi] [--speed 1.0] [--radio]

<scriptdir> comes from voicepack_script.py (script.csv + lines.txt).
For each sentence NNNN: recordings\\NNNN.wav if present, else (with --tts) an AI
voice (Kokoro, local) or a Windows SAPI placeholder. Audio is mixed to mono, resampled to 24 kHz, trimmed,
normalised, optionally band-passed like a team radio, and written as a Wwise
PCM .wem. Re-running only regenerates sentences whose source changed.
"""
import csv
import os
import shutil
import subprocess
import sys

import numpy as np

from wav2wem import read_wav, wwise_pcm

RATE = 24000
HERE = os.path.dirname(os.path.abspath(__file__))


def load(path):
    ch, rate, data = read_wav(path)
    x = np.frombuffer(data, dtype="<i2").astype(np.float64) / 32768.0
    if ch > 1:
        x = x.reshape(-1, ch).mean(axis=1)
    if rate != RATE:
        t = np.arange(int(len(x) * RATE / rate)) * (rate / RATE)
        x = np.interp(t, np.arange(len(x)), x)
    return x


def trim(x, thresh=0.01, pad=0.08):
    idx = np.nonzero(np.abs(x) > thresh)[0]
    if not len(idx):
        return x
    p = int(pad * RATE)
    return x[max(0, idx[0] - p):idx[-1] + p]


def radio(x):
    """Team-radio colour: smooth 350-3800 Hz band-pass (FFT) plus soft clipping."""
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / RATE)
    gain = 1 / np.sqrt(1 + (350 / np.maximum(f, 1)) ** 4) / np.sqrt(1 + (f / 3800) ** 4)
    x = np.fft.irfft(spec * gain, len(x))
    x = x / (np.max(np.abs(x)) or 1.0)
    return np.tanh(2.2 * x) / np.tanh(2.2)


def finish(x, use_radio):
    x = trim(x)
    if use_radio:
        x = radio(x)
    peak = np.max(np.abs(x)) or 1.0
    x = x * (0.89 / peak)  # about -1 dBFS
    return (np.clip(x, -1, 1) * 32767).astype("<i2").tobytes()


def tts(engine, speed, missing, tmpdir):
    os.makedirs(tmpdir, exist_ok=True)
    lst = os.path.join(tmpdir, "list.tsv")
    with open(lst, "w", encoding="utf-8") as f:
        for num, text in missing:
            f.write(f"{os.path.join(tmpdir, num + '.wav')}\t{text}\n")
    if engine == "sapi":
        cmd = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
               os.path.join(HERE, "tts_sapi.ps1"), "-List", lst, "-SampleRate", str(RATE)]
    else:
        voice = engine.split(":", 1)[1] if ":" in engine else "bm_george"
        cmd = [sys.executable, os.path.join(HERE, "tts_kokoro.py"), lst, "--voice", voice, "--speed", speed]
    subprocess.run(cmd, check=True)


def main():
    args = sys.argv[1:]
    def opt(name):
        if name in args:
            i = args.index(name); v = args[i + 1]; del args[i:i + 2]; return v
    def flag(name):
        if name in args:
            args.remove(name); return True
        return False
    recordings, engine, use_radio = opt("--recordings"), opt("--tts"), flag("--radio")
    speed = opt("--speed") or "1.0"
    scriptdir, packdir = args
    os.makedirs(packdir, exist_ok=True)
    rows = list(csv.DictReader(open(os.path.join(scriptdir, "script.csv"), encoding="utf-8-sig"), delimiter=";"))
    tmpdir = os.path.join(scriptdir, "tts", (engine or "none").replace(":", "_") + (f"_x{speed}" if speed != "1.0" else ""))

    sources, missing = {}, []
    for r in rows:
        num = r["file"][:-4]
        rec = os.path.join(recordings, r["file"]) if recordings else None
        if rec and os.path.exists(rec):
            sources[num] = rec
        elif engine:
            wav = os.path.join(tmpdir, num + ".wav")
            sources[num] = wav
            if not os.path.exists(wav):
                missing.append((num, r["text"]))
    if missing:
        print(f"Generating {len(missing)} sentence(s) with {engine}...")
        tts(engine, speed, missing, tmpdir)

    built = skipped = 0
    for num, src in sorted(sources.items()):
        dst = os.path.join(packdir, num + ".wem")
        if os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src):
            skipped += 1
            continue
        pcm = finish(load(src), use_radio)
        open(dst, "wb").write(wwise_pcm(1, RATE, pcm))
        built += 1
    shutil.copy(os.path.join(scriptdir, "lines.txt"), os.path.join(packdir, "lines.txt"))
    recorded = sum(1 for s in sources.values() if recordings and s.startswith(recordings))
    print(f"Pack {packdir}: {built} built, {skipped} up to date, {recorded} recorded, "
          f"{len(sources) - recorded} generated, {len(rows) - len(sources)} missing.")


if __name__ == "__main__":
    main()
