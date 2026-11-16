"""Bunded 30,000-litre deicing-fluid storage station with tanker fill and truck pump interfaces.

Generic insulated horizontal tank, 8.6 x 4.9 m containment pad. Not a manufacturer replica.
"""
import math
import numpy as np
from kit import Part
import baggage as g

NAME, DISPLAY_NAME, KIND = "DeicingTank", "Deicing fluid storage", "DeicingTank"
AO_DISTANCE, AO_STRENGTH = .8, .48
LOD_RATIOS = (.32,.10)
MOVERS, WHEELS, SPINNERS = [],[],[]
MATS = {"Paint":("#DEE5E3",0,.42,False), "Enamel":("#315567",0,.4,False),
        "Zinc":("#A6ADB1",.78,.48,False), "Yellow":("#EDB52A",0,.38,False),
        "Concrete":("#92928A",0,.94,False), "Blue":("#3D758C",0,.32,False)}
GRIME="#696B63"
WORK = {}
LIGHTS = [("WorkLight","Flood",(-2.02,3.70,3.86),(0,-.6,-1),"#FFF4E0",.85,0)]


def grime(pos,ao):
    low=np.exp(-np.maximum(pos[...,2],0)/.7)
    return np.clip(.025*low+.055*(1-ao),0,.13)


def tube(p,pts,radius=.025,material="Metal"):
    for a,b in zip(pts,pts[1:]):
        p.cyl(a,b,radius,material,segs=12)


def build():
    p=Part("Body")
    # Open bund: floor and four walls; deliberate recessed interior rather than a solid plinth.
    p.box((-2.45,-.10,0),(2.45,8.50,.15),"Concrete",bevel=.025)
    for x in (-2.36,2.36):
        p.box((x-.09,-.10,.15),(x+.09,8.50,.68),"Concrete",bevel=.025)
    for f in (-.01,8.41):
        p.box((-2.35,f-.09,.15),(2.35,f+.09,.68),"Concrete",bevel=.025)
    # Stainless vessel on two shaped saddle blocks: barrel plus rounded end caps.
    for f in (2.0,6.2):
        p.box((-.98,f-.24,.15),(.98,f+.24,.76),"Concrete",bevel=.04)
        p.box((-1.14,f-.26,.70),(1.14,f+.26,.86),"Enamel",bevel=.04)
    p.cyl((0,1.12,2.03),(0,7.35,2.03),1.32,"Paint",segs=56,bevel=.14,segments=4)
    # Shallow dished heads visibly distinguish tank shell from a bevelled box.
    profile=[(-.28,.01),(-.25,.58),(-.16,1.03),(0,1.30),(.08,1.32),(.10,.01)]
    p.revolve((0,1.15,2.03),profile,"Paint",segs=56,axis="f")
    p.revolve((0,7.34,2.03),[(-x,r) for x,r in profile],"Paint",segs=56,axis="f")
    # Insulation seams and restrained blue identification band on both sides.
    for f in (1.25,2.50,3.75,5.0,6.25,7.25):
        p.revolve((0,f,2.03),[(-.012,1.321),(.012,1.321),(.012,1.331),(-.012,1.331)],"Zinc",segs=48,axis="f")
    for side in (-1,1):
        x=side*1.327
        p.box((x-.008,2.72,1.85),(x+.008,5.86,2.39),"Enamel",bevel=.025)
        g.label(p,"DEICING FLUID",(side*1.342,4.29,2.20),.185,side,"White")
        g.label(p,"30 000 L",(side*1.342,4.29,1.99),.14,side,"White")
    # Top inspection hatch, vent, walkway, non-slip treads and guardrail.
    p.cyl((0,2.15,3.28),(0,2.15,3.48),.33,"Metal",segs=32,bevel=.015)
    p.cyl((0,2.15,3.46),(0,2.15,3.53),.38,"Paint",segs=32,bevel=.018)
    p.box((-.13,2.12,3.53),(.13,2.18,3.58),"Dark",bevel=.012)
    tube(p,[(0,6.55,3.30),(0,6.55,3.73),(.22,6.55,3.73)],.055)
    p.cyl((.22,6.55,3.68),(.22,6.55,3.81),.105,"Metal",segs=20)
    p.box((-.63,1.60,3.34),(.63,6.74,3.39),"Zinc",bevel=.02)
    for k in range(44):
        f=1.66+k*.115
        p.box((-.60,f,3.392),(.60,f+.018,3.399),"Dark")
    for x in (-.63,.63):
        for f in (1.60,3.30,5.05,6.74):
            p.cyl((x,f,3.34),(x,f,4.35),.023,"Yellow",segs=10)
        tube(p,[(x,1.60,4.35),(x,6.74,4.35)],.023,"Yellow")
        tube(p,[(x,1.60,3.86),(x,6.74,3.86)],.020,"Yellow")
    # Ladder climbs outside the near head with a proper top return and safety hoop.
    for x in (-.42,.42):
        tube(p,[(x,.56,.15),(x,.56,3.68),(x,1.55,3.68)],.028)
    for k in range(11):
        z=.35+k*.29
        p.cyl((-.42,.56,z),(.42,.56,z),.023,"Zinc",segs=12)
    # Pump, filter and heater cabinet outside vessel; incoming fill and outgoing truck hose are distinct.
    p.box((-2.06,2.62,.15),(-1.43,4.73,1.88),"Paint",bevel=.055)
    p.box((-2.10,2.57,1.86),(-1.40,4.79,1.94),"Enamel",bevel=.035)
    for f in (2.76,3.72):
        p.box((-2.075,f,.38),(-2.065,f+.81,1.73),"Enamel",bevel=.025)
        p.box((-2.095,f+.64,.97),(-2.075,f+.69,1.25),"Metal",bevel=.01)
    p.box((-2.11,2.91,1.23),(-2.08,3.25,1.56),"Dark",bevel=.018)
    p.box((-2.12,2.95,1.31),(-2.11,3.21,1.52),"Blue",bevel=.008)
    p.cyl((-2.11,3.43,1.35),(-2.15,3.43,1.35),.052,"StripeRed",segs=14)
    g.label(p,"PUMP",(-2.115,3.86,1.59),.15,-1,"White")
    # Stainless pipe circuit, filter bowl and ball-valve handwheel.
    tube(p,[(-1.26,5.60,1.70),(-1.69,5.60,1.70),(-1.69,5.60,.44),(-1.69,4.77,.44)],.051)
    p.cyl((-1.69,5.13,.39),(-1.69,5.13,1.12),.15,"Metal",segs=24,bevel=.022)
    p.cyl((-1.69,5.13,1.09),(-1.69,5.13,1.16),.18,"Enamel",segs=24)
    tube(p,[(-1.3,1.51,1.66),(-1.88,1.51,1.66),(-1.88,1.51,.86),(-2.31,1.51,.86)],.067)
    p.cyl((-2.25,1.51,.86),(-2.42,1.51,.86),.104,"Blue",segs=20,bevel=.01)
    p.box((-2.40,1.02,1.07),(-2.38,1.98,1.50),"Enamel",bevel=.02)
    g.label(p,"TANKER FILL",(-2.415,1.50,1.29),.10,-1,"White")
    # Opposite-side capped supplier inlet faces the delivery lane when the tank is parked beside refill trucks.
    tube(p,[(1.3,1.51,1.66),(1.88,1.51,1.66),(1.88,1.51,.86),(2.31,1.51,.86)],.067)
    p.cyl((2.25,1.51,.86),(2.42,1.51,.86),.104,"Blue",segs=20,bevel=.01)
    p.box((2.38,1.02,1.07),(2.40,1.98,1.50),"Enamel",bevel=.02)
    g.label(p,"TANKER FILL",(2.415,1.50,1.29),.10,1,"White")
    for x in (-1.88,-1.58):
        p.cyl((x,1.48,.94),(x,1.66,.94),.025,"Steel",segs=12)
    p.cyl((-1.88,1.57,1.00),(-1.88,1.57,1.12),.020,"Steel",segs=10)
    p.revolve((-1.88,1.57,1.13),[(-.01,.09),(.01,.09),(.01,.11),(-.01,.11)],"Yellow",segs=20,axis="z")
    # Hose reel visible behind the cabinet: layered flanges and wound black hose.
    p.cyl((-2.16,5.05,1.61),(-1.86,5.05,1.61),.32,"Enamel",segs=28)
    for x in (-2.18,-1.84):
        p.cyl((x-.018,5.05,1.61),(x+.018,5.05,1.61),.39,"Yellow",segs=28,bevel=.012)
    for k in range(7):
        p.revolve((-2.13+k*.04,5.05,1.61),[(-.018,.27),(.018,.27),(.018,.32),(-.018,.32)],"Dark",segs=28,axis="r")
    tube(p,[(-2.12,5.05,1.30),(-2.12,5.32,.90),(-2.12,5.93,.90),(-2.12,6.02,1.20)],.035,"Dark")
    p.cyl((-2.12,6.02,1.20),(-2.12,6.02,1.43),.058,"Metal",segs=16)
    # Floodlight, four impact bollards and hazard plates.
    p.cyl((-2.02,3.70,.15),(-2.02,3.70,3.75),.045,"Metal",segs=12)
    p.box((-2.20,3.60,3.74),(-1.84,3.83,3.94),"Enamel",bevel=.025)
    p.quad([(-2.17,3.58,3.77),(-1.87,3.58,3.77),(-1.87,3.58,3.90),(-2.17,3.58,3.90)],"LensWhite")
    for x in (-2.15,2.15):
        for f in (.21,8.15):
            p.cyl((x,f,.15),(x,f,1.18),.075,"Yellow",segs=16,bevel=.025)
            p.cyl((x,f,.71),(x,f,.90),.076,"Dark",segs=16)
    return [p]


VIEWS={"front34":((-12,-10,8),(0,3.8,1.6),48),
       "rear34":((11,15,9),(0,4.0,1.8),48),
       "pump":((-8,1.8,4.5),(-1.6,3.5,1.4),50)}
