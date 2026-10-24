using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>What a runway, taxiway edge or junction looks like now: wear, lying snow and grass length (cm), and
    /// the ploughs or mowers at work on it (StripWork.ShaderState).</summary>
    public struct SurfaceValues
    {
        public float wear, snow, grass;
        public Vector4 work;

        public static SurfaceValues Initial => new SurfaceValues { grass = RunwayRules.CutGrass, work = StripWork.Idle };
    }

    /// <summary>
    /// A maintenance team's work on one runway or taxiway edge: clearing its snow or cutting its grass. The team
    /// works in passes up and down the strip, in bands either side of the centreline from the middle out, each band
    /// as wide as the team: one lane per vehicle, in echelon, vehicle i a lane to the left of the one before and
    /// EchelonGapPerLane lanes behind it. Ploughs cast the snow to their left, so they keep it going outwards: every
    /// pass on the left of the centreline runs up the strip, every pass on the right back down. Mowers swap the side
    /// order every pair of passes, so neighbouring bands are mown in opposite directions (the stripes). A team of one
    /// needs many passes; four plough a 45 m runway in four. A team as wide as the strip does it in one centred
    /// pass (Centred), the way an echelon clears a taxiway. The strip is closed from the moment the team enters
    /// until the last vehicle finishes.
    ///
    /// Strip coordinates: s along from the strip's start (a runway's start threshold, a taxiway edge's node a), x
    /// across to the right. The team (VehicleTeam) moves its lead along the passes and reports its position
    /// (Advance); the shaders draw the result from ShaderState (AirfieldCommon.hlsl StripWorked).
    /// </summary>
    public sealed class StripWork
    {
        /// <summary>Distance between vehicles in echelon, in lane widths (16 m for 5 m plough lanes). Keep in sync
        /// with ECHELON_GAP_PER_LANE in AirfieldCommon.hlsl.</summary>
        public const float EchelonGapPerLane = 3.2f;

        /// <summary>Shader state of a strip nobody is working on.</summary>
        public static readonly Vector4 Idle = new Vector4(-1f, 0f, 0f, 0f);

        public readonly WorkKind kind;
        public readonly float length, halfWidth, margin, laneWidth;
        public readonly int vehicles;
        /// <summary>The passes start from the far end (s = length), mirroring the pattern.</summary>
        public readonly bool reversed;

        public StripWork(WorkKind kind, float length, float halfWidth, float margin, float laneWidth, int vehicles, bool reversed)
        {
            this.kind = kind;
            this.length = length;
            this.halfWidth = halfWidth;
            this.margin = margin;
            this.laneWidth = Mathf.Max(laneWidth, 0.5f);
            this.vehicles = Mathf.Max(vehicles, 1);
            this.reversed = reversed;
        }

        public float BandWidth => laneWidth * vehicles;
        public float Gap => laneWidth * EchelonGapPerLane;
        /// <summary>The team covers the whole width at once: a single pass with its band centred on the
        /// centreline (the shader is told by lane width + CentredFlag).</summary>
        public bool Centred => BandWidth >= 2f * halfWidth - 0.05f;
        /// <summary>Passes needed: enough bands to cover each side (one when centred).</summary>
        public int Passes => Centred ? 1 : 2 * Mathf.Max(1, Mathf.CeilToInt(halfWidth / BandWidth - 0.02f));

        /// <summary>Added to the lane width in ShaderState for a centred pass. Keep in sync with
        /// AirfieldCommon.hlsl StripWorked.</summary>
        public const float CentredFlag = 100f;

        /// <summary>The pass the lead is on (or finishing), -1 before the first.</summary>
        public int Pass { get; private set; } = -1;
        /// <summary>The lead's position along the strip (metres), continuing past the end while it turns.</summary>
        public float Front { get; private set; }
        /// <summary>The team is on the strip: closed to aircraft until Done.</summary>
        public bool Entered { get; private set; }
        public bool Done { get; private set; }
        public bool Closing => Entered && !Done;

        /// <summary>0..1 for status displays.</summary>
        public float Progress
        {
            get
            {
                if (Done) return 1f;
                if (Pass < 0) return 0f;
                float span = length + 2f * margin;
                float within = Direction(Pass) > 0 ? Front + margin : length + margin - Front;
                return Mathf.Clamp01((Pass + Mathf.Clamp01(within / span)) / Passes);
            }
        }

        /// <summary>Which side of the centreline pass k works (0 = left, x &lt; 0; in the pattern's own frame, which
        /// is mirrored when reversed) and how far out its band is.</summary>
        int Side(int k) => kind == WorkKind.Plough ? k % 2 : (k / 2) % 2 == 0 ? k % 2 : 1 - k % 2;

        int PatternDirection(int k) => kind == WorkKind.Plough ? (Side(k) == 0 ? 1 : -1) : (k % 2 == 0 ? 1 : -1);

        /// <summary>+1 if pass k runs along the strip (increasing s), -1 back.</summary>
        public int Direction(int k) => PatternDirection(k) * (reversed ? -1 : 1);

        /// <summary>Across position (x) of the lead's lane centre on pass k: the band's lane furthest to the right of
        /// the pass direction (the others follow on its left).</summary>
        public float LeadOffset(int k)
        {
            if (Centred)
            {
                float c = BandWidth * 0.5f - laneWidth * 0.5f;
                return reversed ? -c : c;
            }
            int band = k / 2;
            float min = Side(k) == 0 ? -(band + 1) * BandWidth : band * BandWidth;
            float x = PatternDirection(k) > 0 ? min + BandWidth - laneWidth * 0.5f : min + laneWidth * 0.5f;
            return reversed ? -x : x;
        }

        public float PassStart(int k) => Direction(k) > 0 ? -margin : length + margin;
        public float PassEnd(int k) => Direction(k) > 0 ? length + margin : -margin;

        public void Enter() => Entered = true;

        public void Advance(int pass, float front)
        {
            Entered = true;
            Pass = pass;
            Front = front;
        }

        /// <summary>Show the team's progress without closing the strip yet (an edge of a run the team hasn't
        /// reached).</summary>
        public void Show(int pass, float front)
        {
            Pass = pass;
            Front = front;
        }

        public void Finish()
        {
            Entered = true;
            Done = true;
            Pass = Passes;
        }

        /// <summary>(pass or -1, front, band width (negative = reversed), lane width (negative = mowing)): see
        /// AirfieldCommon.hlsl StripWorked.</summary>
        public Vector4 ShaderState => new Vector4(Done ? Passes : Pass, Front, reversed ? -BandWidth : BandWidth,
            (kind == WorkKind.Mow ? -1f : 1f) * (laneWidth + (Centred ? CentredFlag : 0f)));

        /// <summary>A mown strip keeps its pattern (idle) so the stripes show until the grass grows back.</summary>
        public Vector4 Stripes => new Vector4(-1f, 0f, reversed ? -BandWidth : BandWidth, -(laneWidth + (Centred ? CentredFlag : 0f)));

        /// <summary>Has the team been over this spot? (The C# twin of AirfieldCommon.hlsl StripWorked.)</summary>
        public bool Worked(float s, float x)
        {
            if (Done) return true;
            if (Pass < 0) return false;
            if (reversed) x = -x;
            if (Centred) x -= BandWidth * 0.5f;     // the centred band, seen as the first band on the left
            int side = x < 0f ? 0 : 1;
            int band = Mathf.FloorToInt(Mathf.Abs(x) / BandWidth);
            int k = kind == WorkKind.Plough ? 2 * band + side : 2 * band + (band % 2 == 0 ? side : 1 - side);
            if (k < Pass) return true;
            if (k > Pass) return false;
            int d = PatternDirection(k);
            float min = side == 0 ? -(band + 1) * BandWidth : band * BandWidth;
            int lane = Mathf.FloorToInt(d > 0 ? (min + BandWidth - x) / laneWidth : (x - min) / laneWidth);
            float dir = d * (reversed ? -1f : 1f);
            float front = Front - dir * lane * Gap;
            return dir * (front - s) > 0f;
        }
    }
}
