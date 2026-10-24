using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>
    /// Runway geometry, paint and lights, laid out to ICAO Annex 14 (scaled to the runway's width and length):
    ///  - surface, paved shoulders and blast pads (paved runways 30 m and wider);
    ///  - paint: threshold stripes, designation, centreline, side stripes, aiming point and touchdown zone marks;
    ///  - lights (with lighting): edge (yellow caution zone), threshold / end, PAPI; precision adds centreline,
    ///    touchdown zone and approach lights with sequenced flashers;
    ///  - dirt and grass strips: ragged edges and orange / white edge cones instead of paint and lights.
    /// Surface submeshes: 0 = runway (its surface's material), 1 = shoulders and blast pads (asphalt).
    /// </summary>
    static class RunwayBuilder
    {
        public const float BlastPad = 60f;
        const float StripeLength = 30f, StripeStart = 6f;

        public static void Build(Runway r, SurfaceBuilder surface, PaintBuilder paint, LightBuilder lights)
        {
            var o = Geo.XZ(r.start);
            var axis = r.Axis2D;
            float L = r.Length, hw = r.width * 0.5f, sh = r.ShoulderWidth;
            if (L < 1f) return;
            bool paved = r.surface.IsPaved();
            surface.mesh.owner = paint.mesh.owner = 0;
            surface.state = paint.state = r.State;

            if (paved)
            {
                surface.Rect(0, o, axis, 0f, L, -hw, hw, r.SurfaceHeight, AirfieldKind.Runway, L);
                if (sh > 0f)
                {
                    surface.Rect(1, o, axis, 0f, L, hw, hw + sh, AirfieldHeights.Shoulder, AirfieldKind.Shoulder, L);
                    surface.Rect(1, o, axis, 0f, L, -hw - sh, -hw, AirfieldHeights.Shoulder, AirfieldKind.Shoulder, L);
                }
                if (r.width >= 29f)
                {
                    surface.Rect(1, o, axis, -BlastPad, 0f, -hw - sh, hw + sh, AirfieldHeights.Shoulder, AirfieldKind.Shoulder, L);
                    surface.Rect(1, o, axis, L, L + BlastPad, -hw - sh, hw + sh, AirfieldHeights.Shoulder, AirfieldKind.Shoulder, L);
                }
                Paint(r, paint, o, axis, L, hw, sh);
                if (r.lighting != RunwayLighting.None) Lights(r, lights, o, axis, L, hw, sh);
            }
            else
            {
                surface.Rect(0, o, axis, 0f, L, -hw, hw, r.SurfaceHeight, AirfieldKind.Runway, L, SurfaceBuilder.Fringe);
                Cones(lights, o, axis, L, hw);
            }
        }

        // ------------------------------------------------------------------ paint

        static void Paint(Runway r, PaintBuilder paint, Vector2 o, Vector2 axis, float L, float hw, float sh)
        {
            paint.frame = RunwayFrame.Along(o, axis, L);
            var right = Geo.Right(axis);
            var white = PaintBuilder.White;
            float W = r.width;

            // side stripes, full length
            float side = W >= 29f ? 0.9f : 0.45f;
            foreach (float sgn in new[] { -1f, 1f })
                paint.Box(o + axis * (L * 0.5f) + right * (sgn * (hw - side * 0.5f)), axis, side * 0.5f, L * 0.5f, white);

            // both ends: threshold stripes, designation, aiming point, touchdown zone
            float numberEnd = 0f;
            for (int end = 0; end < 2; end++)
            {
                var t = end == 0 ? o : o + axis * L;
                var d = end == 0 ? axis : -axis;
                var rt = Geo.Right(d);
                string letter = end == 0 ? r.letter : Runway.MirrorLetter(r.letter);
                Vector2 At(float s, float x) => t + d * s + rt * x;

                int stripes = W < 20f ? 4 : W < 26f ? 6 : W < 35f ? 8 : W < 50f ? 12 : 16;
                float span = Mathf.Min(hw - 3f, 27f);
                float sw = span / stripes;
                for (int k = 0; k < stripes / 2; k++)
                {
                    float x = 1.5f * sw + 2f * k * sw;
                    paint.Box(At(StripeStart + StripeLength * 0.5f, x), d, sw * 0.5f, StripeLength * 0.5f, white);
                    paint.Box(At(StripeStart + StripeLength * 0.5f, -x), d, sw * 0.5f, StripeLength * 0.5f, white);
                }

                float s0 = StripeStart + StripeLength + 12f + 4.5f;
                if (!string.IsNullOrEmpty(letter))
                {
                    paint.Glyph(At(s0, 0f), d, letter[0], white);
                    s0 += 12f;
                }
                int number = RunwayRules.Number(RunwayRules.Bearing(Geo.X0Z(d)));
                paint.Text(At(s0, 0f), d, number.ToString("00"), 4.6f, white);
                numberEnd = s0 + 4.5f;

                // aiming point and touchdown zone (not on short or narrow runways, and never past mid-runway)
                float aimS = L < 800f ? -1f : L < 1200f ? 250f : L < 2400f ? 300f : 400f;
                float aimLen = L < 2400f ? 45f : 60f;
                float gap = W * 0.4f;
                float barW = Mathf.Clamp(W * 0.15f, 3f, 10f);
                if (aimS > 0f && aimS + aimLen < L * 0.5f)
                    foreach (float sgn in new[] { -1f, 1f })
                        paint.Box(At(aimS + aimLen * 0.5f, sgn * (gap * 0.5f + barW * 0.5f)), d, barW * 0.5f, aimLen * 0.5f, white);

                if (W >= 29f && L >= 900f)
                {
                    int pairs = L < 1200f ? 2 : L < 1500f ? 3 : L < 2400f ? 4 : 6;
                    int[] bars = { 3, 3, 2, 2, 1, 1 };
                    int drawn = 0;
                    for (int k = 1; drawn < pairs && k < 12; k++)
                    {
                        float s = 150f * k;
                        if (s + 22.5f > L * 0.5f - 30f) break;
                        if (aimS > 0f && s + 22.5f > aimS - 30f && s < aimS + aimLen + 30f) continue;
                        int n = bars[Mathf.Min(drawn, bars.Length - 1)];
                        for (int b = 0; b < n; b++)
                        {
                            float x = gap * 0.5f + 0.9f + b * 3.3f;
                            paint.Box(At(s + 11.25f, x), d, 0.9f, 11.25f, white);
                            paint.Box(At(s + 11.25f, -x), d, 0.9f, 11.25f, white);
                        }
                        drawn++;
                    }
                }

                // blast pad chevrons, pointing at the runway
                if (r.width >= 29f)
                {
                    var yellow = PaintBuilder.Yellow;
                    float half = hw + sh - 1.5f;
                    foreach (float s in new[] { -20f, -50f })
                        foreach (float sgn in new[] { -1f, 1f })
                            paint.Strip(new[] { At(s, 0f), At(s - half, sgn * half) }, 0.45f, yellow);
                }
            }

            // centreline: 30 m dashes, 20 m gaps, between the designations
            float cw = W >= 44f ? 0.9f : W >= 29f ? 0.45f : 0.3f;
            float c0 = numberEnd + 12f;
            if (L - 2f * c0 > 30f)
                paint.Dashes(o + axis * c0, o + axis * (L - c0), cw * 0.5f, 30f, 20f, white);
        }

        // ------------------------------------------------------------------ lights

        static void Lights(Runway r, LightBuilder lights, Vector2 o, Vector2 axis, float L, float hw, float sh)
        {
            bool precision = r.lighting == RunwayLighting.Precision;
            float y = r.SurfaceHeight;
            var right = Geo.Right(axis);
            var white = LightBuilder.White;
            var yellow = LightBuilder.Yellow;
            var off = LightBuilder.Off;

            // edge lights every <= 60 m; precision runways show yellow over the last 600 m (a third if shorter)
            float caution = precision ? Mathf.Min(600f, L / 3f) : 0f;
            int n = Mathf.CeilToInt(L / 60f);
            for (int i = 0; i <= n; i++)
            {
                float s = L * i / n;
                var behind = L - s < caution ? yellow : white;
                var ahead = s < caution ? yellow : white;
                foreach (float sgn in new[] { -1f, 1f })
                {
                    var p = o + axis * s + right * (sgn * (hw + 0.6f));
                    lights.Light(p, 0.35f, axis, behind, ahead, 1f);
                    lights.Elevated(p, y, white);
                }
            }

            // threshold (green, facing the approach) / end (red, facing the runway): one bidirectional row
            int across = Mathf.Max(5, Mathf.FloorToInt(r.width / 3f) + 1);
            for (int end = 0; end < 2; end++)
            {
                float s = end == 0 ? -0.4f : L + 0.4f;
                for (int i = 0; i < across; i++)
                {
                    float x = Mathf.Lerp(-hw + 0.4f, hw - 0.4f, i / (float)(across - 1));
                    var p = o + axis * s + right * x;
                    if (end == 0) lights.Light(p, 0.1f, axis, LightBuilder.Green, LightBuilder.Red, 1.15f);
                    else lights.Light(p, 0.1f, axis, LightBuilder.Red, LightBuilder.Green, 1.15f);
                    lights.Inset(p, y, end == 0 ? LightBuilder.Green : LightBuilder.Red);
                }
            }

            if (precision)
            {
                // centreline every 15 m: white, alternating red / white from 900 m to go, red over the last 300 m
                int m = Mathf.FloorToInt((L - 15f) / 15f);
                for (int i = 0; i <= m; i++)
                {
                    float s = 7.5f + i * 15f;
                    var p = o + axis * s + right * 0.6f;
                    lights.Light(p, 0.05f, axis, Centreline(L - s, i), Centreline(s, i), 0.8f);
                    lights.Inset(p, y, white);
                }
            }

            for (int end = 0; end < 2; end++)
            {
                var t = end == 0 ? o : o + axis * L;
                var d = end == 0 ? axis : -axis;
                var rt = Geo.Right(d);
                Vector2 At(float s, float x) => t + d * s + rt * x;

                // PAPI: four units left of the runway, inner unit set highest (3.5 deg) to outer (2.5 deg)
                float papiS = Mathf.Clamp(L * 0.15f, 120f, 330f);
                for (int k = 0; k < 4; k++)
                {
                    var p = At(papiS, -(hw + sh + 15f + k * 9f));
                    lights.Light(p, 0.7f, d, white, off, 1.5f, AirfieldLightType.Papi, 3.5f - k / 3f);
                    lights.PapiUnit(p, AirfieldHeights.Ground, -d);
                }

                if (!precision) continue;

                // touchdown zone: rows of three each side every 60 m over the first 900 m
                float gap = r.width * 0.4f;
                for (float s = 60f; s <= Mathf.Min(900f, L * 0.5f - 60f); s += 60f)
                    foreach (float sgn in new[] { -1f, 1f })
                        for (int b = 0; b < 3; b++)
                        {
                            var p = At(s, sgn * (gap * 0.5f + b * 1.5f));
                            lights.Light(p, 0.05f, d, white, off, 0.75f);
                            lights.Inset(p, y, white);
                        }

                // approach lights: a barrette every 30 m out to 720 m, a crossbar at 300 m, flashers beyond it
                for (int k = 1; k <= 24; k++)
                {
                    float dist = k * 30f;
                    for (int b = -2; b <= 2; b++)
                        lights.Light(At(-dist, b), 0.7f, d, white, off, 1.05f);
                    lights.Barrette(At(-dist, 0f), AirfieldHeights.Ground, d, 2.2f, 0.65f);
                    if (dist > 300f)
                        lights.Light(At(-dist, 0f), 1.0f, d, white, off, 1.6f, AirfieldLightType.Flasher,
                            (720f - dist) / 420f * 0.6f);
                }
                for (float x = 4f; x <= 15f; x += 1.5f)
                    foreach (float sgn in new[] { -1f, 1f })
                        lights.Light(At(-300f, sgn * x), 0.7f, d, white, off, 1.05f);
                lights.Barrette(At(-300f, -10f), AirfieldHeights.Ground, d, 5.5f, 0.65f);
                lights.Barrette(At(-300f, 10f), AirfieldHeights.Ground, d, 5.5f, 0.65f);
            }
        }

        static Color Centreline(float toGo, int index)
        {
            if (toGo < 300f) return LightBuilder.Red;
            if (toGo < 900f) return index % 2 == 0 ? LightBuilder.Red : LightBuilder.White;
            return LightBuilder.White;
        }

        static void Cones(LightBuilder lights, Vector2 o, Vector2 axis, float L, float hw)
        {
            var right = Geo.Right(axis);
            int n = Mathf.Max(2, Mathf.CeilToInt(L / 60f));
            for (int i = 0; i <= n; i++)
                foreach (float sgn in new[] { -1f, 1f })
                    lights.Cone(o + axis * (L * i / n) + right * (sgn * (hw + SurfaceBuilder.Fringe + 1f)), AirfieldHeights.Ground,
                        i % 2 == 0);
        }
    }
}
