"""Generate the original looping Nasledie ambient theme as a compact WAV asset."""
import math
import struct
import wave
from pathlib import Path

RATE = 22050
BPM = 72.0
BEAT = 60.0 / BPM
BAR = BEAT * 4.0
BARS = 8
DURATION = BAR * BARS
TOTAL = int(RATE * DURATION)

# Dorian-leaning progression: reflective, calm and gently optimistic.
CHORDS = [
    ["D3", "A3", "C4", "F4"],
    ["Bb2", "F3", "A3", "D4"],
    ["F2", "C3", "E3", "A3"],
    ["C3", "G3", "D4", "E4"],
]
NOTE_INDEX = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4,
              "F": 5, "F#": 6, "Gb": 6, "G": 7, "G#": 8, "Ab": 8,
              "A": 9, "A#": 10, "Bb": 10, "B": 11}

def hz(note):
    letter = note[:-1]
    octave = int(note[-1])
    midi = (octave + 1) * 12 + NOTE_INDEX[letter]
    return 440.0 * 2 ** ((midi - 69) / 12.0)

def add_pluck(buf, start, note, amp, pan=0.0, decay=2.1):
    f = hz(note)
    begin = max(0, int(start * RATE))
    count = min(int(decay * RATE), TOTAL - begin)
    left_gain = math.sqrt((1.0 - pan) * 0.5)
    right_gain = math.sqrt((1.0 + pan) * 0.5)
    phase = 2.0 * math.pi * f / RATE
    for j in range(count):
        t = j / RATE
        attack = min(1.0, t / 0.055)
        envelope = attack * math.exp(-2.25 * t)
        tone = (math.sin(phase * j) + 0.27 * math.sin(phase * 2 * j + 0.2)
                + 0.085 * math.sin(phase * 3 * j + 0.7))
        sample = amp * envelope * tone
        idx = begin + j
        buf[idx * 2] += sample * left_gain
        buf[idx * 2 + 1] += sample * right_gain

def add_pad(buf, start, end, note, amp, pan=0.0):
    f = hz(note)
    begin = max(0, int(start * RATE))
    finish = min(TOTAL, int(end * RATE))
    left_gain = math.sqrt((1.0 - pan) * 0.5)
    right_gain = math.sqrt((1.0 + pan) * 0.5)
    phase = 2.0 * math.pi * f / RATE
    span = max(1, finish - begin)
    for j in range(span):
        progress = j / span
        edge = min(1.0, progress / 0.16, (1.0 - progress) / 0.18)
        t = j / RATE
        wobble = 1.0 + 0.006 * math.sin(2.0 * math.pi * 0.09 * t + f)
        sample = amp * max(0.0, edge) * wobble * (math.sin(phase * j) + 0.14 * math.sin(phase * 2 * j))
        idx = begin + j
        buf[idx * 2] += sample * left_gain
        buf[idx * 2 + 1] += sample * right_gain

samples = [0.0] * (TOTAL * 2)
for bar in range(BARS):
    chord = CHORDS[bar % 4]
    start = bar * BAR
    # Soft sustained harmony underneath the hand-played arpeggio.
    for n, note in enumerate(chord[:3]):
        add_pad(samples, start, start + BAR, note, 0.030 if n == 0 else 0.019, (-0.35 + n * 0.35))
    # Slow four-note broken chords, with a fifth note on alternating bars.
    pattern = [chord[0], chord[1], chord[2], chord[3], chord[2] if bar % 2 else chord[1]]
    for step, note in enumerate(pattern):
        t = start + step * (BAR / 4.0) * 0.82
        add_pluck(samples, t, note, 0.115 if step == 0 else 0.078, (-0.45 if step % 2 == 0 else 0.45), 2.25)

# A restrained, original five-note motif. The second pass answers the first an octave lower.
melody_a = [(0.50, "A4"), (1.50, "F4"), (2.50, "E4"), (4.25, "D4"),
            (5.75, "F4"), (7.50, "A4"), (9.25, "G4"), (11.00, "E4"),
            (12.75, "F4"), (14.25, "A4"), (16.50, "G4"), (18.25, "F4"),
            (20.50, "D4"), (22.00, "E4"), (24.00, "G4"), (25.25, "D4")]
for i, (t, note) in enumerate(melody_a):
    if t < DURATION - 0.1:
        add_pluck(samples, t, note, 0.052, 0.24 if i % 2 else -0.24, 1.8)

# Gentle room reflections widen the stereo image without a reverb dependency.
original = samples[:]
delay_l = int(0.105 * RATE) * 2
delay_r = int(0.147 * RATE) * 2
for i in range(len(samples)):
    if i >= delay_l and i % 2 == 0:
        samples[i] += original[i - delay_l] * 0.105
    if i >= delay_r and i % 2 == 1:
        samples[i] += original[i - delay_r] * 0.085

peak = max(abs(v) for v in samples) or 1.0
scale = min(0.88 / peak, 1.0)
fade = int(RATE * 0.04)
frames = bytearray()
for frame in range(TOTAL):
    edge = min(1.0, (frame + 1) / fade, (TOTAL - frame) / fade)
    for channel in range(2):
        value = max(-1.0, min(1.0, samples[frame * 2 + channel] * scale * max(0.0, edge)))
        frames.extend(struct.pack("<h", int(value * 32767)))

out = Path("Assets/Game/Runtime/Client/Resources/Audio/Nasledie_Theme.wav")
out.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(out), "wb") as wav:
    wav.setnchannels(2)
    wav.setsampwidth(2)
    wav.setframerate(RATE)
    wav.writeframes(frames)
print(f"Wrote {out}: {DURATION:.1f}s, {RATE} Hz, stereo PCM, {out.stat().st_size} bytes")
