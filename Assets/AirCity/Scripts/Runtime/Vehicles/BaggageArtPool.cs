using System.Collections.Generic;
using AirCity.Characters;
using UnityEngine;

namespace AirCity.Vehicles
{
    /// <summary>Mesh pools are separated by appearance; every draw supplies the bag's persistent variant.</summary>
    sealed class BaggageArtPool
    {
        readonly Transform root;
        public Transform Root => root;
        readonly GameObject[] prefabs;
        readonly List<Transform>[] pools;
        readonly int[] counts;
        public BaggageArtPool(Transform root,GameObject[] prefabs)
        {
            this.root=root; this.prefabs=prefabs;
            pools=new List<Transform>[prefabs?.Length ?? 0]; counts=new int[pools.Length];
            for (int i=0;i<pools.Length;i++) pools[i]=new List<Transform>();
        }
        public void Begin() => System.Array.Clear(counts,0,counts.Length);
        /// <summary>Drawn size of a variant: x across, y along (the bag's length, its local forward), z height.</summary>
        public Vector3 Size(int variant)
        {
            if (pools.Length==0) return new Vector3(.44f,.67f,.29f);
            var prefab=prefabs[variant%pools.Length];
            var filter=prefab!=null ? prefab.GetComponent<MeshFilter>() : null;
            return filter!=null && filter.sharedMesh!=null ? filter.sharedMesh.bounds.size : new Vector3(.44f,.67f,.29f);
        }
        public void Show(Vector3 position,Quaternion rotation,int variant,RampCrewRig holder=null)
        {
            if (pools.Length==0) return;
            int bucket=variant%pools.Length;
            if (prefabs[bucket]==null) return;
            var pool=pools[bucket]; int at=counts[bucket]++;
            if (at==pool.Count) pool.Add(Object.Instantiate(prefabs[bucket],root).transform);
            var bag=pool[at]; bag.gameObject.SetActive(true);
            var mesh=bag.GetComponent<MeshFilter>().sharedMesh.bounds;
            var localRotation=rotation*Quaternion.Euler(90,0,0);
            if (holder!=null)
            {
                holder.Grip(position,rotation); bag.SetParent(holder.CarrySocket,false);
                bag.localRotation=Quaternion.Euler(90,0,0);
                bag.localPosition=Vector3.up*mesh.extents.z-bag.localRotation*mesh.center;
            }
            else
            {
                bag.SetParent(root,false); bag.localRotation=localRotation;
                bag.localPosition=position+Vector3.up*mesh.extents.z-localRotation*mesh.center;
            }
        }
        public void End()
        {
            for (int bucket=0;bucket<pools.Length;bucket++)
                for (int i=counts[bucket];i<pools[bucket].Count;i++) pools[bucket][i].gameObject.SetActive(false);
        }
    }
}
