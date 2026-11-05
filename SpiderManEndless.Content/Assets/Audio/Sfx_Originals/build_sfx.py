"""Builds the game-ready sound effects from the user's downloaded sounds, plus synthesized web thwips.
Usage: build_sfx.py <input dir> <output dir>"""
import sys, os, subprocess
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

SR = 44100
IN, OUT = sys.argv[1], sys.argv[2]
os.makedirs(OUT, exist_ok=True)


def load(path):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', path, '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).astype(np.float64)


def trim(x, pre_ms=3, thresh=0.04, tail_thresh=0.004, fade_ms=25):
    """Cut leading silence (keeping a few ms before the attack) and the quiet tail, with a fade-out."""
    pk = np.max(np.abs(x))
    start = max(0, int(np.argmax(np.abs(x) > pk * thresh)) - int(pre_ms * SR / 1000))
    loud = np.nonzero(np.abs(x) > pk * tail_thresh)[0]
    end = min(len(x), loud[-1] + 1 + int(fade_ms * SR / 1000))
    y = x[start:end].copy()
    f = min(len(y), int(fade_ms * SR / 1000))
    y[-f:] *= np.linspace(1, 0, f) ** 2
    y[:16] *= np.linspace(0, 1, 16)  # de-click the cut
    return y


def norm(x, peak_db=-1.0):
    return x / np.max(np.abs(x)) * 10 ** (peak_db / 20)


def save(name, x):
    wavfile.write(os.path.join(OUT, name), SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))
    print(f'{name:22s} {len(x) / SR * 1000:5.0f} ms')


def hp(x, hz, order=2):
    return sosfilt(butter(order, hz, 'highpass', fs=SR, output='sos'), x)


def lp(x, hz, order=2):
    return sosfilt(butter(order, hz, 'lowpass', fs=SR, output='sos'), x)


# ---------------------------------------------------------------- essence pickup
# a short bright "point": just the main pop of the bubble, pitched up a fifth so it's light rather
# than a heavy drop, with the gem sparkle forward enough to read as a reward. no saturation
def speed(x, ratio):
    """Plays x faster by ratio (raises pitch and shortens it, like a tape sped up)."""
    idx = np.arange(0, len(x) - 1, ratio)
    return np.interp(idx, np.arange(len(x)), x)

raw = load(f'{IN}/bubbles-single1.wav')
pop = raw[int(0.078 * SR):int(0.165 * SR)].copy()          # the main bloop only, no build-up or second bubble
pop[:32] *= np.linspace(0, 1, 32)
WET_POP = pop.copy()
pop = hp(speed(pop, 1.5), 350, 2)
f = int(0.02 * SR); pop[-f:] *= np.linspace(1, 0, f) ** 2
gem = trim(load(f'{IN}/gem/gem-gather-orig.wav'), pre_ms=0, fade_ms=20)
gem = hp(gem, 2500, 4)
off = int(0.006 * SR)
mix = np.zeros(max(len(pop), off + len(gem)))
mix[:len(pop)] += norm(pop, 0)
mix[off:off + len(gem)] += norm(gem, -7)
save('EssencePickup.wav', norm(mix, -3))

# ---------------------------------------------------------------- UI blip (menu move)
save('ui_blip.wav', norm(trim(load(f'{IN}/beep.wav'), pre_ms=1, fade_ms=10), -6))

# ---------------------------------------------------------------- punches
for i, n in enumerate([10, 11, 14, 17], 1):     # sharp, lighter hits: an attack connecting
    save(f'PunchLight{i}.wav', norm(trim(load(f'{IN}/punch/hits/hit{n:02d}.mp3.flac'), fade_ms=40), -1.5))
for i, n in enumerate([23, 28, 31, 22], 1):     # dense, heavy hits: getting hit / big finishers
    save(f'PunchHeavy{i}.wav', norm(trim(load(f'{IN}/punch/hits/hit{n:02d}.mp3.flac'), fade_ms=50), -1.0))


# ---------------------------------------------------------------- web thwip (synthesized)
def resonator(x, f_start, f_end, q, sweep_s):
    """Two-pole resonant band-pass whose center frequency glides exponentially from f_start to f_end."""
    y = np.zeros_like(x); y1 = y2 = 0.0
    n_sweep = max(1, int(sweep_s * SR))
    for n in range(len(x)):
        t = min(n / n_sweep, 1.0)
        f = f_start * (f_end / f_start) ** t
        w = 2 * np.pi * f / SR
        r = np.exp(-w / (2 * q))
        a1, a2 = 2 * r * np.cos(w), -r * r
        v = (1 - r) * x[n] + a1 * y1 + a2 * y2
        y2, y1 = y1, v
        y[n] = v
    return y


def env(n, attack_s, tau_s, delay_s=0.0):
    t = np.arange(n) / SR - delay_s
    e = np.where(t < 0, 0.0, np.where(t < attack_s, t / max(attack_s, 1e-6), np.exp(-(t - attack_s) / tau_s)))
    return e


def thwip(seed, dur=0.17):
    """A movie-style web shooter: a crisp trigger click, a short burst of pressurized gas (the
    pneumatic "tssh"), and a wet sticky splat right behind it. Short and bright, no low end."""
    rng = np.random.default_rng(seed)
    n = int(dur * SR)
    t = np.arange(n) / SR
    j = lambda a, b: rng.uniform(a, b)

    # 1. mechanism: precise and techy, not springy. every part is tightly damped (dies in a few
    #    milliseconds) so nothing rings or wobbles:
    #    - a tiny solenoid "zt" (an electric actuator firing) right at the press
    #    - the latch "tchk": a hard, dry metal tick with a short burst of high modes
    #    - the stop "thock": a lower, dull hit for weight, made from damped noise, not a tone
    def metal_tick(at_s, amp, base, ring_s):
        out = np.zeros(n); k = int(at_s * SR)
        if k >= n: return out
        tt = np.arange(n - k) / SR
        for mi, ratio in enumerate([1.0, 1.47, 2.09, 2.93, 3.61]):
            out[k:] += np.sin(2 * np.pi * base * ratio * tt + rng.uniform(0, 6)) * np.exp(-tt / (ring_s / (1 + 0.3 * mi))) / (1 + 0.3 * mi)
        out[k:] += hp(rng.standard_normal(n - k), 3000) * np.exp(-tt / 0.0005) * 1.5
        return out * amp

    def thock(at_s, amp):
        out = np.zeros(n); k = int(at_s * SR)
        if k >= n: return out
        tt = np.arange(n - k) / SR
        body = sosfilt(butter(2, [180, 1100], 'bandpass', fs=SR, output='sos'), rng.standard_normal(n - k))
        out[k:] = body * np.exp(-tt / 0.0035)
        return out * amp

    def solenoid(at_s, amp, f, length_s):
        out = np.zeros(n); k = int(at_s * SR); m = min(int(length_s * SR), n - k)
        tt = np.arange(m) / SR
        sq = np.sign(np.sin(2 * np.pi * f * tt)) * np.exp(-tt / (length_s / 3))
        out[k:k + m] = sosfilt(butter(2, [800, 6000], 'bandpass', fs=SR, output='sos'), sq)
        return out * amp

    base = j(2900, 3500)
    clack_at = j(0.014, 0.018)
    zt = solenoid(0.0, 0.35, j(950, 1150), j(0.006, 0.008))
    click = (zt / max(np.max(np.abs(zt)), 1e-9) * 0.35
             + metal_tick(j(0.003, 0.005), 1.0, base, j(0.0035, 0.0045))
             + metal_tick(clack_at, 0.8, base * j(0.78, 0.86), j(0.003, 0.004))
             + thock(clack_at, 1.6))
    #    the shooter re-latching after the shot: a quick, small, dry double tick
    rl = j(0.075, 0.09)
    relatch = (metal_tick(rl, 0.45, base * j(1.05, 1.2), 0.0025)
               + metal_tick(rl + j(0.006, 0.009), 0.35, base * j(0.9, 1.0), 0.0025)
               + thock(rl + 0.007, 0.5))

    # 2. pneumatic release: a hard, fast hiss of escaping gas. very short, and the band falls a
    #    little as the pressure drops
    hiss = resonator(rng.standard_normal(n), j(7500, 8500), j(4200, 5000), 1.3, 0.05)
    hiss = hp(hiss, 2500, 2) * env(n, 0.0015, j(0.016, 0.022), 0.002)

    # 3. wet: a real water pop from the bubble sample, sped way up into a tiny "plip" ...
    plip = speed(WET_POP, j(2.1, 2.5))
    plip = hp(plip, 500, 2)
    wet = np.zeros(n); k = int(j(0.006, 0.010) * SR); m = min(len(plip), n - k)
    wet[k:k + m] = norm(plip[:m], 0)
    #    ... and a sticky squelch: a resonant band dropping fast, chopped up irregularly so it
    #    sounds like goo rather than a tone
    chop = np.repeat(rng.uniform(0.2, 1.0, n // 90 + 1), 90)[:n]
    goo = resonator(rng.standard_normal(n), j(2400, 2900), j(800, 1000), 5.0, j(0.03, 0.04))
    goo *= chop * env(n, 0.002, j(0.018, 0.024), j(0.005, 0.008))

    # 4. weight: a short punchy thump so the shot lands like an impact, not a hiss
    f0, f1 = j(170, 200), j(85, 100)
    thump = np.sin(2 * np.pi * np.cumsum(f0 * (f1 / f0) ** np.clip(t / 0.03, 0, 1)) / SR)
    thump = thump * env(n, 0.001, j(0.016, 0.020))
    thump += lp(rng.standard_normal(n), 700) * env(n, 0.001, 0.010) * 0.6
    #    ... and a fuller mid "pff" under the hiss: the body of the gas burst
    pff = resonator(rng.standard_normal(n), j(3000, 3400), j(1300, 1600), 1.2, 0.045)
    pff = pff * env(n, 0.002, j(0.024, 0.030), 0.002)

    #    ... and the web itself: a thin, stringy zip flicking out (bright and light)
    flutter = 1 + 0.5 * np.sign(np.sin(2 * np.pi * j(140, 190) * t)) * np.exp(-t / 0.04)
    zipw = resonator(rng.standard_normal(n), j(6500, 7800), j(2400, 3000), 5.0, j(0.04, 0.055))
    zipw *= flutter * env(n, 0.002, j(0.026, 0.034))

    # the shot fires just after the clack, so the lever is heard on its own first. the web part is
    # kept light (no low end at all: the weight lives in the mechanism) but loud enough to lead
    shot = (norm(hiss, 0) * 0.85 + norm(zipw, 0) * 0.65 + wet * 0.7 + norm(goo, 0) * 0.6 + norm(pff, 0) * 0.3)
    shot = hp(shot, 650, 2)
    d = int((clack_at + 0.011) * SR)   # leave the clack a moment in the clear
    shot = np.concatenate([np.zeros(d), shot])[:n]
    relatch = np.concatenate([np.zeros(d), relatch])[:n]
    mix = norm(click, 0) * 1.0 + norm(shot, 0) * 0.95 + relatch / np.max(np.abs(click)) * 0.9
    mix = hp(lp(mix, 13000), 50, 2)
    fade = int(0.03 * SR)
    mix[-fade:] *= np.linspace(1, 0, fade) ** 2
    last = np.nonzero(np.abs(mix) > np.max(np.abs(mix)) * 0.003)[0][-1] + 1
    mix = np.tanh(1.4 * norm(mix[:last], 0))                  # light glue, keeps the transient
    return norm(mix, -1)


for i, seed in enumerate([5800, 5801, 5802, 5803], 1):
    save(f'WebThwip{i}.wav', thwip(seed))


# ---------------------------------------------------------------- web release whoosh (synthesized)
def whoosh(seed, dur=None):
    """A soft, airy swoosh: smooth noise through a gentle band that rises and falls with a bell
    shaped swell. Mid-range and rounded off on top, so it's soft rather than hissy."""
    rng = np.random.default_rng(seed)
    dur = dur or rng.uniform(0.48, 0.70)
    lo, rise, q = rng.uniform(380, 650), rng.uniform(650, 1150), rng.uniform(0.9, 1.6)
    swell_at = rng.uniform(0.22, 0.38)
    n = int(dur * SR)
    t = np.arange(n) / SR
    # band goes up as he speeds away, then back down
    band = np.zeros(n); y1 = y2 = 0.0
    x = rng.standard_normal(n)
    peak = rng.uniform(0.38, 0.45)
    for i in range(n):
        u = i / n
        fc = lo + rise * np.sin(np.pi * u) ** 1.5
        w = 2 * np.pi * fc / SR; r = np.exp(-w / (2 * q))
        v = (1 - r) * x[i] + 2 * r * np.cos(w) * y1 - r * r * y2
        y2, y1 = y1, v; band[i] = v
    u = np.clip(t / dur, 0, 1)
    swell = np.where(u < swell_at, np.abs(np.sin(np.pi / 2 * np.minimum(u, swell_at) / swell_at)) ** 1.5,
                     np.cos(np.pi / 2 * (u - swell_at) / (1 - swell_at)) ** 2)  # quick swell, long easing tail
    out = lp(hp(band * swell, 200), 3500, 2)
    return norm(out, -6)


for i, seed in enumerate([7300, 7301, 7302, 7303], 1):
    save(f'WebWhoosh{i}.wav', whoosh(seed))


# ---------------------------------------------------------------- jump (synthesized)
def jump(seed, dur=0.18):
    """A jump with weight but still quick: a solid low push-off thump, a short shoe scuff, and a
    cloth flick that rises as he springs up. Punchy start, short tail, nothing hissy."""
    rng = np.random.default_rng(seed)
    n = int(dur * SR)
    t = np.arange(n) / SR
    j = lambda a, b: rng.uniform(a, b)
    # push-off: low thump that drops in pitch (the legs driving into the roof)
    f0, f1 = j(105, 155), j(55, 75)
    thump = np.sin(2 * np.pi * np.cumsum(f0 * (f1 / f0) ** np.clip(t / 0.04, 0, 1)) / SR)
    thump *= env(n, 0.002, j(0.026, 0.032))
    # scuff: gritty sole on concrete
    scuff = sosfilt(butter(2, [j(280, 450), j(1300, 2000)], 'bandpass', fs=SR, output='sos'), rng.standard_normal(n))
    scuff *= env(n, 0.001, j(0.014, 0.018))
    # flick: a fuller cloth swish rising as he springs up
    flick = resonator(rng.standard_normal(n), j(950, 1500), j(2800, 4200), j(1.1, 1.8), j(0.06, 0.10))
    flick = hp(flick, 700) * env(n, j(0.010, 0.014), j(0.035, 0.045), 0.006)
    mix = norm(thump, 0) * j(0.75, 0.9) + norm(scuff, 0) * j(0.3, 0.55) + norm(flick, 0) * j(0.5, 0.7)
    mix = hp(lp(mix, 9000), 45)
    fade = int(0.04 * SR)
    mix[-fade:] *= np.linspace(1, 0, fade) ** 2
    return norm(np.tanh(1.3 * norm(mix, 0)), -2.5)


for i, seed in enumerate([9100, 9101, 9102, 9103, 9104], 1):
    save(f'NimbleJump{i}.wav', jump(seed))


# ---------------------------------------------------------------- web tension (seamless loop)
def tension_loop(seed, loop_s=2.0, xfade_s=0.3):
    """A rope held at its limit, steady: one continuous stick-slip creak (fibers grabbing and
    slipping over and over at an even, slightly jittery rate, each grab ringing the rope's body),
    with a constant fizz of fiber crackle on top. Even all the way through, so it reads as tension
    rather than as separate events. The tail is cross-faded into the head so it loops cleanly."""
    rng = np.random.default_rng(seed)
    n = int(loop_s * SR) + int(xfade_s * SR)
    t = np.arange(n) / SR

    creak = np.zeros(n)
    body = np.array([520.0, 1190.0, 1730.0])          # the rope's resonances (fixed: one rope)
    pos = 0.0
    while pos < n / SR - 0.001:
        k = int(pos * SR); m = min(n - k, 600)
        tt = np.arange(m) / SR
        g = sum(np.sin(2 * np.pi * f * rng.uniform(0.97, 1.03) * tt) * np.exp(-tt / (0.004 / (1 + i))) / (1 + i)
                for i, f in enumerate(body))
        g = g + rng.standard_normal(m) * np.exp(-tt / 0.0005) * 0.7
        creak[k:k + m] += g * rng.uniform(0.75, 1.0)
        pos += 1.0 / 42.0 * rng.uniform(0.7, 1.35)    # ~42 grabs a second, unevenly spaced
    # a very gentle, slow breathing in strength (whole cycles, so the loop lines up)
    creak *= 0.85 + 0.15 * np.sin(2 * np.pi * (2 / loop_s) * t)

    crackle = np.zeros(n)
    for k in np.nonzero(rng.random(n) < 110 / SR)[0]:
        m = min(n - k, 60)
        crackle[k:k + m] += rng.uniform(-1, 1) * np.exp(-np.arange(m) / 6)
    crackle = hp(crackle, 2400, 2)

    mix = norm(creak, 0) + norm(crackle, 0) * 0.3
    mix = lp(hp(mix, 250), 8000)
    L, X = int(loop_s * SR), int(xfade_s * SR)
    out = mix[:L].copy()
    w = np.linspace(0, 1, X)
    out[:X] = mix[:X] * np.sqrt(w) + mix[L:L + X] * np.sqrt(1 - w)
    return norm(out, -6)


save('WebTension.wav', tension_loop(4200))


# ---------------------------------------------------------------- falling scream
def falling_scream(path):
    """The "no!" as he falls away: the first moment stays as is, then the voice slowly drops in
    pitch (like moving away), gets duller and quieter, and a long city echo lets it drone out
    instead of cutting off."""
    x = load(path)
    x = x[np.argmax(np.abs(x) > np.max(np.abs(x)) * 0.02):]
    # pitch: play rate glides from 1.0 down to 0.84 after the first 0.35s (about 3 semitones lower,
    # and a little longer)
    hold = int(0.35 * SR)
    rate = np.concatenate([np.ones(hold), np.linspace(1.0, 0.84, max(1, len(x) - hold))])
    idx = np.cumsum(rate); idx = idx[idx < len(x) - 1]
    y = np.interp(idx, np.arange(len(x)), x)
    u = np.linspace(0, 1, len(y))
    # distance: cross-fade toward a muffled copy, and fade down (about -18 dB by the end)
    far = np.clip((u - 0.15) / 0.85, 0, 1)
    y = y * (1 - far) + lp(y, 1100, 2) * far
    y *= 10 ** (-18 * far ** 1.3 / 20)
    # the drone: a long, dark echo tail that rises as he gets farther away
    tail_s = 2.4
    n_ir = int(tail_s * SR)
    rng = np.random.default_rng(31)
    ir = rng.standard_normal(n_ir) * np.exp(-np.arange(n_ir) / SR / 0.7)
    ir = lp(ir, 2500, 2); ir[:int(0.03 * SR)] = 0   # a short gap before the echo arrives
    wet = np.convolve(y * (0.3 + 0.7 * far), ir)
    out = np.zeros(len(wet))
    out[:len(y)] += norm(y, 0)
    out += norm(wet, 0) * 0.45
    f = int(1.2 * SR)
    out[-f:] *= np.linspace(1, 0, f) ** 2
    out = out[:np.nonzero(np.abs(out) > np.max(np.abs(out)) * 0.002)[0][-1] + 1]   # drop the silent end
    return norm(out, -2)


save('ScreamFall.wav', falling_scream(f'{IN}/scream.mp3'))
