using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using CampusSim;

public static class CampusEntrancePavingAudit
{
    [MenuItem("Campus/Map/Audit Entrance Paving Contacts")]
    public static void Export()
    {
        var paving=GameObject.Find("Continuous pedestrian network");
        if(!paving)throw new Exception("Continuous pedestrian network is missing.");
        var collider=paving.GetComponent<MeshCollider>();
        if(!collider)throw new Exception("Pedestrian mesh collider is missing.");
        var report=new StringBuilder("entrance,terminal_samples,paving_hits,min_gap_m,max_gap_m,status\n");
        foreach(var landmark in UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None))
        foreach(var portal in new[]{landmark.generalEntrance,landmark.accessibleEntrance})
        {
            if(!portal)continue;
            var approach=portal.Find("Terrain approach draft");
            if(!approach)continue;
            var landing=portal.Find("Landing");
            if(!landing)landing=portal.Find("Threshold");
            if(!landing)landing=portal.Find("Walkway");
            if(!landing)continue;
            var vertices=approach.GetComponent<MeshFilter>().sharedMesh.vertices;
            float start=landing.localPosition.z+landing.localScale.z/2;
            // The builder stores top strip vertices first, then closed side walls.
            int strips=Mathf.CeilToInt((vertices.Max(v=>v.z)-start)/.25f);
            if(strips<1||2*strips+1>=vertices.Length)throw new Exception("Unexpected approach mesh: "+portal.name);
            int hits=0;float min=float.PositiveInfinity,max=float.NegativeInfinity;
            for(int i=0;i<=32;i++)
            {
                var world=approach.TransformPoint(Vector3.Lerp(vertices[2*strips],vertices[2*strips+1],i/32f));
                if(!collider.Raycast(new Ray(world+Vector3.up*100,Vector3.down),out var hit,200))continue;
                hits++;float gap=world.y-hit.point.y;min=Mathf.Min(min,gap);max=Mathf.Max(max,gap);
            }
            string status=hits==33&&min>=-.001f&&max<=.015f?"surface_contact_only":"unresolved_in_this_surface_audit";
            report.AppendLine(FormattableString.Invariant($"{portal.name},33,{hits},{(hits>0?min.ToString(System.Globalization.CultureInfo.InvariantCulture):"")},{(hits>0?max.ToString(System.Globalization.CultureInfo.InvariantCulture):"")},{status}"));
        }
        File.WriteAllText("Docs/MapResearch/Iterations/entrance-paving-current.csv",report.ToString());
        Debug.Log("Entrance paving audit saved. Only the continuous pedestrian mesh is tested; frontage roads, other paths, route topology and real accessibility are not verified.");
    }
}
