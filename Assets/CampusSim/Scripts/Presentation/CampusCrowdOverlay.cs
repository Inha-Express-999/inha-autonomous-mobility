using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using InhaExpress.Client.MapClient;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace InhaExpress.Client.Presentation
{
    /// <summary>
    /// PC overview only. Colors roads by the server's time-of-day crowd prior.
    /// Display only: never reads pedestrian Ground Truth and never feeds planning.
    /// Mobile must not create this overlay (AGENTS §11/§13).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CampusCrowdOverlay : MaskableGraphic
    {
        [Serializable] public sealed class RoadGeometryDocument
        {
            public int schema_version;
            public string map_version, geometry_hash, coordinate_system;
            public bool simulation_only;
            public RoadGeometry[] roads;
        }
        [Serializable] public sealed class RoadGeometry
        {
            public string road_id;
            public Vector3[] points;
        }
        [Serializable] public sealed class CrowdDocument
        {
            public int schema_version;
            public string run_id, geometry_hash, source, verification, time_of_day, unit;
            public bool observed, in_congestion_window, time_override;
            public CrowdRoad[] roads;
            public CrowdZone[] zones;
        }
        [Serializable] private sealed class CrowdTimeCommand { public string action; }
        [Serializable] public sealed class CrowdRoad
        {
            public string road_id, level, zone_id, reason;
            public float rho_prior;
        }
        [Serializable] public sealed class CrowdZone
        {
            public string id, name, level;
            public float rho_prior;
        }

        public static readonly Color CautionColor = new Color(.941f, .627f, .188f);
        public static readonly Color AvoidColor = new Color(.851f, .263f, .290f);
        private const float StaleSeconds = 3f;

        private CampusServerMap map;
        private Camera view;
        private readonly Dictionary<string, Vector3[]> roads = new Dictionary<string, Vector3[]>();
        private CrowdDocument latest;
        private float receivedAt = -100;
        private bool overview = true, enabledByOperator = true;
        private string geometryError, timeError;
        private bool changingTime;
        private Vector3 cameraPosition;
        private Quaternion cameraRotation;
        private float cameraSize, cameraAspect;
        private readonly RaycastHit[] hits = new RaycastHit[32];

        public bool ShowOverview
        {
            set { if (overview == value) return; overview = value; SetVerticesDirty(); }
        }

        public bool Visible
        {
            get => enabledByOperator;
            set { if (enabledByOperator == value) return; enabledByOperator = value; SetVerticesDirty(); }
        }

        public bool TimeOverridden => IsFresh && latest.time_override;

        public bool IsFresh => latest != null && Time.unscaledTime - receivedAt <= StaleSeconds;
        public int HighlightedRoads => IsFresh && latest.roads != null ? latest.roads.Length : 0;

        public string StatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(geometryError)) return "혼잡 예상: " + geometryError;
                if (!string.IsNullOrEmpty(timeError)) return "혼잡 예상: " + timeError;
                if (roads.Count == 0) return "혼잡 예상: 도로 형상 대기";
                if (!IsFresh) return "혼잡 예상: 갱신 중단";
                string time = latest.time_of_day != null && latest.time_of_day.Length >= 5 ? latest.time_of_day.Substring(0, 5) : "—";
                string window = latest.in_congestion_window ? "혼잡 시간대" : "평시";
                string count = HighlightedRoads > 0 ? HighlightedRoads + "개 구간" : "혼잡 구간 없음";
                string source = latest.time_override ? "혼잡 시각 " : "";
                return $"{source}{time} 예상 {window} · {count} · 시간대 추정(실측 아님)";
            }
        }

        public void Initialize(CampusServerMap source, Camera camera)
        {
            map = source;
            view = camera;
            raycastTarget = false;
            StopAllCoroutines();
            StartCoroutine(Run());
        }

        /// <summary>Validates a road geometry document against the authored map hash.</summary>
        public static RoadGeometryDocument ParseRoads(string json, string expectedHash)
        {
            var document = JsonUtility.FromJson<RoadGeometryDocument>(json);
            if (document == null || document.schema_version != 1 || !document.simulation_only ||
                document.coordinate_system != "unity_legacy_x_east_z_north_m" || document.roads == null)
                throw new ArgumentException("Unsupported road geometry document.");
            if (string.IsNullOrEmpty(expectedHash) || document.geometry_hash != expectedHash)
                throw new ArgumentException("Road geometry hash differs from authored map.");
            var ids = new HashSet<string>();
            foreach (var road in document.roads)
            {
                if (road == null || string.IsNullOrEmpty(road.road_id) || !ids.Add(road.road_id) ||
                    road.points == null || road.points.Length < 2)
                    throw new ArgumentException("Invalid or duplicate road geometry.");
                foreach (var point in road.points)
                    if (!Finite(point.x) || !Finite(point.z))
                        throw new ArgumentException("Road geometry must be finite.");
            }
            return document;
        }

        /// <summary>Validates a crowd prior snapshot. Observed density is not supported yet.</summary>
        public static CrowdDocument ParseCrowd(string json, string expectedHash)
        {
            var document = JsonUtility.FromJson<CrowdDocument>(json);
            if (document == null || document.schema_version != 1 || document.observed ||
                document.source != "time_of_day_prior" || document.roads == null ||
                document.geometry_hash != expectedHash)
                throw new ArgumentException("Unsupported crowd prior snapshot.");
            foreach (var road in document.roads)
                if (road == null || string.IsNullOrEmpty(road.road_id) ||
                    (road.level != "CAUTION" && road.level != "AVOID") ||
                    !Finite(road.rho_prior) || road.rho_prior < 0)
                    throw new ArgumentException("Invalid crowd road level.");
            return document;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private IEnumerator Run()
        {
            while (roads.Count == 0)
            {
                using (var request = UnityWebRequest.Get(map.serverUrl.TrimEnd('/') + "/v1/demo/unity-roads"))
                {
                    request.timeout = 5;
                    yield return request.SendWebRequest();
                    try
                    {
                        if (request.result != UnityWebRequest.Result.Success)
                            throw new InvalidOperationException("서버 연결 대기");
                        LoadRoads(ParseRoads(request.downloadHandler.text, map.geometryHash));
                        geometryError = null;
                    }
                    catch (ArgumentException) { geometryError = "지도 해시 불일치"; }
                    catch (InvalidOperationException error) { geometryError = error.Message; }
                }
                if (roads.Count == 0) yield return new WaitForSecondsRealtime(5);
            }
            while (true)
            {
                using (var request = UnityWebRequest.Get(map.serverUrl.TrimEnd('/') + "/v1/demo/crowd"))
                {
                    request.timeout = 2;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            latest = ParseCrowd(request.downloadHandler.text, map.geometryHash);
                            receivedAt = Time.unscaledTime;
                            SetVerticesDirty();
                        }
                        catch (ArgumentException) { latest = null; }
                    }
                }
                yield return new WaitForSecondsRealtime(1);
            }
        }

        /// <summary>
        /// Moves only the crowd prior's time of day ("next_peak" or "reset").
        /// The simulation clock, vehicles and sensor timing are unchanged.
        /// </summary>
        public IEnumerator ChangeTime(string action)
        {
            if (changingTime || map == null) yield break;
            changingTime = true;
            using (var request = new UnityWebRequest(map.serverUrl.TrimEnd('/') + "/v1/demo/crowd/time", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new CrowdTimeCommand { action = action })));
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 5;
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                timeError = null;
                if (request.result != UnityWebRequest.Result.Success) timeError = "시각 변경 실패";
                else
                {
                    try
                    {
                        latest = ParseCrowd(request.downloadHandler.text, map.geometryHash);
                        receivedAt = Time.unscaledTime;
                        SetVerticesDirty();
                    }
                    catch (ArgumentException) { timeError = "시각 변경 응답 오류"; }
                }
            }
            changingTime = false;
        }

        private void LoadRoads(RoadGeometryDocument document)
        {
            roads.Clear();
            foreach (var road in document.roads)
            {
                var points = (Vector3[])road.points.Clone();
                float height = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    height = SurfaceHeight(points[i], height);
                    points[i].y = height + .25f;
                }
                roads.Add(road.road_id, points);
            }
            SetVerticesDirty();
        }

        private float SurfaceHeight(Vector3 point, float previous)
        {
            int count = Physics.RaycastNonAlloc(new Ray(new Vector3(point.x, 1000, point.z), Vector3.down), hits, 2000);
            float? terrain = null;
            for (int i = 0; i < count; i++)
            {
                var collider = hits[i].collider;
                if (collider.name == "Continuous asphalt network" || collider.name.StartsWith("Road ", StringComparison.Ordinal))
                    return hits[i].point.y;
                if (collider is TerrainCollider) terrain = hits[i].point.y;
            }
            return terrain ?? previous;
        }

        private void LateUpdate()
        {
            if (view == null) return;
            if (latest != null && !IsFresh) { latest = null; SetVerticesDirty(); }
            if (!enabledByOperator || !overview || latest == null) return;
            if (cameraPosition != view.transform.position || cameraRotation != view.transform.rotation ||
                cameraSize != view.orthographicSize || cameraAspect != view.aspect)
            {
                cameraPosition = view.transform.position; cameraRotation = view.transform.rotation;
                cameraSize = view.orthographicSize; cameraAspect = view.aspect;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (!enabledByOperator || !overview || view == null || latest?.roads == null) return;
            var rect = rectTransform.rect;
            foreach (var road in latest.roads)
            {
                if (!roads.TryGetValue(road.road_id, out var points)) continue;
                bool avoid = road.level == "AVOID";
                var tint = avoid ? AvoidColor : CautionColor;
                float dashPhase = 0;
                for (int i = 1; i < points.Length; i++)
                {
                    var a = view.WorldToViewportPoint(points[i - 1]);
                    var b = view.WorldToViewportPoint(points[i]);
                    if (a.z <= 0 || b.z <= 0) continue;
                    Vector2 from = new Vector2(rect.xMin + a.x * rect.width, rect.yMin + a.y * rect.height);
                    Vector2 to = new Vector2(rect.xMin + b.x * rect.width, rect.yMin + b.y * rect.height);
                    var delta = to - from;
                    float length = delta.magnitude;
                    if (length < .01f) continue;
                    var normal = new Vector2(-delta.y, delta.x) / length;
                    AddQuad(mesh, from, to, normal * (avoid ? 4.5f : 4f), new Color(1, 1, 1, .8f));
                    if (!avoid) { AddQuad(mesh, from, to, normal * 2.5f, tint); continue; }
                    // AVOID is dashed so the level is distinguishable without color (AGENTS §13).
                    const float dash = 9, gap = 4;
                    float position = -dashPhase;
                    while (position < length)
                    {
                        float start = Mathf.Max(0, position), end = Mathf.Min(length, position + dash);
                        if (end > start)
                            AddQuad(mesh, from + delta * (start / length), from + delta * (end / length), normal * 3f, tint);
                        position += dash + gap;
                    }
                    dashPhase = (dashPhase + length) % (dash + gap);
                }
            }
        }

        private static void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 normal, Color tint)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a - normal, tint, Vector2.zero); mesh.AddVert(a + normal, tint, Vector2.zero);
            mesh.AddVert(b + normal, tint, Vector2.zero); mesh.AddVert(b - normal, tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
