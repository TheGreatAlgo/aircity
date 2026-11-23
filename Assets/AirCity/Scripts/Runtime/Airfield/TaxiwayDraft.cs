using System;
using System.Collections.Generic;
using System.Linq;
using AirCity.Airfield.Building;
using AirCity.World;
using UnityEngine;

namespace AirCity.Airfield
{
    /// <summary>Detached, fully built network geometry shared by the construction quote and its preview.</summary>
    public sealed class TaxiwayDraft
    {
        internal readonly SurfaceBuilder surface=new SurfaceBuilder(Surfaces.Count),verges=new SurfaceBuilder(1);
        internal readonly PaintBuilder paint;
        internal readonly List<AirCity.World.BuiltSurfaceMap.Triangle> ownership;
        internal readonly AirCity.World.LandscapeMesh structures;
        internal readonly Dictionary<int,RoadBridgeDesign> bridgeDesigns=new Dictionary<int,RoadBridgeDesign>();
        internal readonly LightBuilder lights=new LightBuilder();
        internal readonly TaxiwayBuilder builder;
        internal readonly List<(Vector3 a,Vector3 b)> outline=new List<(Vector3,Vector3)>();
        internal TaxiwayDraft(Airport airport,TaxiwayNetwork network)
        {
            var layouts=network.Layouts();var runways=airport.Runways.ToArray();
            paint=new PaintBuilder {parkingBays=StandMarkings.Equipment(airport),roadExclusions=TaxiwayNetwork.DepotPaintExclusions(airport)};
            builder=new TaxiwayBuilder(network,runways,network.Frontages(layouts));
            builder.Build(surface,paint,lights);builder.BuildVerges(verges);
            surface.mesh.owner=paint.mesh.owner=-1;surface.state=paint.state=SurfaceValues.Initial;
            var stands=new StandBuilder();
            foreach(var layout in layouts)stands.Build(layout,surface,paint,airport.Bridges.TypesFor(layout.stand));
            builder.Warp(surface.mesh,layouts);builder.Warp(paint.mesh,layouts,true);builder.Warp(lights.glows,layouts,true);
            builder.Warp(lights.fixtures,layouts,false,true);builder.Warp(verges.mesh,layouts);
            structures = AirfieldRoadGeometry.Build(airport, network, builder, surface.mesh, out ownership, bridgeDesigns);
        }
        internal void Outline(HashSet<int> affected)
        {
            outline.Clear();outline.AddRange(MeshBoundary.Edges(surface.mesh,owner=>owner>=0 && Array.Exists(builder.owners[owner].edges,e=>affected.Contains(e.id))));
        }
    }
}
