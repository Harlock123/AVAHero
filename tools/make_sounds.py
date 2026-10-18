#!/usr/bin/env python3
"""Synthesises AVAHero's sound effects and tunes into ../src/AVAHero/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle, saw and noise voices. Down the mine
plays In the Hall of the Mountain King (Grieg, 1875); the title's march is a new composition. The miner's thanks are
spoken by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAHero", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Land.wav", tone(0.05, 160, 80, "square", attack=0.001, release=1.2, gain=0.5), 0.1)
    write("Screen.wav", tone(0.03, 500, 700, "square", attack=0.001, release=1.0, gain=0.3), 0.06)
    # The laser: a buzzing whine, short, played again and again while it fires.
    write("Laser.wav", tone(0.09, 2400, 1900, "saw", attack=0.002, release=0.5, vibrato=0.06, vib_rate=90, gain=0.5), 0.12)
    write("Kill.wav", cat(tone(0.06, 900, 1500, "square", release=0.4), tone(0.1, 1500, 400, "noise", release=1.0, gain=0.6)), 0.24)
    write("Crumble.wav", tone(0.45, 700, 120, "noise", attack=0.002, release=0.8), 0.28)
    write("Light.wav", tone(0.35, 4000, 3000, "noise", attack=0.01, release=0.3, gain=0.6), 0.14)
    write("Boom.wav", mix(tone(0.8, 80, 30, "sine", attack=0.002, release=1.0), tone(0.6, 1200, 80, "noise", attack=0.001, release=1.2, gain=0.9)), 0.5)
    write("Dark.wav", tone(0.3, 600, 150, "tri", attack=0.002, release=0.8), 0.2)
    write("Die.wav", cat(*[tone(0.04, 1400 - i * 55, kind="square", release=0.2, gain=0.6) for i in range(22)]), 0.3)
    write("Rescue.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6", "G6"]], tone(0.35, hz("C7"), kind="pulse", vibrato=0.02, release=0.6)), 0.28)
    write("Tick.wav", tone(0.025, 1800, 1200, "square", attack=0.001, release=1.0, gain=0.5), 0.12)
    write("LowPower.wav", cat(*[tone(0.12, f, kind="square", release=0.2) for f in [880, 660, 880, 660]]), 0.2)
    words = "TH AE1 NG K S , HH IY1 R OW !"
    write("SayThanks.wav", mix(say(words, rate=RATE, pitch=110, speed=1.0, size=1.0), [s * 0.3 for s in say(words, rate=RATE, pitch=165, speed=1.0, size=1.0, seed=11)]), 0.5)

    # Down the mine: In the Hall of the Mountain King (Grieg, 1875), the theme creeping, then hurrying.
    e = 0.5
    phrase = [("A3", e), ("B3", e), ("C4", e), ("D4", e), ("E4", e), ("C4", e), ("E4", 1),
              ("D#4", e), ("B3", e), ("D#4", 1), ("D4", e), ("A#3", e), ("D4", 1),
              ("A3", e), ("B3", e), ("C4", e), ("D4", e), ("E4", e), ("C4", e), ("E4", e), ("A4", e),
              ("G4", e), ("E4", e), ("C4", e), ("E4", e), ("G4", 2)]
    up = [(n[:-1] + str(int(n[-1]) + 1), b) for n, b in phrase]
    bass = [("A2", 1), ("E2", 1)] * 2 + [("B2", 1), ("F#2", 1), ("A#2", 1), ("F2", 1)] + [("A2", 1), ("E2", 1)] * 2 + [("C3", 1), ("G2", 1), ("C3", 2)]
    write("Music.wav", cat(tune(110, phrase, bass, kind="pulse"), tune(150, up, bass, kind="pulse")), 0.26)
    # The title: a march of its own.
    lead = [("E5", 1), ("C5", 0.5), ("E5", 0.5), ("G5", 1), ("E5", 1), ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 0.5), ("D5", 2),
            ("D5", 1), ("B4", 0.5), ("D5", 0.5), ("F5", 1), ("D5", 1), ("E5", 0.5), ("D5", 0.5), ("C5", 0.5), ("B4", 0.5), ("C5", 2)]
    tbass = [("C3", 1), ("G2", 1)] * 2 + [("F2", 1), ("G2", 1), ("G2", 2)] + [("G2", 1), ("D3", 1)] * 2 + [("G2", 1), ("G2", 1), ("C3", 2)]
    write("TitleTune.wav", cat(tune(124, lead, tbass, kind="pulse"), silence(1.0)), 0.32)
    write("Victory.wav", tune(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1.5), ("B5", 0.5), ("C6", 0.5), ("D6", 0.5), ("E6", 3)],
                              [("C3", 1), ("G2", 1), ("A2", 1), ("F2", 1), ("G2", 1), ("C3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("E5", 1), ("C5", 1), ("A4", 1), ("E4", 1), ("A4", 3)], [("A2", 2), ("F2", 2), ("A1", 3)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
