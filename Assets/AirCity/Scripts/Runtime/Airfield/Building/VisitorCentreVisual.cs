using System.Collections.Generic;
using UnityEngine;

namespace AirCity.Airfield.Building
{
    /// <summary>Owns only the transient resources generated for a visitor-centre model.</summary>
    [ExecuteAlways,DisallowMultipleComponent]
    public sealed class VisitorCentreVisual : MonoBehaviour
    {
        public int geometryVersion;
        readonly List<Object> resources = new List<Object>();
        public Light[] interiorLights=System.Array.Empty<Light>();
        public float[] lightIntensities=System.Array.Empty<float>();
        public T Own<T>(T resource) where T : Object
        {
            if (resource != null && !resources.Contains(resource)) resources.Add(resource);
            return resource;
        }
        public void SetLighting(float glow)
        {
            for(int i=0;i<interiorLights.Length;i++)
            {
                var light=interiorLights[i];
                if(light!=null) {light.enabled=glow>.02f;light.intensity=Mathf.Clamp01(glow)*(i<lightIntensities.Length?lightIntensities[i]:22f);}
            }
        }
        public void RegisterNightLight(Light light,float intensity)
        {
            int i=interiorLights.Length;
            System.Array.Resize(ref interiorLights,i+1);System.Array.Resize(ref lightIntensities,i+1);
            interiorLights[i]=light;lightIntensities[i]=intensity;
        }
        void Update()=>SetLighting(AirCity.Aircraft.AircraftLights.Glow);
        void OnDestroy()
        {
            var owned=new HashSet<Object>(resources);
            // Site/driveway builders may add transient meshes after the shell was built.
            foreach(var filter in GetComponentsInChildren<MeshFilter>(true))
                if(filter.sharedMesh!=null && (filter.sharedMesh.hideFlags&HideFlags.DontSave)!=0)owned.Add(filter.sharedMesh);
            foreach (var resource in owned) if (resource != null) AirfieldPart.DestroyAny(resource);
            resources.Clear();
        }
    }
}
