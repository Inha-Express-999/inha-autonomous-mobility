#if UNITY_EDITOR
using System.Collections;
using InhaExpress.Client.Simulation;
using InhaExpress.Client.Presentation;
using InhaExpress.Client.MapClient;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace InhaExpress.Client.Tests
{
    public sealed class PedestrianSpawnTests
    {
        [UnityTest]
        public IEnumerator OperatorCrossingShowsAnimatedIdleThenWalksAfterFiveSeconds()
        {
            var vehicle = new GameObject("Animated waiting vehicle");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mapObject = new GameObject("Crossing test map");
            GameObject fixture = null;
            try
            {
                vehicle.transform.position = new Vector3(10000, 100, 10000);
                var selected = vehicle.AddComponent<CameraTestVehicle>();
                selected.id = "animated-waiting";
                // Deliberately misalign the vehicle with the road: the crossing
                // must follow the road normal, rather than the current steering yaw.
                vehicle.transform.rotation = Quaternion.Euler(0, 20, 0);
                ground.name = "Continuous asphalt network";
                ground.transform.position = vehicle.transform.position + Vector3.down * .5f;
                ground.transform.localScale = new Vector3(8, 1, 50);
                // Wide flat supporting verge, beneath the isolated asphalt strip.
                var verge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                verge.transform.position = ground.transform.position + Vector3.down * .05f;
                verge.transform.localScale = new Vector3(30, .9f, 50);
                verge.transform.SetParent(ground.transform, true);
                foreach (float side in new[] { -1f, 1f })
                {
                    var kerb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    kerb.name = "Crossing curb regression";
                    kerb.transform.position = vehicle.transform.position + new Vector3(side * 4, .1f, 0);
                    kerb.transform.localScale = new Vector3(.3f, .2f, 50);
                    kerb.transform.SetParent(ground.transform, true);
                }
                mapObject.SetActive(false);
                var map = mapObject.AddComponent<CampusServerMap>();
                map.connectOnPlay = false;
                var doc = new ServerMapDocument {
                    nodes = new[] { new ServerMapNode { id = "a", position = vehicle.transform.position + Vector3.back * 25 },
                        new ServerMapNode { id = "b", position = vehicle.transform.position + Vector3.forward * 25 } },
                    edges = new[] { new ServerMapEdge { u = "a", v = "b", key = "0" } }
                };
                foreach (var node in doc.nodes) node.position.y = 0;
                typeof(CampusServerMap).GetField("localMap", System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance).SetValue(map, doc);
                map.enabled = false; mapObject.SetActive(true);
                Physics.SyncTransforms();
                Assert.That(PedestrianScenarioActions.SpawnAhead, Is.Not.Null);
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(ground.transform, true);
                wall.transform.position = vehicle.transform.position + Vector3.up;
                wall.transform.localScale = new Vector3(.25f, 2, 1); // Scaled by the road parent: spans every candidate.
                Physics.SyncTransforms();
                var rejected = PedestrianScenarioActions.SpawnAhead(selected);
                Assert.That(Object.FindFirstObjectByType<CampusPedestrianScenario>(), Is.Null,
                    "A wall must still block crossing placement: " + rejected);
                Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
                var message = PedestrianScenarioActions.SpawnAhead(selected);
                var scenario = Object.FindFirstObjectByType<CampusPedestrianScenario>();
                Assert.That(scenario, Is.Not.Null, message);
                fixture = scenario.gameObject;
                var actor = scenario.Actor;
                Assert.That(actor, Is.Not.Null, message);
                var trigger = fixture.GetComponent<PedestrianSpawnTrigger>();
                Assert.That(trigger.initialWaitSeconds, Is.EqualTo(5));
                Assert.That(trigger.spawnPoint.position.x - vehicle.transform.position.x, Is.LessThan(-4));
                Assert.That(trigger.destination.position.x - vehicle.transform.position.x, Is.GreaterThan(4));
                Assert.That(trigger.spawnPoint.position.z, Is.EqualTo(trigger.destination.position.z).Within(.01f));
                Assert.That(System.Array.Exists(trigger.walkingPath, p => p.y > vehicle.transform.position.y + .15f),
                    Is.True, "The walking path must step over the curb, not reject it or walk through it.");
                var animator = actor.GetComponentInChildren<Animator>();
                Assert.That(animator.runtimeAnimatorController, Is.TypeOf<AnimatorOverrideController>());
                Assert.That(animator.GetBool("walk"), Is.False);
                // Inspect bone motion even though this isolated fixture is off screen.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Update(0);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle 1"), Is.True);
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                var before = actor.transform.position;
                var headBefore = head.localRotation;
                CapturePose(actor, "idle-start");
                yield return new WaitForSeconds(1.1f);
                Assert.That(Vector3.Distance(actor.transform.position, before), Is.LessThan(.01f),
                    "The collider stays stationary while the visual looks around.");
                Assert.That(Quaternion.Angle(headBefore, head.localRotation), Is.GreaterThan(5),
                    "Idle must visibly animate the head instead of appearing frozen.");
                Assert.That(animator.GetBool("walk"), Is.False);
                var headLeft = head.localRotation;
                CapturePose(actor, "idle-look-left");
                yield return new WaitForSeconds(3.2f);
                Assert.That(Vector3.Distance(actor.transform.position, before), Is.LessThan(.01f));
                Assert.That(animator.GetBool("walk"), Is.False, "Keep the explicit five-second wait.");
                Assert.That(Quaternion.Angle(headLeft, head.localRotation), Is.GreaterThan(8),
                    "The idle should visibly look in both directions during the wait.");
                CapturePose(actor, "idle-look-right");
                yield return new WaitForSeconds(1.2f);
                Assert.That(Vector3.Dot(actor.transform.position - before, vehicle.transform.right),
                    Is.GreaterThan(.3f), "Walk across after the idle period.");
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Walking"), Is.True);
                CapturePose(actor, "walking");
                Assert.That(PedestrianScenarioActions.SpawnAhead(selected), Does.Contain("기존 보행자"));
            }
            finally
            {
                if (fixture != null) Object.DestroyImmediate(fixture);
                Object.DestroyImmediate(vehicle);
                Object.DestroyImmediate(ground);
                Object.DestroyImmediate(mapObject);
            }
        }

        private static void CapturePose(GameObject actor, string name)
        {
            var rig = new GameObject("Waiting animation capture");
            var target = RenderTexture.GetTemporary(384, 512, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var image = new Texture2D(384, 512, TextureFormat.RGB24, false);
            try
            {
                var camera = rig.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.32f, .36f, .4f);
                camera.orthographic = true;
                camera.orthographicSize = 1.1f;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 20;
                camera.transform.position = actor.transform.position + new Vector3(3, 1.2f, -3);
                camera.transform.LookAt(actor.transform.position + Vector3.up * .9f);
                var lightObject = new GameObject("Capture light");
                lightObject.transform.SetParent(rig.transform, false);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 2;
                light.transform.rotation = Quaternion.Euler(35, -40, 0);
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 384, 512), 0, 0);
                image.Apply();
                var folder = System.IO.Path.GetFullPath(Application.dataPath +
                    "/../artifacts/validation/2026-11-02-operator-encounter/poses");
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(rig);
            }
        }

        [UnityTest]
        public IEnumerator VehicleEntrySpawnsAnimatedSensorVisiblePedestrianOnce()
        {
            var root = new GameObject("Pedestrian trigger test");
            var vehicle = new GameObject("Synthetic vehicle");
            var sensor = new GameObject("Test sensor");
            try
            {
                // Isolate from any currently loaded campus colliders.
                root.transform.position = new Vector3(10000, 100, 10000);
                var trigger = root.AddComponent<PedestrianSpawnTrigger>();
                trigger.GetComponent<BoxCollider>().isTrigger = true;
                trigger.GetComponent<BoxCollider>().size = Vector3.one * 2;
                trigger.pedestrianPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/CampusSim/Art/Pedestrian/Pedestrian.prefab");
                Assert.That(trigger.pedestrianPrefab, Is.Not.Null);
                trigger.spawnPoint = new GameObject("Start").transform;
                trigger.spawnPoint.SetParent(root.transform, false);
                trigger.spawnPoint.localPosition = new Vector3(3, 0, 0);
                trigger.destination = new GameObject("End").transform;
                trigger.destination.SetParent(root.transform, false);
                trigger.destination.localPosition = new Vector3(3, 0, 2);
                vehicle.transform.position = root.transform.position + Vector3.back * 4;
                vehicle.AddComponent<BoxCollider>();
                var body = vehicle.AddComponent<Rigidbody>();
                body.isKinematic = true;
                vehicle.AddComponent<LidarTarget>().SetObservedClass(LidarEntityClass.Vehicle);
                yield return new WaitForFixedUpdate();
                body.position = root.transform.position;
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
                yield return null;
                Assert.That(trigger.SpawnCount, Is.EqualTo(1), "Actual Physics trigger entry must spawn.");
                Assert.That(trigger.TrySpawn(), Is.False, "One-shot prevents repeat spawns.");
                var actor = trigger.LastSpawned;
                var animator = actor.GetComponentInChildren<Animator>();
                Assert.That(animator, Is.Not.Null);
                Assert.That(animator.avatar.isValid, Is.True);
                Assert.That(animator.GetBool("walk"), Is.True);
                Assert.That(animator.applyRootMotion, Is.False);
                Vector3 before = actor.transform.position;
                sensor.transform.position = before + new Vector3(0, 1, -2);
                var rig = sensor.AddComponent<LidarSensorRig>();
                Physics.SyncTransforms();
                rig.ScanNow();
                bool seen = false;
                for (int i = 0; i < rig.LatestObservation.DetectionCount; i++)
                    seen |= rig.LatestObservation.GetDetection(i).EntityClass == LidarEntityClass.Pedestrian;
                Assert.That(seen, Is.True, "Raycast LiDAR must classify the actor collider.");
                yield return new WaitForSeconds(.4f);
                Assert.That(Vector3.Distance(before, actor.transform.position), Is.GreaterThan(.3f));
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Walking"), Is.True);
                yield return new WaitForSeconds(1.5f);
                Assert.That(animator.GetBool("walk"), Is.False, "Arrival switches to idle.");
                yield return new WaitForSeconds(2.2f);
                Assert.That(actor == null, Is.True, "Arrived actor must clean itself up.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(vehicle);
                Object.DestroyImmediate(sensor);
            }
        }
    }
}
#endif
