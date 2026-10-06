"""Own offline illustrations: shaded geometric solids, never a browser renderer.
Requires Pillow only to regenerate; the app consumes the exported WebP files.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math
OUT = Path(__file__).resolve().parents[2] / 'Aquarella/wwwroot/images/products'
PALETTE = ['#5189e8','#48ae86','#eac14d','#e46a74','#9a76db','#ee9852']
TYPES = ['notebook','pencil','pen','ruler','eraser','scissors','glue','folder','paper','ball','car','toy','gift','printer','generic']
S=4

def rgb(h): return tuple(bytes.fromhex(h.lstrip('#')))
def shade(c,f): return tuple(round(min(255,max(0,v*f))) for v in c)
def render(kind, color):
 c=rgb(color); im=Image.new('RGBA',(112*S,112*S)); shadow=Image.new('RGBA',im.size)
 ImageDraw.Draw(shadow).ellipse((23*S,85*S,92*S,99*S),fill=(37,49,75,38))
 im.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(4*S)))
 def poly(points,col): ImageDraw.Draw(im).polygon([(int(x*S),int(y*S)) for x,y in points],fill=col)
 def line(points,col,width=1): ImageDraw.Draw(im).line([(int(x*S),int(y*S)) for x,y in points],fill=col,width=int(width*S),joint='curve')
 def ellipse(box,col): ImageDraw.Draw(im).ellipse(tuple(int(v*S) for v in box),fill=col)
 def rounded(box,col,r=5): ImageDraw.Draw(im).rounded_rectangle(tuple(int(v*S) for v in box),radius=int(r*S),fill=col)
 def gradient(points,col):
  mask=Image.new('L',im.size);ImageDraw.Draw(mask).polygon([(int(x*S),int(y*S)) for x,y in points],fill=255)
  layer=Image.new('RGBA',im.size);d=ImageDraw.Draw(layer)
  for y in range(im.height):
   t=y/im.height; k=1.22-.38*t;d.line((0,y,im.width,y),fill=shade(col,k)+(255,))
  im.paste(layer,(0,0),mask)
 def box(x,y,w,h,depth,col):
  gradient([(x,y),(x+w,y+7),(x+w,y+h+7),(x,y+h)],col)
  poly([(x,y),(x+depth,y-9),(x+w+depth,y-2),(x+w,y+7)],shade(col,1.28))
  gradient([(x+w,y+7),(x+w+depth,y-2),(x+w+depth,y+h-2),(x+w,y+h+7)],shade(col,.74))
  line([(x+1,y+1),(x+w,y+8)],shade(col,1.4),1)
 if kind=='notebook':
  box(28,27,44,58,12,(220,228,238));box(25,23,47,4,15,c)
  poly([(25,23),(72,30),(72,87),(25,80)],c)
  gradient([(29,24),(72,30),(72,86),(29,80)],c)
  for y in range(32,77,9): rounded((22,y,31,y+3),(220,230,244),1)
  line([(34,29),(34,78)],shade(c,.85),1)
 elif kind in ('pencil','pen'):
  gradient([(30,78),(69,20),(78,26),(39,85)],c)
  poly([(39,85),(78,26),(83,31),(44,88)],shade(c,.72))
  poly([(30,78),(39,85),(44,88),(25,99)],(231,187,133) if kind=='pencil' else (197,210,224))
  poly([(25,99),(29,90),(33,95)],(52,62,80))
  if kind=='pencil': poly([(69,20),(75,12),(88,23),(83,31),(78,26)],(231,164,173))
  else: line([(78,29),(72,32),(52,61)],(225,233,243),3);line([(74,20),(80,12)],(215,223,235),3)
  line([(33,75),(71,24)],shade(c,1.45),2)
 elif kind=='ruler':
  box(20,45,68,17,8,c)
  for x in range(26,86,7): line([(x,47+(x-20)*7/68),(x,54+(x-20)*7/68)],shade(c,.57),1)
 elif kind=='eraser':
  box(27,47,46,23,14,(237,216,209));gradient([(27,47),(56,51),(56,74),(27,70)],c)
  poly([(27,47),(41,38),(70,42),(56,51)],shade(c,1.23))
 elif kind=='scissors':
  poly([(48,57),(51,18),(55,12),(59,58)],(195,209,222));poly([(53,57),(85,28),(90,24),(63,64)],(225,233,241))
  line([(52,21),(53,54)],(247,249,252),2)
  for b in [(21,58,53,91),(53,64,86,96)]:
   ellipse(b,shade(c,.72));ellipse((b[0],b[1]-4,b[2],b[3]-4),c);ellipse((b[0]+8,b[1]+5,b[2]-8,b[3]-13),(0,0,0,0))
  ellipse((49,52,61,64),(143,159,183));ellipse((52,54,58,60),(236,242,249))
 elif kind=='glue':
  box(35,40,30,43,11,(230,237,246));box(35,60,30,14,11,c)
  rounded((38,23,75,42),shade(c,.8),5);rounded((36,19,73,37),c,5)
  poly([(50,20),(51,10),(63,10),(66,22)],shade(c,1.12))
 elif kind=='folder':
  box(22,38,60,45,9,shade(c,.8));poly([(22,38),(24,28),(48,31),(54,39),(82,45),(82,87),(22,80)],c)
  gradient([(23,45),(83,52),(76,88),(18,80)],shade(c,1.1))
 elif kind=='paper':
  box(24,39,53,33,12,(227,234,243))
  for y in range(46,71,5): line([(25,y),(76,y+7)],(192,208,226),1)
  poly([(24,39),(36,30),(89,37),(77,46)],(248,250,253))
  poly([(48,42),(60,33),(74,35),(62,44),(62,77),(48,75)],c)
 elif kind=='ball':
  ellipse((25,27,90,92),shade(c,.72));ellipse((23,22,87,86),c)
  ellipse((29,26,61,53),shade(c,1.22));line([(25,49),(43,61),(65,61),(85,51)],shade(c,.64),2)
  line([(50,24),(43,43),(47,62),(65,85)],shade(c,.68),2)
 elif kind=='car':
  for x in (29,73): ellipse((x,66,x+19,91),(43,52,67));ellipse((x+5,71,x+14,84),(180,199,218))
  box(20,57,60,21,12,c);poly([(33,57),(43,35),(68,38),(79,59)],shade(c,1.18))
  poly([(43,40),(51,41),(50,56),(36,54)],(177,220,239));poly([(54,42),(66,44),(74,59),(54,57)],(177,220,239))
  rounded((22,67,32,72),(250,237,177),2);line([(24,59),(79,66)],shade(c,1.3),2)
 elif kind=='toy':
  box(34,53,31,27,9,c);box(46,25,25,20,7,shade(c,1.08))
  rounded((27,53,38,78),shade(c,.85));rounded((71,56,81,79),shade(c,.85))
  rounded((37,81,50,93),(100,124,153),3);rounded((58,84,71,96),(100,124,153),3)
  ellipse((51,32,56,37),(50,66,84));ellipse((64,34,69,39),(50,66,84));rounded((47,61,59,70),(229,237,245),2)
 elif kind=='gift':
  box(25,44,47,38,14,c);box(23,38,51,9,15,shade(c,1.13))
  poly([(44,41),(54,42),(54,87),(44,85)],(247,222,154));poly([(44,41),(58,32),(68,34),(54,43)],(252,236,185))
  for b in [(34,21,57,37),(55,23,79,40)]: ellipse(b,(244,206,133));ellipse((b[0]+5,b[1]+4,b[2]-5,b[3]-4),shade(c,.9))
 elif kind=='printer':
  box(35,31,40,22,7,(232,239,247));box(22,52,61,27,10,(120,145,175))
  poly([(27,52),(37,44),(81,48),(88,61),(82,69),(25,63)],c)
  rounded((32,65,77,74),(43,60,80),3);poly([(41,70),(70,74),(75,91),(44,88)],(246,249,252))
  line([(49,78),(65,80)],(184,200,217),1);ellipse((75,54,79,58),(184,236,210))
 else:
  box(27,38,45,45,13,c);poly([(46,41),(59,32),(67,33),(54,42),(54,88),(46,87)],(232,218,182))
 return im.resize((112,112),Image.Resampling.LANCZOS)

if __name__=='__main__':
 OUT.mkdir(parents=True,exist_ok=True)
 for kind in TYPES:
  for index,color in enumerate(PALETTE): render(kind,color).save(OUT/f'{kind}-{index}.webp','WEBP',quality=88,method=6)
 print(f'{len(TYPES)*len(PALETTE)} WebP: {sum(p.stat().st_size for p in OUT.glob("*.webp"))} bytes')
