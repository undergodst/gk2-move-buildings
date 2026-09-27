from PIL import Image
src=Image.open('remove_cursor_src.png').convert('RGBA')
im=src.copy()
# box border shades -> blueprint blues (same palette family as the icon)
remap={
 (208,36,0):(120,159,207),(156,27,0):(89,126,185),(182,31,0):(107,140,193),(130,23,0):(61,117,186),
 (104,18,0):(55,100,171),(73,13,0):(43,80,134),(228,69,0):(153,183,219),(232,94,41):(170,196,228),
 (255,126,37):(190,210,236),(227,57,0):(120,159,207),(206,72,0):(120,159,207),(207,60,0):(120,159,207),
}
for y in range(32):
    for x in range(32):
        r,g,b,a=im.getpixel((x,y))
        if a and (r,g,b) in remap: im.putpixel((x,y),remap[(r,g,b)]+(a,))
# interior: blueprint background with brick grid, like the icon
BASE=(55,100,171); HL=(48,88,151); VL=(89,126,185)
x0,y0,x1,y1=10,10,25,25
for y in range(y0,y1+1):
    for x in range(x0,x1+1):
        im.putpixel((x,y),BASE+(255,))
for y in (14,21):
    for x in range(x0,x1+1): im.putpixel((x,y),HL+(255,))
for (ya,yb,xs) in [(y0,13,(15,21)),(15,20,(12,18,24)),(22,y1,(15,21))]:
    for x in xs:
        for y in range(ya,yb+1): im.putpixel((x,y),VL+(255,))
# four-way arrow, icon style: dark fill, light outline, lit top-left edges, shadow
FILL=(46,90,154); LINE=(198,210,233); HI=(247,250,255); SH=(37,71,120)
c=17.5; reach=6.6; shaft=1.0; head_len=3; head_half=3.1
def inside(x,y):
    dx,dy=abs(x-c),abs(y-c); along,across=max(dx,dy),min(dx,dy)
    if along>reach: return False
    if along>=reach-head_len: return across<=head_half*(reach-along)/head_len+0.01
    return across<=shaft
mask={(x,y) for y in range(32) for x in range(32) if inside(x,y)}
outline={(x+dx,y+dy) for (x,y) in mask for dx,dy in ((1,0),(-1,0),(0,1),(0,-1))}-mask
DARK=(28,52,96)
for (x,y) in outline:
    if x0<=x<=x1 and y0<=y<=y1: im.putpixel((x,y),DARK+(255,))
for (x,y) in mask:
    lit=(x-1,y) in outline or (x,y-1) in outline
    shade=(x+1,y) in outline or (x,y+1) in outline
    im.putpixel((x,y),(HI if lit and not shade else (170,190,222) if shade and not lit else LINE)+(255,))
im.save('move_cursor.png')
prev=Image.new('RGBA',(2*256+20,256),(60,60,60,255))
prev.alpha_composite(src.resize((256,256),Image.NEAREST),(0,0)); prev.alpha_composite(im.resize((256,256),Image.NEAREST),(276,0))
prev.save('cursor_preview.png')
