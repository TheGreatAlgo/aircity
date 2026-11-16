using System;
using AirCity.Game;
using AirCity.World;
using NUnit.Framework;
using UnityEngine;

namespace AirCity.Tests.Game
{
    public class CareerClockTests
    {
        [Test]
        public void ClockUsesSavedSimulationTimeAndRollsAcrossDates()
        {
            Assert.AreEqual(new DateTime(2026, 4, 1, 8, 0, 0), CareerClock.Date(0f));
            Assert.AreEqual("08:05", CareerClock.TimeLabel(5f));
            Assert.AreEqual("02 Apr 2026", CareerClock.DateLabel(CareerClock.SimulationSecondsPerDay));
            var state = new CareerState { operatingSeconds = CareerClock.SimulationSecondsPerDay * 31f + 120f };
            var restored = JsonUtility.FromJson<CareerState>(JsonUtility.ToJson(state));
            Assert.AreEqual(CareerClock.Date(state.operatingSeconds), CareerClock.Date(restored.operatingSeconds));
            Assert.AreEqual("02 May 2026", CareerClock.DateLabel(restored.operatingSeconds));
            Assert.AreEqual("10:00", CareerClock.TimeLabel(restored.operatingSeconds));
        }

        [TestCase(1, "Winter")]
        [TestCase(4, "Spring")]
        [TestCase(7, "Summer")]
        [TestCase(10, "Autumn")]
        public void SeasonsFollowTheCalendarAcrossYears(int month, string season)
        {
            float elapsed = (float)(new DateTime(2027, month, 15) - CareerClock.Epoch).TotalMinutes;
            Assert.AreEqual(season, CareerClock.Season(elapsed));
        }

        [Test]
        public void ClimateIsDeterministicAndVariesWithBiomeAndTime()
        {
            var random = UnityEngine.Random.state;
            var woodland = new ValleyWorld { seed = 42, preset = RegionPreset.Woodland };
            var mountain = new ValleyWorld { seed = 42, preset = RegionPreset.Mountains };
            var tropical = new ValleyWorld { seed = 42, preset = RegionPreset.Tropical };
            var desert = new ValleyWorld { seed = 42, preset = RegionPreset.Desert };
            float morning = CareerClock.Temperature(woodland, 0f);
            Assert.AreEqual(morning, CareerClock.Temperature(woodland, 0f));
            Assert.Greater(CareerClock.Temperature(woodland, 7f * 60f), morning);
            Assert.Less(CareerClock.Temperature(mountain, 0f), morning);
            Assert.Greater(CareerClock.Temperature(tropical, 0f), morning);
            Assert.Greater(CareerClock.Temperature(desert, 0f), morning);
            float summer = (float)(new DateTime(2026, 7, 15, 15, 0, 0) - CareerClock.Epoch).TotalMinutes;
            float winter = (float)(new DateTime(2027, 1, 15, 15, 0, 0) - CareerClock.Epoch).TotalMinutes;
            Assert.Greater(CareerClock.Temperature(woodland, summer), CareerClock.Temperature(woodland, winter) + 20f);
            Assert.AreEqual(random, UnityEngine.Random.state);
        }

        [Test]
        public void ClockToleratesInvalidOrExtremeDiagnosticTime()
        {
            Assert.AreEqual(CareerClock.Epoch, CareerClock.Date(-1f));
            Assert.AreEqual(CareerClock.Epoch, CareerClock.Date(float.NaN));
            Assert.LessOrEqual(CareerClock.Date(float.MaxValue), DateTime.MaxValue);
        }
    }
}
