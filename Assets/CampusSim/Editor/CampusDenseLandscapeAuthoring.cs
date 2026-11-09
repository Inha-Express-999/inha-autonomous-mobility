using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;
using InhaExpress.Client.MapClient;

public static class CampusDenseLandscapeAuthoring
{
 const string RootName="Campus lush Idyllic landscape";
 const string Folder="Assets/CampusSim/Generated/DenseLandscape20261104";
 const string Evidence="artifacts/validation/2026-11-04-dense-landscape";
 [Serializable]public class TreePoint{public float x,z,yaw,height;public string species;public int tone;public bool willow;}
 [Serializable]public class GrassPoint{public float x,z,yaw,height;public int species,tone;}
 [Serializable]public class Plan{public int seed;public TreePoint[] trees;public GrassPoint[] grass;}
 static GameObject[] Roots()=>SceneManager.GetActiveScene().GetRootGameObjects();
 static Terrain Terrain()=>Roots().SelectMany(g=>g.GetComponentsInChildren<Terrain>()).Single();
 static void Guard(){if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=CampusServerMapAuthoring.ScenePath)throw new Exception("Open CampusTerrain edit mode");}
 public static float Ground(Terrain terrain,Vector3 p,float radius)
 {
  float y=float.NegativeInfinity;
  foreach(var d in new[]{Vector3.zero,new Vector3(radius,0,0),new Vector3(-radius,0,0),new Vector3(0,0,radius),new Vector3(0,0,-radius),new Vector3(radius,0,radius),new Vector3(-radius,0,radius),new Vector3(radius,0,-radius),new Vector3(-radius,0,-radius)})y=Mathf.Max(y,terrain.SampleHeight(p+d)+terrain.transform.position.y);
  return y;
 }
 static Bounds BoundsOf(GameObject go){var rr=go.GetComponentsInChildren<Renderer>();var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);return b;}
 static readonly Color[] Tones={new Color(.34f,.50f,.22f),new Color(.45f,.60f,.29f),new Color(.30f,.46f,.28f),new Color(.53f,.63f,.32f),new Color(.40f,.57f,.35f)};
 static Material Leaf(string species,int tone,Dictionary<string,Material> cache)
 {
  string texture=species.StartsWith("Willow")?"WillowTree_Branch":species.StartsWith("Blossom")?"Tree_Blossoms":"BroadleafTree_Leaves";
  string key=texture+"_"+tone;if(cache.TryGetValue(key,out var mat))return mat;
  string path=Folder+"/"+key+".mat";mat=AssetDatabase.LoadAssetAtPath<Material>(path);
  if(!mat){mat=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusLeaves.mat"));AssetDatabase.CreateAsset(mat,path);}
  Color color=species.StartsWith("Blossom")?Color.Lerp(new Color(.93f,.68f,.72f),new Color(.98f,.87f,.80f),tone/4f):Tones[tone];
  mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Idyllic Fantasy Nature/Textures/Trees/"+texture+".png"));mat.SetColor("_BaseColor",color);mat.SetColor("_ColorDim",color*.7f);mat.enableInstancing=true;EditorUtility.SetDirty(mat);cache[key]=mat;return mat;
 }
 [MenuItem("Campus/Map/Plant Dense Random Idyllic Landscape")]
 public static void Build()
 {
  Guard();var plan=JsonUtility.FromJson<Plan>(File.ReadAllText("Assets/CampusSim/Data/dense_landscape.json"));
  if(Roots().Any(g=>g.name==RootName))throw new Exception("Dense landscape exists; preserve it and use Reground instead of duplicating.");
  Directory.CreateDirectory(Folder);AssetDatabase.Refresh();var terrain=Terrain();var root=new GameObject(RootName);Undo.RegisterCreatedObjectUndo(root,"Plant random Idyllic landscape");
  var trees=new GameObject("Mixed groves and Inkyung willows");trees.transform.SetParent(root.transform,false);trees.AddComponent<CampusVegetationQuality>();
  var grass=new GameObject("Dense meadow patches");grass.transform.SetParent(root.transform,false);grass.AddComponent<CampusVegetationQuality>();
  var cache=new Dictionary<string,Material>();var bark=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusBark.mat");
  int n=0;
  foreach(var point in plan.trees)
  {
   string path="Assets/Idyllic Fantasy Nature/Prefabs/"+point.species+".prefab";var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!prefab)throw new Exception(path);
   var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,trees.transform);go.name=(point.willow?"Inkyung willow ":"Mixed grove ")+(n++).ToString("D4")+" | "+point.species;
   go.transform.SetPositionAndRotation(new Vector3(point.x,0,point.z),Quaternion.Euler(0,point.yaw,0));go.transform.localScale*=point.height/BoundsOf(go).size.y;
   foreach(var r in go.GetComponentsInChildren<Renderer>())
   {
    r.sharedMaterials=r.sharedMaterials.Select(m=>m.name.IndexOf("Bark",StringComparison.OrdinalIgnoreCase)>=0?bark:Leaf(point.species,point.tone,cache)).ToArray();
    GameObjectUtility.SetStaticEditorFlags(r.gameObject,0);
   }
   var anchor=go.AddComponent<CampusPlantGroundAnchor>();anchor.sourcePrefab=path;anchor.pivotAboveBottom=go.transform.position.y-BoundsOf(go).min.y;
   anchor.footprintRadius=Mathf.Clamp(go.GetComponentsInChildren<Collider>().Select(c=>Mathf.Max(c.bounds.extents.x,c.bounds.extents.z)).DefaultIfEmpty(.35f).Max(),.2f,.8f);
   var p=go.transform.position;p.y=Ground(terrain,p,anchor.footprintRadius)+anchor.clearance+anchor.pivotAboveBottom;go.transform.position=p;
  }
  int patchIndex=0;
  var groups=plan.grass.GroupBy(p=>(Mathf.FloorToInt(p.x/18),Mathf.FloorToInt(p.z/18),p.species));
  AssetDatabase.StartAssetEditing();
  try{foreach(var group in groups)
  {
   int species=group.Key.species;var points=group.ToArray();var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Idyllic Fantasy Nature/Prefabs/Grass_0"+(species+1)+".prefab");var blade=prefab.GetComponent<MeshFilter>().sharedMesh;
   var combine=new CombineInstance[points.Length];var positions=new Vector3[points.Length];var radii=new float[points.Length];
   for(int j=0;j<points.Length;j++)
   {
    var point=points[j];float scale=point.height/blade.bounds.size.y;float radius=new Vector2(blade.bounds.extents.x,blade.bounds.extents.z).magnitude*scale;radii[j]=radius;
    var p=new Vector3(point.x,0,point.z);p.y=Ground(terrain,p,radius)+.02f;positions[j]=p;
    combine[j]=new CombineInstance{mesh=blade,transform=Matrix4x4.TRS(p-Vector3.up*blade.bounds.min.y*scale,Quaternion.Euler(0,point.yaw,0),Vector3.one*scale)};
   }
   var mesh=new Mesh{name="Idyllic meadow "+patchIndex,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(combine,true,true);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Folder+"/Meadow_"+patchIndex+".asset");
   var go=new GameObject("Meadow "+(patchIndex++).ToString("D4"));go.transform.SetParent(grass.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
   var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/VegetationVariety/Grass_"+species+"_"+(patchIndex%3)+".mat");renderer.shadowCastingMode=ShadowCastingMode.Off;
   var binding=go.AddComponent<CampusGroundedGrass>();binding.roots=positions;binding.radii=radii;binding.verticesPerClump=blade.vertexCount;
  }}finally{AssetDatabase.StopAssetEditing();}
  foreach(var old in Roots().Where(g=>g!=root&&(g.name=="Campus planted groves"||g.name=="Campus dense grove addition"||g.name=="Campus dense grass cover"))){Undo.RecordObject(old,"Replace regular planting with dense random groves");old.SetActive(false);}
  // Keep the pavilion, paths and furniture; replace only old lakeside tree models.
  foreach(var old in Roots().Where(g=>g.name=="Inkyung lakeside garden").SelectMany(g=>g.GetComponentsInChildren<CampusTerrainBinding>(true)).Where(b=>b.kind==CampusTerrainBinding.BindingKind.Vegetation)){Undo.RecordObject(old.gameObject,"Replace sparse willows");old.gameObject.SetActive(false);}
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
  File.WriteAllText(Evidence+"/applied.txt",$"trees={plan.trees.Length}\nwillows={plan.trees.Count(t=>t.willow)}\ngrass_clumps={plan.grass.Length}\ngrass_patches={patchIndex}\nseed={plan.seed}\n");
  Capture();
 }
 [MenuItem("Campus/Map/Reground Dense Landscape")]
 public static void Reground()
 {
  var root=Roots().SingleOrDefault(g=>g.name==RootName);if(!root)return;var terrain=Terrain();
  foreach(var anchor in root.GetComponentsInChildren<CampusPlantGroundAnchor>()){Undo.RecordObject(anchor.transform,"Ground tree base");var p=anchor.transform.position;p.y=Ground(terrain,p,anchor.footprintRadius)+anchor.clearance+anchor.pivotAboveBottom;anchor.transform.position=p;}
  foreach(var patch in root.GetComponentsInChildren<CampusGroundedGrass>())
  {
   var mesh=patch.GetComponent<MeshFilter>().sharedMesh;Undo.RegisterCompleteObjectUndo(mesh,"Ground meadow patch");Undo.RegisterCompleteObjectUndo(patch,"Remember clump bases");var vertices=mesh.vertices;
   for(int i=0;i<patch.roots.Length;i++)
   {
    var p=patch.roots[i];float radius=0;
    for(int j=0;j<patch.verticesPerClump;j++){var v=patch.transform.TransformPoint(vertices[i*patch.verticesPerClump+j]);radius=Mathf.Max(radius,new Vector2(v.x-p.x,v.z-p.z).magnitude);}
    patch.radii[i]=radius;float height=Ground(terrain,p,radius)+.02f;float delta=height-p.y;
    // Check each blade vertex as well as the footprint, including cell-boundary slopes.
    for(int j=0;j<patch.verticesPerClump;j++){var v=patch.transform.TransformPoint(vertices[i*patch.verticesPerClump+j]);delta=Mathf.Max(delta,terrain.SampleHeight(v)+terrain.transform.position.y+.015f-v.y);}
    for(int j=0;j<patch.verticesPerClump;j++)vertices[i*patch.verticesPerClump+j].y+=delta;p.y+=delta;patch.roots[i]=p;
   }
   mesh.vertices=vertices;mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);EditorUtility.SetDirty(patch);
  }
  EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
 }
 public static void MoveHitechEntrance()
 {
  Guard();var entrance=Roots().SelectMany(g=>g.GetComponentsInChildren<CampusLandmarkEntrances>(true)).Single(e=>e.landmarkId=="hitech");
  Undo.RecordObject(entrance.generalEntrance,"Move synthetic entrance to inner courtyard");var p=new Vector3(328.83f,0,9.15f);p.y=Ground(Terrain(),p,.6f)+.04f;entrance.generalEntrance.SetPositionAndRotation(p,Quaternion.Euler(0,206.8f,0));
  Undo.RecordObject(entrance,"Record unverified courtyard entrance");entrance.verificationStatus="synthetic_inner_courtyard_entrance_not_verified_access";EditorUtility.SetDirty(entrance);CampusEntranceInventory.Export();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
 }
 public static void PlaceHitechShelter()
 {
  Guard();var marker=Roots().SelectMany(g=>g.GetComponentsInChildren<ServerMapMarker>()).Single(m=>m.kind==ServerMapMarker.MarkerKind.Stop&&m.serverId=="hitech_road_candidate");
  var shelter=marker.GetComponentsInChildren<ServicePointVisual>(true).Single(v=>v.pointKind==ServerMapMarker.MarkerKind.Stop).transform;
  Undo.RecordObject(shelter,"Place shelter inside Hitech courtyard");shelter.SetPositionAndRotation(new Vector3(324.5837f,0,.3745f),Quaternion.Euler(0,208.81f,0));CampusStopSignAuthoring.Build();
 }
 public static void Capture(){CampusRoadConnectionRepair.Capture("dense-after-campus",new Vector3(20,0,-160),340);CampusRoadConnectionRepair.Capture("dense-after-hitech",new Vector3(330,0,0),70);CampusRoadConnectionRepair.Capture("dense-after-lake",new Vector3(100,0,-100),140);}
}
