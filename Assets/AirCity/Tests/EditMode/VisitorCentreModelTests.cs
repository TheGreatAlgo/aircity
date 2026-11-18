using System.Linq;
using AirCity.Airfield.Building;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AirCity.Tests
{
    public sealed class VisitorCentreModelTests
    {
        GameObject host;
        Material supplied;
        [TearDown] public void Cleanup()
        { if(host!=null)Object.DestroyImmediate(host);if(supplied!=null)Object.DestroyImmediate(supplied); }

        GameObject Make()
        {
            host=new GameObject("Visitor centre model test");
            supplied=new Material(Shader.Find("AirCity/Building"));
            return VisitorCentreBuilder.Build(host.transform,new Vector2(35,42),12,37,supplied);
        }

        [Test] public void ModelHasFiniteContainedGeometryAndThreeReducingDetailLevels()
        {
            var root=Make();var group=root.GetComponent<LODGroup>();
            Assert.NotNull(group);var levels=group.GetLODs();Assert.AreEqual(3,levels.Length);
            Assert.Less(Vector3.Distance(new Vector3(35,12,42),root.transform.position),.001f);
            int previous=int.MaxValue;
            foreach(var level in levels)
            {
                int vertices=0;
                foreach(var renderer in level.renderers)
                {
                    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;var b=mesh.bounds;
                    Assert.IsFalse(float.IsNaN(b.size.x)||float.IsInfinity(b.size.x));
                    Assert.Greater(b.size.sqrMagnitude,0f);
                    Assert.GreaterOrEqual(b.min.x,-12.01f);Assert.LessOrEqual(b.max.x,12.01f);
                    Assert.GreaterOrEqual(b.min.z,-8.01f);Assert.LessOrEqual(b.max.z,10.01f);
                    Assert.GreaterOrEqual(b.min.y,-.18f);Assert.LessOrEqual(b.max.y,VisitorCentreBuilder.Height+.01f);
                    if(renderer.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)vertices+=mesh.vertexCount;
                }
                Assert.Less(vertices,previous,"higher LODs must retire fine architectural detail");previous=vertices;
            }
        }

        [Test] public void FrontGlazingUsesArchitecturalTransparentMaterialAndKeepsSharedPalette()
        {
            var root=Make();
            var panes=root.transform.Find("Visitor centre glass LOD0").GetComponent<MeshRenderer>();
            Assert.GreaterOrEqual(panes.sharedMaterial.renderQueue,2900);
            StringAssert.DoesNotContain("Water",panes.sharedMaterial.shader.name);
            Assert.AreSame(supplied,root.transform.Find("Visitor centre LOD0").GetComponent<MeshRenderer>().sharedMaterial);
            var mesh=panes.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.bounds.size.y,4f,"entry glazing includes the tall gable clerestory");
        }

        [Test] public void DestroyingGeneratedRootReleasesOwnedMeshesAndGlassButRetainsSharedMaterial()
        {
            var root=Make();
            var meshes=root.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Distinct().ToArray();
            var glass=root.transform.Find("Visitor centre glass LOD0").GetComponent<MeshRenderer>().sharedMaterial;
            Object.DestroyImmediate(root);
            Assert.True(meshes.All(mesh=>mesh==null));Assert.True(glass==null);Assert.True(supplied!=null);
        }
    }
}
