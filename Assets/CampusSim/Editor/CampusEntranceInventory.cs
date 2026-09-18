using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CampusSim;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CampusEntranceInventory
{
    static readonly string[] Required = {
        "main_gate", "inha_station", "rear_gate", "building_5", "building_2",
        "hitech", "anniversary_60", "biryong_plaza", "dorm_1", "dorm_2", "dorm_3"
    };

    [MenuItem("Campus/Map/Export Entrance Authoring Inventory")]
    public static void Export()
    {
        if (SceneManager.GetActiveScene().path != "Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new InvalidOperationException("Open the working campus scene.");
        var landmarks = UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None);
        var duplicates = landmarks.GroupBy(l => l.landmarkId).Where(g => g.Count() > 1).ToArray();
        if (duplicates.Length > 0) throw new InvalidOperationException("Duplicate landmark IDs: " + string.Join(",", duplicates.Select(g => g.Key)));
        var byId = landmarks.ToDictionary(l => l.landmarkId);
        var ids = Required.Concat(byId.Keys).Distinct().OrderBy(id => id).ToArray();
        var text = new StringBuilder("landmark_id,role,entrance_id,structure_status,unity_x,unity_y,unity_z,landing_present,approach_present,pedestrian_exclusion,route_validated,verification_status\n");
        int missing = 0, present = 0, approaches = 0;
        foreach (var id in ids)
        {
            byId.TryGetValue(id, out var landmark);
            if (landmark && landmark.generalEntrance && landmark.accessibleEntrance == landmark.generalEntrance)
                throw new InvalidOperationException(id + ": general and accessible portals must be distinct.");
            foreach (var role in new[] { "general", "accessible" })
            {
                var portal = !landmark ? null : role == "general" ? landmark.generalEntrance : landmark.accessibleEntrance;
                if (!portal)
                {
                    missing++;
                    text.AppendLine(string.Join(",", Q(id), role, "", landmark ? "missing_entrance" : "missing_landmark", "", "", "", "false", "false", "unknown", "false", "unresolved"));
                    continue;
                }
                present++;
                bool landing = portal.Find("Landing") || portal.Find("Threshold") || portal.Find("Walkway");
                var approach = portal.Find("Terrain approach draft");
                var area = approach ? approach.GetComponent<CampusPedestrianArea>() : null;
                if (approach) approaches++;
                var p = portal.position;
                text.AppendLine(string.Join(",", Q(id), role, Q(portal.name), "present_draft", F(p.x), F(p.y), F(p.z),
                    landing.ToString().ToLowerInvariant(), (approach != null).ToString().ToLowerInvariant(),
                    area ? area.excludeFromVehicleRouting.ToString().ToLowerInvariant() : "unknown",
                    landmark.routeValidated.ToString().ToLowerInvariant(), Q(landmark.verificationStatus)));
            }
        }
        const string folder = "Docs/MapResearch/Iterations/EntranceInventory";
        Directory.CreateDirectory(folder);
        File.WriteAllText(folder + "/entrances.csv", text.ToString(), new UTF8Encoding(false));
        File.WriteAllText(folder + "/README.md",
            "# Current entrance authoring inventory\n\n" +
            "Generated UTC: " + DateTime.UtcNow.ToString("O") + "\n\n" +
            "Project version: " + File.ReadAllText("VERSION").Trim() + "\n\n" +
            "Landmarks checked: " + ids.Length + "; present portals: " + present + "; missing roles: " + missing + "; approach meshes: " + approaches + ".\n\n" +
            "Coordinates are current Unity world metres (Y up), using the scene's legacy approximate map frame. They are not a verified geodetic or server map export.\n\n" +
            "This inventory does not create Stops or approve routes. A visible ramp, separate accessible portal, or low mesh grade does not establish step-free access. Missing coordinates remain blank. Pedestrian areas must remain excluded from vehicle routing.\n");
        Debug.Log("Entrance inventory exported: " + present + " present, " + missing + " missing roles across " + ids.Length + " landmarks.");
    }
    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    static string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
