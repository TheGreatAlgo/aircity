using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirCity.EditorTools.Aircraft
{
    /// <summary>
    /// Import settings for generated aircraft under Assets/AirCity/Aircraft/:
    ///  - models: metres, no axis-conversion rotation, materials set up for the active render pipeline
    ///    (URP Lit; Built-in Standard / HDRP Lit as fallbacks), flat-colour materials merged into one
    ///    palette-textured "Trim" material, and a LODGroup built from the _LODn name suffixes;
    ///  - livery sources (masks, detail, font, logo placement): linear, not CPU-readable. The compositor
    ///    reads them back from the GPU only while it builds a new livery;
    ///  - the "Collider" child becomes a MeshCollider on the Aircraft layer, "GroundShadow" a contact shadow
    ///    quad outside the LODGroup;
    ///  - airline logos (Assets/AirCity/Liveries/Logos): sRGB with alpha, high-quality compression.
    /// </summary>
    public class AircraftAssetPostprocessor : AssetPostprocessor
    {
        const string AircraftRoot = "Assets/AirCity/Aircraft/";
        const string LogoRoot = "Assets/AirCity/Liveries/Logos/";
        const string GroundShadowShader = "Assets/AirCity/Shaders/AircraftGroundShadow.shader";

        static bool InAircraft(string path) => path.Replace('\\', '/').StartsWith(AircraftRoot);

        // bump when the import rules change, so existing aircraft reimport
        public override uint GetVersion() => 7;

        // ------------------------------------------------------------------ textures

        void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            if (assetPath.Replace('\\', '/').StartsWith(LogoRoot))
            {
                ti.sRGBTexture = true;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }
            if (!InAircraft(assetPath)) return;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            ti.maxTextureSize = 2048;
            if (file.EndsWith("_Dirt") || file.EndsWith("_RegistrationDirt"))
            {
                // the Lit shader's detail albedo must be linear; 0.5 = no change
                ti.sRGBTexture = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.Compressed;
            }
            else if (file.EndsWith("_LogoUV"))
            {
                ti.sRGBTexture = false;
                ti.isReadable = false;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;   // exact coordinates
            }
            else if (file.EndsWith("_GroundShadow"))
            {
                ti.textureType = TextureImporterType.SingleChannel;
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.singleChannelComponent = TextureImporterSingleChannelComponent.Alpha;
                ti.SetTextureSettings(s);
                ti.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                ti.sRGBTexture = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;   // 256x256 Alpha8: 64 KB
            }
            else if (file.Contains("_Mask_"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.isReadable = false;
                ti.mipmapEnabled = false;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;   // BC7: clean zone edges
            }
            else if (file.EndsWith("_Detail") || file == "RegistrationFont")
            {
                // detail = red channel (shading), font = alpha channel (glyph coverage)
                ti.textureType = TextureImporterType.SingleChannel;
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.singleChannelComponent = file == "RegistrationFont"
                    ? TextureImporterSingleChannelComponent.Alpha
                    : TextureImporterSingleChannelComponent.Red;
                ti.SetTextureSettings(s);
                ti.sRGBTexture = false;
                ti.isReadable = false;
                ti.mipmapEnabled = false;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.Uncompressed;   // R8 / Alpha8
            }
            else
            {
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.anisoLevel = 4;
                if (file.EndsWith("_Registration_Default")) ti.npotScale = TextureImporterNPOTScale.None;
            }
        }

        // ------------------------------------------------------------------ models

        void OnPreprocessModel()
        {
            if (!InAircraft(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importNormals = ModelImporterNormals.Import;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.isReadable = true;   // OnPostprocessModel rebuilds meshes, then drops the CPU copies itself
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!InAircraft(assetPath)) return;
            RestoreNesting(root);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Collider") MakeCollider(t.gameObject);
                else if (t.name == "GroundShadow") SetUpGroundShadow(t.GetComponent<MeshRenderer>());
            }
            var trim = CreateTrimMaterial(Path.GetFileNameWithoutExtension(assetPath));
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                MergeIntoTrim(r, trim);
            BuildLodGroup(root);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) mf.sharedMesh.UploadMeshData(true);   // = Read/Write off
        }

        /// <summary>The generator exports nested parts flat as "&lt;Parent&gt;__&lt;Name&gt;" (Blender's FBX exporter
        /// mis-converts deeper nodes); put them back under their parent, keeping their world placement.</summary>
        static void RestoreNesting(GameObject root)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                int i = t.name.IndexOf("__", System.StringComparison.Ordinal);
                if (i <= 0) continue;
                string parentName = t.name.Substring(0, i);
                var parent = System.Array.Find(all, x => x.name == parentName);
                if (parent == null)
                {
                    Debug.LogWarning($"{root.name}: no parent '{parentName}' for '{t.name}'");
                    continue;
                }
                t.SetParent(parent, true);
                t.name = t.name.Substring(i + 2);
            }
        }

        /// <summary>The coarse picking mesh: no rendering, a MeshCollider on the Aircraft layer.</summary>
        static void MakeCollider(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;   // no ?. on Unity objects (fake null)
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) Object.DestroyImmediate(mr);
            if (mf != null) Object.DestroyImmediate(mf);
            var c = go.AddComponent<MeshCollider>();
            c.sharedMesh = mesh;
            int layer = LayerMask.NameToLayer(AirCity.Aircraft.Aircraft.LayerName);
            if (layer >= 0) go.layer = layer;
            else Debug.LogWarning($"Layer '{AirCity.Aircraft.Aircraft.LayerName}' missing (Project Settings > Tags and Layers)");
        }

        static void SetUpGroundShadow(MeshRenderer r)
        {
            if (r == null) return;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ------------------------------------------------------------------ LODs

        // Screen-height fractions below which each LOD hands over to the next (scaled by
        // QualitySettings.lodBias, which is 2 on PC): LOD0 above 18%, LOD1 above 6%, LOD2 down to 0.2%.
        static readonly float[] LodHeights = { 0.18f, 0.06f, 0.002f };

        /// <summary>One LODGroup on the root. A renderer named X_LODn belongs to level n; everything else
        /// (the animated parts) to LOD0. The generator merges those parts into the far LODs' meshes.</summary>
        internal static void BuildLodGroup(GameObject root)
        {
            var levels = new SortedDictionary<int, List<Renderer>>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "GroundShadow") continue;   // drawn at every distance
                var m = Regex.Match(r.name, @"_LOD(\d+)$");
                int level = m.Success ? int.Parse(m.Groups[1].Value) : 0;
                if (!levels.TryGetValue(level, out var list)) levels[level] = list = new List<Renderer>();
                list.Add(r);
            }
            foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
                if (g.gameObject != root) Object.DestroyImmediate(g);
            if (levels.Count < 2) return;
            if (levels.Count > LodHeights.Length)
                Debug.LogWarning($"{root.name}: {levels.Count} LOD levels, only the first {LodHeights.Length} are used");

            var lods = new List<LOD>();
            foreach (var kv in levels)
                if (lods.Count < LodHeights.Length) lods.Add(new LOD(LodHeights[lods.Count], kv.Value.ToArray()));
            var group = root.GetComponent<LODGroup>();
            if (group == null) group = root.AddComponent<LODGroup>();
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
            group.fadeMode = LODFadeMode.CrossFade;      // dithered, so swaps aren't a visible pop when zooming
            group.animateCrossFading = true;
        }

        // ------------------------------------------------------------------ trim palette

        // Everything except the livery-painted materials is a flat colour. Those submeshes are merged
        // into one "Trim" submesh that samples a 16x16 palette (a 4x4 texel cell per material), so a
        // renderer draws at most Body + Registration + Trim. Looks come from TryGetLook.
        static readonly string[] TrimMaterials =
        {
            "Glass", "Metal", "Chrome", "DarkMetal", "Rubber", "Dark", "PropBlade", "PropTip",
            "LightRed", "LightGreen", "LightWhite", "LightLens",
        };

        const int PaletteCells = 4, CellTexels = 4;

        static Vector2 PaletteUV(int slot) =>
            new Vector2((slot % PaletteCells + 0.5f) / PaletteCells, (slot / PaletteCells + 0.5f) / PaletteCells);

        Material CreateTrimMaterial(string model)
        {
            float emissionScale = 0f;
            foreach (var n in TrimMaterials)
            {
                TryGetLook(n, out var l);
                emissionScale = Mathf.Max(emissionScale, l.emission.maxColorComponent);
            }
            var albedo = Palette($"{model}_TrimAlbedo", false);
            var metalGloss = Palette($"{model}_TrimMetalGloss", true);   // R = metallic, A = smoothness
            var emission = Palette($"{model}_TrimEmission", false);       // scaled by _EmissionColor
            for (int i = 0; i < TrimMaterials.Length; i++)
            {
                TryGetLook(TrimMaterials[i], out var l);
                var e = emissionScale > 0 ? l.emission / emissionScale : Color.black;
                FillCell(albedo, i, new Color(l.color.r, l.color.g, l.color.b, 1f));
                FillCell(metalGloss, i, new Color(l.metallic, 0f, 0f, l.smoothness));
                FillCell(emission, i, new Color(e.r, e.g, e.b, 1f));
            }
            foreach (var t in new[] { albedo, metalGloss, emission })
            {
                t.Apply(false, false);
                context.AddObjectToAsset(t.name, t);
            }

            var shader = LitShader();
            var mat = new Material(shader != null ? shader : Shader.Find("Standard")) { name = "Trim" };
            SetColor(mat, Color.white, "_BaseColor", "_Color");
            foreach (var p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
                if (mat.HasProperty(p)) mat.SetTexture(p, albedo);
            if (mat.HasProperty("_MetallicGlossMap"))
            {
                mat.SetTexture("_MetallicGlossMap", metalGloss);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");   // URP
                mat.EnableKeyword("_METALLICGLOSSMAP");       // Built-in
                SetFloat(mat, 1f, "_Smoothness", "_GlossMapScale");   // smoothness = map alpha * this
            }
            if (emissionScale > 0 && mat.HasProperty("_EmissionMap"))
            {
                mat.SetTexture("_EmissionMap", emission);
                mat.SetColor("_EmissionColor", new Color(emissionScale, emissionScale, emissionScale, 1f));
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            context.AddObjectToAsset("Trim", mat);
            return mat;
        }

        static Texture2D Palette(string name, bool linear)
        {
            int size = PaletteCells * CellTexels;
            return new Texture2D(size, size, TextureFormat.RGBA32, false, linear)
            {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
        }

        static void FillCell(Texture2D t, int slot, Color c)
        {
            int x0 = slot % PaletteCells * CellTexels, y0 = slot / PaletteCells * CellTexels;
            for (int y = 0; y < CellTexels; y++)
                for (int x = 0; x < CellTexels; x++)
                    t.SetPixel(x0 + x, y0 + y, c);
        }

        /// <summary>Rebuild the mesh so all trim submeshes become one, with UVs on their palette cells.</summary>
        static void MergeIntoTrim(MeshRenderer r, Material trim)
        {
            var mf = r.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            var mats = r.sharedMaterials;
            if (mesh == null || mats.Length != mesh.subMeshCount) return;
            var slot = new int[mats.Length];
            bool any = false;
            for (int s = 0; s < mats.Length; s++)
            {
                slot[s] = mats[s] != null ? System.Array.IndexOf(TrimMaterials, mats[s].name) : -1;
                any |= slot[s] >= 0;
            }
            if (!any) return;
            if (!mesh.isReadable)
            {
                // reading would return empty arrays and this would silently write an empty mesh
                Debug.LogError($"{mesh.name}: mesh not readable during import, trim materials left unmerged");
                return;
            }

            var pos = mesh.vertices;
            var nrm = mesh.normals;
            var tan = mesh.tangents;
            var uv = mesh.uv;
            var col = mesh.colors32;
            var outPos = new List<Vector3>();
            var outNrm = new List<Vector3>();
            var outTan = new List<Vector4>();
            var outUv = new List<Vector2>();
            var outCol = new List<Color32>();
            var remap = new Dictionary<(int, int), int>();   // (vertex, palette slot or -1) -> new vertex

            int Vertex(int v, int s)
            {
                if (remap.TryGetValue((v, s), out int i)) return i;
                i = outPos.Count;
                outPos.Add(pos[v]);
                if (nrm.Length > 0) outNrm.Add(nrm[v]);
                if (tan.Length > 0) outTan.Add(tan[v]);
                if (col.Length > 0) outCol.Add(col[v]);
                outUv.Add(s >= 0 ? PaletteUV(s) : uv.Length > 0 ? uv[v] : Vector2.zero);
                remap[(v, s)] = i;
                return i;
            }

            var submeshes = new List<List<int>>();
            var outMats = new List<Material>();
            var trimTris = new List<int>();
            for (int s = 0; s < mats.Length; s++)
            {
                var tris = mesh.GetTriangles(s);
                var target = slot[s] >= 0 ? trimTris : new List<int>(tris.Length);
                foreach (int v in tris) target.Add(Vertex(v, slot[s]));
                if (slot[s] >= 0) continue;
                submeshes.Add(target);
                outMats.Add(mats[s]);
            }
            submeshes.Add(trimTris);
            outMats.Add(trim);

            mesh.Clear();
            mesh.indexFormat = outPos.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(outPos);
            if (outNrm.Count > 0) mesh.SetNormals(outNrm);
            if (outTan.Count > 0) mesh.SetTangents(outTan);
            if (outCol.Count > 0) mesh.SetColors(outCol);
            mesh.SetUVs(0, outUv);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i, false);
            mesh.RecalculateBounds();
            r.sharedMaterials = outMats.ToArray();
        }

        internal static Shader LitShader()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return Shader.Find("Standard");
            string t = rp.GetType().Name;
            if (t.Contains("Universal")) return Shader.Find("Universal Render Pipeline/Lit");
            if (t.Contains("HDRenderPipeline") || t.Contains("HighDefinition")) return Shader.Find("HDRP/Lit");
            return null;   // unknown SRP: keep Unity's default choice
        }

        struct Look
        {
            public Color color;
            public float metallic, smoothness;
            public Color emission;
            public string texture;   // suffix of <Model>_<suffix>.png in the model's Textures folder
            public string dirt;      // same, for the grime detail map
        }

        static bool TryGetLook(string material, out Look look)
        {
            look = new Look { color = Color.white, smoothness = 0.5f, emission = Color.black };
            switch (material)
            {
                case "Body": look.smoothness = 0.55f; look.texture = "Body_Default"; look.dirt = "Dirt"; return true;
                case "Registration":
                    look.smoothness = 0.55f; look.texture = "Registration_Default"; look.dirt = "RegistrationDirt"; return true;
                case "Glass": look.color = new Color(0.07f, 0.09f, 0.11f); look.smoothness = 0.95f; return true;
                case "Metal": look.color = new Color(0.66f, 0.66f, 0.68f); look.metallic = 1f; look.smoothness = 0.6f; return true;
                case "Chrome": look.color = new Color(0.93f, 0.93f, 0.95f); look.metallic = 1f; look.smoothness = 0.9f; return true;
                case "DarkMetal": look.color = new Color(0.22f, 0.2f, 0.18f); look.metallic = 0.8f; look.smoothness = 0.4f; return true;
                case "Rubber": look.color = new Color(0.07f, 0.07f, 0.07f); look.smoothness = 0.15f; return true;
                case "Dark": look.color = new Color(0.05f, 0.05f, 0.055f); look.smoothness = 0.3f; return true;
                case "PropBlade": look.color = new Color(0.06f, 0.06f, 0.06f); look.metallic = 0.3f; look.smoothness = 0.5f; return true;
                case "PropTip": look.color = new Color(0.95f, 0.95f, 0.95f); look.smoothness = 0.55f; return true;
                case "LightRed": look.color = new Color(1f, 0.1f, 0.06f); look.emission = new Color(2f, 0.1f, 0.05f); return true;
                case "LightGreen": look.color = new Color(0.1f, 1f, 0.3f); look.emission = new Color(0.1f, 2f, 0.4f); return true;
                case "LightWhite": look.color = Color.white; look.emission = new Color(1.5f, 1.5f, 1.5f); return true;
                case "LightLens": look.color = new Color(0.9f, 0.92f, 0.97f); look.smoothness = 0.95f; look.emission = new Color(0.25f, 0.25f, 0.27f); return true;
            }
            return false;
        }

        void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!InAircraft(assetPath)) return;
            if (description.materialName == "GroundShadow")
            {
                // by path with a dependency: on a fresh import the shader may not be loaded yet, so Shader.Find fails
                context.DependsOnArtifact(GroundShadowShader);
                var shadowShader = AssetDatabase.LoadAssetAtPath<Shader>(GroundShadowShader);
                if (shadowShader != null) material.shader = shadowShader;
                var shadow = ModelTexture("GroundShadow");
                if (shadow != null) material.SetTexture("_MainTex", shadow);
                return;
            }
            if (!TryGetLook(description.materialName, out var look)) return;
            var shader = LitShader();
            if (shader != null) material.shader = shader;

            SetColor(material, look.color, "_BaseColor", "_Color");
            SetFloat(material, look.metallic, "_Metallic");
            SetFloat(material, look.smoothness, "_Smoothness", "_Glossiness");
            if (look.texture != null)
            {
                var tex = ModelTexture(look.texture);
                if (tex != null)
                    foreach (var p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
                        if (material.HasProperty(p)) material.SetTexture(p, tex);
            }
            if (look.dirt != null && material.HasProperty("_DetailAlbedoMap"))
            {
                // grime as the Lit shader's scaled detail albedo; AircraftLiveryApplier sets the amount per aircraft.
                // Enabled here too so player builds keep the shader variant the runtime materials need.
                var dirt = ModelTexture(look.dirt);
                if (dirt != null)
                {
                    material.SetTexture("_DetailAlbedoMap", dirt);
                    material.SetFloat("_DetailAlbedoMapScale", 0.3f);
                    material.EnableKeyword("_DETAIL_SCALED");
                }
            }
            if (look.emission.maxColorComponent > 0)
            {
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", look.emission);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", look.emission * 50f);
            }
        }

        /// <summary>Models/&lt;Model&gt;.fbx -> Textures/&lt;Model&gt;_&lt;suffix&gt;.png (and re-import when it changes).</summary>
        Texture2D ModelTexture(string suffix)
        {
            string model = Path.GetFileNameWithoutExtension(assetPath);
            string dir = Path.GetDirectoryName(Path.GetDirectoryName(assetPath)).Replace('\\', '/');
            string texPath = $"{dir}/Textures/{model}_{suffix}.png";
            context.DependsOnArtifact(texPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        }

        static void SetColor(Material m, Color c, params string[] props)
        {
            foreach (var p in props)
                if (m.HasProperty(p)) m.SetColor(p, c);
        }

        static void SetFloat(Material m, float v, params string[] props)
        {
            foreach (var p in props)
                if (m.HasProperty(p)) m.SetFloat(p, v);
        }
    }
}
