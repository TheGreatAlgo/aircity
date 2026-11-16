using System;
using System.Text.RegularExpressions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AirCity.Game
{
    // Read the serializer's identity rather than assuming Unity's obsolete 32-bit instance ID API.
    internal static class SavedObjectReference
    {
        [Serializable] sealed class Reference { public Object value; }
        internal static readonly Regex Pattern = new Regex(@"(""(?:instanceID|entityId)""\s*:\s*)(-?\d+)");
        internal static long Identity(Object value)
        {
            var match = Pattern.Match(JsonUtility.ToJson(new Reference { value = value }));
            if (!match.Success) throw new InvalidOperationException("Unsupported Unity object reference encoding.");
            return long.Parse(match.Groups[2].Value);
        }
    }
}
