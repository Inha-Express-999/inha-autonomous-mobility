using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class CampusRoadGradeAudit
{
    [MenuItem("Campus/Map/Audit Active Road Face Grades")]
    public static void Export()
    {
        var meshes = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
            .Where(m => m.gameObject.activeInHierarchy &&
                (m.name.Contains("Continuous asphalt") || m.name.StartsWith("Road "))).ToArray();
        if (meshes.Length == 0) throw new System.InvalidOperationException("No active road meshes found.");
        var text = new StringBuilder("object,triangle,x,y,z,areaM2,grade\n");
        foreach (var filter in meshes)
        {
            var mesh = filter.sharedMesh;
            if (!mesh) continue;
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = filter.transform.TransformPoint(vertices[triangles[i]]);
                var b = filter.transform.TransformPoint(vertices[triangles[i + 1]]);
                var c = filter.transform.TransformPoint(vertices[triangles[i + 2]]);
                var normal = Vector3.Cross(b - a, c - a);
                float area = Mathf.Abs(normal.y) * .5f;
                if (area < .0001f) continue;
                var center = (a + b + c) / 3;
                float grade = new Vector2(normal.x, normal.z).magnitude / Mathf.Abs(normal.y);
                string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
                string name = "\"" + filter.name.Replace("\"", "\"\"") + "\"";
                text.AppendLine(string.Join(",", name, (i / 3).ToString(), F(center.x),
                    F(center.y), F(center.z), F(area), F(grade)));
            }
        }
        const string folder = "Docs/MapResearch/Iterations";
        Directory.CreateDirectory(folder);
        File.WriteAllText(folder + "/all-active-road-triangles.csv", text.ToString());
        Debug.Log("Exported active asphalt face grades. Run AgentScripts/summarize_road_grades.py for area-weighted metrics. Other surfaces and route legality are outside this audit.");
    }
}
