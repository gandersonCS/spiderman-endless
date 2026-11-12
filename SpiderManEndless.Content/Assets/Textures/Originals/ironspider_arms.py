"""Usage: python ironspider_arms.py ironspider_emblems.json ../spiderman_ironspider_arms.png

Draws Iron Spider's gold spider arms for the menu attack frames onto an overlay sheet.

Layout (must match Views/SpiderArms.cs): one CELL_W x CELL_H cell per attack frame,
row 0 = Jab Punch, row 1 = Upward Kick, row 2 = Uppercut (15 frames incl. landing), row 3 = Air Kick (2 frames).
Each frame sits centered in its cell: offset = ((CELL_W - w) // 2, (CELL_H - h) // 2).
"""
import sys, json, math
from PIL import Image
from ironspider_frames import F
emb={k:v for k,v in json.load(open(sys.argv[1])).items() if not k.startswith('_')}; out=sys.argv[2]
CELL_W, CELL_H = 96, 112
ROWS=[('jab',7,[0.35,0.75,1,1,1,0.6,0.25]),
      ('kick',7,[0.35,0.75,1,1,1,0.6,0.25]),
      ('upper',15,[0.3,0.65,1,1,1,1,1,1,1,1,0.75,0.55,0.35,0.15,0]),
      ('airkick',2,[0.6,1])]
GOLD=(248,184,0,255); GOLD_D=(216,160,16,255); TIP=(255,224,96,255); BLACK=(0,0,0,255)
sheet=Image.new('RGBA',(CELL_W*15,CELL_H*len(ROWS)),(0,0,0,0)); sp=sheet.load()
def frame_for(group,i):
  if group=='upper' and i>=10: return 'land',i-10
  return group,i
for row,(group,n,ext) in enumerate(ROWS):
  for i in range(n):
    g,k=frame_for(group,i); x,y,w,h=F[g][k]
    nx,ny,hx,hy,hw,view=emb[f'{g}:{k}']
    ox,oy=i*CELL_W+(CELL_W-w)//2, row*CELL_H+(CELL_H-h)//2
    e=ext[i]
    if e<=0: continue
    ax,ay=hx-nx,hy-ny; L=math.hypot(ax,ay); dx,dy=ax/L,ay/L; ux,uy=-dy,dx
    Ax,Ay=ox+nx+ax*0.3+0.5, oy+ny+ay*0.3+0.5
    lines=set(); tips=set()
    for s in (-1,1):
      for (ku,kv),(tu,tv) in [((12,-12),(9,6)), ((14,-3),(8,12))]:
        Kx=Ax+e*(s*ku*ux+kv*dx); Ky=Ay+e*(s*ku*uy+kv*dy)
        Tx=Kx+e*(s*tu*ux+tv*dx); Ty=Ky+e*(s*tu*uy+tv*dy)
        for (x0,y0),(x1,y1) in [((Ax,Ay),(Kx,Ky)),((Kx,Ky),(Tx,Ty))]:
          m=int(max(abs(x1-x0),abs(y1-y0))*2)+1
          for t in range(m+1):
            lines.add((int(x0+(x1-x0)*t/m), int(y0+(y1-y0)*t/m)))
        tips.add((int(Tx),int(Ty)))
    cell=lambda p: row*CELL_H<=p[1]<(row+1)*CELL_H and i*CELL_W<=p[0]<(i+1)*CELL_W
    lines={p for p in lines if cell(p)}
    # black outline first, then the gold on top (lower/right side a shade darker for some depth)
    for (px,py) in lines:
      for ddx,ddy in ((1,0),(-1,0),(0,1),(0,-1)):
        q=(px+ddx,py+ddy)
        if cell(q) and q not in lines: sp[q]=BLACK
    for p in lines: sp[p]=GOLD
    for (px,py) in lines:
      if (px,py+1) not in lines and (px+1,py) not in lines and (px,py+1) in lines: pass
    for p in tips:
      if cell(p): sp[p]=TIP
sheet.save(out)
print(sheet.size)
