using InhaExpress.Client.Presentation;
using InhaExpress.Client.MapClient;
using System.Collections.Generic;
using UnityEngine;

namespace InhaExpress.Client.Simulation
{
    /// <summary>Operator-created Unity ground truth fixture. Server receives only ordinary LiDAR returns.</summary>
    public sealed class CampusPedestrianScenario : MonoBehaviour
    {
        private static CampusPedestrianScenario active;
        private PedestrianSpawnTrigger trigger;
        private float started;
        public GameObject Actor => trigger != null ? trigger.LastSpawned : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            active = null;
            PedestrianScenarioActions.SpawnAhead = Spawn;
            PedestrianScenarioActions.ReadStatus = () => active == null ? "차량 앞 도로에 보행자를 등장시킵니다" :
                active.Actor == null ? "횡단 완료 · 차량 안전 확인 후 재출발" :
                active.Actor.GetComponent<SyntheticPedestrianWalker>().IsWaiting ? "주변을 살피는 중 · 5초 후 횡단" :
                "보행자 횡단 중 · 차량 안전 확인";
        }

        private static string Spawn(IOperatorVehicle vehicle)
        {
            if (vehicle == null || vehicle.VehicleTransform == null) return "차량을 먼저 선택하세요";
            if (active != null && active.Actor != null) return "기존 보행자의 횡단이 끝난 후 다시 실행하세요";
            var prefab = Resources.Load<GameObject>("Pedestrians/CrossingVisual");
            if (prefab == null) return "보행자 에셋을 불러올 수 없습니다";
            float lead = 10 + (vehicle.VehicleBody != null ? vehicle.VehicleBody.linearVelocity.magnitude * 5 : 0);
            Vector3 center = default;
            Vector3[] path = null;
            // Nearby alternatives avoid rejecting the whole action at a driveway,
            // curb transition or junction seam. All candidates stay ahead on a road.
            foreach (float offset in new[] { 0f, 4f, -4f, 8f, -8f })
            {
                if (TryCrossing(vehicle.VehicleTransform, Mathf.Max(6, lead + offset), out center,
                    out var start, out var end) && TryWalkPath(start, end, out path)) break;
            }
            if (path == null) return "앞쪽 도로에 횡단 공간이 없습니다 · 직선 도로에서 다시 실행하세요";
            if (active != null) Destroy(active.gameObject);
            var root = new GameObject("PC UI Synthetic Pedestrian Crossing");
            root.transform.position = center;
            active = root.AddComponent<CampusPedestrianScenario>();
            active.started = Time.time;
            var trigger = root.AddComponent<PedestrianSpawnTrigger>();
            trigger.GetComponent<BoxCollider>().enabled = false; // UI is the sole trigger in this fixture.
            trigger.pedestrianPrefab = prefab;
            trigger.initialWaitSeconds = 5; // Visible waiting animation, then a continuous crossing.
            trigger.walkingPath = path;
            trigger.spawnPoint = new GameObject("Crossing Start").transform;
            trigger.spawnPoint.SetParent(root.transform, false);
            trigger.spawnPoint.position = path[0];
            trigger.destination = new GameObject("Crossing End").transform;
            trigger.destination.SetParent(root.transform, false);
            trigger.destination.position = path[path.Length - 1];
            active.trigger = trigger;
            if (!trigger.TrySpawn()) { Destroy(root); return "보행자 생성에 실패했습니다"; }
            Debug.Log("CAMPUS_PEDESTRIAN_UI_SPAWN vehicle=" + vehicle.OperatorVehicleId);
            return vehicle.OperatorVehicleId + " 앞 보행자 생성 · 주변 확인 5초 후 횡단";
        }

        // Static map and Physics are used only to place the operator's test actor.
        // Neither the route nor this ground truth is sent to the safety planner.
        private static bool TryCrossing(Transform vehicle, float lead, out Vector3 center, out Vector3 start, out Vector3 end)
        {
            center = start = end = default;
            var map = FindFirstObjectByType<CampusServerMap>();
            if (map == null || map.Document == null) return false;
            var roadObject = GameObject.Find("Continuous asphalt network");
            var road = roadObject != null ? roadObject.GetComponent<Collider>() : null;
            if (road == null) return false;
            var nodes = new Dictionary<string, Vector3>();
            foreach (var node in map.Document.nodes) nodes[node.id] = node.position;
            var forward = Vector3.ProjectOnPlane(vehicle.forward, Vector3.up).normalized;
            // Preserve the five-second looking-around animation without letting a
            // moving vehicle pass the crossing before the actor starts walking.
            var target = vehicle.position + forward * lead;
            target.y = 0;
            float best = 4; // Refuse crossings away from the mapped road.
            Vector3 tangent = default;
            foreach (var edge in map.Document.edges)
            {
                var a = nodes[edge.u]; var b = nodes[edge.v];
                var delta = b - a;
                if (delta.sqrMagnitude < .01f || Mathf.Abs(Vector3.Dot(delta.normalized, forward)) < .7f) continue;
                var point = a + delta * Mathf.Clamp01(Vector3.Dot(target - a, delta) / delta.sqrMagnitude);
                var ahead = Vector3.Dot(point - new Vector3(vehicle.position.x, 0, vehicle.position.z), forward);
                float error = Vector3.Distance(point, target);
                if (ahead < lead - 4 || ahead > lead + 4 || error >= best) continue;
                best = error; center = point; tangent = delta.normalized;
            }
            if (tangent == Vector3.zero) return false;
            if (Vector3.Dot(tangent, forward) < 0) tangent = -tangent;
            center.y = vehicle.position.y;
            if (!RoadPoint(road, center, out center)) return false;
            var right = Vector3.Cross(Vector3.up, tangent);
            if (!RoadEdge(road, center, -right, out start) || !RoadEdge(road, center, right, out end)) return false;
            // Start at the verge nearest the followed vehicle's lane.
            if (Vector3.Dot(vehicle.position - center, right) > .1f)
                (start, end) = (end, start);
            return true;
        }

        private static bool RoadPoint(Collider road, Vector3 point, out Vector3 result)
        {
            result = point;
            if (!road.Raycast(new Ray(point + Vector3.up * 2, Vector3.down), out var hit, 4)) return false;
            result = hit.point;
            return hit.normal.y > .9f;
        }

        private static bool RoadEdge(Collider road, Vector3 center, Vector3 direction, out Vector3 edge)
        {
            edge = center;
            for (float distance = .25f; distance <= 10; distance += .25f)
            {
                var point = center + direction * distance;
                if (RoadPoint(road, point, out _)) continue;
                edge = center + direction * (distance + .3f);
                return true;
            }
            return false; // Wide junction: no unambiguous crossing here.
        }

        private static bool TryWalkPath(Vector3 start, Vector3 end, out Vector3[] path)
        {
            path = null;
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / .25f));
            var points = new Vector3[count + 1];
            for (int i = 0; i <= count; i++)
            {
                var point = Vector3.Lerp(start, end, i / (float)count);
                if (!Ground(point, out var surface)) return false;
                // Support the complete capsule footprint on the higher side of a
                // traversable curb. A center-only ray leaves the capsule intersecting
                // the curb's vertical lip, falsely treating every sidewalk as a wall.
                foreach (var offset in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                    if (Ground(point + offset * .28f, out var support) && support.y > surface.y)
                        surface.y = support.y;
                if (i > 0 && Mathf.Abs(surface.y - points[i - 1].y) > .35f) return false;
                if (Physics.CheckCapsule(surface + Vector3.up * .4f, surface + Vector3.up * 1.45f,
                    .28f, ~0, QueryTriggerInteraction.Ignore)) return false;
                points[i] = new Vector3(point.x, surface.y, point.z);
            }
            path = points;
            return true;
        }

        private static bool Ground(Vector3 point, out Vector3 result)
        {
            result = point;
            var hits = Physics.RaycastAll(point + Vector3.up * 2, Vector3.down, 4, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (hit.rigidbody != null || Vector3.Dot(hit.normal, Vector3.up) < .9f) continue;
                if (Mathf.Abs(hit.point.y - point.y) >= 1.5f) continue;
                if (hit.point.y > best) { best = hit.point.y; result = hit.point + Vector3.up * .02f; }
            }
            return !float.IsNegativeInfinity(best);
        }

        private void Update()
        {
            if (Actor == null && Time.time - started > 20) Destroy(gameObject);
        }
    }
}
