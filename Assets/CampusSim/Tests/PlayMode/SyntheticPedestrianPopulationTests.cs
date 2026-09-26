using System.Collections;
using InhaExpress.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class SyntheticPedestrianPopulationTests
    {
        private GameObject root;
        private GameObject template;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (root != null) Object.Destroy(root);
            if (template != null) Object.Destroy(template);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThreeHundredActorsMovePauseAndReplayWithoutGhostColliders()
        {
            root = new GameObject("Population test");
            template = new GameObject("Synthetic capsule template");
            template.SetActive(false);
            var walker = template.AddComponent<SyntheticPedestrianWalker>();
            var population = root.AddComponent<SyntheticPedestrianPopulation>();
            var origin = new Vector3(10000, 0, 10000);
            population.SpawnLanes(walker, 300, origin, 20, 2f, 3f, 1f);
            Assert.That(population.Count, Is.EqualTo(300));
            var first = population.Actors[0];
            var last = population.Actors[299];
            Assert.That(first.transform.position, Is.EqualTo(origin));
            Assert.That(last.transform.position, Is.EqualTo(origin + new Vector3(38, 0, 28)));
            foreach (var actor in population.Actors)
                Assert.That(actor.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Pedestrian")));
            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Assert.That(first.GetComponent<Rigidbody>().position.z, Is.GreaterThan(origin.z));
            Assert.That(last.GetComponent<Rigidbody>().position.z, Is.GreaterThan(origin.z + 28));
            population.SetPaused(true);
            yield return new WaitForFixedUpdate();
            var held = first.GetComponent<Rigidbody>().position;
            for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            Assert.That(first.GetComponent<Rigidbody>().position, Is.EqualTo(held));

            population.SpawnLanes(walker, 300, origin, 20, 2f, 3f, 1f);
            Assert.That(first.gameObject.activeSelf, Is.False, "Retired colliders must deactivate immediately.");
            Assert.That(population.Actors[299].transform.position, Is.EqualTo(origin + new Vector3(38, 0, 28)));
            yield return null;
            Assert.That(root.GetComponentsInChildren<CapsuleCollider>(true).Length, Is.EqualTo(300));
            population.Clear();
            Assert.That(population.Count, Is.Zero);
            yield return null;
            Assert.That(root.GetComponentsInChildren<CapsuleCollider>(true), Is.Empty);
        }
    }
}
