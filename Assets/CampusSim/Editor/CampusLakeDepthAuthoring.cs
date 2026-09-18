using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusLakeDepthAuthoring
{
    static float Cross(Vector2 a, Vector2 b) => a.x*b.y-a.y*b.x;
    static bool Inside(Vector2 p, Vector3[] v, int[] indices)
    {
        for(int i=0;i<indices.Length;i+=3)
        {
            Vector2 P(int k)=>new Vector2(v[indices[i+k]].x,v[indices[i+k]].z);
            var a=P(0);var b=P(1);var c=P(2);
            if(Mathf.Abs(Cross(b-a,c-a))<.001f)continue;
            float x=Cross(b-a,p-a),y=Cross(c-b,p-b),z=Cross(a-c,p-c);
            if((x>=0&&y>=0&&z>=0)||(x<=0&&y<=0&&z<=0))return true;
        }
        return false;
    }
    [MenuItem("Campus/Map/Deepen Lake Basins")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        var data=terrain.terrainData;int n=data.heightmapResolution;var heights=data.GetHeights(0,0,n,n);var origin=terrain.transform.position;
        var report=new System.Text.StringBuilder("Artificial visual basin depths, not surveyed bathymetry. Water planes held at their existing levels.\n");
        foreach(var binding in UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Where(b=>b.kind==CampusTerrainBinding.BindingKind.Water))
        {
            var filter=binding.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="Water");
            var vertices=filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();var triangles=filter.sharedMesh.triangles;
            float level=vertices.Average(v=>v.y);float depth=binding.name.Contains("인경호")?4:2;
            // An exterior sample anchors the water group, so digging beneath its center cannot lower the water plane on Bake.
            var bounds=filter.GetComponent<Renderer>().bounds;
            var shore=new Vector3(bounds.max.x+16,0,bounds.center.z);
            binding.samplePoint=shore;binding.surfaceOffset=binding.transform.position.y-binding.originalPosition.y-terrain.SampleHeight(shore)-origin.y;EditorUtility.SetDirty(binding);
            int cells=0;
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                var p=new Vector2(origin.x+x*data.size.x/(n-1),origin.z+z*data.size.z/(n-1));
                if(!Inside(p,vertices,triangles))continue;
                // Fade the excavation near shore. Eight radial samples keep corners and narrow channels shallow.
                float inset=0;
                for(float radius=1;radius<=12;radius++)
                {
                    bool contained=true;for(int k=0;k<8;k++){float a=k*Mathf.PI/4;if(!Inside(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,vertices,triangles)){contained=false;break;}}
                    if(!contained)break;inset=radius;
                }
                float target=(level-Mathf.Lerp(.35f,depth,Mathf.SmoothStep(0,1,inset/10))-origin.y)/data.size.y;
                heights[z,x]=Mathf.Min(heights[z,x],Mathf.Clamp01(target));cells++;
            }
            report.AppendLine(binding.name+": requested inner depth="+depth+"m, changed grid candidates="+cells+", waterY="+level);
        }
        data.SetHeights(0,0,heights);EditorUtility.SetDirty(data);
        CampusTerrainAuthoring.Bake();
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/lake-depths.txt",report.ToString());
    }
}
