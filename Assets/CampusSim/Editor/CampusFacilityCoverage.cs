using System;
using System.IO;
using System.Linq;
using System.Text;
using CampusSim;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

public static class CampusFacilityCoverage
{
    [MenuItem("Campus/Map/Export Representative Facility Coverage")]
    public static void Export()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new InvalidOperationException("Open CampusTerrain.");
        var landmarks=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None);
        var buildings=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Where(b=>b.kind==CampusTerrainBinding.BindingKind.Building&&b.name.Contains(" | OSM "))
            .OrderBy(b=>b.name).ToArray();
        var report=new StringBuilder("# Representative facility coverage\n\nGenerated UTC: "+DateTime.UtcNow.ToString("O")+"\n\n");
        report.AppendLine("Scene building inventory only. Named associations below identify existing authoring records, not verified walking connections or approved Stops. Unnamed OSM buildings require identification. This is not the complete official facility list.\n");
        report.AppendLine("| Scene facility | Source | Associated landmark draft | Separate portals | Service coverage |\n|---|---|---|---|---|");
        int unresolved=0;
        foreach(var b in buildings)
        {
            var name=b.name.Split(new[]{" | OSM "},StringSplitOptions.None)[0];
            string id=name=="5호관"?"building_5":name=="2남관"||name=="2북관"?"building_2":name=="하이테크센터"?"hitech":name=="60주년기념관"?"anniversary_60":null;
            var landmark=b.GetComponent<CampusLandmarkEntrances>();
            if(!landmark&&id!=null)landmark=landmarks.SingleOrDefault(l=>l.landmarkId==id);
            if(landmark)id=landmark.landmarkId;
            bool paired=landmark&&landmark.generalEntrance&&landmark.accessibleEntrance&&landmark.generalEntrance!=landmark.accessibleEntrance;
            if(!landmark)unresolved++;
            report.AppendLine("| "+name+" | "+b.name.Substring(b.name.IndexOf(" | OSM ")+3)+" | "+(landmark?id:"unassigned")+" | "+(paired?"draft pair":"not established")+" | unverified |");
        }
        report.AppendLine("\nScene building parts: "+buildings.Length+"; parts without a landmark association: "+unresolved+". Multiple parts may represent one facility. Dormitories, gates, station and plaza are tracked separately in EntranceInventory/entrances.csv.");
        Directory.CreateDirectory("Docs/MapResearch/Iterations/EntranceInventory");
        File.WriteAllText("Docs/MapResearch/Iterations/EntranceInventory/facilities.md",report.ToString());
        Debug.Log("Facility coverage: "+buildings.Length+" building parts; "+unresolved+" unassigned.");
    }
}
