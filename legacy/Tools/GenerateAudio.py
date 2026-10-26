"""Generate original, deterministic music and interface sounds for JERICHO."""

from __future__ import annotations

import math
import random
import struct
import wave
from pathlib import Path

RATE = 22050
OUT = Path(__file__).resolve().parents[1] / "Assets/Game/Runtime/Client/Resources/Audio"
NOTE_INDEX = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4,
              "F": 5, "F#": 6, "Gb": 6, "G": 7, "G#": 8, "Ab": 8,
              "A": 9, "A#": 10, "Bb": 10, "B": 11}

THEMES = {
    "Nasledie_Theme": {
        "bpm": 70, "chords": [["D3", "A3", "C4", "F4"], ["Bb2", "F3", "A3", "D4"],
                              ["F2", "C3", "E3", "A3"], ["C3", "G3", "D4", "E4"]],
        "motif": ["A4", "F4", "E4", "D4", "F4", "A4", "G4", "E4"], "weight": 0.90,
    },
    "Theme_Cities": {
        "bpm": 72, "chords": [["A2", "E3", "A3", "C4"], ["F2", "C3", "G3", "A3"],
                              ["C3", "G3", "B3", "E4"], ["G2", "D3", "A3", "B3"]],
        "motif": ["E4", "A4", "G4", "E4", "C4", "D4", "E4", "A4"], "weight": 0.92,
    },
    "Theme_ConnectedLands": {
        "bpm": 76, "chords": [["G2", "D3", "A3", "B3"], ["E2", "B2", "F#3", "A3"],
                              ["C3", "G3", "D4", "E4"], ["D3", "A3", "C4", "F#4"]],
        "motif": ["B4", "A4", "G4", "E4", "F#4", "A4", "D5", "B4"], "weight": 0.94,
    },
    "Theme_Industry": {
        "bpm": 80, "chords": [["E2", "B2", "E3", "G3"], ["C3", "G3", "B3", "D4"],
                              ["A2", "E3", "G3", "C4"], ["D3", "A3", "C4", "F#4"]],
        "motif": ["G4", "B4", "D5", "B4", "A4", "G4", "E4", "F#4"], "weight": 0.95,
    },
    "Theme_Synthesis": {
        "bpm": 82, "chords": [["D3", "A3", "E4", "F#4"], ["B2", "F#3", "C#4", "E4"],
                              ["G2", "D3", "A3", "B3"], ["A2", "E3", "B3", "C#4"]],
        "motif": ["F#4", "A4", "D5", "E5", "C#5", "B4", "A4", "F#4"], "weight": 0.86,
    },
}


def hz(note: str) -> float:
    octave = int(note[-1])
    midi = (octave + 1) * 12 + NOTE_INDEX[note[:-1]]
    return 440.0 * 2 ** ((midi - 69) / 12.0)


def pan_gains(pan: float) -> tuple[float, float]:
    return math.sqrt((1.0 - pan) * 0.5), math.sqrt((1.0 + pan) * 0.5)


def mix(samples: list[float], frame: int, value: float, pan: float) -> None:
    if frame < 0 or frame * 2 + 1 >= len(samples):
        return
    left, right = pan_gains(max(-0.9, min(0.9, pan)))
    samples[frame * 2] += value * left
    samples[frame * 2 + 1] += value * right


def add_pluck(samples: list[float], start: float, note: str, amp: float, pan: float,
              decay: float = 1.5, brightness: float = 1.0) -> None:
    begin = max(0, int(start * RATE))
    count = min(int(decay * RATE), len(samples) // 2 - begin)
    base = hz(note)
    for j in range(count):
        t = j / RATE
        attack = min(1.0, t / 0.018)
        env = attack * math.exp(-3.25 * t / max(0.35, decay))
        phase = 2.0 * math.pi * base * t
        tone = (math.sin(phase) + 0.34 * math.sin(2.01 * phase + 0.13) * brightness
                + 0.12 * math.sin(3.02 * phase + 0.38) * brightness
                + 0.045 * math.sin(4.04 * phase + 0.62) * brightness)
        mix(samples, begin + j, amp * env * tone, pan)


def add_pad(samples: list[float], start: float, duration: float, note: str,
            amp: float, pan: float, warmth: float = 0.16) -> None:
    begin = max(0, int(start * RATE))
    count = min(int(duration * RATE), len(samples) // 2 - begin)
    base = hz(note)
    for j in range(count):
        t = j / RATE
        edge = min(1.0, t / 0.45, max(0.0, duration - t) / 0.62)
        swell = 0.88 + 0.12 * math.sin(2.0 * math.pi * 0.08 * t + base)
        tone = math.sin(2.0 * math.pi * base * t) + warmth * math.sin(4.0 * math.pi * base * t + 0.15)
        mix(samples, begin + j, amp * max(0.0, edge) * swell * tone, pan)


def add_reed(samples: list[float], start: float, note: str, amp: float, duration: float,
             pan: float) -> None:
    begin = max(0, int(start * RATE))
    count = min(int(duration * RATE), len(samples) // 2 - begin)
    base = hz(note)
    for j in range(count):
        t = j / RATE
        edge = min(1.0, t / 0.12, max(0.0, duration - t) / 0.18)
        vibrato = 0.004 * math.sin(2.0 * math.pi * 4.8 * t)
        phase = 2.0 * math.pi * base * (t + vibrato)
        tone = math.sin(phase) + 0.18 * math.sin(2.0 * phase + 0.2) + 0.035 * math.sin(3.0 * phase)
        mix(samples, begin + j, amp * max(0.0, edge) * tone, pan)


def add_frame_drum(samples: list[float], start: float, amp: float, seed: int) -> None:
    begin = max(0, int(start * RATE))
    count = min(int(0.24 * RATE), len(samples) // 2 - begin)
    rng = random.Random(seed)
    for j in range(count):
        t = j / RATE
        env = math.exp(-18.0 * t)
        freq = 92.0 - 42.0 * min(1.0, t / 0.2)
        body = math.sin(2.0 * math.pi * freq * t) * 0.68
        skin = (rng.random() * 2.0 - 1.0) * math.exp(-30.0 * t) * 0.20
        mix(samples, begin + j, amp * env * (body + skin), -0.12)


def write_wav(path: Path, samples: list[float], peak_limit: float = 0.78) -> None:
    peak = max((abs(v) for v in samples), default=1.0) or 1.0
    # Normalize up when a layered cue is naturally quiet, while keeping the chosen ceiling.
    scale = peak_limit / peak
    frames = bytearray()
    total_frames = len(samples) // 2
    fade_frames = min(int(RATE * 0.06), max(1, int(total_frames * 0.08)))
    for frame in range(total_frames):
        edge = min(1.0, (frame + 1) / fade_frames, (total_frames - frame) / fade_frames)
        for channel in range(2):
            value = max(-0.98, min(0.98, samples[frame * 2 + channel] * scale * max(0.0, edge)))
            frames.extend(struct.pack("<h", int(value * 32767)))
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(2)
        wav.setsampwidth(2)
        wav.setframerate(RATE)
        wav.writeframes(frames)


def make_theme(name: str, settings: dict) -> Path:
    beat = 60.0 / settings["bpm"]
    bar = beat * 4.0
    duration = bar * 8.0
    samples = [0.0] * (int(duration * RATE) * 2)
    motifs = settings["motif"]
    for bar_index in range(8):
        chord = settings["chords"][bar_index % len(settings["chords"])]
        start = bar_index * bar
        for index, note in enumerate(chord[:3]):
            add_pad(samples, start, bar * 0.98, note, 0.021 if index == 0 else 0.014,
                    -0.28 + index * 0.28, warmth=0.14)
        pattern = [chord[0], chord[1], chord[2], chord[3]]
        for step, note in enumerate(pattern):
            add_pluck(samples, start + step * beat, note, 0.060 if step == 0 else 0.043,
                      -0.32 if step % 2 == 0 else 0.32, decay=1.35,
                      brightness=settings["weight"])
        if bar_index % 2 == 0:
            phrase = motifs[(bar_index // 2 * 2) % len(motifs):(bar_index // 2 * 2 + 2) % len(motifs)]
            if len(phrase) < 2:
                phrase = motifs[-2:]
            add_reed(samples, start + beat * 1.5, phrase[0], 0.031, beat * 1.65, -0.16)
            add_reed(samples, start + beat * 3.0, phrase[1], 0.027, beat * 1.55, 0.17)
        if bar_index % 2 == 0 or settings["bpm"] >= 80:
            add_frame_drum(samples, start, 0.034 if bar_index % 2 == 0 else 0.023,
                           settings["bpm"] * 100 + bar_index)
            if settings["bpm"] >= 76:
                add_frame_drum(samples, start + beat * 2.0, 0.021,
                               settings["bpm"] * 200 + bar_index)
    path = OUT / (name + ".wav")
    write_wav(path, samples, peak_limit=0.40)
    return path


def add_sfx_tone(samples: list[float], start: float, f0: float, f1: float,
                 duration: float, amp: float, pan: float = 0.0,
                 harmonics: tuple[float, ...] = (1.0, 0.25, 0.08), decay: float = 4.0) -> None:
    begin = max(0, int(start * RATE))
    count = min(int(duration * RATE), len(samples) // 2 - begin)
    for j in range(count):
        t = j / RATE
        ratio = t / max(duration, 0.001)
        freq = f0 + (f1 - f0) * ratio
        phase = 2.0 * math.pi * (f0 * t + 0.5 * (f1 - f0) * t * ratio)
        attack = min(1.0, t / 0.008)
        env = attack * math.exp(-decay * ratio)
        tone = sum(weight * math.sin(phase * (i + 1) + i * 0.21) for i, weight in enumerate(harmonics))
        mix(samples, begin + j, amp * env * tone, pan)


def make_sfx(name: str, duration: float, events: list[tuple]) -> Path:
    samples = [0.0] * (int(duration * RATE) * 2)
    for event in events:
        kind = event[0]
        if kind == "tone":
            _, start, f0, f1, span, amp, pan, harmonics, decay = event
            add_sfx_tone(samples, start, f0, f1, span, amp, pan, harmonics, decay)
        elif kind == "drum":
            _, start, amp = event
            add_frame_drum(samples, start, amp, int(start * 10000) + int(amp * 1000))
    path = OUT / "Sfx" / (name + ".wav")
    write_wav(path, samples, peak_limit=0.25)
    return path


def generate_effects() -> list[Path]:
    cues = [
        ("UI_Click", 0.12, [("tone", 0.0, 720, 520, 0.085, 0.13, 0.0, (1.0, 0.22, 0.04), 6.0)]),
        ("UI_Confirm", 0.42, [("tone", 0.0, 523, 523, 0.19, 0.12, -0.08, (1.0, 0.28, 0.07), 3.0),
                              ("tone", 0.12, 784, 784, 0.24, 0.10, 0.10, (1.0, 0.21, 0.05), 3.2)]),
        ("UI_Cancel", 0.30, [("tone", 0.0, 392, 262, 0.22, 0.10, 0.0, (1.0, 0.22, 0.05), 3.0)]),
        ("World_Move", 0.24, [("tone", 0.0, 294, 220, 0.13, 0.10, -0.16, (1.0, 0.16), 5.0),
                              ("tone", 0.06, 440, 330, 0.12, 0.04, 0.17, (1.0, 0.12), 6.0)]),
        ("Turn_Advance", 0.90, [("tone", 0.0, 196, 247, 0.68, 0.075, -0.15, (1.0, 0.25, 0.09), 2.0),
                                ("tone", 0.18, 392, 494, 0.54, 0.044, 0.15, (1.0, 0.12), 2.6)]),
        ("Build_Complete", 0.88, [("drum", 0.0, 0.10), ("tone", 0.08, 392, 392, 0.34, 0.11, -0.10, (1.0, 0.3, 0.08), 2.3),
                                  ("tone", 0.27, 587, 587, 0.42, 0.09, 0.12, (1.0, 0.24, 0.07), 2.2)]),
        ("Research_Discovery", 1.10, [("tone", 0.0, 523, 523, 0.48, 0.08, -0.18, (1.0, 0.32, 0.1), 1.8),
                                      ("tone", 0.19, 659, 659, 0.52, 0.075, 0.0, (1.0, 0.28, 0.08), 1.9),
                                      ("tone", 0.38, 784, 784, 0.64, 0.07, 0.18, (1.0, 0.24, 0.06), 1.8)]),
        ("Era_Transition", 2.45, [("tone", 0.0, 196, 196, 1.65, 0.045, -0.18, (1.0, 0.24, 0.08), 1.3),
                                  ("tone", 0.12, 392, 523, 0.72, 0.075, -0.10, (1.0, 0.20, 0.06), 1.4),
                                  ("tone", 0.65, 523, 659, 0.72, 0.075, 0.10, (1.0, 0.20, 0.06), 1.4),
                                  ("tone", 1.18, 659, 784, 1.00, 0.075, 0.18, (1.0, 0.18, 0.05), 1.3)]),
        ("Diplomacy_Seal", 0.94, [("drum", 0.0, 0.055), ("tone", 0.08, 440, 440, 0.36, 0.10, -0.12, (1.0, 0.22, 0.07), 2.0),
                                  ("tone", 0.34, 587, 659, 0.46, 0.08, 0.12, (1.0, 0.2, 0.06), 2.0)]),
        ("Battle_Resolve", 0.56, [("drum", 0.0, 0.16), ("tone", 0.035, 180, 120, 0.28, 0.08, 0.04, (1.0, 0.20), 5.0)]),
        ("Victory", 2.65, [("tone", 0.0, 392, 392, 0.75, 0.075, -0.24, (1.0, 0.25, 0.08), 1.2),
                            ("tone", 0.28, 494, 494, 0.86, 0.070, -0.08, (1.0, 0.22, 0.07), 1.2),
                            ("tone", 0.56, 587, 587, 1.00, 0.068, 0.08, (1.0, 0.20, 0.06), 1.2),
                            ("tone", 0.84, 784, 784, 1.40, 0.062, 0.24, (1.0, 0.18, 0.05), 1.1)]),
        ("Defeat", 2.45, [("tone", 0.0, 392, 349, 0.78, 0.065, 0.18, (1.0, 0.2, 0.05), 1.5),
                           ("tone", 0.48, 330, 262, 0.86, 0.060, -0.12, (1.0, 0.18, 0.05), 1.5),
                           ("tone", 0.98, 247, 196, 1.15, 0.052, 0.06, (1.0, 0.16, 0.04), 1.5)]),
    ]
    return [make_sfx(name, duration, events) for name, duration, events in cues]


def main() -> None:
    paths = [make_theme(name, settings) for name, settings in THEMES.items()]
    paths.extend(generate_effects())
    for path in paths:
        with wave.open(str(path), "rb") as wav:
            duration = wav.getnframes() / wav.getframerate()
            print(f"{path.relative_to(OUT)} · {duration:.2f}s · {wav.getnchannels()}ch · {wav.getframerate()}Hz · {path.stat().st_size} bytes")


if __name__ == "__main__":
    main()
