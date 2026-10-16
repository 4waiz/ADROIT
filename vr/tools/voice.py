#!/usr/bin/env python3
# SPDX-License-Identifier: LicenseRef-BRIDGE
"""Speak BRIDGE VR's narration with Kokoro (the af_heart voice), one OGG file per line.

    python vr/tools/voice.py --lines narration.json --out "<player>/BRIDGE VR_Data/StreamingAssets/Voice"

narration.json is what the app writes when run with ``-autotest narration``: every line its
narrator can say, each with the key the app finds the audio by (a hash of the words, see
vr/project/Assets/Bridge/Scripts/Core/Voice.cs). A line already spoken is copied from --cache
rather than spoken again, and files in --out that no line asks for are removed.

Needs kokoro-onnx and soundfile, and Kokoro's two model files, kokoro-v1.0.onnx and
voices-v1.0.bin (from https://github.com/thewh1teagle/kokoro-onnx/releases, "model-files-v1.0"),
in --model-dir, $KOKORO_MODEL_DIR, or %LOCALAPPDATA%/BridgeVR/kokoro. Kokoro-82M and its voices
are Apache 2.0; kokoro-onnx is MIT.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import sys
import time
from pathlib import Path

import numpy as np

VOICE = "af_heart"
SPEED = 1.0

# How the narrator should say what the app writes. The app's own words stay as they are on screen;
# these spellings only steer the voice (each was checked against Kokoro's phonemes).
SAY: list[tuple[str, str]] = [
    (r"\bBRIDGE's\b", "Bridge's"),
    (r"\bBRIDGE\b", "Bridge"),
    # A button letter after a verb reads as the article ("press uh") unless it is named.
    (r"\b([Pp]ress|[Tt]ap|[Hh]old) ([ABXY])\b(?!-)", r"\1 the \2-button"),
    (r"\bCell 01\b", "Cell oh one"),
    (r"\b([Cc])obots\b", r"\1o-bots"),
    (r"\b([Cc])obot\b", r"\1o-bot"),
    (r"\bISO/TS 15066\b", "I S O T S fifteen oh six six"),
    (r"\bISO 10218\b", "I S O ten two one eight"),
    (r"\bISO\b", "I S O"),
    (r"\bIndustry 4\.0\b", "Industry four point oh"),
    (r"\bIndustry 5\.0\b", "Industry five point oh"),
    (r"\bAMRs\b", "A M Rs"),
    (r"\bAMR\b", "A M R"),
    (r"\bOEE\b", "O E E"),
    (r"(\d)\s?°", r"\1 degrees"),
    (r"°", " degrees"),
    (r"(\d)\s?%", r"\1 percent"),
    (r"(\d)\s?mm\b", r"\1 millimetres"),
    (r"(\d)\s?cm\b", r"\1 centimetres"),
    (r"(\d)\s?m/s\b", r"\1 metres a second"),
    (r"(\d)\s?kg\b", r"\1 kilograms"),
    (r"(\d)\s?ms\b", r"\1 milliseconds"),
    (r"(\d)\s?kWh\b", r"\1 kilowatt hours"),
    (r"(\d)\s?Wh\b", r"\1 watt hours"),
    (r"(\d)\s?W\b", r"\1 watts"),
    (r"(\d)\s?N\b", r"\1 newtons"),
    (r"(\d)\s?m\b", r"\1 metres"),
    (r"(\d)\s?s\b", r"\1 seconds"),
    (r" x ", " times "),
]


def say(text: str) -> str:
    """The text as the voice should read it."""
    for pattern, spoken in SAY:
        text = re.sub(pattern, spoken, text)
    return text


def find_models(given: str | None) -> Path:
    places = [given, os.environ.get("KOKORO_MODEL_DIR")]
    local = os.environ.get("LOCALAPPDATA")
    if local:
        places.append(str(Path(local) / "BridgeVR" / "kokoro"))
    for place in places:
        if place and (Path(place) / "kokoro-v1.0.onnx").is_file() and (Path(place) / "voices-v1.0.bin").is_file():
            return Path(place)
    raise SystemExit(
        "Kokoro's model files were not found. Put kokoro-v1.0.onnx and voices-v1.0.bin (from "
        "https://github.com/thewh1teagle/kokoro-onnx/releases) in %LOCALAPPDATA%/BridgeVR/kokoro, "
        "or pass --model-dir."
    )


def polish(samples: np.ndarray, rate: int) -> np.ndarray:
    """Even loudness from line to line, and no long tail of silence."""
    x = np.asarray(samples, dtype=np.float32)
    loud = np.flatnonzero(np.abs(x) > 0.01)
    if loud.size:
        x = x[: min(len(x), loud[-1] + int(0.12 * rate))]
    peak = float(np.max(np.abs(x))) if x.size else 0.0
    if peak > 0:
        x = x * min(0.89 / peak, 4.0)
    return x


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lines", type=Path, required=True, help="narration.json from the app")
    ap.add_argument("--out", type=Path, required=True, help="the player's StreamingAssets/Voice folder")
    ap.add_argument("--cache", type=Path, default=None, help="where spoken lines are kept between builds")
    ap.add_argument("--model-dir", default=None)
    args = ap.parse_args()

    data = json.loads(args.lines.read_text(encoding="utf-8"))
    voice = data.get("voice", VOICE)
    lines = data["lines"]
    cache = args.cache or args.out.parent / "VoiceCache"
    cache.mkdir(parents=True, exist_ok=True)
    args.out.mkdir(parents=True, exist_ok=True)

    import soundfile as sf

    kokoro = None
    spoken = copied = 0
    seconds = 0.0
    t0 = time.time()
    for line in lines:
        words = say(line["text"])
        # The cache is keyed by what is said and how, so a new spelling speaks the line again.
        name = hashlib.sha1(f"{voice}|{SPEED}|{words}".encode()).hexdigest()[:20] + ".ogg"
        cached = cache / name
        if not cached.is_file():
            if kokoro is None:
                from kokoro_onnx import Kokoro

                models = find_models(args.model_dir)
                kokoro = Kokoro(str(models / "kokoro-v1.0.onnx"), str(models / "voices-v1.0.bin"))
            samples, rate = kokoro.create(words, voice=voice, speed=SPEED, lang="en-us")
            audio = polish(samples, rate)
            tmp = cached.with_suffix(".tmp.ogg")
            # In blocks: libsndfile's Vorbis encoder overflows the stack on Windows when handed a
            # long line in one write.
            with sf.SoundFile(tmp, "w", samplerate=rate, channels=1, format="OGG", subtype="VORBIS") as f:
                for i in range(0, len(audio), 8192):
                    f.write(audio[i : i + 8192])
            tmp.replace(cached)
            spoken += 1
        else:
            copied += 1
        seconds += sf.info(cached).duration
        shutil.copyfile(cached, args.out / f"{line['key']}.ogg")

    wanted = {f"{line['key']}.ogg" for line in lines}
    removed = 0
    for old in args.out.glob("*.ogg"):
        if old.name not in wanted:
            old.unlink()
            removed += 1
    print(
        f"voice: {len(lines)} lines, {spoken} spoken, {copied} from the cache, {removed} removed; "
        f"{seconds / 60:.1f} minutes of narration ({voice}) in {time.time() - t0:.0f} s"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
