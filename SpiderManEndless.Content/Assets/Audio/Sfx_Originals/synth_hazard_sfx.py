"""
Synthesizes the hazard sound effects (no samples): SpiderSense.wav, PumpkinBlast.wav, PylonSlam.wav.
Run: python synth_hazard_sfx.py <output folder>
"""
import sys, wave
import numpy as np

RATE = 44100
rng = np.random.default_rng(580)

def save(name, x, peak=0.85):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(f"{OUT}/{name}", "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(data.tobytes())

def t_of(seconds):
    return np.arange(int(seconds * RATE)) / RATE

def lowpass(x, cutoff):
    # one-pole low-pass, cutoff in Hz (scalar or per-sample array)
    a = np.exp(-2 * np.pi * np.broadcast_to(cutoff, x.shape) / RATE)
    y = np.zeros_like(x); prev = 0.0
    for i in range(len(x)):
        prev = (1 - a[i]) * x[i] + a[i] * prev
        y[i] = prev
    return y

def spider_sense():
    # the "tingle": a bright cluster of detuned high partials with a fast shimmer (tremolo) and a
    # wobbling pitch, sharp attack and a ~0.5s ringing tail
    t = t_of(0.6)
    env = np.minimum(t / 0.008, 1) * np.exp(-t * 6.5)
    wobble = 1 + 0.012 * np.sin(2 * np.pi * 23 * t)
    x = np.zeros_like(t)
    for f, amp in [(1760, 1.0), (2217, 0.8), (2637, 0.7), (3520, 0.45), (4186, 0.3), (1760 * 1.007, 0.6)]:
        x += amp * np.sin(2 * np.pi * f * wobble * t + rng.uniform(0, 6.28))
    shimmer = 0.55 + 0.45 * np.sin(2 * np.pi * 31 * t) ** 2
    zing = np.sin(2 * np.pi * (900 * t + 2600 * t * t)) * np.exp(-t * 25) * 0.5   # rising zap at the start
    return (x * shimmer + zing) * env

def pumpkin_blast():
    # low thump + a crackling noise burst that darkens as it decays
    t = t_of(1.1)
    thump = np.sin(2 * np.pi * (70 * t - 25 * t * t)) * np.exp(-t * 7) * 1.2
    noise = rng.uniform(-1, 1, len(t))
    body = lowpass(noise, 6000 * np.exp(-t * 4) + 300) * np.exp(-t * 4.5) * 2.2
    crackle = (rng.uniform(0, 1, len(t)) > 0.997) * rng.uniform(-1, 1, len(t)) * np.exp(-t * 3) * 0.8
    attack = np.minimum(t / 0.003, 1)
    return (thump + body + crackle) * attack

def pylon_slam():
    # heavy metal clang: thud + inharmonic ringing partials
    t = t_of(0.9)
    thud = np.sin(2 * np.pi * (95 * t - 30 * t * t)) * np.exp(-t * 14) * 1.3
    ring = np.zeros_like(t)
    for f, amp, decay in [(187, 0.6, 5), (431, 0.5, 6), (667, 0.35, 8), (1093, 0.25, 10), (1529, 0.15, 13)]:
        ring += amp * np.sin(2 * np.pi * f * t) * np.exp(-t * decay)
    hit = lowpass(rng.uniform(-1, 1, len(t)), 3000) * np.exp(-t * 40) * 1.5
    return (thud + ring + hit) * np.minimum(t / 0.002, 1)

if __name__ == "__main__":
    OUT = sys.argv[1] if len(sys.argv) > 1 else "."
    save("SpiderSense.wav", spider_sense(), 0.8)
    save("PumpkinBlast.wav", pumpkin_blast(), 0.9)
    save("PylonSlam.wav", pylon_slam(), 0.85)
