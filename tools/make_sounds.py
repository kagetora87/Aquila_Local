"""Generates Aquila's interface sounds (src/Sounds/*.wav): 44.1 kHz, 16-bit, mono.

Soft sine tones at low level, as specified in the redesign plan (section 7).
Run: python3 tools/make_sounds.py
"""
import math, os, random, struct, wave

RATE = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Sounds")


def env(t, attack, tau):
    if t < attack:
        return t / attack
    return math.exp(-(t - attack) / tau)


def glide(f0, f1, glide_s):
    """Exponential frequency glide; returns phase function."""
    def freq(t):
        if t >= glide_s:
            return f1
        return f0 * (f1 / f0) ** (t / glide_s)
    return freq


def render(length_s, voices, level):
    n = int(RATE * length_s)
    buf = [0.0] * n
    for v in voices:
        start = int(RATE * v.get("at", 0.0))
        phase = 0.0
        freq = v["freq"]
        for i in range(start, n):
            t = (i - start) / RATE
            f = freq(t) if callable(freq) else freq
            phase += 2 * math.pi * f / RATE
            buf[i] += v.get("amp", 1.0) * env(t, v["attack"], v["tau"]) * math.sin(phase)
    peak = max(abs(x) for x in buf) or 1.0
    buf = [x / peak * level for x in buf]
    fade = int(RATE * 0.005)
    for i in range(fade):
        g = 0.5 * (1 - math.cos(math.pi * i / fade))
        buf[n - 1 - i] *= g
    return buf


def chime_note(f, at, attack, tau, partials):
    out = [dict(freq=f, at=at, attack=attack, tau=tau, amp=1.0)]
    for k, a in partials:
        out.append(dict(freq=f * k, at=at, attack=attack, tau=tau * 0.7, amp=a))
    return out


def write(name, samples):
    os.makedirs(OUT, exist_ok=True)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", max(-32767, min(32767, int(x * 32767)))) for x in samples))


def main():
    write("send", render(0.110, [dict(freq=glide(620, 880, 0.070), attack=0.004, tau=0.035)], 0.16))
    write("reply", render(0.520,
        chime_note(659.25, 0.0, 0.008, 0.130, [(2, 0.18), (3, 0.06)]) +
        chime_note(987.77, 0.095, 0.008, 0.130, [(2, 0.18), (3, 0.06)]), 0.15))
    ready = []
    for f, at in ((523.25, 0.0), (659.25, 0.08), (783.99, 0.16)):
        ready += chime_note(f, at, 0.025, 0.240, [(2, 0.12)])
    ready.append(dict(freq=261.63, at=0.0, attack=0.025, tau=0.300, amp=0.3))
    write("ready", render(0.800, ready, 0.14))
    write("stop", render(0.120, [dict(freq=glide(660, 440, 0.080), attack=0.004, tau=0.040)], 0.13))
    notice = []
    for at in (0.0, 0.130):
        notice.append(dict(freq=311.13, at=at, attack=0.005, tau=0.045, amp=1.0))
        notice.append(dict(freq=233.08, at=at, attack=0.005, tau=0.045, amp=0.6))
    write("notice", render(0.260, notice, 0.16))
    # Copy: tiny tick plus a short burst of band-passed noise.
    tick = render(0.030, [dict(freq=1400, attack=0.001, tau=0.008)], 1.0)
    random.seed(7)
    n = len(tick)
    noise = [0.0] * n
    y1 = y2 = 0.0
    w0 = 2 * math.pi * 3000 / RATE
    alpha = math.sin(w0) / (2 * 2.0)
    b0, a0, a1, a2 = alpha, 1 + alpha, -2 * math.cos(w0), 1 - alpha
    x1 = x2 = 0.0
    for i in range(int(RATE * 0.010)):
        x = random.uniform(-1, 1) * (1 - i / (RATE * 0.010))
        y = (b0 * x - b0 * x2 - a1 * y1 - a2 * y2) / a0
        x2, x1, y2, y1 = x1, x, y1, y
        noise[i] = y
    npk = max(abs(v) for v in noise) or 1.0
    mix = [tick[i] + 0.3 * noise[i] / npk for i in range(n)]
    pk = max(abs(v) for v in mix)
    write("copy", [v / pk * 0.09 for v in mix])
    write("attach", render(0.070, [dict(freq=glide(300, 520, 0.040), attack=0.002, tau=0.022)], 0.12))
    write("delete", render(0.180, [dict(freq=glide(330, 165, 0.140), attack=0.005, tau=0.060)], 0.13))


if __name__ == "__main__":
    main()
