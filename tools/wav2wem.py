"""Convert a PCM .wav into a Wwise PCM .wem (fmt tag 0xFFFE) usable as a pack line.

Usage: wav2wem.py <in.wav> <out.wem>
Input: 16-bit PCM, mono or stereo, any rate (the game's lines are 48 kHz mono).
"""
import struct
import sys


def read_wav(path):
    d = open(path, "rb").read()
    if d[:4] != b"RIFF" or d[8:12] != b"WAVE":
        raise SystemExit("not a WAV file")
    o, fmt, data = 12, None, None
    while o + 8 <= len(d):
        cid, size = d[o:o + 4], struct.unpack_from("<I", d, o + 4)[0]
        body = d[o + 8:o + 8 + size]
        if cid == b"fmt ":
            fmt = struct.unpack_from("<HHIIHH", body)
        elif cid == b"data":
            data = body
        o += 8 + size + (size & 1)
    if not fmt or data is None:
        raise SystemExit("missing fmt/data chunk")
    tag, ch, rate, _, _, bits = fmt
    if tag not in (1, 0xFFFE) or bits != 16:
        raise SystemExit(f"need 16-bit PCM (got tag {tag:#x}, {bits} bits)")
    return ch, rate, data


def wwise_pcm(ch, rate, data):
    # Wwise packs AkChannelConfig into dwChannelMask: count | type(1=standard)<<8 | mask<<12
    mask = 0x4 if ch == 1 else 0x3
    layout = ch | (1 << 8) | (mask << 12)
    fmt = struct.pack("<HHIIHHHHI", 0xFFFE, ch, rate, rate * ch * 2, ch * 2, 16, 6, 16, layout)
    body = b"WAVE" + b"fmt " + struct.pack("<I", len(fmt)) + fmt + b"data" + struct.pack("<I", len(data)) + data
    return b"RIFF" + struct.pack("<I", len(body)) + body


def main():
    ch, rate, data = read_wav(sys.argv[1])
    open(sys.argv[2], "wb").write(wwise_pcm(ch, rate, data))
    print(f"{sys.argv[2]}: {ch} ch, {rate} Hz, {len(data) / (rate * ch * 2):.2f} s")


if __name__ == "__main__":
    main()
