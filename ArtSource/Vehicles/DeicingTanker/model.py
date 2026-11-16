"""Unbranded 20,000-litre rigid three-axle deicing-fluid delivery tanker.

Metres (right, forward, up), rear bogie forward axle at origin. Cab shares the deicing
fleet's proportions and finishes; the cylindrical vessel, pump and delivery reel are distinct.
"""
import math
import numpy as np
from kit import Part
import baggage as g

NAME, DISPLAY_NAME, KIND = "DeicingTanker", "Deicing fluid delivery tanker", "DeicingTanker"
AO_DISTANCE, AO_STRENGTH = .7, .48
LOD_RATIOS, MOVING_LODS, SEPARATE_MOVERS = (.30,.09), True, True
# Smart Project already packs these parts into one valid atlas; Blender 5.2's second island-pack collapses it.
UV_SMART_ATLAS=True
MATS = {"Paint":("#ECEEEB",0,.35,False),"Enamel":("#344858",0,.38,False),
        "Yellow":("#EDB52A",0,.32,False),"Zinc":("#A6ADB1",.78,.48,False),
        "Blue":("#3D758C",0,.32,False),"Tank":("#D5E0DE",.20,.36,False)}
GRIME="#746D64"
WHEELBASE=5.70
WHEELS=[("Wheel_"+axle+side,(sign*1.02,f,.51),.51,axle=="F")
        for axle,f in (("F",WHEELBASE),("M",0),("R",-1.3))
        for side,sign in (("L",-1),("R",1))]
MOVERS=[w[0] for w in WHEELS]+["HoseReel"]
SPINNERS=[]
WORK=dict(laneWidth=2.5,workSpeed=.7,transitSpeed=8.3,turnRadius=10.5)
LIGHTS=[("Beacon_L","Beacon",(-.83,6.48,3.27),(0,0,1),"#FF9A1A",.65,0),
        ("Beacon_R","Beacon",(.83,6.48,3.27),(0,0,1),"#FF9A1A",.65,.37),
        *[("Head_"+s,"Head",(x,8.51,.80),(0,1,-.06),"#FFF4E0",.55,0) for s,x in (("L",-.94),("R",.94))],
        *[("Tail_"+s,"Tail",(x,-3.0,.77),(0,-1,0),"#E0181C",.45,0) for s,x in (("L",-.98),("R",.98))]]


def grime(pos,ao):
    low=np.exp(-np.maximum(pos[...,2],0)/.65)
    return np.clip(.035*low+.045*(1-ao),0,.14)


def tube(p,pts,radius=.023,material="Metal"):
    for a,b in zip(pts,pts[1:]):p.cyl(a,b,radius,material,segs=10)


def body():
    p = Part("Body")
    # Ladder frame, suspension, engine and low service modules: real wheel openings remain clear.
    for x in (-.44, .44):
        p.box((x-.07, -1.85, .48), (x+.07, 8.32, .74), "Chassis", bevel=.025)
    for f in (-1.5, 0, 2.1, 4.3, 5.7, 7.8):
        p.box((-.52, f-.07, .52), (.52, f+.07, .69), "Chassis", bevel=.015)
    for f in (0, WHEELBASE):
        p.cyl((-1.05, f, .51), (1.05, f, .51), .075, "Chassis", segs=16)
        p.box((-.23, f-.20, .36), (.23, f+.20, .66), "Chassis", bevel=.11, segments=3)
        for x in (-.53, .53):
            p.box((x-.055, f-.62, .65), (x+.055, f+.62, .71), "Steel", bevel=.015)
    p.box((-.53, 5.8, .74), (.53, 7.50, 1.55), "Chassis", bevel=.13)
    # Cab side profile: vertical lower face, sloping windscreen and rounded high roof.
    profile = [(5.85, 1.14), (5.85, 3.06), (6.05, 3.18), (7.79, 3.18),
               (8.35, 2.73), (8.43, 1.18), (7.39, 1.14),
               (7.31, 1.38), (6.73, 1.50), (6.16, 1.39), (6.02, 1.14)]
    p.prism(profile, "r", -1.20, 2.40, "Paint", bevel=.075, segments=3)
    p.box((-1.17, 6.03, 3.15), (1.17, 7.88, 3.23), "Paint", bevel=.07, segments=3)
    # Windshield is laid precisely on the sloped front face, split by a narrow centre seal.
    for side in (-1, 1):
        x0, x1 = sorted((side*.055, side*1.09))
        p.quad([(x0, 8.378, 2.74), (x1, 8.378, 2.74),
                (x1, 7.857, 3.132), (x0, 7.857, 3.132)][::-1], "Glass")
        x = side*1.244
        frame = [(x, 6.17, 2.13), (x, 7.97, 2.13), (x, 7.95, 2.72),
                 (x, 7.77, 3.07), (x, 6.17, 3.07)]
        window = [(x+side*.003, 6.23, 2.18), (x+side*.003, 7.83, 2.18),
                (x+side*.003, 7.83, 2.71), (x+side*.003, 7.71, 3.01),
                 (x+side*.003, 6.23, 3.01)]
        p.quad(frame if side>0 else frame[::-1], "Dark")
        p.quad(window if side>0 else window[::-1], "Glass")
        p.box((x-.012, 6.12, 1.48), (x+.012, 6.145, 3.08), "Enamel")
        p.box((x-.014, 7.87, 1.52), (x+.014, 7.89, 2.12), "Enamel")
        p.box((x-.035, 6.33, 1.99), (x+.035, 6.63, 2.04), "Dark", bevel=.012)
        p.box((x-.04, 6.19, 1.54), (x+.04, 7.85, 1.73), "Enamel", bevel=.008)
        g.label(p, "FT 014", (side*1.254, 7.06, 1.87), .15, side, "Enamel")
        # Mirror brackets, small/large mirrors, door step and anti-slip treads.
        tube(p, [(side*1.19, 7.83, 2.82), (side*1.48, 8.02, 2.82), (side*1.48, 8.02, 2.20)], .021, "Dark")
        p.box((side*1.48-.085, 7.92, 2.22), (side*1.48+.085, 8.04, 2.69), "Dark", bevel=.035)
        p.box((side*1.48-.070, 7.913, 2.25), (side*1.48+.070, 7.918, 2.66), "Metal", bevel=.025)
        for z, f0 in ((.43, 7.40), (.72, 7.47), (1.02, 7.53)):
            p.box((side*1.07-.19, f0, z), (side*1.07+.19, 7.91, z+.045), "Metal", bevel=.012)
        g.beacon(p, (side*.83, 6.48, 3.27))
        for z in (2.84, 2.91):
            tube(p, [(side*.72, 8.16, z), (side*.12, 8.20, z+.055)], .014, "Dark")
    p.box((-1.22, 8.34, .61), (1.22, 8.53, .99), "Paint", bevel=.04)
    p.box((-.76, 8.43, 1.10), (.76, 8.46, 1.89), "Dark", bevel=.045)
    for k in range(5):
        z = 1.18+k*.135
        p.box((-.69, 8.463, z), (.69, 8.481, z+.035), "Enamel", bevel=.008)
    for x in (-.94, .94):
        g.lamp(p, x, 8.51, .80)
    # Rear tandem is an actual third axle, with its own animated wheel pivots.
    for x in (-.44,.44):
        p.box((x-.07,-2.87,.48),(x+.07,-1.80,.74),"Chassis",bevel=.025)
    p.cyl((-1.05,-1.30,.51),(1.05,-1.30,.51),.075,"Chassis",segs=16)
    p.box((-.23,-1.50,.36),(.23,-1.10,.66),"Chassis",bevel=.10)
    p.box((-1.13,-2.88,1.02),(1.13,5.46,1.14),"Enamel",bevel=.04)
    for f in (-1.62,3.90):
        p.box((-.88,f-.22,1.10),(.88,f+.22,1.40),"Steel",bevel=.035)
    # Insulated circular barrel with dished end caps and two retaining bands.
    p.cyl((0,-2.19,2.17),(0,5.10,2.17),1.04,"Tank",segs=56,bevel=.11,segments=4)
    cap=[(-.20,.01),(-.18,.49),(-.11,.87),(0,1.02),(.045,1.04),(.06,.01)]
    p.revolve((0,-2.20,2.17),cap,"Tank",segs=56,axis="f")
    p.revolve((0,5.10,2.17),[(-x,r) for x,r in cap],"Tank",segs=56,axis="f")
    for f in (-1.62,3.90):
        p.revolve((0,f,2.17),[(-.055,1.041),(.055,1.041),(.055,1.061),(-.055,1.061)],"Enamel",segs=48,axis="f")
    for side in (-1,1):
        x=side*1.054
        p.box((x-.008,-.91,1.95),(x+.008,3.19,2.47),"Enamel",bevel=.018)
        g.label(p,"DEICING FLUID",(side*1.070,1.16,2.27),.19,side,"White")
        g.label(p,"20 000 L",(side*1.070,1.16,2.06),.14,side,"White")
        # Tandem mudguard spans both rear wheels, with reflective side guard and long mudflap.
        p.box((side*1.04-.21,-2.03,1.08),(side*1.04+.21,.70,1.18),"Dark",bevel=.035)
        p.box((side*1.04-.21,-2.02,.22),(side*1.04+.21,-1.97,.95),"Plastic",bevel=.012)
        p.box((side*1.07-.14,1.11,.53),(side*1.07+.14,4.87,1.02),"Enamel",bevel=.04)
        for f in (1.38,4.43):
            p.box((side*1.218-.008,f,.72),(side*1.218+.008,f+.31,.76),"StripeWhite")
        # Walkway has a low stowed handrail rather than a deicer's boom equipment.
        for f in (-1.84,.35,2.63,4.74):
            p.cyl((side*.52,f,3.15),(side*.52,f,3.74),.020,"Yellow",segs=10)
        tube(p,[(side*.52,-1.84,3.74),(side*.52,4.74,3.74)],.020,"Yellow")
    p.box((-.53,-1.84,3.19),(.53,4.74,3.24),"Zinc",bevel=.018)
    for k in range(45):
        f=-1.76+k*.14
        p.box((-.49,f,3.241),(.49,f+.021,3.25),"Dark")
    for f in (-1.05,1.45,4.05):
        p.cyl((0,f,3.15),(0,f,3.35),.24,"Metal",segs=24,bevel=.015)
        p.cyl((0,f,3.34),(0,f,3.39),.28,"Paint",segs=24,bevel=.012)
        p.box((-.10,f-.014,3.39),(.10,f+.014,3.42),"Dark",bevel=.01)
    for x in (-.40,.40):
        tube(p,[(x,-2.50,.45),(x,-2.50,3.49),(x,-1.85,3.49)],.024)
    for k in range(10):
        z=.61+k*.28
        p.cyl((-.40,-2.50,z),(.40,-2.50,z),.021,"Metal",segs=10)
    # Pump/filter cabinet and a protected delivery reel live at the rear-left side.
    p.box((-.99,-2.91,1.15),(.99,-2.48,1.86),"Paint",bevel=.05)
    p.box((-.87,-2.925,1.27),(-.05,-2.915,1.75),"Enamel",bevel=.02)
    p.box((.08,-2.925,1.27),(.86,-2.915,1.75),"Enamel",bevel=.02)
    p.cyl((-.73,-2.98,1.49),(-.73,-2.94,1.49),.073,"White",segs=24)
    p.box((.18,-2.98,1.47),(.50,-2.94,1.68),"Dark",bevel=.015)
    p.box((.21,-2.986,1.50),(.47,-2.98,1.65),"Blue",bevel=.008)
    tube(p,[(-1.17,-2.10,1.11),(-1.17,-2.35,.91),(-1.17,-2.0,.91)],.040,"Dark")
    p.cyl((-1.17,-2.0,1.0),(-1.25,-2.0,1.0),.078,"Metal",segs=20)
    p.cyl((-1.25,-2.0,1.0),(-1.27,-2.0,1.0),.085,"Blue",segs=20)
    # Rear underrun protection, lamps and chevrons.
    p.box((-1.23,-3.03,.59),(1.23,-2.88,.91),"Dark",bevel=.03)
    for x in (-.98,.98):g.lamp(p,x,-3.04,.77,False)
    for k in range(12):
        x=-1.14+k*.19
        p.quad([(x,-3.061,.65),(x+.10,-3.061,.65),(x+.19,-3.061,.86),(x+.09,-3.061,.86)],"StripeRed" if k%2 else "StripeWhite")
    return p


def hose_reel():
    p=Part("HoseReel",(-1.0,-2.10,1.39))
    p.cyl((-1.18,-2.10,1.39),(-.85,-2.10,1.39),.28,"Dark",segs=32)
    for x in (-1.20,-.83):
        p.cyl((x-.02,-2.10,1.39),(x+.02,-2.10,1.39),.35,"Yellow",segs=32,bevel=.012)
    for k in range(7):
        p.revolve((-1.16+k*.043,-2.10,1.39),[(-.017,.24),(.017,.24),(.017,.285),(-.017,.285)],"Dark",segs=28,axis="r")
    return p


def build():
    return [body(),hose_reel()]+[g.wheel(n,c,r,.33) for n,c,r,_ in WHEELS]


VIEWS={"front34":((-14,18,9),(0,2.7,1.7),48),
       "rear34":((12,-13,8),(0,2.0,1.7),48),
       "hose":((-7,-7,3.9),(-.6,-1.4,1.45),52)}
