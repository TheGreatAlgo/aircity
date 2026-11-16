using System;
using System.Collections.Generic;
using AirCity.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.World
{
    /// <summary>Low-cost tree silhouettes and CPU-baked distance cards. Shared by all roots in one biome.</summary>
    public sealed class RegionTreePalette : IDisposable
    {
        public TreeLibrary Library { get; private set; }
        public TreeLibrary Source { get; private set; }
        public RegionPreset Preset { get; private set; }
        readonly List<Object> owned = new List<Object>();
        int conifers;
        const int Tile = 128, AtlasWidth = Tile * 2;
        enum Shape { Alder, Birch, SpreadingOak, Coconut, LeaningPalm, FanPalm, Umbrella, Emergent, Coastal, Pandanus }
        struct Limb { public Vector3 a, b; public float ra, rb; public Color color; }
        struct Crown { public Vector3 centre, radius; public Color color; }
        struct Frond { public Vector3 root, direction; public float length, width, droop; public Color color; }
        struct Volume { public Vector3 centre, right, up, forward, radius; public Color color; public byte foliage; }
        struct Range { public int start, count; }
        sealed class Description
        {
            public string name;
            public float top = 22f;
            public readonly List<Limb> limbs = new List<Limb>();
            public readonly List<Crown> crowns = new List<Crown>();
            public readonly List<Frond> fronds = new List<Frond>();
            public readonly List<Volume> volumes = new List<Volume>();
        }

        public static RegionTreePalette Create(TreeLibrary source, RegionPreset preset)
        {
            bool warm = preset == RegionPreset.Tropical || preset == RegionPreset.Desert;
            if (!warm && (source == null || source.variants == null || source.variants.Length == 0)) return null;
            var palette = new RegionTreePalette { Source = source, Preset = preset };
            try { palette.Build(); return palette; }
            catch { palette.Dispose(); throw; }
        }

        void Build()
        {
            Library = Own(ScriptableObject.CreateInstance<TreeLibrary>());
            Library.name = Preset + " mixed tree palette";
            var variants = new List<TreeVariant>();
            if (Preset == RegionPreset.Woodland || Preset == RegionPreset.Mountains)
            {
                foreach (var variant in Source.variants)
                    variants.Add(new TreeVariant { name = variant.name, height = variant.height,
                        // The former medium tree is ample geometry at city-builder camera distances.
                        levels = new[] { variant.levels[1], variant.levels[1], variant.levels[2] },
                        bark = variant.bark != null ? variant.bark : Source.bark,
                        foliage = variant.foliage != null ? variant.foliage : Source.foliage,
                        distantFoliage = variant.distantFoliage != null ? variant.distantFoliage : Source.distantFoliage });
                conifers = variants.Count;
                Library.bark = Source.bark; Library.foliage = Source.foliage; Library.distantFoliage = Source.distantFoliage;
            }
            Shape[] shapes = Preset == RegionPreset.Woodland ? new[] { Shape.Alder, Shape.Birch, Shape.SpreadingOak } :
                Preset == RegionPreset.Tropical ? new[] { Shape.Coconut, Shape.LeaningPalm, Shape.FanPalm, Shape.Umbrella, Shape.Emergent, Shape.Coastal, Shape.Pandanus } :
                Preset == RegionPreset.Desert ? new[] { Shape.Coconut, Shape.FanPalm } : Array.Empty<Shape>();
            if (shapes.Length > 0)
            {
                var shader = Shader.Find("AirCity/TreeCanopy");
                if (shader == null) throw new InvalidOperationException("The shared tree canopy shader is unavailable.");
                var bark = Own(new Material(shader) { name = "Mixed tree bark", enableInstancing = true }); bark.SetFloat("_Canopy", 0f);
                var foliage = Own(new Material(shader) { name = "Mixed tree leaves", enableInstancing = true });
                var distant = Own(new Material(shader) { name = "Mixed tree distance silhouettes", enableInstancing = true }); distant.SetFloat("_Impostor", 1f);
                var descriptions = new List<Description>(); foreach (var shape in shapes) descriptions.Add(Describe(shape));
                var atlas = Bake(descriptions); distant.SetTexture("_BaseMap", atlas);
                if (conifers == 0) { Library.bark = bark; Library.foliage = foliage; Library.distantFoliage = distant; }
                int rows = Mathf.NextPowerOfTwo(shapes.Length);
                for (int i = 0; i < shapes.Length; i++)
                {
                    var description = descriptions[i];
                    variants.Add(new TreeVariant { name = description.name, height = 24f,
                        levels = new[] { Own(Model(description, 0)), Own(Model(description, 1)), Own(Impostor(i, rows, description.top)) },
                        bark = bark, foliage = foliage, distantFoliage = distant });
                }
            }
            Library.variants = variants.ToArray();
        }

        /// <summary>Stable habitat patches: coast palms, interior rainforest, mixed deciduous/conifer woodland.</summary>
        public int Variant(ValleyWorld world, Vector2 point, float roll)
        {
            if (Preset == RegionPreset.Mountains) return Mathf.Min(conifers - 1, (int)(roll * conifers));
            if (Preset == RegionPreset.Desert) return roll < .6f ? 0 : 1;
            if (Preset == RegionPreset.Woodland)
            {
                float grove = Mathf.PerlinNoise(point.x * .0014f + world.seed % 71, point.y * .0014f + 17f);
                float broadleaf = Mathf.Lerp(.18f, .70f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, .7f, grove)));
                return roll < broadleaf ? conifers + Mathf.Min(2, (int)(roll / broadleaf * 3f)) :
                    Mathf.Min(conifers - 1, (int)((roll - broadleaf) / (1f - broadleaf) * conifers));
            }
            bool coast = false;
            if (world.landCover != null && world.landCover.Length == ValleyWorld.Resolution * ValleyWorld.Resolution)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt((point.x + ValleyWorld.Size * .5f) / world.Spacing), 0, ValleyWorld.Resolution - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt((point.y + ValleyWorld.Size * .5f) / world.Spacing), 0, ValleyWorld.Resolution - 1);
                for (int dz = -6; dz <= 6 && !coast; dz += 3)
                for (int dx = -6; dx <= 6; dx += 3)
                {
                    int nx = Mathf.Clamp(x + dx, 0, ValleyWorld.Resolution - 1), nz = Mathf.Clamp(z + dz, 0, ValleyWorld.Resolution - 1);
                    byte cover = world.landCover[nz * ValleyWorld.Resolution + nx];
                    if (cover == 80 || cover == 95) { coast = true; break; }
                }
            }
            float palm = coast ? .58f : .12f;
            if (roll < palm) return Mathf.Min(2, (int)(roll / palm * 3f));
            float crown = (roll - palm) / (1f - palm);
            if (crown < (coast ? .22f : .58f)) return 3;
            if (crown < (coast ? .40f : .87f)) return 4;
            return crown < .94f ? 5 : 6;
        }

        static Description Describe(Shape shape)
        {
            var d = new Description();
            var wood = new Color(.22f, .14f, .075f);
            void Limb(Vector3 a, Vector3 b, float ra, float rb, Color? tint = null)
            {
                var color = tint ?? wood; d.limbs.Add(new Limb { a = a, b = b, ra = ra, rb = rb, color = color });
                var along = (b - a).normalized; var right = Vector3.Cross(along, Mathf.Abs(along.y) > .9f ? Vector3.forward : Vector3.up).normalized;
                d.volumes.Add(new Volume { centre = (a+b)*.5f, right = right, up = along, forward = Vector3.Cross(right,along),
                    radius = new Vector3(Mathf.Max(ra,rb), (b-a).magnitude*.55f, Mathf.Max(ra,rb)), color = color });
            }
            void Crown(Vector3 p, Vector3 radius, Color color)
            {
                color *= .48f;
                d.crowns.Add(new Crown { centre = p, radius = radius, color = color });
                d.volumes.Add(new Volume { centre = p, radius = radius, right = Vector3.right, up = Vector3.up, forward = Vector3.forward, color = color, foliage = 1 });
            }
            void Frond(Vector3 root, Vector3 direction, float length, float width, float droop, Color color)
            {
                color *= .58f; color.a = 1f;
                direction.Normalize(); d.fronds.Add(new Frond { root = root, direction = direction, length = length, width = width, droop = droop, color = color });
                var across = Vector3.Cross(Vector3.up,direction).normalized;
                for (int part = 0; part < 3; part++)
                {
                    float t = (part+.5f)/3f;
                    var along = (direction + Vector3.up * (.3f-droop*t*2f/length)).normalized;
                    d.volumes.Add(new Volume { centre = root+direction*length*t+Vector3.up*(Mathf.Sin(t*Mathf.PI)*1.1f-droop*t*t),
                        right = across, up = Vector3.Cross(along,across), forward = along,
                        radius = new Vector3(Mathf.Max(.10f, Mathf.Sin(t*Mathf.PI)*width*.65f), .10f, length*.21f), color = color, foliage = 1 });
                }
            }
            if (shape == Shape.Coconut || shape == Shape.LeaningPalm || shape == Shape.FanPalm)
            {
                bool fan = shape == Shape.FanPalm;
                d.name = fan ? "Short fan palm" : shape == Shape.LeaningPalm ? "Leaning coconut palm" : "Tall coconut palm";
                float height = fan ? 14f : shape == Shape.LeaningPalm ? 22f : 26f;
                float lean = fan ? .6f : shape == Shape.LeaningPalm ? 4.6f : 1.3f;
                Vector3 Stem(float t) => new Vector3(lean*t*t,height*t,.6f*t*t);
                for (int i = 0; i < 4; i++) Limb(Stem(i/4f),Stem((i+1)/4f),Mathf.Lerp(.42f,.19f,i/4f),Mathf.Lerp(.42f,.19f,(i+1)/4f));
                int count = fan ? 11 : 10;
                for (int i = 0; i < count; i++)
                {
                    float angle=i*Mathf.PI*2f/count;
                    Frond(Stem(1f),new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)), fan ? 5.2f : i%3==0?6.4f:8.4f,
                        fan ? 1.45f : .90f, fan ? 2.8f : i%2==0?4.2f:2.7f,new Color(.12f,.28f,.045f)*Mathf.Lerp(.83f,1.12f,(i%4)/3f));
                }
                d.top=height; return d;
            }
            if (shape == Shape.Pandanus)
            {
                d.name="Low pandanus crown"; d.top=11f;
                Limb(Vector3.zero,new Vector3(0,7,0),.35f,.20f);
                for(int branch=0;branch<3;branch++)
                {
                    float a=branch*2.39996f;var end=new Vector3(Mathf.Cos(a)*2.8f,10f+branch*.8f,Mathf.Sin(a)*2.8f);
                    Limb(new Vector3(0,5,0),end,.19f,.10f);
                    for(int spoke=0;spoke<8;spoke++)
                    {
                        float angle=spoke*Mathf.PI*.25f+branch;
                        Frond(end,new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)),3.8f,.48f,2.5f,new Color(.15f,.30f,.055f));
                    }
                }
                for(int root=0;root<4;root++) { float a=root*Mathf.PI*.5f;Limb(new Vector3(Mathf.Cos(a)*1.2f,0,Mathf.Sin(a)*1.2f),new Vector3(0,3.4f,0),.09f,.14f); }
                return d;
            }
            bool birch=shape==Shape.Birch, alder=shape==Shape.Alder, oak=shape==Shape.SpreadingOak;
            bool umbrella=shape==Shape.Umbrella, emergent=shape==Shape.Emergent;
            d.name=birch?"Slender birch crown":alder?"Rounded alder crown":oak?"Spreading woodland crown":
                umbrella?"Tropical umbrella crown":emergent?"Tall rainforest crown":"Low coastal broadleaf";
            Color leaf=birch?new Color(.15f,.31f,.065f):alder?new Color(.09f,.25f,.045f):oak?new Color(.10f,.23f,.04f):
                umbrella?new Color(.11f,.27f,.045f):emergent?new Color(.065f,.20f,.04f):new Color(.14f,.29f,.06f);
            float trunk=birch?16f:alder?14f:oak?10f:umbrella?17f:emergent?20f:8f;
            float crown=birch?22f:alder?21f:oak?18f:umbrella?23f:emergent?25f:12f;
            float spread=birch?2.1f:alder?3.2f:oak?5f:umbrella?5.5f:emergent?3.2f:3.5f;
            var bark=birch?new Color(.58f,.57f,.47f):wood;
            Limb(Vector3.zero,new Vector3(.25f,trunk,0),birch?.24f:.48f,birch?.10f:.19f,bark);
            int branches=birch?4:alder?5:6;
            for(int i=0;i<branches;i++)
            {
                float a=i*2.39996f;float y=crown+(i%3-1)*(birch?1.5f:1f);
                var centre=new Vector3(Mathf.Cos(a)*spread,y,Mathf.Sin(a)*spread);
                Limb(new Vector3(.2f,trunk-2f+i*.3f,0),centre,birch?.10f:.17f,.045f,bark);
                var radius=birch?new Vector3(2.4f,4.3f,2.3f):alder?new Vector3(3.4f,3.7f,3.1f):oak?new Vector3(4.1f,2.8f,3.8f):
                    umbrella?new Vector3(4.7f,2.6f,4.1f):emergent?new Vector3(3.3f,3.8f,3.1f):new Vector3(3.8f,2.7f,3.4f);
                Crown(centre,radius,leaf*Mathf.Lerp(.82f,1.11f,(i%4)/3f));
            }
            Crown(new Vector3(-.6f,crown+2.2f,.4f),birch?new Vector3(2.7f,4f,2.4f):new Vector3(spread*.85f,2.8f,spread*.8f),leaf*1.08f);
            d.top=crown+1f;return d;
        }

        static Mesh Model(Description description, int level)
        {
            var b=new Builder();int sides=level==0?7:5;
            foreach(var limb in description.limbs) b.Tube(limb,sides);
            foreach(var crown in description.crowns) b.Crown(crown,level==0?9:6,level==0?4:3);
            foreach(var frond in description.fronds) b.Frond(frond,level==0?3:2);
            return b.Mesh(description.name+" LOD"+level);
        }

        Texture2D Bake(List<Description> descriptions)
        {
            var volumes=new List<Volume>();var ranges=new List<Range>();
            foreach(var d in descriptions) { ranges.Add(new Range {start=volumes.Count,count=d.volumes.Count});volumes.AddRange(d.volumes); }
            int height=Mathf.NextPowerOfTwo(descriptions.Count)*Tile;
            using var source=new NativeArray<Volume>(volumes.ToArray(),Allocator.TempJob);
            using var mapping=new NativeArray<Range>(ranges.ToArray(),Allocator.TempJob);
            using var pixels=new NativeArray<byte>(Bc1TextureBuilder.ChainLength(AtlasWidth,height),Allocator.TempJob);
            new BakeJob {volumes=source,ranges=mapping,pixels=pixels,width=AtlasWidth}.Schedule(AtlasWidth*height,256).Complete();
            var texture=Own(Bc1TextureBuilder.Build(pixels,AtlasWidth,height));
            texture.name=Preset+" front and top canopy silhouettes";texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Trilinear;
            return texture;
        }

        [BurstCompile]
        struct BakeJob:IJobParallelFor
        {
            [ReadOnly] public NativeArray<Volume> volumes;
            [ReadOnly] public NativeArray<Range> ranges;
            [NativeDisableParallelForRestriction] public NativeArray<byte> pixels;
            public int width;
            public void Execute(int index)
            {
                int x=index%width,y=index/width,style=y/Tile;
                if(style>=ranges.Length)return;
                bool top=x/Tile==1;
                float u=(x%Tile+.5f)/Tile,v=(y%Tile+.5f)/Tile;
                var origin=top?new Vector3((u-.5f)*32f,40f,(v-.5f)*32f):new Vector3((u-.5f)*32f,v*40f,-32f);
                var direction=top?Vector3.down:Vector3.forward;
                float nearest=float.MaxValue;Color color=Color.black;
                var range=ranges[style];
                for(int i=range.start;i<range.start+range.count;i++)
                {
                    var volume=volumes[i];var delta=origin-volume.centre;
                    var o=new Vector3(Vector3.Dot(delta,volume.right)/volume.radius.x,Vector3.Dot(delta,volume.up)/volume.radius.y,Vector3.Dot(delta,volume.forward)/volume.radius.z);
                    var d=new Vector3(Vector3.Dot(direction,volume.right)/volume.radius.x,Vector3.Dot(direction,volume.up)/volume.radius.y,Vector3.Dot(direction,volume.forward)/volume.radius.z);
                    float a=Vector3.Dot(d,d),b=Vector3.Dot(o,d),c=Vector3.Dot(o,o)-1f,disc=b*b-a*c;
                    if(disc<0f)continue;
                    float distance=(-b-Mathf.Sqrt(disc))/a;if(distance<0f||distance>=nearest)continue;
                    var hit=o+d*distance;
                    if(volume.foliage!=0)
                    {
                        var location=origin+direction*distance;
                        float grain=Mathf.Abs(Mathf.Sin(Mathf.Floor(location.x*3.7f)*12.9898f+Mathf.Floor(location.y*4.3f)*78.233f+Mathf.Floor(location.z*3.7f)*45.164f)*43758.5453f);
                        grain-=Mathf.Floor(grain);
                        float facing=Mathf.Abs(top?hit.y:hit.z);
                        if(grain>Mathf.Lerp(.56f,.92f,facing))continue;
                    }
                    nearest=distance;
                    color=volume.color*Mathf.Lerp(.71f,1f,Mathf.Clamp01(hit.y*.5f+.5f));
                }
                pixels[index*3]=(byte)(Mathf.Pow(Mathf.Clamp01(color.r),1f/2.2f)*255f);
                pixels[index*3+1]=(byte)(Mathf.Pow(Mathf.Clamp01(color.g),1f/2.2f)*255f);
                pixels[index*3+2]=(byte)(Mathf.Pow(Mathf.Clamp01(color.b),1f/2.2f)*255f);
            }
        }

        static Mesh Impostor(int style,int rows,float crown)
        {
            var b=new Builder();float v0=(style+.008f)/rows,v1=(style+.992f)/rows;
            b.Card(new Vector3(-16,0,0),new Vector3(-16,40,0),new Vector3(16,40,0),new Vector3(16,0,0),new Vector2(.004f,v0),new Vector2(.496f,v1));
            b.Card(new Vector3(0,0,-16),new Vector3(0,40,-16),new Vector3(0,40,16),new Vector3(0,0,16),new Vector2(.004f,v0),new Vector2(.496f,v1));
            b.Card(new Vector3(-16,crown,-16),new Vector3(-16,crown,16),new Vector3(16,crown,16),new Vector3(16,crown,-16),new Vector2(.504f,v0),new Vector2(.996f,v1));
            return b.Mesh("Six triangle mixed-tree silhouette");
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
            readonly List<Color> colors=new List<Color>();readonly List<Vector2> uv=new List<Vector2>();
            readonly List<int>[] triangles={new List<int>(),new List<int>()};
            int Vertex(Vector3 p,Vector3 n,Color color,Vector2 t=default) {int i=vertices.Count;vertices.Add(p);normals.Add(n);colors.Add(color);uv.Add(t);return i;}
            void Quad(int sub,int a,int b,int c,int d) {triangles[sub].AddRange(new[]{a,b,c,a,c,d});}
            public void Tube(Limb limb,int sides)
            {
                var along=(limb.b-limb.a).normalized;var right=Vector3.Cross(along,Mathf.Abs(along.y)>.9f?Vector3.forward:Vector3.up).normalized;var forward=Vector3.Cross(right,along);
                int first=vertices.Count;
                for(int row=0;row<2;row++)for(int side=0;side<=sides;side++) {float angle=side*Mathf.PI*2f/sides;var n=right*Mathf.Cos(angle)+forward*Mathf.Sin(angle);Vertex((row==0?limb.a:limb.b)+n*(row==0?limb.ra:limb.rb),n,limb.color);}
                for(int side=0;side<sides;side++)Quad(0,first+side,first+side+sides+1,first+side+sides+2,first+side+1);
            }
            public void Crown(Crown crown,int sides,int rings)
            {
                // Overlapping leaf tufts have open silhouettes without a closed spherical shell.
                int count=sides>=9?18:10;
                for(int i=0;i<count;i++)
                {
                    float y=1f-2f*(i+.5f)/count,a=i*2.39996f+crown.centre.x*.7f+crown.centre.z*.31f;
                    float r=Mathf.Sqrt(Mathf.Max(0f,1f-y*y));var radial=new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r);
                    var normal=(radial+Vector3.up*.30f).normalized;
                    var right=Vector3.Cross(normal,Mathf.Abs(normal.y)>.85f?Vector3.forward:Vector3.up).normalized;
                    var up=Vector3.Cross(right,normal).normalized;
                    var centre=crown.centre+Vector3.Scale(radial,crown.radius)*.56f;
                    float scale=.78f+(i%4)*.075f;
                    var width=right*Mathf.Max(crown.radius.x,crown.radius.z)*1.40f*scale;
                    var height=up*crown.radius.y*1.55f*scale;
                    var color=crown.color*Mathf.Lerp(.83f,1.12f,(i%5)/4f);color.a=.5f;
                    int first=vertices.Count;
                    Vertex(centre-width*.5f-height*.5f,normal,color,Vector2.zero);
                    Vertex(centre-width*.5f+height*.5f,normal,color,Vector2.up);
                    Vertex(centre+width*.5f+height*.5f,normal,color,Vector2.one);
                    Vertex(centre+width*.5f-height*.5f,normal,color,Vector2.right);
                    Quad(1,first,first+1,first+2,first+3);
                }
            }
            public void Frond(Frond frond,int segments)
            {
                int first=vertices.Count;var across=Vector3.Cross(Vector3.up,frond.direction).normalized;
                for(int i=0;i<=segments;i++)
                {
                    float t=i/(float)segments;var p=frond.root+frond.direction*frond.length*t+Vector3.up*(Mathf.Sin(t*Mathf.PI)*1.1f-frond.droop*t*t);
                    float width=Mathf.Sin(t*Mathf.PI)*frond.width;
                    Vertex(p-across*width,Vector3.up,frond.color);Vertex(p+across*width,Vector3.up,frond.color);
                }
                for(int i=0;i<segments;i++)Quad(1,first+i*2,first+i*2+2,first+i*2+3,first+i*2+1);
            }
            public void Card(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector2 min,Vector2 max)
            {
                int first=vertices.Count;Vertex(a,Vector3.up,Color.white,new Vector2(min.x,min.y));Vertex(b,Vector3.up,Color.white,new Vector2(min.x,max.y));
                Vertex(c,Vector3.up,Color.white,new Vector2(max.x,max.y));Vertex(d,Vector3.up,Color.white,new Vector2(max.x,min.y));Quad(1,first,first+1,first+2,first+3);
            }
            public Mesh Mesh(string name)
            {
                var mesh=new Mesh {name=name,subMeshCount=2};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetUVs(0,uv);
                mesh.SetTriangles(triangles[0],0);mesh.SetTriangles(triangles[1],1);mesh.RecalculateBounds();mesh.UploadMeshData(true);return mesh;
            }
        }

        T Own<T>(T value) where T:Object {value.hideFlags=HideFlags.HideAndDontSave;owned.Add(value);return value;}
        public void Dispose()
        {
            foreach(var value in owned) if(value!=null) {if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
            owned.Clear();Library=null;
        }
    }
}
