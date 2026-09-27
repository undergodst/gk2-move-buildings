from PIL import Image
S=32
im=Image.new("RGBA",(S,S),(0,0,0,0))
P=lambda x,y,c: im.putpixel((x,y),c+(255,))
BASE=(55,100,171); WASH=(61,117,186); WASH2=(58,112,182)
HLINE=(48,88,151); HLINE_HI=(58,97,155); VLINE=(89,126,185); VLINE_HI=(107,140,193)
B_TOP=(153,183,219); B_LEFT=(151,174,211); B_BOTTOM=(120,159,207); B_RIGHT=(91,136,195)
FILL=(46,90,154); FILL_DARK=(43,80,134); LINE=(198,210,233); LINE_HI=(247,250,255); SHADOW=(37,71,120)
x0,y0,x1,y1=2,2,29,29
# background with a light wash in the top-left like the vanilla cards
for y in range(y0,y1+1):
    for x in range(x0,x1+1):
        d=(x-x0)+(y-y0)
        P(x,y, WASH if d<6 else WASH2 if d<10 else BASE)
# brick grid: horizontal lines at 9,16,23; vertical segments staggered per row band
for y in (9,16,23):
    for x in range(x0+1,x1):
        P(x,y, HLINE_HI if x%7==2 else HLINE)
bands=[(y0+1,8),(10,15),(17,22),(24,y1-1)]
for i,(ya,yb) in enumerate(bands):
    xs=(9,16,23) if i%2==0 else (12,19,26)
    for x in xs:
        for y in range(ya,yb+1):
            P(x,y, VLINE_HI if y==ya else VLINE)
# border
for x in range(x0,x1+1): P(x,y0,B_TOP); P(x,y1,B_BOTTOM)
for y in range(y0,y1+1): P(x0,y,B_LEFT); P(x1,y,B_RIGHT)
P(x0,y0,B_TOP); P(x1,y0,B_TOP)
# four-way arrow mask
c=15.5; reach=11.5; shaft=1.0; head_len=4; head_half=3.6
def inside(x,y):
    dx,dy=abs(x-c),abs(y-c)
    along,across=max(dx,dy),min(dx,dy)
    if along>reach: return False
    if along>=reach-head_len:
        t=(reach-along)/head_len
        return across<=head_half*t+0.01
    return across<=shaft
mask={(x,y) for y in range(S) for x in range(S) if inside(x,y)}
outline={(x+dx,y+dy) for (x,y) in mask for dx in (-1,0,1) for dy in (-1,0,1) if abs(dx)+abs(dy)==1}-mask
# drop shadow one pixel down-right of the outline
for (x,y) in outline:
    s=(x+1,y+1)
    if s not in mask and s not in outline and x0<s[0]<x1 and y0<s[1]<y1: P(*s,SHADOW)
for (x,y) in outline:
    lit=((x+1,y) in mask or (x,y+1) in mask)  # top/left edges catch the light
    P(x,y, LINE_HI if lit else LINE)
for (x,y) in mask:
    P(x,y, FILL_DARK if ((x-1,y) in outline or (x,y-1) in outline) is False and (x+y)%5==0 else FILL)
# small hub in the center, like the chest's lock
pass
# Game build icons are 48x48 sprites: the 32x32 drawing sits in an 8px transparent margin.
full=Image.new("RGBA",(48,48),(0,0,0,0)); full.alpha_composite(im,(8,8))
full.save("move_icon.png")
bg=Image.new("RGBA",(S*8+24,S*8+24),(34,40,52,255)); bg.alpha_composite(im.resize((S*8,S*8),Image.NEAREST),(12,12)); bg.save("move_icon_preview.png")
