"""Darkens the task force soldier sheet: same red and blue, but murkier and more militaristic.
Run: python recolor_soldier.py <in.png> <out.png>"""
import sys
from PIL import Image
import numpy as np

PALETTE = {
    (121, 155, 244): (50, 62, 96),     # light blue armor -> dark navy
    (149, 175, 247): (76, 90, 126),    # blue highlight   -> steel blue
    (28, 89, 140):   (24, 32, 54),     # dark blue        -> near-black navy
    (100, 100, 100): (54, 56, 58),     # gray gear        -> gunmetal
    (55, 55, 55):    (30, 31, 33),
    (163, 163, 163): (92, 94, 96),
    (140, 32, 33):   (92, 16, 18),     # reds -> deep crimson
    (225, 36, 15):   (150, 22, 18),
    (239, 90, 90):   (178, 48, 44),
}

a = np.array(Image.open(sys.argv[1]).convert("RGBA"))
for old, new in PALETTE.items():
    mask = (a[:, :, 0] == old[0]) & (a[:, :, 1] == old[1]) & (a[:, :, 2] == old[2]) & (a[:, :, 3] > 0)
    a[mask, :3] = new
Image.fromarray(a).save(sys.argv[2])
