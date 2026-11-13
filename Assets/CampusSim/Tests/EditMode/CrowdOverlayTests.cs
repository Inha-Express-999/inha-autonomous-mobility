using System;
using InhaExpress.Client.Presentation;
using NUnit.Framework;

namespace InhaExpress.Client.Tests
{
    public sealed class CrowdOverlayTests
    {
        private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private static string Roads(string hash = Hash, string secondId = "r2", string coordinates = "unity_legacy_x_east_z_north_m") =>
            "{\"schema_version\":1,\"map_version\":\"m\",\"geometry_hash\":\"" + hash + "\",\"coordinate_system\":\"" + coordinates +
            "\",\"simulation_only\":true,\"roads\":[" +
            "{\"road_id\":\"r1\",\"points\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":10,\"y\":0,\"z\":0}]}," +
            "{\"road_id\":\"" + secondId + "\",\"points\":[{\"x\":10,\"y\":0,\"z\":0},{\"x\":10,\"y\":0,\"z\":5}]}]}";

        private static string Crowd(string level = "AVOID", bool observed = false, string hash = Hash, string source = "time_of_day_prior") =>
            "{\"schema_version\":1,\"run_id\":\"run\",\"geometry_hash\":\"" + hash + "\",\"source\":\"" + source +
            "\",\"observed\":" + (observed ? "true" : "false") + ",\"time_of_day\":\"08:55:00\",\"in_congestion_window\":true," +
            "\"roads\":[{\"road_id\":\"r1\",\"level\":\"" + level + "\",\"rho_prior\":0.5,\"zone_id\":\"z\",\"reason\":\"PRIOR_DENSITY\"}],\"zones\":[]}";

        [Test]
        public void RoadGeometryRequiresAuthoredHashAndUniqueRoads()
        {
            var document = CampusCrowdOverlay.ParseRoads(Roads(), Hash);
            Assert.That(document.roads.Length, Is.EqualTo(2));
            Assert.That(document.roads[1].points[1].z, Is.EqualTo(5f));
            Assert.Throws<ArgumentException>(() => CampusCrowdOverlay.ParseRoads(Roads(hash: new string('b', 64)), Hash));
            Assert.Throws<ArgumentException>(() => CampusCrowdOverlay.ParseRoads(Roads(secondId: "r1"), Hash));
            Assert.Throws<ArgumentException>(() => CampusCrowdOverlay.ParseRoads(Roads(coordinates: "aeqd"), Hash));
            Assert.Throws<ArgumentException>(() => CampusCrowdOverlay.ParseRoads(Roads(), null));
        }

        [Test]
        public void CrowdSnapshotAcceptsOnlyPriorCautionOrAvoid()
        {
            var crowd = CampusCrowdOverlay.ParseCrowd(Crowd(), Hash);
            Assert.That(crowd.roads[0].level, Is.EqualTo("AVOID"));
            Assert.That(crowd.in_congestion_window, Is.True);
            Assert.That(CampusCrowdOverlay.ParseCrowd(Crowd("CAUTION"), Hash).roads.Length, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => CampusCrowdOverlay.ParseCrowd(Crowd("CLOSED"), Hash));
            Assert.Throws<ArgumentException>(() => CampusCrowdOverlay.ParseCrowd(Crowd(observed: true), Hash));
            Assert.Throws<ArgumentException>(() => CampusCrowdOverlay.ParseCrowd(Crowd(source: "ground_truth"), Hash));
            Assert.Throws<ArgumentException>(() => CampusCrowdOverlay.ParseCrowd(Crowd(hash: new string('c', 64)), Hash));
        }

        [Test]
        public void CrowdSnapshotCarriesOperatorTimeOverride()
        {
            Assert.That(CampusCrowdOverlay.ParseCrowd(Crowd(), Hash).time_override, Is.False);
            var json = Crowd().Replace("\"in_congestion_window\":true", "\"in_congestion_window\":true,\"time_override\":true");
            Assert.That(CampusCrowdOverlay.ParseCrowd(json, Hash).time_override, Is.True);
        }
    }
}
