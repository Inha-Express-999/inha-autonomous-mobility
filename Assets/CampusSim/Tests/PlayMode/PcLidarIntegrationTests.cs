using System.Collections;
using InhaExpress.Client.Presentation;
using InhaExpress.Client.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class PcLidarIntegrationTests
    {
        // Channel 3 of the PC layout (-30..+15 deg, 7 channels) is -7.5 deg.
        private const int MinusSevenFiveChannel = 3;
        private const int ForwardColumn = 180, RearColumn = 0;

        private static int Range(PhysicalLidarFrame frame, int channel, int column) =>
            frame.ranges_mm[channel * frame.columns + column];

        [UnityTest]
        public IEnumerator ContinuousScanSendsRawRangesWithoutLabelsAndTracksRemoval()
        {
            var root = new GameObject("lidar-owner");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0,-.1f,0);
            ground.transform.localScale = new Vector3(40,.2f,40);
            var unrelated = GameObject.CreatePrimitive(PrimitiveType.Cube);
            unrelated.transform.position = new Vector3(25,1,25);
            var ui = new GameObject("operator-canvas").AddComponent<Canvas>();
            GameObject obstacle = null;
            try
            {
                var source = PcLidarIntegration.Attach(root.transform,"V01",new Bounds(new Vector3(0,.5f,0),new Vector3(1,1,2)));
                Assert.That(PhysicalLidarSource.Create, Is.Not.Null);
                Physics.SyncTransforms();
                var empty = source.Capture(1);
                Assert.That(empty.valid, Is.True);
                Assert.That(empty.columns, Is.EqualTo(360));
                Assert.That(empty.channel_elevations_deg.Length, Is.EqualTo(7));
                Assert.That(empty.channel_elevations_deg[MinusSevenFiveChannel], Is.EqualTo(-7.5f).Within(.001f));
                Assert.That(empty.ranges_mm.Length, Is.EqualTo(2520));
                Assert.That(empty.azimuth_span_deg, Is.EqualTo(360f));
                Assert.That(empty.sensor_up_m, Is.EqualTo(1.25f).Within(.01f));
                Assert.That(empty.ground_up_m, Is.EqualTo(0f).Within(.01f), "Body-bottom fallback without wheels.");
                // Raw sensor: flat ground is returned, not pre-filtered in Unity.
                float flatGroundMm = 1250f / Mathf.Sin(7.5f * Mathf.Deg2Rad);
                Assert.That(Range(empty, MinusSevenFiveChannel, ForwardColumn), Is.EqualTo(flatGroundMm).Within(40));

                obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.transform.position = new Vector3(0,.5f,4);
                obstacle.AddComponent<LidarTarget>().SetObservedClass(LidarEntityClass.Pedestrian);
                Physics.SyncTransforms();
                var detected = source.Capture(2);
                float faceMm = 3500f / Mathf.Cos(7.5f * Mathf.Deg2Rad);
                Assert.That(Range(detected, MinusSevenFiveChannel, ForwardColumn), Is.EqualTo(faceMm).Within(40));
                Assert.That(JsonUtility.ToJson(detected), Does.Not.Contain("pedestrian"), "Labels must not cross the control boundary.");
                var integration = root.GetComponentInChildren<PcLidarIntegration>();
                Assert.That(integration.EffectActive, Is.True);
                source.Capture(3);
                Assert.That(unrelated.GetComponent<Renderer>().enabled, Is.True);
                Assert.That(ui.enabled, Is.True, "Automatic overlay must preserve the operator UI.");
                Assert.That(integration.GetComponentInChildren<ParticleSystem>().particleCount, Is.GreaterThan(0));
                Object.Destroy(obstacle); obstacle=null;
                yield return null;
                Physics.SyncTransforms();
                Assert.That(Range(source.Capture(4), MinusSevenFiveChannel, ForwardColumn), Is.EqualTo(flatGroundMm).Within(40));
                yield return new WaitForSecondsRealtime(.8f);
                source.SetSafetyFeedback(false);
                source.Capture(5);
                Assert.That(integration.EffectActive, Is.False);
            }
            finally
            {
                if(obstacle!=null) Object.Destroy(obstacle);
                Object.Destroy(root); Object.Destroy(ground); Object.Destroy(unrelated); Object.Destroy(ui.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RearVehicleIsObservedAndOwnCollidersAreExcluded()
        {
            var root=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var other=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                root.transform.position=new Vector3(0,.5f,0);
                other.transform.position=new Vector3(0,.5f,-4);
                PcLidarIntegration.Attach(other.transform,"V02",new Bounds(Vector3.zero,Vector3.one));
                var source=PcLidarIntegration.Attach(root.transform,"V01",new Bounds(Vector3.zero,Vector3.one));
                Physics.SyncTransforms();
                var frame=source.Capture(1);
                Assert.That(frame.valid,Is.True);
                float faceMm = 3500f / Mathf.Cos(7.5f * Mathf.Deg2Rad);
                Assert.That(Range(frame, MinusSevenFiveChannel, RearColumn), Is.EqualTo(faceMm).Within(40));
                Assert.That(System.Array.Exists(frame.ranges_mm, r => r > 0 && r < 900), Is.False, "Own body must be masked.");
                Assert.That(JsonUtility.ToJson(frame), Does.Not.Contain("vehicle"));
            }
            finally {Object.Destroy(root);Object.Destroy(other);}
            yield return null;
        }
    }
}
