"""Builds Textures/spiderman_ironspider.png (Iron Spider costume) from Textures/spiderman.png.

Usage (from this folder):
    python recolor_ironspider.py ../spiderman.png ../spiderman_ironspider.png ironspider_emblems.json [hand_painted.png]

ironspider_emblems.json marks each frame's torso (neck point, hip point, half width, front/back) so the
gold spider emblem and shoulder pads follow his body, plus "_parts": hand-placed boots/gauntlets where
the automatic rules miss. Boots and gauntlets are otherwise found from the classic suit's colors (red
boots past the blue legs, a band behind each red hand). The optional last sheet is copied on top
wherever it has gold pixels, so hand touch-ups survive a rebuild.
Needs Pillow, numpy and scipy (pip install pillow numpy scipy).
"""
import json
# Red & gold suit from the classic sheet (follows the hand-edited example frame):
#  - one red suit using the example's 4 reds, shading kept (shadows stay dark)
#  - gold eyes, gold trim where the classic red meets blue (shoulders / chest emblem)
#  - gold boot tops (the classic red boots, minus the sole) and gold gauntlets (band behind each hand)
import sys
from collections import deque
from ironspider_frames import F
from ironspider_limbs import boots_and_gauntlets
from PIL import Image
src, dst = sys.argv[1], sys.argv[2]
EMB = json.load(open(sys.argv[3])) if len(sys.argv) > 3 else {}
PARTS = EMB.pop('_parts', {})
from ironspider_frames import F
R_,r_,P_=(216,0,0),(144,0,0),(248,88,120)
B_,b_,L_,l_=(0,0,200),(0,0,120),(0,88,248),(80,160,248)
RED={R_,r_,P_}; BLUE={B_,b_,L_,l_,(0,0,248),(0,152,248)}
HI=(255,120,120); BRIGHT=(255,0,0); MID=(201,0,0); DARK=(139,0,0)
GOLD=(248,184,0); GOLD_D=(216,160,16)
BASE={R_:MID, r_:DARK, P_:HI, B_:BRIGHT, (0,0,248):BRIGHT, b_:DARK, L_:MID, (0,152,248):MID, l_:HI}
GOLDMAP={R_:GOLD, P_:GOLD, r_:GOLD_D}
TRIM_REACH=11
im=Image.open(src).convert('RGBA'); px=im.load(); W,H=im.size
def c(x,y):
  if 0<=x<W and 0<=y<H and px[x,y][3]: return px[x,y][:3]
  return None
N8=[(dx,dy) for dx in (-1,0,1) for dy in (-1,0,1) if dx or dy]
# eyes
eyes=set()
for y in range(H):
  for x in range(W):
    if c(x,y)==l_:
      nb=[c(x+dx,y+dy) for dx,dy in N8]
      if (0,0,0) in nb and any(n in RED for n in nb) and not any(n in BLUE and n!=l_ for n in nb): eyes.add((x,y))
st=list(eyes)
while st:
  x,y=st.pop()
  for dx,dy in N8:
    n=(x+dx,y+dy)
    if n not in eyes and c(*n)==l_: eyes.add(n); st.append(n)
gold=set()
# motion trails: red touching the background, grown through red with no black nearby, big pieces only
seeds=set()
for y in range(H):
  for x in range(W):
    if c(x,y) in RED and any(c(x+dx,y+dy) is None for dx,dy in N8): seeds.add((x,y))
grown=set(seeds); st=list(seeds)
while st:
  x,y=st.pop()
  for dx,dy in N8:
    n=(x+dx,y+dy)
    if n in grown or c(*n) not in RED: continue
    if any(c(n[0]+a,n[1]+b)==(0,0,0) for a,b in N8): continue
    grown.add(n); st.append(n)
trail=set()
_left=set(grown)
while _left:
  s0=_left.pop(); comp=[s0]; q=[s0]
  while q:
    x,y=q.pop()
    for dx,dy in N8:
      n=(x+dx,y+dy)
      if n in _left: _left.remove(n); comp.append(n); q.append(n)
  if len(comp)>=8: trail.update(comp)
def red_components(rect):
  fx,fy,fw,fh=rect; seen=set(); comps=[]
  for y in range(fy,fy+fh):
    for x in range(fx,fx+fw):
      if (x,y) in seen or c(x,y) not in RED: continue
      comp=[(x,y)]; seen.add((x,y)); q=[(x,y)]
      while q:
        cx,cy=q.pop()
        for dx,dy in N8:
          n=(cx+dx,cy+dy)
          if n not in seen and fx<=n[0]<fx+fw and fy<=n[1]<fy+fh and c(*n) in RED: seen.add(n); comp.append(n); q.append(n)
      comps.append(comp)
  return comps
import numpy as np
from scipy import ndimage
alpha=np.array(im)[:,:,3]>0
lab,n=ndimage.label(ndimage.binary_dilation(alpha,iterations=2),structure=np.ones((3,3)))
rects=[]
for sl in ndimage.find_objects(lab):
  ys,xs=sl; rects.append((xs.start,ys.start,xs.stop-xs.start,ys.stop-ys.start))
ANNOT={tuple(F[k.split(':')[0]][int(k.split(':')[1])]):v for k,v in EMB.items() if not k.startswith('_')}
emblem_px=set()
for rect in rects:
  annotated=[r for r in ANNOT if r[0]<=rect[0]+rect[2]//2<r[0]+r[2] and r[1]<=rect[1]+rect[3]//2<r[1]+r[3]]
  comps=red_components(rect)
  # head = component holding the eyes (or the biggest one)
  head=None
  for comp in comps:
    if any((x+dx,y+dy) in eyes for x,y in comp for dx,dy in N8): head=comp; break
  if head is None and comps: head=max(comps,key=len)
  for comp in comps:
    if comp is head: continue
    touches_blue=any(c(x+dx,y+dy) in BLUE for x,y in comp for dx,dy in N8)
    cy=sum(y for x,y in comp)/len(comp)
    low=cy>=rect[1]+rect[3]*0.45          # boots sit in the lower half; a red arm up top isn't one
    bare=sum(1 for x,y in comp if any(c(x+dx,y+dy) is None for dx,dy in N8))
    outlined=bare<=len(comp)*0.3          # motion trails have no black outline
    if touches_blue and low and outlined and 4<=len(comp)<=45:
      # boot: gild everything but the sole (lowest row) and the toe-tip column furthest from the leg
      bottom=max(y for x,y in comp)
      for p in comp:
        if p[1]<bottom: gold.add(p)
  if annotated:
    ann=ANNOT[annotated[0]]; fr=annotated[0]
    axis=(fr[0]+ann[0],fr[1]+ann[1],fr[0]+ann[2],fr[1]+ann[3],ann[4])
    bt,ga=boots_and_gauntlets(c,fr,eyes,trail,axis)
    gold.update(bt); gold.update(ga)
  if head is None: continue
  # gauntlets: walk the head/torso/arms red out from the eyes; the far ends are the hands
  hs=set(head)-trail   # don't walk out along a motion trail
  start=[p for p in hs if any((p[0]+dx,p[1]+dy) in eyes for dx,dy in N8)] or [head[0]]
  dist={p:0 for p in start}; q=deque(start)
  while q:
    p=q.popleft()
    for dx,dy in N8:
      n=(p[0]+dx,p[1]+dy)
      if n in hs and n not in dist: dist[n]=dist[p]+1; q.append(n)
  for p,d in ([] if annotated else dist.items()):
    if d<=TRIM_REACH and any(c(p[0]+dx,p[1]+dy) in BLUE and (p[0]+dx,p[1]+dy) not in eyes for dx,dy in N8):
      gold.add(p)
  used=set()
  for _ in range(2):
    cand=[p for p in dist if p not in used and dist[p]>=10]
    if not cand: break
    tip=max(cand,key=lambda p:dist[p])
    # distance back from this tip
    td={tip:0}; q=deque([tip])
    while q:
      p=q.popleft()
      for dx,dy in N8:
        n=(p[0]+dx,p[1]+dy)
        if n in dist and n not in td and td[p]<9: td[n]=td[p]+1; q.append(n)
    for p,d in td.items():
      used.add(p)
      if 2<=d<=4 and dist[p]<dist[tip]-1: gold.add(p)
def paint_emblem(fr, ann):
  fx,fy,fw,fh=fr; nx,ny,hx,hy,hw,view=ann
  import math
  ax,ay=hx-nx,hy-ny; L=math.hypot(ax,ay); dx,dy=ax/L,ay/L; ux,uy=-dy,dx
  def P(u,v): return (fx+nx+0.5+ux*u*hw+dx*v*L, fy+ny+0.5+uy*u*hw+dy*v*L)
  segs=[((0,0.15),(0,0.6))]
  for sgn in (-1,1):
    segs+= [((0,0.22),(sgn*1.0,0.0)), ((0,0.5),(sgn*0.8,0.95))]
  for (u0,v0),(u1,v1) in segs:
    (x0,y0),(x1,y1)=P(u0,v0),P(u1,v1)
    n=int(max(abs(x1-x0),abs(y1-y0))*3)+1
    for i in range(n+1):
      t=i/n; x=int(x0+(x1-x0)*t); y=int(y0+(y1-y0)*t)
      if fx<=x<fx+fw and fy<=y<fy+fh and c(x,y) in RED|BLUE and (x,y) not in eyes: emblem_px.add((x,y))
  # shoulder pads: the pixel at each shoulder and its suit neighbours
  for sgn in (-1,1):
    sx,sy=P(sgn*1.0,0.02)
    for ddx in (-1,0):
      for ddy in (-1,0):
        x,y=int(sx+ddx+0.5),int(sy+ddy+0.5)
        if c(x,y) in RED|BLUE and (x,y) not in eyes: emblem_px.add((x,y))
for fr,ann in ANNOT.items(): paint_emblem(fr,ann)
# hand-placed boots ("b") and gauntlets ("g"): the classic red glove/boot pixels around each point
for key,pts in PARTS.items():
  if key.startswith('_'): continue
  g,i=key.split(':'); fx,fy,fw,fh=F[g][int(i)]
  for kind,px_,py_ in pts:
    rad=2.3 if kind=='b' else 1.6
    for yy in range(fy+py_-3,fy+py_+4):
      for xx in range(fx+px_-3,fx+px_+4):
        if (xx-fx-px_)**2+(yy-fy-py_)**2<=rad*rad and fx<=xx<fx+fw and fy<=yy<fy+fh and c(xx,yy) in RED and (xx,yy) not in eyes:
          gold.add((xx,yy))
out=im.copy(); op=out.load()
for y in range(H):
  for x in range(W):
    col=c(x,y)
    if col is None: continue
    if (x,y) in eyes: op[x,y]=GOLD+(255,)
    elif (x,y) in emblem_px: op[x,y]=(GOLD_D if col in (r_,b_) else GOLD)+(255,)
    elif (x,y) in gold and col in GOLDMAP: op[x,y]=GOLDMAP[col]+(255,)
    elif col in BASE: op[x,y]=BASE[col]+(255,)
out.save(dst)
