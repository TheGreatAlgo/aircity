"""Compact game-scale deicing fluid recovery skid; external connections terminate below its enclosure.

No above-ground pipe network. Metres (right,forward,up), origin at the skid's ground centre.
"""
import numpy as np
from kit import Part
import baggage as g

NAME,DISPLAY_NAME,KIND="DeicingRecycler","Deicing fluid recycling skid","DeicingRecycler"
AO_DISTANCE,AO_STRENGTH=.45,.5
LOD_RATIOS=(.32,.10)
MOVERS,WHEELS,SPINNERS=[],[],[]
MATS={"Paint":("#DEE5E3",0,.40,False),"Enamel":("#315567",0,.40,False),
      "Yellow":("#EDB52A",0,.36,False),"Zinc":("#A6ADB1",.78,.48,False),
      "Green":("#4E8A77",0,.37,False),"Concrete":("#92928A",0,.94,False)}
GRIME="#69716A"
WORK={}
LIGHTS=[]


def grime(pos,ao):
    return np.clip(.025*np.exp(-np.maximum(pos[...,2],0)/.5)+.05*(1-ao),0,.12)


def build():
    p=Part("Body")
    # Solid skid and closed lower tray: recovery connections disappear underneath the cabinet.
    p.box((-1.30,-2.30,0),(1.30,2.30,.13),"Concrete",bevel=.025)
    for x in (-1.12,1.12):
        p.box((x-.075,-2.14,.13),(x+.075,2.14,.30),"Enamel",bevel=.025)
    p.box((-1.20,-2.17,.30),(1.20,2.17,.44),"Enamel",bevel=.035)
    # Enclosed process cabinet, with maintenance doors, ventilation and a sampling/control station.
    p.box((-.99,-2.00,.42),(.99,-.17,2.18),"Paint",bevel=.065,segments=3)
    p.box((-1.05,-2.06,2.16),(1.05,-.12,2.26),"Enamel",bevel=.035)
    for side in (-1,1):
        x=side*1.0
        p.box((x-.008,-1.87,.61),(x+.008,-.30,1.99),"Enamel",bevel=.018)
        p.box((x-.03,-1.57,1.11),(x+.03,-1.53,1.42),"Metal",bevel=.01)
        for k in range(6):
            f=-1.36+k*.14
            p.box((x-.016,f,.75),(x+.016,f+.055,.95),"Dark",bevel=.006)
        p.box((x-.022,-1.10,1.43),(x+.022,-.44,1.91),"Dark",bevel=.02)
        p.box((x-.026,-1.02,1.52),(x+.026,-.52,1.85),"Green",bevel=.012)
        g.label(p,"RECYCLE",(side*1.025,-1.10,1.20),.15,side,"White")
        g.label(p,"GLYCOL",(side*1.025,-1.11,2.06),.10,side,"Enamel")
        # Guarded filter vessels, their short process joints remain wholly inside the skid footprint.
        for f in (.20,.71):
            p.cyl((side*.75,f,.50),(side*.75,f,1.65),.19,"Metal",segs=28,bevel=.025)
            p.cyl((side*.75,f,1.60),(side*.75,f,1.71),.235,"Enamel",segs=28,bevel=.015)
            p.cyl((side*.75,f,1.70),(side*.75,f,1.77),.065,"Metal",segs=16)
        for f in (.06,.99):
            p.cyl((side*1.12,f,.43),(side*1.12,f,1.90),.023,"Yellow",segs=10)
        p.cyl((side*1.12,.06,1.90),(side*1.12,.99,1.90),.023,"Yellow",segs=10)
    # Sealed buffer/recovery vessel and access hatch; recycled stock remains part of the game ledger.
    p.cyl((0,1.42,.47),(0,1.42,2.04),.63,"Paint",segs=40,bevel=.07,segments=3)
    p.cyl((0,1.42,2.02),(0,1.42,2.11),.66,"Enamel",segs=40,bevel=.025)
    p.cyl((0,1.42,2.10),(0,1.42,2.26),.21,"Metal",segs=24,bevel=.02)
    p.box((-.10,1.40,2.25),(.10,1.44,2.29),"Dark",bevel=.01)
    p.box((-.20,2.045,.90),(.20,2.065,1.78),"Enamel",bevel=.025)
    for k in range(7):
        z=1.01+k*.095
        p.box((-.15,2.071,z),(.15,2.075,z+.033),"Green",bevel=.003)
    # Low pump/motor set is guarded in a sealed plinth; there are no visible underground runs.
    p.box((-.39,-.04,.46),(.39,.88,.78),"Enamel",bevel=.045)
    p.cyl((0,.16,.74),(0,.72,.74),.24,"Metal",segs=24,bevel=.022)
    p.cyl((0,.68,.74),(0,.85,.74),.28,"Enamel",segs=24,bevel=.02)
    for x in (-.31,.31):
        p.box((x-.025,.16,.69),(x+.025,.68,.88),"Enamel",bevel=.008)
    for x in (-1.12,1.12):
        for f in (-2.03,2.03):
            p.cyl((x,f,.13),(x,f,.40),.065,"Metal",segs=12)
    return [p]


VIEWS={"front34":((-6,-7,4.7),(0,0,1.0),48),
       "rear34":((6,7,4.6),(0,0,1.0),48),
       "filters":((-4.7,2.8,3.0),(0,.55,1.0),52)}
