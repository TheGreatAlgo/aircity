using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.World
{
    public class GlobeProjectionTests
    {
        [Test]
        public void GeographicEastAndNorthUseTheSameBasisAsGuiPins()
        {
            Assert.That(Vector3.Distance(Vector3.forward,GlobeProjection.Project(0,0,0,0)),Is.LessThan(.000001f));
            Assert.That(Vector3.Distance(Vector3.right,GlobeProjection.Project(0,90,0,0)),Is.LessThan(.000001f));
            Assert.That(Vector3.Distance(Vector3.up,GlobeProjection.Project(90,0,0,0)),Is.LessThan(.000001f));
            Assert.Less(GlobeProjection.Project(0,180,0,0).z,0f);
        }

        [TestCase(43.6532,-79.3832,43.6532f,-79.3832f)]
        [TestCase(-16.5,-151.74,-20f,-145f)]
        [TestCase(10,179.9,0f,-179f)]
        [TestCase(-10,-179.9,0f,179f)]
        [TestCase(89,170,85f,170f)]
        [TestCase(-89,-170,-85f,-170f)]
        [TestCase(90,0,80f,70f)]
        [TestCase(-90,0,-80f,-70f)]
        [TestCase(40,20,90f,20f)]
        [TestCase(-40,20,-90f,20f)]
        public void FrontLocationsRoundTripAtTheDatelineAndPoles(double latitude,double longitude,float viewLatitude,float viewLongitude)
        {
            var projected=GlobeProjection.Project(latitude,longitude,viewLatitude,viewLongitude);
            Assert.GreaterOrEqual(projected.z,0f);
            Assert.AreEqual(1f,projected.magnitude,.000001f);
            Assert.IsTrue(GlobeProjection.TryPick(new Vector2(projected.x,projected.y),viewLatitude,viewLongitude,out var selected));
            var roundTrip=GlobeProjection.Project(selected.x,selected.y,viewLatitude,viewLongitude);
            Assert.That(Vector3.Distance(projected,roundTrip),Is.LessThan(.00001f));
            Assert.AreEqual(latitude,selected.x,.0001d);
            Assert.That(selected.y,Is.GreaterThanOrEqualTo(-180f).And.LessThan(180f));
            if(System.Math.Abs(latitude)<89.99d)
                Assert.AreEqual(0d,GlobeProjection.WrapLongitude(selected.y-longitude),.0001d);
        }

        [Test]
        public void PickingSelectsTheFrontHemisphereAndRejectsPointsOutsideTheGlobe()
        {
            Assert.IsFalse(GlobeProjection.TryPick(new Vector2(1.01f,0),0,0,out _));
            Assert.IsFalse(GlobeProjection.TryPick(new Vector2(float.NaN,0),0,0,out _));
            Assert.IsFalse(GlobeProjection.TryPick(Vector2.zero,float.PositiveInfinity,0,out _));
            var back=GlobeProjection.Project(0,180,0,0);
            Assert.Less(back.z,0f);
            Assert.IsTrue(GlobeProjection.TryPick(new Vector2(back.x,back.y),0,0,out var picked));
            Assert.AreEqual(0f,picked.x,.000001f); Assert.AreEqual(0f,picked.y,.000001f);
            Assert.IsTrue(GlobeProjection.TryPick(Vector2.right,0,0,out var limb));
            Assert.AreEqual(90f,limb.y,.0001f);
        }

        [TestCase(180d,-180d)]
        [TestCase(540d,-180d)]
        [TestCase(-540d,-180d)]
        [TestCase(721d,1d)]
        [TestCase(-721d,-1d)]
        public void LongitudeWrappingHasOneDatelineRepresentation(double input,double expected)
            =>Assert.AreEqual(expected,GlobeProjection.WrapLongitude(input));
    }
}
