using System;
using System.Collections.Generic;
using AirCity.World;
using UnityEngine;

namespace AirCity.Game
{
    /// <summary>Copies mutable save data before background serialization; no worker observes the live career.</summary>
    public static class CareerSnapshot
    {
        public static CareerState Capture(CareerState source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new CareerState {
                saveVersion = source.saveVersion, expenseRemainder = source.expenseRemainder, mode = source.mode, airport = Airport(source.airport), world = World(source.world), selectedSite = source.selectedSite,
                completedBuilds = source.completedBuilds, funds = source.funds, completedCharters = source.completedCharters,
                expanded = source.expanded, charterActive = source.charterActive, charterElapsed = source.charterElapsed,
                operatingSeconds = source.operatingSeconds, roadTrafficSeconds = source.roadTrafficSeconds,
                publicRoads = Roads(source.publicRoads), accessRoadRoute = Copy(source.accessRoadRoute),
                handsOn = source.handsOn, wholeRegion = source.wholeRegion, layout = Layout(source.layout)
            };
        }

        static AirportSaveData Airport(AirportSaveData source)
        {
            if (source == null) return null;
            return new AirportSaveData {
                nodes = Copy(source.nodes, n => n == null ? null : new AirportSaveData.Node { name = n.name, parent = n.parent, position = n.position, rotation = n.rotation, scale = n.scale }),
                parts = Copy(source.parts, p => p == null ? null : new AirportSaveData.Part { node = p.node, kind = p.kind, json = p.json }),
                references = Copy(source.references),
                appearances = Copy(source.appearances, a => a == null ? null : new AirportSaveData.Appearance { name = a.name, json = a.json })
            };
        }

        static ValleyWorld World(ValleyWorld source)
        {
            if (source == null) return null;
            return new ValleyWorld {
                generatorVersion = source.generatorVersion, forestVersion = source.forestVersion, seed = source.seed, preset = source.preset, river = source.river,
                hasOcean = source.hasOcean, hasRiver = source.hasRiver, seaLevel = source.seaLevel,
                elevationScale = source.elevationScale, elevationOffset = source.elevationOffset, sourceExtent = source.sourceExtent,
                hasCoordinates = source.hasCoordinates, latitude = source.latitude, longitude = source.longitude,
                elevationSha256 = source.elevationSha256, coverSha256 = source.coverSha256,
                landscapeSource = source.landscapeSource, landCover = source.landCover == null ? null : (byte[])source.landCover.Clone(), roadHeights = source.roadHeights == null ? null : (float[])source.roadHeights.Clone(),
                waterHeights = source.waterHeights == null ? null : (float[])source.waterHeights.Clone(),
                heights = source.heights == null ? null : (float[])source.heights.Clone(),
                disturbance = source.disturbance == null ? null : (float[])source.disturbance.Clone(),
                clearedProps = Copy(source.clearedProps),
                trees = Copy(source.trees, t => t == null ? null : new ForestTree { position = t.position, height = t.height, removed = t.removed }),
                sites = Copy(source.sites, s => s == null ? null : new AirportSite {
                    name = s.name, description = s.description, centre = s.centre, elevation = s.elevation, landPrice = s.landPrice }),
                publicRoad = Copy(source.publicRoad), settlements = Copy(source.settlements)
            };
        }

        static CareerLayout Layout(CareerLayout source)
        {
            if (source == null) return null;
            return new CareerLayout {
                unlocked = source.unlocked, clearedTrees = source.clearedTrees, earthwork = source.earthwork, elevation = source.elevation,
                runwayBuilt = source.runwayBuilt, standBuilt = source.standBuilt, receptionBuilt = source.receptionBuilt,
                receptionSiteVersion = source.receptionSiteVersion,
                runwayA = source.runwayA, runwayB = source.runwayB, stand = source.stand, standAlong = source.standAlong,
                reception = source.reception, standHeading = source.standHeading, receptionHeading = source.receptionHeading,
                taxiways = Copy(source.taxiways, t => t == null ? null : new CareerTaxiway { points = Copy(t.points) })
            };
        }

        static PublicRoadNetwork Roads(PublicRoadNetwork source)
        {
            if (source == null) return null;
            // A malformed graph must still reach worker validation without dereferencing a missing list/node.
            if (source.nodes == null || source.segments == null || source.nodes.Contains(null) || source.segments.Contains(null))
                return new PublicRoadNetwork { nodes = null, segments = null };
            return source.Clone(); // Includes the private next-id counter as well as every node and segment.
        }

        static List<T> Copy<T>(List<T> source) => source == null ? null : new List<T>(source);
        static List<TResult> Copy<TSource, TResult>(List<TSource> source, Func<TSource, TResult> clone)
        {
            if (source == null) return null;
            var result = new List<TResult>(source.Count);
            foreach (var item in source) result.Add(clone(item));
            return result;
        }
    }
}
