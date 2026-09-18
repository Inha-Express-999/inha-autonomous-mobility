using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusEntranceWayfinding
{
    static readonly string[] Required = { "main_gate", "inha_station", "rear_gate", "building_5", "building_2", "hitech", "anniversary_60", "biryong_plaza", "dorm_1", "dorm_2", "dorm_3" };
    [Serializable] class Inventory { public string scope = "visual authoring only; not approved Stops or runtime graph"; public Entry[] landmarks; }
    [Serializable] class Entry { public string id, status; public bool present, general, accessible, routeValidated; public Vector3 generalPosition, accessiblePosition; }

    [MenuItem("Campus/Map/Label Dormitory Entrances And Audit Coverage")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != "Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new InvalidOperationException("Open CampusTerrain in edit mode.");
        var all = UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None);
        if (all.GroupBy(l => l.landmarkId).Any(g => g.Count() > 1)) throw new Exception("Duplicate landmark identifiers.");
        var dark = AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Graphite.mat");
        var white = AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Pearl.mat");
        var teal = AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Signal teal.mat");
        if (!dark || !white || !teal) throw new Exception("Missing wayfinding materials.");
        foreach (var landmark in all.Where(l => l.landmarkId == "dorm_1" || l.landmarkId == "dorm_2" || l.landmarkId == "dorm_3" || l.landmarkId == "library" || l.landmarkId == "main_building" || l.landmarkId == "student_center"))
        foreach (var portal in new[] { landmark.generalEntrance, landmark.accessibleEntrance })
        {
            if (!portal || portal.Find("Role sign")) continue;
            bool access = portal == landmark.accessibleEntrance;
            var sign = new GameObject("Role sign").transform;
            Undo.RegisterCreatedObjectUndo(sign.gameObject, "Create entrance role sign");
            sign.SetParent(portal, false); sign.localPosition = new Vector3(0, 3.1f, .55f);
            // Positive local Z faces away from the building, toward the arriving passenger.
            Box(sign, "Backing", Vector3.zero, new Vector3(3.1f, .58f, .10f), dark);
            Box(sign, "Role stripe", new Vector3(0, -.28f, .065f), new Vector3(3.1f, .055f, .025f), access ? teal : white);
            var label = new GameObject("Label").transform; label.SetParent(sign, false);
            Undo.RegisterCreatedObjectUndo(label.gameObject, "Create entrance label");
            label.localPosition = new Vector3(access ? -.25f : 0, 0, .065f); label.localRotation = Quaternion.Euler(0, 180, 0);
            var text = label.gameObject.AddComponent<TextMesh>(); text.text = access ? "ACCESS ONLY" : "GENERAL";
            text.fontSize = 64; text.characterSize = .035f; text.anchor = TextAnchor.MiddleCenter; text.color = Color.white;
            if (access)
            {
                var icon = new GameObject("Wheelchair pictogram").transform; icon.SetParent(sign, false); icon.localPosition = new Vector3(1.2f, -.03f, .075f);
                Undo.RegisterCreatedObjectUndo(icon.gameObject, "Create access pictogram");
                for (int i = 0; i < 20; i++)
                {
                    float a = i * Mathf.PI * 2 / 20, b = (i + 1) * Mathf.PI * 2 / 20;
                    Stroke(icon, new Vector3(Mathf.Cos(a) * .13f, Mathf.Sin(a) * .13f - .05f, 0), new Vector3(Mathf.Cos(b) * .13f, Mathf.Sin(b) * .13f - .05f, 0), white);
                }
                Stroke(icon, new Vector3(-.04f, .15f, .01f), new Vector3(0, -.02f, .01f), white);
                Stroke(icon, new Vector3(0, -.02f, .01f), new Vector3(.17f, -.02f, .01f), white);
                Stroke(icon, new Vector3(.17f, -.02f, .01f), new Vector3(.23f, -.16f, .01f), white);
                Box(icon, "Head", new Vector3(-.05f, .22f, .01f), new Vector3(.07f, .07f, .025f), white);
            }
        }
        var inventory = new Inventory { landmarks = Required.Select(id => {
            var l = all.SingleOrDefault(x => x.landmarkId == id);
            return new Entry { id = id, present = l != null, general = l && l.generalEntrance, accessible = l && l.accessibleEntrance,
                routeValidated = l && l.routeValidated, status = l ? l.verificationStatus : "missing_scene_landmark",
                generalPosition = l && l.generalEntrance ? l.generalEntrance.position : Vector3.zero,
                accessiblePosition = l && l.accessibleEntrance ? l.accessibleEntrance.position : Vector3.zero };
        }).ToArray() };
        Directory.CreateDirectory("Docs/MapResearch/Iterations");
        File.WriteAllText("Docs/MapResearch/Iterations/landmark-coverage.json", JsonUtility.ToJson(inventory, true));
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("Dormitory role signs saved. Required landmark coverage: " + inventory.landmarks.Count(l => l.present) + "/11; paired: " + inventory.landmarks.Count(l => l.general && l.accessible) + "/11.");
    }
    static void Box(Transform parent, string name, Vector3 p, Vector3 size, Material material)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(g, "Create wayfinding element");
        g.transform.localPosition = p; g.transform.localScale = size; g.GetComponent<Renderer>().sharedMaterial = material;
        Undo.DestroyObjectImmediate(g.GetComponent<Collider>());
        GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
    }
    static void Stroke(Transform parent, Vector3 a, Vector3 b, Material material)
    {
        Box(parent, "Icon stroke", (a + b) / 2, new Vector3(.024f, Vector3.Distance(a, b), .02f), material);
        parent.GetChild(parent.childCount - 1).localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
    }
}

