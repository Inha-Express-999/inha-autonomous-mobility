using UnityEngine;
using UnityEngine.UI;

namespace InhaExpress.Client.Presentation
{
    /// <summary>PC overview only. Draw the server plan without affecting Physics or planning.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CampusRouteOverlay : MaskableGraphic
    {
        private Camera view;
        private string mission;
        private Vector3[] points;
        private float[] distances;
        private float progress;
        private bool visible, overview = true;
        private Vector3 cameraPosition;
        private Quaternion cameraRotation;
        private float cameraSize, cameraAspect;
        private readonly RaycastHit[] roadHits = new RaycastHit[32];

        public bool ShowOverview
        {
            set { if (overview == value) return; overview = value; SetVerticesDirty(); }
        }

        public void SetRoute(Camera camera, string id, Vector3[] route, float travelled, bool active)
        {
            view = camera;
            if (mission != id || points == null && route != null && route.Length > 1)
            {
                mission = id;
                points = route == null ? null : (Vector3[])route.Clone();
                distances = points == null ? null : new float[points.Length];
                float height = 0;
                for (int i = 0; points != null && i < points.Length; i++)
                {
                    if (i > 0) distances[i] = distances[i - 1] + Vector2.Distance(
                        new Vector2(points[i - 1].x, points[i - 1].z), new Vector2(points[i].x, points[i].z));
                    int count = Physics.RaycastNonAlloc(new Ray(new Vector3(points[i].x, 1000, points[i].z), Vector3.down), roadHits, 2000);
                    for (int hit = 0; hit < count; hit++)
                    {
                        string road = roadHits[hit].collider.name;
                        if (road == "Continuous asphalt network" || road.StartsWith("Road "))
                        { height = roadHits[hit].point.y; break; }
                    }
                    points[i].y = height + .2f;
                }
                SetVerticesDirty();
            }
            if (progress != travelled || visible != active)
            { progress = travelled; visible = active; SetVerticesDirty(); }
        }

        private void LateUpdate()
        {
            if (view == null || !visible || !overview) return;
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
            if (!visible || !overview || view == null || points == null || points.Length < 2) return;
            var rect = rectTransform.rect;
            float arrowDistance = 0;
            for (int i = 1; i < points.Length; i++)
            {
                if (distances[i] < progress) continue;
                var start = points[i - 1];
                if (distances[i - 1] < progress)
                    start = Vector3.Lerp(start, points[i], Mathf.InverseLerp(distances[i - 1], distances[i], progress));
                var a = view.WorldToViewportPoint(start);
                var b = view.WorldToViewportPoint(points[i]);
                if (a.z <= 0 || b.z <= 0) continue;
                Vector2 from = new Vector2(rect.xMin + a.x * rect.width, rect.yMin + a.y * rect.height);
                Vector2 to = new Vector2(rect.xMin + b.x * rect.width, rect.yMin + b.y * rect.height);
                var delta = to - from;
                if (delta.sqrMagnitude < .001f) continue;
                var direction = delta.normalized;
                var normal = new Vector2(-direction.y, direction.x);
                AddQuad(mesh, from, to, normal * 3, new Color(1, 1, 1, .85f));
                AddQuad(mesh, from, to, normal * 1.5f, color);
                arrowDistance += delta.magnitude;
                if (arrowDistance > 70)
                {
                    arrowDistance = 0;
                    int index = mesh.currentVertCount;
                    var center = (from + to) * .5f;
                    mesh.AddVert(center + direction * 7, color, Vector2.zero);
                    mesh.AddVert(center - direction * 5 + normal * 5, color, Vector2.zero);
                    mesh.AddVert(center - direction * 5 - normal * 5, color, Vector2.zero);
                    mesh.AddTriangle(index, index + 1, index + 2);
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
