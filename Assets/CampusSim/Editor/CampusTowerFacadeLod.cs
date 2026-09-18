using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusTowerFacadeLod
{
    const string Folder="Assets/CampusSim/Generated/TowerFacadeLod";
    [Serializable] class Map { public Feature[] features; }
    [Serializable] class Feature { public string id,name,kind;public float height;public Vector3[] points;public Ring[] holes; }
    [Serializable] class Ring { public Vector3[] points; }
    [MenuItem("Campus/Map/Simplify Tower Facades With LOD")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity")throw new Exception("Open working scene.");
        if(Directory.Exists(Folder))throw new Exception("Facade LOD assets already exist; preserve authored changes.");
        var map=JsonUtility.FromJson<Map>(File.ReadAllText("Assets/InhaCampus/Source/campus.json"));
        var buildings=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Where(b=>b.kind==CampusTerrainBinding.BindingKind.Building&&b.transform.Find("Facades")&&(b.name.Contains("60주년")||b.name.Contains("하이테크"))).ToArray();
        if(buildings.Length!=2||buildings.Any(b=>b.GetComponent<LODGroup>()))throw new Exception("Expected two ungrouped tower facades.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var template=buildings[0].transform.Find("Window glazing").GetComponent<Renderer>().sharedMaterial;
        Material Mat(string name,Color color)
        {
            var m=new Material(template){name=name,enableInstancing=true};m.SetColor("_BaseColor",color);m.SetColor("_ColorDim",color*.74f);m.SetTexture("_BaseMap",null);
            AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat");return m;
        }
        var glass=Mat("Calm blue glass",new Color(.24f,.39f,.42f));var reflection=Mat("Soft glass variation",new Color(.28f,.43f,.45f));
        var frame=Mat("Muted facade grid",new Color(.33f,.43f,.44f));var rim=Mat("Light stone rim",new Color(.58f,.63f,.60f));
        var report=new System.Text.StringBuilder("Tower detail LOD; building mass, roof, colliders and entrance anchors retained.\n");
        foreach(var building in buildings)
        {
            var feature=map.features.Single(f=>f.kind=="building"&&building.sourceId.Contains(f.id));
            var fine=new[]{"Window glazing","Window reflections","Aluminium mullions","Stone bands and parapets"}.Select(name=>building.transform.Find(name).GetComponent<MeshRenderer>()).ToArray();
            var oldPaths=fine.Select(r=>AssetDatabase.GetAssetPath(r.sharedMaterial)).ToArray();
            building.transform.Find("Facades").GetComponent<Renderer>().sharedMaterial=glass;
            fine[0].sharedMaterial=glass;fine[1].sharedMaterial=reflection;fine[2].sharedMaterial=frame;fine[3].sharedMaterial=rim;
            // Fine strips cannot cast useful shadows at overview scale; the building shell still does.
            foreach(var r in fine.Take(3))r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            var vertices=new List<Vector3>();var indices=new[]{new List<int>(),new List<int>()};
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal,int material)
            {
                int start=vertices.Count;vertices.AddRange(new[]{a,b,c,d});
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)>=0)indices[material].AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
                else indices[material].AddRange(new[]{start,start+2,start+1,start,start+3,start+2});
            }
            var rings=new List<Vector3[]>{feature.points};if(feature.holes!=null)rings.AddRange(feature.holes.Select(r=>r.points));
            int floors=Mathf.Max(1,Mathf.RoundToInt(feature.height/3.8f));float floorHeight=feature.height/floors;
            for(int ring=0;ring<rings.Count;ring++)
            {
                var p=rings[ring];float area=0;for(int i=0;i<p.Length;i++)area+=p[i].x*p[(i+1)%p.Length].z-p[(i+1)%p.Length].x*p[i].z;
                for(int edge=0;edge<p.Length;edge++)
                {
                    var a=p[edge];var b=p[(edge+1)%p.Length];var direction=(b-a).normalized;float length=Vector3.Distance(a,b);if(length<.05f)continue;
                    var normal=new Vector3(direction.z,0,-direction.x)*(area>0?1:-1)*(ring==0?1:-1);a+=normal*.34f;b+=normal*.34f;
                    void Band(float y,float width,int material)=>Quad(a+Vector3.up*(y-width/2),a+Vector3.up*(y+width/2),b+Vector3.up*(y+width/2),b+Vector3.up*(y-width/2),normal,material);
                    Band(.55f,1.1f,1);Band(feature.height+.2f,.5f,1);
                    for(int floor=2;floor<floors;floor+=2)Band(floor*floorHeight,.19f,0);
                    int bays=Mathf.Max(1,Mathf.FloorToInt(length/6));
                    for(int bay=1;bay<bays;bay++)
                    {
                        var center=Vector3.Lerp(a,b,bay/(float)bays);var side=direction*.10f;
                        Quad(center-side+Vector3.up,center-side+Vector3.up*(feature.height-.2f),center+side+Vector3.up*(feature.height-.2f),center+side+Vector3.up,normal,0);
                    }
                }
            }
            var mesh=new Mesh{name=feature.id+" distant facade",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.subMeshCount=2;for(int i=0;i<2;i++)mesh.SetTriangles(indices[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Folder+"/"+feature.id+".asset");
            var go=new GameObject("Distant facade grid");go.transform.SetParent(building.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var coarse=go.AddComponent<MeshRenderer>();coarse.sharedMaterials=new[]{frame,rim};coarse.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            var group=building.gameObject.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.None;group.SetLODs(new[]{new LOD(.55f,fine.Cast<Renderer>().ToArray()),new LOD(.012f,new Renderer[]{coarse})});group.RecalculateBounds();
            // Use facade height: a long low wing must not force subpixel window frames into the near LOD.
            group.size=feature.height;
            int high=fine.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.vertexCount);
            report.AppendLine(feature.name+": detailed vertices="+high+", distant vertices="+mesh.vertexCount+", retained ratio="+(mesh.vertexCount/(float)high).ToString("F5")+", source materials="+string.Join(";",oldPaths));
        }
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Docs/MapResearch/Iterations/tower-facade-lod.txt",report.ToString());
    }
}
