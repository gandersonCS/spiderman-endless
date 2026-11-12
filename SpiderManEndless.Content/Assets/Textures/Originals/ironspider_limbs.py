"""Finds hands/feet for the Iron Spider recolor (gold boots and gauntlets)."""
# hands & feet per frame: walk the sprite outward from the eyes; limb tips are where the walk ends
from collections import deque
N8=[(dx,dy) for dx in (-1,0,1) for dy in (-1,0,1) if dx or dy]
RED={(216,0,0),(144,0,0),(248,88,120)}
BLUE={(0,0,200),(0,0,120),(0,88,248),(80,160,248),(0,0,248),(0,152,248)}
def walk(c, rect, eyes, trail):
  fx,fy,fw,fh=rect
  inside=lambda p: fx<=p[0]<fx+fw and fy<=p[1]<fy+fh
  start=[p for p in eyes if inside(p)]
  dist={p:0 for p in start}; q=deque(start)
  while q:
    p=q.popleft()
    for dx,dy in N8:
      n=(p[0]+dx,p[1]+dy)
      if n not in dist and inside(n) and c(*n) is not None and n not in trail:
        dist[n]=dist[p]+1; q.append(n)
  return dist
def back(dist, tip, hi):
  # walking distance back toward the head from a tip, along this limb only
  td={tip:0}; q=deque([tip])
  while q:
    p=q.popleft()
    if td[p]>=hi: continue
    for dx,dy in N8:
      n=(p[0]+dx,p[1]+dy)
      if n in dist and n not in td and dist[n]<=dist[p]:
        td[n]=td[p]+1; q.append(n)
  return td
def limbs(c, rect, eyes, trail, min_d=9):
  dist=walk(c,rect,eyes,trail)
  maxima=[p for p,d in dist.items() if d>=min_d and all(dist.get((p[0]+dx,p[1]+dy),-1)<=d for dx,dy in N8)]
  maxima.sort(key=lambda p:-dist[p])
  tips=[]
  for p in maxima:
    # same hand/foot if close to a kept tip
    if all(max(abs(p[0]-t[0]),abs(p[1]-t[1]))>5 for t,_ in tips):
      td=back(dist,p,14)
      far=[q for q,d in td.items() if 6<=d<=14]
      cols=[c(*q) for q in far]
      blue=sum(1 for k in cols if k in BLUE); red=sum(1 for k in cols if k in RED)
      tips.append((p,'foot' if blue>red else 'hand'))
  return dist,tips

def boots_and_gauntlets(c, rect, eyes, trail, axis=None):
  """Returns (boot_px, gauntlet_px) for one frame."""
  dist,tips=limbs(c,rect,eyes,trail,min_d=8)
  boots=set(); gaunt=set()
  # boots: red just beyond where a blue leg ends (farther from the head than the nearby blue)
  for p,d in dist.items():
    if c(*p) not in RED: continue
    near_blue=[dist[q] for dx in range(-4,5) for dy in range(-4,5)
               for q in [(p[0]+dx,p[1]+dy)] if q in dist and c(*q) in BLUE and c(*q)!=(80,160,248)]
    if near_blue and d>=max(near_blue)-1 and d>=10:
      if axis and _near_axis(p,axis): continue
      boots.add(p)
  # gauntlets: band behind tips whose limb is red (arms), not blue (legs)
  for tip,kind in tips:
    td=back(dist,tip,14)
    cols=[c(*q) for q,dd in td.items() if 5<=dd<=12]
    red=sum(1 for k in cols if k in RED); blue=sum(1 for k in cols if k in BLUE)
    if red<4 or blue>red*0.35: continue
    if axis and _near_axis(tip,axis,3): continue
    for q,dd in td.items():
      if 2<=dd<=4 and c(*q) in RED: gaunt.add(q)
  return boots-gaunt, gaunt

def _near_axis(p,axis,extra=1):
  (nx,ny,hx,hy,hw)=axis
  ax,ay=hx-nx,hy-ny; L2=ax*ax+ay*ay or 1
  t=max(-0.15,min(1.1,((p[0]-nx)*ax+(p[1]-ny)*ay)/L2))
  qx,qy=nx+ax*t,ny+ay*t
  return (p[0]-qx)**2+(p[1]-qy)**2 <= (hw+extra)**2
