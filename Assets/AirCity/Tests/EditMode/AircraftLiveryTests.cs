using System.Collections.Generic;
using System.Linq;
using AirCity.Aircraft;
using AirCity.EditorTools.Aircraft;
using AirCity.Rendering;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace AirCity.Tests.Aircraft
{
    /// <summary>Runs once per aircraft type: every type must satisfy the same livery and LOD contract.</summary>
    [TestFixture("Cessna172")]
    [TestFixture("Narrowbody")]
    public class AircraftLiveryTests
    {
        readonly string model;
        AircraftType profile;
        GameObject prefab;
        readonly List<Object> spawned = new List<Object>();

        public AircraftLiveryTests(string model) => this.model = model;

        [OneTimeSetUp]
        public void LoadAssets()
        {
            profile = AircraftSetup.AllTypes().FirstOrDefault(p => p.model == model);
            Assert.IsNotNull(profile, $"{model}.aircraft not imported");
            prefab = AircraftSetup.Prefab(profile);
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in spawned)
                if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
            AircraftLiveryApplier.ClearSharedCache();
        }

        AircraftLivery Example(string name)
        {
            var e = profile.examples.First(x => x.name == name);
            return new AircraftLivery
            {
                style = e.style, baseColor = e.baseColor, color1 = e.color1, color2 = e.color2, color3 = e.color3,
                registrationColor = e.registrationColor, defaultRegistration = e.registration, logo = e.logo,
            };
        }

        /// <summary>GPU bytes of a texture with its full mip chain, from its format (BC1 = 8 bytes / 4x4 block).</summary>
        static long GpuBytes(Texture2D t)
        {
            long total = 0;
            for (int m = 0; m < t.mipmapCount; m++)
            {
                int w = Mathf.Max(1, t.width >> m), h = Mathf.Max(1, t.height >> m);
                total += t.format == TextureFormat.DXT1 ? ((w + 3) / 4) * ((h + 3) / 4) * 8L : w * h * 4L;
            }
            return total;
        }

        static Color32[] Pixels(Texture2D tex)
        {
            var copy = AircraftLiveryCompositor.ReadBack(tex, false, new RectInt(0, 0, tex.width, tex.height));
            var px = copy.GetPixels32();
            Object.DestroyImmediate(copy);
            return px;
        }

        [Test]
        public void Profile_IsImportedCompletely()
        {
            Assert.AreEqual(4, profile.schemes.Length);
            Assert.IsTrue(profile.schemes.All(s => s.mask != null), "scheme mask missing");
            Assert.IsNotNull(profile.detail);
            Assert.IsNotNull(profile.font);
            Assert.IsNotNull(profile.font.atlas);
            Assert.AreEqual(profile.font.chars.Length, profile.font.inkLeft.Length);
            Assert.Greater(profile.port.crop.width, 0f);
            Assert.Greater(profile.starboard.crop.width, 0f);
            Assert.IsNotNull(profile.dirt);
            Assert.IsNotNull(profile.logoUV);
            Assert.IsNotNull(profile.groundShadow);
            Assert.IsNotNull(prefab, $"{model}.prefab missing - run AirCity/Aircraft/Create Missing Prefabs");
        }

        [Test]
        public void BodyTexture_MatchesGeneratorReference()
        {
            // the generator composed ClassicBlue in numpy; the C# compositor must agree
            var tex = AircraftLiveryCompositor.GetBodyTexture(profile, Example("ClassicBlue"));
            var a = Pixels(tex);
            var b = Pixels(profile.defaultBody);
            Assert.AreEqual(b.Length, a.Length);
            double sum = 0;
            int bad = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                sum += d / 3.0;
                if (d > 60) bad++;
            }
            double mean = sum / a.Length;
            Debug.Log($"body vs reference: mean abs diff {mean:F2}/255, {bad} texels off by >20/channel");
            Assert.Less(mean, 2.5, "mean difference too large");
            Assert.Less(bad, a.Length / 500, "too many outliers");
        }

        [Test]
        public void IdenticalLiveries_ShareOneTexture()
        {
            var t1 = AircraftLiveryCompositor.GetBodyTexture(profile, Example("SunsetRed"));
            var t2 = AircraftLiveryCompositor.GetBodyTexture(profile, Example("SunsetRed"));
            Assert.AreSame(t1, t2);
            var other = Example("SunsetRed");
            other.color1 = Color.green;
            Assert.AreNotSame(t1, AircraftLiveryCompositor.GetBodyTexture(profile, other));
        }

        [Test]
        public void Registration_PaintsLetters()
        {
            var l = Example("ClassicBlue");
            var blank = AircraftLiveryCompositor.ComposeRegistration(profile, l, "");
            var text = AircraftLiveryCompositor.ComposeRegistration(profile, l, "N172CB");
            spawned.Add(blank);
            spawned.Add(text);
            var a = Pixels(blank);
            var b = Pixels(text);
            int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 90) changed++;
            float frac = changed / (float)a.Length;
            Debug.Log($"registration: {frac:P1} of patch texels are lettering");
            Assert.That(frac, Is.InRange(0.01f, 0.3f));
        }

        [Test]
        public void Registration_IgnoresUnsupportedCharacters()
        {
            var tex = AircraftLiveryCompositor.ComposeRegistration(profile, Example("BushYellow"), "c-f? bsh!");
            spawned.Add(tex);
            Assert.IsNotNull(tex);
        }

        [Test]
        public void Prefab_IsPaintedInEditMode()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            spawned.Add(go);
            var applier = go.GetComponent<AircraftLiveryApplier>();
            Assert.IsNotNull(applier);
            applier.SetLivery(Example("SkyhawkSP"), "N652SP");
            var body = go.GetComponentsInChildren<Renderer>().First(r => r.name == "Body_LOD0");
            int slot = System.Array.FindIndex(body.sharedMaterials, m => m.name == "Body");
            Assert.GreaterOrEqual(slot, 0);
            var block = new MaterialPropertyBlock();
            body.GetPropertyBlock(block, slot);
            Assert.AreSame(applier.CurrentBodyTexture, block.GetTexture("_BaseMap"));
            Assert.IsNotNull(go.GetComponent<LODGroup>(), "FBX _LODn meshes should produce a LODGroup");
        }

        [Test]
        public void Lods_CutTrianglesAndDrawCalls()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            spawned.Add(go);
            var lods = go.GetComponent<LODGroup>().GetLODs();
            Assert.AreEqual(3, lods.Length);
            int[] maxDraws = { 24, 4, 2 };   // submeshes = draw calls per pass
            var tris = new int[lods.Length];
            var draws = new int[lods.Length];
            var report = new System.Text.StringBuilder("LODs:");
            for (int i = 0; i < lods.Length; i++)
            {
                foreach (var r in lods[i].renderers)
                {
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    Assert.IsFalse(mesh.isReadable, $"{mesh.name} keeps a CPU copy");
                    draws[i] += mesh.subMeshCount;
                    for (int s = 0; s < mesh.subMeshCount; s++) tris[i] += (int)mesh.GetIndexCount(s) / 3;
                    report.Append($" [{r.name}: {mesh.name}, {mesh.vertexCount} verts, {mesh.subMeshCount} submeshes]");
                }
                report.Append($" => LOD{i} {tris[i]} tris, {draws[i]} draws;");
            }
            Debug.Log(report);
            for (int i = 0; i < lods.Length; i++)
            {
                if (i > 0) Assert.Less(tris[i], tris[i - 1], $"LOD{i} triangles");
                Assert.Greater(tris[i], 0, $"LOD{i} triangles");
                Assert.LessOrEqual(draws[i], maxDraws[i], $"LOD{i} draw calls");
                if (i > 0)
                    foreach (var r in lods[i].renderers)
                        Assert.IsFalse(r.sharedMaterials.Any(m => m.name == "Registration"), $"{r.name}: LOD{i} shouldn't draw the registration patch");
            }
            // every renderer is in exactly one LOD: nothing keeps rendering at full detail when zoomed out.
            // (The contact shadow quad is the exception: it draws at every distance, as a single draw call.)
            var inLods = lods.SelectMany(l => l.renderers).ToArray();
            Assert.That(inLods, Is.Unique);
            var all = go.GetComponentsInChildren<Renderer>(true).Where(r => r.name != "GroundShadow" && r.name != "LightGlows");
            Assert.That(inLods, Is.EquivalentTo(all));
            Assert.AreEqual(LODFadeMode.CrossFade, go.GetComponent<LODGroup>().fadeMode);
        }

        [Test]
        public void Bc1Encoder_MatchesUnityCompressor()
        {
            // an uncompressed livery as the test image
            AircraftLiveryCompositor.CompressGeneratedTextures = false;
            Color32[] src;
            try
            {
                src = Pixels(AircraftLiveryCompositor.GetBodyTexture(profile, Example("SunsetRed")));
            }
            finally
            {
                AircraftLiveryCompositor.CompressGeneratedTextures = true;
            }
            int w = profile.atlasSize, h = profile.atlasSize;

            var chain = new NativeArray<byte>(Bc1TextureBuilder.ChainLength(w, h), Allocator.TempJob);
            for (int i = 0; i < src.Length; i++)
            {
                chain[i * 3] = src[i].r;
                chain[i * 3 + 1] = src[i].g;
                chain[i * 3 + 2] = src[i].b;
            }
            var sw = Stopwatch.StartNew();
            var ours = Bc1TextureBuilder.Build(chain, w, h);
            long oursMs = sw.ElapsedMilliseconds;
            chain.Dispose();
            spawned.Add(ours);

            var unity = new Texture2D(w, h, TextureFormat.RGB24, true);
            spawned.Add(unity);
            unity.SetPixels32(src);
            sw.Restart();
            unity.Apply(true);
            unity.Compress(true);
            unity.Apply(false, true);
            long unityMs = sw.ElapsedMilliseconds;

            Assert.AreEqual(TextureFormat.DXT1, ours.format);
            Assert.AreEqual(unity.mipmapCount, ours.mipmapCount);
            double eOurs = MeanError(Pixels(ours), src), eUnity = MeanError(Pixels(unity), src);
            Debug.Log($"BC1 {w}x{h}: Burst encoder {oursMs} ms, mean error {eOurs:F2}/255; " +
                      $"Texture2D.Compress(HQ) {unityMs} ms, mean error {eUnity:F2}/255");
            Assert.Less(eOurs, eUnity * 1.25 + 0.1, "BC1 encoder quality");
        }

        static double MeanError(Color32[] a, Color32[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
            return sum / (a.Length * 3.0);
        }

        [Test]
        public void Fleet_100Aircraft_10Liveries_StaysWithinBudget()
        {
            var liveries = Enumerable.Range(0, 10).Select(i =>
            {
                var l = Example(profile.examples[i % profile.examples.Length].name);
                l.color1 = Color.HSVToRGB(i / 10f, 0.7f, 0.6f);   // all distinct
                return l;
            }).ToArray();

            var sw = Stopwatch.StartNew();
            var regTextures = new List<Texture2D>();
            for (int i = 0; i < 100; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                spawned.Add(go);
                var a = go.GetComponent<AircraftLiveryApplier>();
                a.SetLivery(liveries[i % 10], $"N{100 + i}AC");
                regTextures.Add(a.CurrentRegistrationTexture);
            }
            sw.Stop();

            var bodies = liveries.Select(l => AircraftLiveryCompositor.GetBodyTexture(profile, l)).Distinct().ToArray();
            var regs = regTextures.Distinct().ToArray();
            Assert.AreEqual(10, bodies.Length, "aircraft with the same livery must share one body texture");
            Assert.AreEqual(100, regs.Length);
            foreach (var t in bodies.Concat(regs))
            {
                Assert.AreEqual(TextureFormat.DXT1, t.format, $"{t.name} should be compressed");
                Assert.IsFalse(t.isReadable, $"{t.name} should have dropped its CPU copy");
            }
            // The editor's Profiler.GetRuntimeMemorySizeLong over-reports script-created textures
            // (48 MB for a 2048 DXT1), so budget on the GPU size the format implies.
            long bodyBytes = bodies.Sum(GpuBytes), regBytes = regs.Sum(GpuBytes);
            Debug.Log($"fleet: 100 aircraft / 10 liveries painted in {sw.ElapsedMilliseconds} ms; GPU memory: " +
                      $"bodies {bodyBytes / 1048576f:F1} MB, registrations {regBytes / 1048576f:F1} MB");
            Assert.Less(bodyBytes + regBytes, 64L * 1024 * 1024);
        }
    }
}
