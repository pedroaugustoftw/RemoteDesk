from PIL import Image, ImageDraw
from pathlib import Path
root=Path(__file__).parent
im=Image.new('RGBA',(1024,1024),(0,0,0,0))
d=ImageDraw.Draw(im)
outer=[(24,24),(144,24),(208,88),(208,144),(144,144),(216,232),(160,232),(96,152),(72,152),(72,232),(24,232)]
d.polygon([(x*4,y*4) for x,y in outer],fill='#F4433B')
d.polygon([(x*4,y*4) for x,y in [(72,72),(136,72),(160,96),(160,104),(72,104)]],fill=(0,0,0,0))
im.save(root/'logo-preview.png')
im.save(root/'RemoteDesk.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(256,256)])
