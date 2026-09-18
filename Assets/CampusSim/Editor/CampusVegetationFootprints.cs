using System.Collections.Generic;
using UnityEngine;

// Editor placement mask: projected mesh triangles, never whole-building AABBs.
// Bounds are only a broad phase, so courtyards and adjacent lawns remain available.
public sealed class CampusVegetationFootprints
{
    sealed class Shape
    {
        public Bounds bounds;
        public readonly List<Vector2[]> triangles = new List<Vector2[]>();
    }
    readonly List<Shape> shapes = new List<Shape>();
    public CampusVegetationFootprints(IEnumerable<MeshFilter> filters)
    {
        var seen = new HashSet<MeshFilter>();
        foreach (var filter in filters)
        {
            if (!filter || !filter.sharedMesh || !seen.Add(filter) || filter.GetComponent<TextMesh>()) continue;
            var renderer = filter.GetComponent<Renderer>();
            if (!renderer || !renderer.enabled) continue;
            var shape = new Shape { bounds = renderer.bounds };
            var vertices = filter.sharedMesh.vertices;
            var indices = filter.sharedMesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var triangle = new Vector2[3];
                for (int k = 0; k < 3; k++)
                {
                    var p = filter.transform.TransformPoint(vertices[indices[i + k]]);
                    triangle[k] = new Vector2(p.x, p.z);
                }
                if (Mathf.Abs(Cross(triangle[1] - triangle[0], triangle[2] - triangle[0])) > .0001f)
                    shape.triangles.Add(triangle);
            }
            if (shape.triangles.Count > 0) shapes.Add(shape);
        }
    }
    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static float SegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
    {
        var delta = b - a;
        float t = delta.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, delta) / delta.sqrMagnitude) : 0;
        return (p - a - delta * t).sqrMagnitude;
    }
    public bool Overlaps(Vector2 p, float radius)
    {
        foreach (var shape in shapes)
        {
            var b = shape.bounds;
            if (p.x < b.min.x - radius || p.x > b.max.x + radius || p.y < b.min.z - radius || p.y > b.max.z + radius) continue;
            foreach (var t in shape.triangles)
            {
                float a = Cross(t[1] - t[0], p - t[0]), c = Cross(t[2] - t[1], p - t[1]), d = Cross(t[0] - t[2], p - t[2]);
                if ((a >= 0 && c >= 0 && d >= 0) || (a <= 0 && c <= 0 && d <= 0)) return true;
                for (int i = 0; i < 3; i++)
                    if (SegmentDistanceSquared(p, t[i], t[(i + 1) % 3]) <= radius * radius) return true;
            }
        }
        return false;
    }
}
