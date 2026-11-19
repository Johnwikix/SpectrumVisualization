"""Regenerate the embedded AnimCJK stroke medians from pinned upstream source files."""

import gzip
import hashlib
import json
import struct
import urllib.request
from pathlib import Path

COMMIT = "99dba566b92daf45ae20d05e6c91d3f68d8619f2"
FILES = {
    "graphicsJaKana.txt": "3f61aee523e2cf95e76240b760234a1c6c437d4e9308dca88723178dd8bdb61c",
    "graphicsZhHans.txt": "3bb2150fe12edc7d82e18df0044c3e942aca6b8f399f8fd17aa0eee5197cbd62",
    "graphicsJa.txt": "3b2d95465152fb2a7e108774219e969c43af14f1e51e66498bc96522a674cfeb",
    "licenses/COPYING.txt": "19baa5148c9709776a95d78bf451a909086d7eb424b548c044c70fc1bf8330f2",
    "licenses/APL/english/ARPHICPL.TXT": "3a5e90c0957524a89e48203febcd4492ca4393678abaa7e5b4d70f3ff32b386d",
}


def main():
    import argparse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, help="Optional directory of already downloaded source files")
    args = parser.parse_args()
    target = Path(__file__).resolve().parents[2] / "External" / "Folia" / "Nebbia"
    target.mkdir(exist_ok=True)
    glyphs = {}
    for file, digest in FILES.items():
        name = Path(file).name
        if args.source:
            data = (args.source / name).read_bytes()
        else:
            url = f"https://raw.githubusercontent.com/parsimonhi/animCJK/{COMMIT}/{file}"
            with urllib.request.urlopen(url, timeout=120) as response:
                data = response.read()
        if hashlib.sha256(data).hexdigest() != digest:
            raise ValueError(f"Unexpected SHA-256 for {file}")
        if file.startswith("licenses/"):
            lines = (line.rstrip(b" \t") for line in data.splitlines())
            (target / name).write_bytes(b"\n".join(lines) + b"\n")
            continue
        for row in data.decode("utf-8").splitlines():
            if not row.strip():
                continue
            entry = json.loads(row)
            char = entry["character"]
            if char in glyphs:
                continue
            strokes = [[(x, 900 - y) for x, y in stroke] for stroke in entry["medians"]]
            strokes = [stroke for stroke in strokes if len(stroke) > 1
                       and all(-20 < x < 1044 and -20 < y < 1044 for x, y in stroke)]
            if strokes:
                glyphs[char] = strokes
    # Coordinates are kept in the original grid. Only medians are distributed.
    raw = bytearray(struct.pack("<4sI", b"NBS1", len(glyphs)))
    for char, strokes in sorted(glyphs.items()):
        raw.extend(struct.pack("<IB", ord(char), len(strokes)))
        for stroke in strokes:
            raw.extend(struct.pack("<H", len(stroke)))
            for x, y in stroke:
                raw.extend(struct.pack("<hh", round(x), round(y)))
    (target / "strokes.bin.gz").write_bytes(gzip.compress(raw, compresslevel=9, mtime=0))


if __name__ == "__main__":
    main()
