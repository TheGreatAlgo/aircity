using System;
using UnityEngine;

namespace AirCity.World
{
    /// <summary>Orthographic globe coordinates: east is right, north is up and positive z faces the viewer.</summary>
    public static class GlobeProjection
    {
        const double Radians = Math.PI / 180d;

        public static Vector3 Project(double latitude, double longitude, float viewLatitude, float viewLongitude)
        {
            if (!Finite(latitude) || !Finite(longitude) || !Finite(viewLatitude) || !Finite(viewLongitude)) return Vector3.zero;
            double lat = Math.Max(-90d, Math.Min(90d, latitude)) * Radians, lon = WrapLongitude(longitude) * Radians;
            double centreLat = Math.Max(-90d, Math.Min(90d, viewLatitude)) * Radians, centreLon = WrapLongitude(viewLongitude) * Radians;
            double delta = lon - centreLon;
            double cosLat = Math.Cos(lat), sinLat = Math.Sin(lat), cosCentre = Math.Cos(centreLat), sinCentre = Math.Sin(centreLat);
            return new Vector3((float)(cosLat * Math.Sin(delta)),
                (float)(sinLat * cosCentre - cosLat * Math.Cos(delta) * sinCentre),
                (float)(sinLat * sinCentre + cosLat * Math.Cos(delta) * cosCentre));
        }

        /// <summary>Pick the front hemisphere from sphere-radius units, with y pointing up.</summary>
        public static bool TryPick(Vector2 unit, float viewLatitude, float viewLongitude, out Vector2 latLon)
        {
            latLon = default;
            if (!Finite(unit.x) || !Finite(unit.y) || !Finite(viewLatitude) || !Finite(viewLongitude)) return false;
            double radiusSquared = (double)unit.x * unit.x + (double)unit.y * unit.y;
            if (radiusSquared > 1d + .000001d) return false;
            double front = Math.Sqrt(Math.Max(0d, 1d - radiusSquared));
            double latitude = Math.Max(-90d, Math.Min(90d, viewLatitude)) * Radians, longitude = WrapLongitude(viewLongitude) * Radians;
            double sinLat = Math.Sin(latitude), cosLat = Math.Cos(latitude), sinLon = Math.Sin(longitude), cosLon = Math.Cos(longitude);
            // Inverse camera basis: east*x + north*y + centre*front.
            double x = cosLon * unit.x - sinLat * sinLon * unit.y + cosLat * sinLon * front;
            double y = cosLat * unit.y + sinLat * front;
            double z = -sinLon * unit.x - sinLat * cosLon * unit.y + cosLat * cosLon * front;
            double pickedLat = Math.Asin(Math.Max(-1d, Math.Min(1d, y))) / Radians;
            double pickedLon = x * x + z * z < 1e-18d ? viewLongitude : Math.Atan2(x, z) / Radians;
            float pickedLongitude = (float)WrapLongitude(pickedLon);
            if (pickedLongitude >= 180f) pickedLongitude -= 360f;
            latLon = new Vector2((float)pickedLat, pickedLongitude);
            return true;
        }

        public static double WrapLongitude(double longitude)
        {
            double value = (longitude + 180d) % 360d;
            if (value < 0d) value += 360d;
            return value - 180d;
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
