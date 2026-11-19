import math, json
import sys
D=(sys.argv[1] if len(sys.argv) > 1 else '.').rstrip('/') + '/'
def f(v): return ("%.2f" % v).rstrip('0').rstrip('.')
def m(w,h): return f"M0,0 L0.01,0 L0.01,0.01 Z M{w-0.01:.2f},{h-0.01:.2f} L{w},{h-0.01:.2f} L{w},{h} Z"
def poly(pts): return "M"+" L".join(f"{f(x)},{f(y)}" for x,y in pts)+" Z"
def circle(cx,cy,r): return f"M{f(cx-r)},{f(cy)} A{f(r)},{f(r)} 0 1 0 {f(cx+r)},{f(cy)} A{f(r)},{f(r)} 0 1 0 {f(cx-r)},{f(cy)} Z"
def star4(cx,cy,r): return poly([(cx,cy-r),(cx+r*0.25,cy-r*0.25),(cx+r,cy),(cx+r*0.25,cy+r*0.25),(cx,cy+r),(cx-r*0.25,cy+r*0.25),(cx-r,cy),(cx-r*0.25,cy-r*0.25)])
def onion(cx,base,half,top):
    h=base-top
    return (f"M{f(cx-half)},{f(base)} C{f(cx-half*1.45)},{f(base-h*0.28)} {f(cx-half*1.1)},{f(base-h*0.75)} {f(cx-half*0.35)},{f(base-h*0.86)} "
            f"C{f(cx-half*0.12)},{f(base-h*0.9)} {f(cx-half*0.04)},{f(base-h*0.95)} {f(cx)},{f(top)} "
            f"C{f(cx+half*0.04)},{f(base-h*0.95)} {f(cx+half*0.12)},{f(base-h*0.9)} {f(cx+half*0.35)},{f(base-h*0.86)} "
            f"C{f(cx+half*1.1)},{f(base-h*0.75)} {f(cx+half*1.45)},{f(base-h*0.28)} {f(cx+half)},{f(base)} Z")
# ---------- Eid ----------
sky=[]
for x,l in [(300,30),(500,55),(580,25),(660,60),(740,35),(820,50)]:
    sky.append(poly([(x-0.6,0),(x+0.6,0),(x+0.6,l),(x-0.6,l)]))
    y=l
    sky.append(poly([(x-6,y),(x+6,y),(x+10,y+6),(x-10,y+6)]))                      # cap
    sky.append(poly([(x-12,y+7),(x+12,y+7),(x+14,y+16),(x+12,y+34),(x,y+44),(x-12,y+34),(x-14,y+16)]))  # body
for (x,y,r) in [(340,95,4),(540,100,3.5),(620,105,4.5),(700,20,3),(780,100,4),(860,20,3.5),(880,105,3)]:
    sky.append(star4(x,y,r))
eid_sky=m(1000,600)+" "+" ".join(sky)
eid_sun=m(100,100)+" M60,4 A46,46 0 1 0 96,72 A38,38 0 1 1 60,4 Z "+star4(80,38,9)
far=[poly([(0,190),(1000,190),(1000,200),(0,200)])]
for cx,w,h in [(140,60,70),(330,90,105),(520,70,80),(700,110,120),(880,70,85)]:
    far.append(poly([(cx-w/2,190),(cx-w/2,190-h*0.45),(cx+w/2,190-h*0.45),(cx+w/2,190)]))
    far.append(onion(cx,190-h*0.45,w*0.42,190-h))
for x,h in [(255,120),(410,130),(615,110),(800,135),(960,100)]:
    far.append(poly([(x-5,190),(x-4,190-h),(x+4,190-h),(x+5,190)]))
    far.append(onion(x,190-h,6,190-h-16))
eid_far=m(1000,200)+" "+" ".join(far)
eid_near=m(1000,200)+" "+poly([(0,192),(1000,192),(1000,200),(0,200)])
eid_side=m(262,120)+" M150,26 A26,26 0 1 0 170,62 A21,21 0 1 1 150,26 Z "+star4(162,44,6)
# ---------- Diwali ----------
sky=[]
for cx,cy,R in [(315,50,38),(560,58,48),(745,55,52)]:
    for k in range(16):
        a=2*math.pi*k/16; a2=a+0.05; r0=R*0.25
        sky.append(poly([(cx+r0*math.cos(a),cy+r0*math.sin(a)),(cx+R*math.cos(a),cy+R*math.sin(a)),(cx+R*math.cos(a2),cy+R*math.sin(a2))]))
    for k in range(10):
        a=2*math.pi*(k+0.5)/10; sky.append(circle(cx+R*1.12*math.cos(a),cy+R*1.12*math.sin(a),2.2))
di_sky=m(1000,600)+" "+" ".join(sky)
petals=[]
for k in range(8):
    a=2*math.pi*k/8; ca,sa=math.cos(a),math.sin(a)
    def P(r,t): return (50+r*ca-t*sa, 50+r*sa+t*ca)
    p0,p1,p2,p3=P(10,0),P(30,13),P(46,0),P(30,-13)
    petals.append(f"M{f(p0[0])},{f(p0[1])} Q{f(p1[0])},{f(p1[1])} {f(p2[0])},{f(p2[1])} Q{f(p3[0])},{f(p3[1])} {f(p0[0])},{f(p0[1])} Z")
di_sun=m(100,100)+" "+" ".join(petals)+" "+circle(50,50,7)
bowls=[poly([(0,192),(1000,192),(1000,200),(0,200)])]; flames=[]
x=30
while x<1000:
    bowls.append(f"M{f(x-16)},{f(176)} Q{f(x)},{f(194)} {f(x+16)},{f(176)} Q{f(x+8)},{f(180)} {f(x)},{f(179)} Q{f(x-8)},{f(180)} {f(x-16)},{f(176)} Z")
    flames.append(f"M{f(x)},{f(176)} C{f(x-6)},{f(170)} {f(x-4)},{f(162)} {f(x+1)},{f(154)} C{f(x+4)},{f(162)} {f(x+6)},{f(170)} {f(x)},{f(176)} Z")
    x+=70
di_far=m(1000,200)+" "+" ".join(bowls)
di_near=m(1000,200)+" "+" ".join(flames)
side=[]; sflames=[]
for x in [130,175,220]:
    side.append(f"M{f(x-14)},{f(66)} Q{f(x)},{f(82)} {f(x+14)},{f(66)} Q{f(x)},{f(70)} {f(x-14)},{f(66)} Z")
    side.append(f"M{f(x)},{f(66)} C{f(x-5)},{f(60)} {f(x-3)},{f(53)} {f(x+1)},{f(46)} C{f(x+4)},{f(53)} {f(x+5)},{f(60)} {f(x)},{f(66)} Z")
di_side=m(262,120)+" "+" ".join(side)
json.dump({"eid":[eid_sky,eid_sun,eid_far,eid_near,eid_side],"diwali":[di_sky,di_sun,di_far,di_near,di_side]},open(D+'art.json','w'))
print("ok")
