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
        [UnityTest]
        public IEnumerator ContinuousScanSeesSpawnedColliderAndRemovalWithoutTriggerWiring()
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
                Assert.That(empty.ray_count, Is.EqualTo(2520));
                Assert.That(empty.hits, Is.Empty, "Flat ground must not become an obstacle.");
                obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.transform.position = new Vector3(0,.5f,4);
                obstacle.AddComponent<LidarTarget>().SetObservedClass(LidarEntityClass.Pedestrian);
                Physics.SyncTransforms();
                var detected = source.Capture(2);
                Assert.That(detected.hits.Length, Is.GreaterThan(0));
                Assert.That(System.Array.Exists(detected.hits,h=>h.entity_class=="pedestrian" && h.forward_m>3),Is.True);
                var integration = root.GetComponentInChildren<PcLidarIntegration>();
                Assert.That(integration.EffectActive, Is.True);
                source.Capture(3);
                Assert.That(unrelated.GetComponent<Renderer>().enabled, Is.True);
                Assert.That(ui.enabled, Is.True, "Automatic overlay must preserve the operator UI.");
                Assert.That(integration.GetComponentInChildren<ParticleSystem>().particleCount, Is.GreaterThan(0));
                Object.Destroy(obstacle); obstacle=null;
                yield return null;
                Physics.SyncTransforms();
                Assert.That(source.Capture(4).hits, Is.Empty);
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
                Assert.That(System.Array.Exists(frame.hits,h=>h.entity_class=="vehicle" && h.forward_m< -3),Is.True);
                Assert.That(System.Array.Exists(frame.hits,h=>Mathf.Abs(h.forward_m)<1 && Mathf.Abs(h.lateral_m)<1),Is.False);
            }
            finally {Object.Destroy(root);Object.Destroy(other);}
            yield return null;
        }
    }
}
