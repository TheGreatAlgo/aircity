"""Unbranded cab-over aircraft deicer with an articulated knuckle boom and enclosed tip cabin.

Proportions reference the commercial 10.9 x 2.5 m deicer class, not a manufacturer replica.
Metres (right, forward, up); rear axle at the origin. All articulated parts export flat;
DeicingRig restores their hierarchy without inheriting FBX nested-axis conversion.
"""
import math
import bpy
import numpy as np
from mathutils import Matrix, Vector
from kit import Part, M, rounded_rect
import baggage as g

NAME, DISPLAY_NAME, KIND = "DeicingTruck", "Aircraft deicing truck", "DeicingTruck"
AO_DISTANCE, AO_STRENGTH = .7, .48
LOD_RATIOS, MOVING_LODS, SEPARATE_MOVERS = (.30, .09), True, True
MATS = {
    "Paint": ("#ECEEEB", 0, .35, False),
    "Yellow": ("#EDB52A", 0, .32, False),
    "Enamel": ("#344858", 0, .38, False),
    "Boom": ("#B3BDC2", .25, .36, False),
    "Zinc": ("#A6ADB1", .78, .48, False),
    "Blue": ("#3D758C", 0, .32, False),
}
GRIME = "#746D64"
WHEELBASE = 5.70
WHEELS = [("Wheel_" + end + side, (sign * 1.02, f, .51), .51, end == "F")
          for end, f in (("F", WHEELBASE), ("R", 0))
          for side, sign in (("L", -1), ("R", 1))]
MOVERS = [w[0] for w in WHEELS] + ["BoomYaw", "BoomLift", "BoomElbow", "BoomExtend", "BoomCabin", "SprayNozzle"]
SPINNERS = []
WORK = dict(laneWidth=2.5, workSpeed=.7, transitSpeed=8.3, turnRadius=9,
            sprayPoint=(0, 9.92, 4.25), sprayDirection=(0, 1, 0))
LIGHTS = [("Beacon_L", "Beacon", (-.83, 6.48, 3.27), (0, 0, 1), "#FF9A1A", .65, 0),
          ("Beacon_R", "Beacon", (.83, 6.48, 3.27), (0, 0, 1), "#FF9A1A", .65, .37),
          *[("Head_"+s, "Head", (x, 8.51, .80), (0, 1, -.06), "#FFF4E0", .55, 0)
            for s, x in (("L", -.94), ("R", .94))],
          *[("Tail_"+s, "Tail", (x, -2.04, .77), (0, -1, 0), "#E0181C", .45, 0)
            for s, x in (("L", -.98), ("R", .98))]]


def grime(pos, ao):
    low = np.exp(-np.maximum(pos[..., 2], 0) / .65)
    grain = .5+.5*np.sin(pos[..., 1]*23+np.sin(pos[..., 0]*15))
    return np.clip(.035*low+.045*(1-ao)+.012*grain*low, 0, .14)


def tube(p, pts, radius=.023, material="Metal"):
    for a, b in zip(pts, pts[1:]):
        p.cyl(a, b, radius, material, segs=10)


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
        g.label(p, "DI 021", (side*1.254, 7.06, 1.87), .15, side, "Enamel")
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
    # Insulated tank body with three compartments, hatch collars and perimeter walkway.
    p.box((-1.16, -1.91, 1.10), (1.16, 5.43, 2.58), "Paint", bevel=.19, segments=4)
    p.box((-1.19, -1.87, 1.16), (1.19, 5.31, 1.28), "Enamel", bevel=.04)
    for side in (-1, 1):
        x=side*1.177
        p.box((x-.007, -.95, 1.78), (x+.007, 4.96, 2.04), "Enamel", bevel=.009)
        for f in (.48, 2.88):
            p.box((x-.010, f, 1.37), (x+.010, f+.017, 2.45), "Zinc")
        g.label(p, "DEICING", (side*1.197, 1.56, 1.91), .20, side, "White")
        g.label(p, "HOT FLUID", (side*1.194, 4.09, 1.56), .10, side, "Enamel")
        # Wheel mudguards and rear mudflaps.
        for f in (0, WHEELBASE):
            if f == 0:
                p.box((side*1.05-.19, f-.70, 1.10), (side*1.05+.19, f+.70, 1.17), "Dark", bevel=.04)
            p.box((side*1.03-.20, f-.64, .23), (side*1.03+.20, f-.60, .77), "Plastic", bevel=.008)
        p.box((side*1.01-.18, 1.03, .55), (side*1.01+.18, 3.78, 1.05), "Enamel", bevel=.04)
        for f in (1.13, 2.41):
            p.box((x-.015, f, .70), (x+.015, f+.026, .98), "Metal")
        # Fill manifold, capped couplings and pipe labels.
        for k, material in enumerate(("Blue", "LensOrange", "Yellow")):
            f = -1.24+k*.39
            p.cyl((side*1.18, f, 1.42), (side*1.32, f, 1.42), .062, "Metal", segs=14)
            p.cyl((side*1.32, f, 1.42), (side*1.35, f, 1.42), .072, material, segs=14)
        # Top handrail and ladder down the rear corner.
        for f in (-1.62, -.55, 3.35, 5.02):
            p.cyl((side*1.02, f, 2.51), (side*1.02, f, 3.16), .020, "Yellow", segs=10)
        tube(p, [(side*1.02, -1.62, 3.16), (side*1.02, 5.02, 3.16)], .020, "Yellow")
        for z in (.70, 1.01, 1.32, 1.63, 1.94, 2.25, 2.56):
            p.cyl((side*.68, -1.991, z), (side*1.02, -1.991, z), .021, "Metal", segs=10)
        for x0 in (.68, 1.02):
            p.cyl((side*x0, -1.991, .60), (side*x0, -1.991, 2.72), .024, "Metal", segs=10)
    for f in (-.70, 2.28, 4.33):
        p.cyl((0, f, 2.53), (0, f, 2.69), .27, "Metal", segs=28, bevel=.015)
        p.cyl((0, f, 2.68), (0, f, 2.72), .30, "Paint", segs=28, bevel=.012)
        p.box((-.12, f-.015, 2.72), (.12, f+.015, 2.75), "Dark", bevel=.01)
    # Rear pump access, louvres, lamps and conspicuity markers.
    p.box((-.51, -1.993, 1.37), (.51, -1.963, 2.26), "Enamel", bevel=.03)
    for z in (1.54, 1.68, 1.82, 1.96, 2.10):
        p.box((-.43, -2.006, z), (.43, -1.997, z+.045), "Dark", bevel=.007)
    p.box((-1.25, -2.09, .62), (1.25, -1.92, .91), "Dark", bevel=.025)
    for x in (-.98, .98):
        g.lamp(p, x, -2.095, .77, False)
    for k in range(12):
        x=-1.14+k*.19
        p.quad([(x,-2.102,.65),(x+.10,-2.102,.65),(x+.19,-2.102,.87),(x+.09,-2.102,.87)], "StripeRed" if k%2 else "StripeWhite")
    # Pedestal foot and bearing fixed to the chassis, not floating on a tank hatch.
    p.box((-.67, .60, 2.54), (.67, 1.83, 2.66), "Enamel", bevel=.07)
    p.cyl((0, 1.2, 2.61), (0, 1.2, 2.76), .56, "Steel", segs=40, bevel=.022)
    return p


def articulated():
    # All pivots have identity orientation. The lower member has an 8.5308-degree rest slope,
    # while the telescopic upper member is horizontal. Flat export avoids FBX nested-axis errors.
    yaw = Part("BoomYaw", (0,1.2,2.6))
    yaw.cyl((0,1.2,2.70),(0,1.2,2.91),.49,"Yellow",segs=32,bevel=.02)
    for x in (-.32,.32):
        yaw.box((x-.055,.94,2.67),(x+.055,1.47,3.03),"Enamel",bevel=.045)
    yaw.cyl((-.42,1.2,2.80),(.42,1.2,2.80),.135,"Steel",segs=24,bevel=.014)
    for x in (-.445,.445):
        yaw.cyl((x-.015,1.2,2.80),(x+.015,1.2,2.80),.17,"Yellow",segs=24,bevel=.008)
    lower_length=math.sqrt(4**2+.6**2)
    lower_angle=math.degrees(math.atan2(.6,4))
    pose=M(0,1.2,2.8,pitch=lower_angle)
    lift = Part("BoomLift", (0,1.2,2.80))
    lift.box((-.23,-.08,-.16),(.23,lower_length+.05,.16),"Yellow",bevel=.042,segments=3,matrix=pose)
    for side in (-1,1):
        lift.box((side*.236-.009,.35,-.09),(side*.236+.009,lower_length-.31,.09),"Enamel",bevel=.008,matrix=pose)
    # A paired hose run and hydraulic ram make the lower arm a distinct load-bearing member.
    tube(lift,[(.29,1.34,2.78),(.29,4.98,3.33)],.025,"Dark")
    lift.cyl((-.32,1.62,2.69),(-.32,3.64,3.01),.075,"Steel",segs=18)
    lift.cyl((-.32,3.57,3.00),(-.32,4.61,3.16),.037,"Chrome",segs=16)
    lift.cyl((-.37,5.2,3.4),(.37,5.2,3.4),.16,"Steel",segs=28,bevel=.018)
    for side in (-1,1):
        lift.box((side*.30-.045,4.91,3.21),(side*.30+.045,5.39,3.60),"Yellow",bevel=.055,segments=3)
        lift.cyl((side*.36-.012,5.2,3.4),(side*.36+.012,5.2,3.4),.115,"Enamel",segs=24)
    elbow = Part("BoomElbow", (0,5.2,3.4))
    elbow.box((-.20,5.08,3.25),(.20,8.47,3.55),"Boom",bevel=.04,segments=3)
    elbow.box((-.24,8.35,3.22),(.24,8.53,3.58),"Enamel",bevel=.025)
    elbow.box((-.18,5.61,3.554),(.18,7.79,3.582),"Yellow",bevel=.008)
    for f in (5.58,6.18,6.78,7.38):
        elbow.box((-.204,f,3.29),(-.195,f+.075,3.50),"Dark",bevel=.006)
    tube(elbow,[(.255,5.31,3.36),(.255,8.04,3.36)],.025,"Dark")
    # Elbow's separate ram follows the upper member; main and inner shafts retain their real lengths.
    elbow.cyl((-.29,5.21,3.28),(-.29,6.49,3.28),.062,"Steel",segs=16)
    elbow.cyl((-.29,6.42,3.28),(-.29,7.14,3.28),.031,"Chrome",segs=12)
    extension = Part("BoomExtend", (0,7.3,3.4))
    extension.box((-.145,7.30,3.29),(.145,8.59,3.51),"Paint",bevel=.025)
    # Overlapping inner beam continues behind its pivot, giving 3 m of supported telescopic travel.
    extension.box((-.142,5.10,3.30),(.142,7.35,3.50),"Paint",bevel=.018)
    extension.box((-.19,8.43,3.26),(.19,8.64,3.54),"Enamel",bevel=.025)
    tube(extension,[(.18,5.16,3.41),(.18,8.56,3.41)],.018,"Dark")
    for f in (7.63,8.16):
        extension.box((-.148,f,3.35),(-.141,f+.08,3.45),"Yellow")
    cabin = Part("BoomCabin", (0,8.6,3.4))
    # Counter-levelled enclosed basket, with sloped front glazing, roof drip-edge and actual entry door.
    cabin.cyl((-.23,8.6,3.4),(.23,8.6,3.4),.11,"Steel",segs=20)
    cabin.box((-.68,7.93,3.37),(.68,9.31,3.51),"Enamel",bevel=.06,segments=3)
    profile=[(7.98,3.50),(7.98,5.21),(8.13,5.28),(9.05,5.28),
             (9.32,4.98),(9.32,3.62),(9.22,3.50)]
    cabin.prism(profile,"r",-.64,1.28,"Yellow",bevel=.055,segments=3)
    cabin.box((-.70,7.92,5.23),(.70,9.12,5.32),"Yellow",bevel=.045,segments=3)
    # Dark window seals surround blue-black glazing rather than a black cube for the whole cabin.
    for side in (-1,1):
        x=side*.646
        seal=[(x,8.10,4.02),(x,9.20,4.02),(x,9.20,4.98),(x,9.01,5.16),(x,8.10,5.16)]
        glass=[(x+side*.004,8.15,4.08),(x+side*.004,9.14,4.08),
               (x+side*.004,9.14,4.96),(x+side*.004,8.97,5.10),(x+side*.004,8.15,5.10)]
        cabin.quad(seal if side>0 else seal[::-1],"Dark")
        cabin.quad(glass if side>0 else glass[::-1],"Glass")
        cabin.box((x-.012,8.04,3.65),(x+.012,8.065,5.13),"Enamel")
        cabin.box((x-.012,8.03,3.64),(x+.012,9.23,3.67),"Enamel")
        cabin.box((x-.028,8.18,3.84),(x+.028,8.35,3.89),"Dark",bevel=.012)
        # High-contrast lower side stripe and small anti-slip access step.
        cabin.box((x-.008,8.22,3.69),(x+.008,9.18,3.78),"Enamel",bevel=.007)
        cabin.box((side*.58-.13,8.08,3.28),(side*.58+.13,8.50,3.34),"Metal",bevel=.015)
    cabin.quad([(-.54,9.327,4.04),(.54,9.327,4.04),(.54,9.327,4.94),(-.54,9.327,4.94)][::-1],"Dark")
    cabin.quad([(-.48,9.333,4.10),(.48,9.333,4.10),(.48,9.333,4.88),(-.48,9.333,4.88)][::-1],"Glass")
    tube(cabin,[(-.32,9.345,4.26),(.24,9.345,4.59)],.013,"Dark")
    cabin.box((-.14,9.34,3.71),(.14,9.38,3.86),"Dark",bevel=.02)
    cabin.box((-.11,9.383,3.74),(.11,9.39,3.83),"LensWhite",bevel=.01)
    # Spray monitor is attached in front of the tip cabin, within the operator's direct view.
    tube(cabin,[(.44,9.18,3.79),(.44,9.47,3.79),(.44,9.47,4.15),(0,9.45,4.15)],.043,"Metal")
    nozzle = Part("SprayNozzle", (0,9.45,4.25))
    nozzle.cyl((-.16,9.45,4.25),(.16,9.45,4.25),.11,"Yellow",segs=16)
    nozzle.cyl((0,9.39,4.25),(0,9.70,4.25),.074,"Metal",segs=20,bevel=.01)
    nozzle.cyl((0,9.68,4.25),(0,9.88,4.25),.065,"Dark",segs=20,r2=.034)
    nozzle.cyl((0,9.88,4.25),(0,9.92,4.25),.038,"Yellow",segs=16)
    for side in (-1,1):
        tube(nozzle,[(side*.13,9.37,4.17),(side*.22,9.91,4.17)],.012,"Yellow")
        nozzle.sphere((side*.22,9.91,4.17),.025,"LensOrange",segs=10,rings=6)
    return [yaw,lift,elbow,extension,cabin,nozzle]


def build():
    return [body()]+[g.wheel(n,c,r,.33) for n,c,r,_ in WHEELS]+articulated()


VIEWS = {"front34": ((-14,20,10),(0,3.5,2.5),48),
         "rear34": ((14,-13,9),(0,3.2,2.5),48),
         "side": ((-18,4,6),(0,3.8,2.6),48),
         "working": ((-18,20,13),(0,4.5,5.3),43),
         "cabin": ((-5,14,7),(0,8.8,4.4),55)}


def preview_pose(view):
    names=("BoomYaw","BoomLift","BoomElbow","BoomExtend","BoomCabin","SprayNozzle")
    parts={n:bpy.data.objects.get(n) for n in names}
    if not all(parts.values()):
        return
    for child,parent in zip(names[1:],names[:-1]):
        ob=parts[child]
        if ob.parent != parts[parent]:
            world=ob.matrix_world.copy(); ob.parent=parts[parent]; ob.matrix_world=world
            bpy.context.view_layer.update()
    yaw,lift,elbow,extend,cabin,nozzle=[parts[n] for n in names]
    working=view=="working"
    yaw.rotation_euler.z=math.radians(28 if working else 0)
    lift.rotation_euler.x=math.radians(50 if working else 0)
    elbow.rotation_euler.x=math.radians(-55 if working else 0)
    extend.location.y=2.1+(1.8 if working else 0)
    cabin.rotation_euler.x=math.radians(5 if working else 0)
    nozzle.rotation_euler.x=math.radians(-35 if working else 0)
    bpy.context.view_layer.update()
