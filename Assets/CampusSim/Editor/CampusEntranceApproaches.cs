using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusEntranceApproaches
{
    [MenuItem("Campus/Map/Verify Local Entrance Sculpt Response")]
    public static void VerifyLocalSculpt()
    {
        FitAfterTerrainBake();
        var portal=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).Single(l=>l.landmarkId=="dorm_2").accessibleEntrance;
        var mesh=portal.Find("Terrain approach draft").GetComponent<MeshFilter>().sharedMesh;
        var baseline=mesh.vertices;
        var terrain=UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();var data=terrain.terrainData;
        var point=portal.TransformPoint(new Vector3(0,0,5));var relative=point-terrain.transform.position;int n=data.heightmapResolution;
        int x=Mathf.Clamp(Mathf.RoundToInt(relative.x/data.size.x*(n-1)),0,n-1),z=Mathf.Clamp(Mathf.RoundToInt(relative.z/data.size.z*(n-1)),0,n-1);
        var saved=data.GetHeights(x,z,1,1);float rise=0;
        try
        {
            data.SetHeights(x,z,new float[,]{{Mathf.Min(1,saved[0,0]+2/data.size.y)}});
            FitAfterTerrainBake();var raised=mesh.vertices;
            if(Vector3.Distance(raised[0],baseline[0])>.0001f||Vector3.Distance(raised[1],baseline[1])>.0001f)throw new Exception("Landing seam moved.");
            rise=raised.Select((v,i)=>v.y-baseline[i].y).Max();if(rise<.02f)throw new Exception("Local sculpt did not update approach.");
        }
        finally{data.SetHeights(x,z,saved);FitAfterTerrainBake(true);}
        if(mesh.vertices.Select((v,i)=>Vector3.Distance(v,baseline[i])).Max()>.0001f)throw new Exception("Approach restoration mismatch.");
        File.WriteAllText("Docs/MapResearch/Iterations/local-sculpt-test.txt","PASS: dorm_2 accessible approach responds to one terrain sample raised 2m; landing seam fixed; original terrain sample and mesh restored. Maximum local mesh rise="+rise+"m. This checks authoring response, not accessibility or runtime path safety.\n");
    }
    [MenuItem("Campus/Map/Fit Entrance Approach Drafts")]
    public static void Fit() => FitAfterTerrainBake(true);
    public static void FitAfterTerrainBake(bool saveScene = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != "Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new Exception("Open CampusTerrain in edit mode.");
        var landmarks = UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None);
        if (landmarks.Length == 0) return;
        foreach(var landmark in landmarks.Where(l=>l.landmarkId=="inha_station"))
        {
            var portal=landmark.generalEntrance;
            if(!portal||portal.Find("Landing"))continue;
            var passage=landmark.transform.Find("Covered passage");
            if(!passage)throw new Exception("Station passage is required to align its landing.");
            // The source pavilion opens at local -Z. The approach builder uses
            // portal +Z as outward, so point the portal out of the canopy.
            Undo.RecordObject(portal,"Orient station entrance outward");
            portal.localRotation=Quaternion.Euler(0,180,0);
            var landing=GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(landing,"Create station entrance landing");
            landing.name="Landing";landing.transform.SetParent(portal,false);
            float width=passage.localScale.x;
            float top=passage.localPosition.y+passage.localScale.y/2-portal.localPosition.y;
            landing.transform.localPosition=new Vector3(0,top-.06f,.5f);
            landing.transform.localScale=new Vector3(width,.12f,1);
            landing.GetComponent<Renderer>().sharedMaterial=passage.GetComponent<Renderer>().sharedMaterial;
            GameObjectUtility.SetStaticEditorFlags(landing,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        }
        var terrain = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Single();
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Walkway.mat");
        if (!material) throw new Exception("Walkway material missing.");
        const string folder = "Assets/CampusSim/Generated/EntranceApproaches";
        if (!Directory.Exists(folder)) { Directory.CreateDirectory(folder); AssetDatabase.Refresh(); }
        var report = new StringBuilder("Synthetic entrance approach geometry; not measured accessibility or connected pedestrian routes.\nAutomatically refitted after Terrain Bake. Values are mesh grades, not regulatory approval.\n");
        foreach (var landmark in landmarks)
        foreach (var portal in new[] { landmark.generalEntrance, landmark.accessibleEntrance })
        {
            if (!portal) continue;
            // Gates already have a Walkway slab; use it as the landing instead
            // of silently omitting their terrain approaches because of its name.
            var landing = portal.Find("Landing") ?? portal.Find("Threshold") ?? portal.Find("Walkway");
            if (!landing) { report.AppendLine(portal.name + ": no landing mesh; approach unresolved"); continue; }
            if(landmark.landmarkId=="building_2"&&portal==landmark.accessibleEntrance)
                FitBuildingTwoThreshold(landing);
            bool gatePavingFit=landmark.landmarkId=="main_gate"||(landmark.landmarkId=="rear_gate"&&portal==landmark.generalEntrance);
            if(gatePavingFit)
            {
                var paving=GameObject.Find("Continuous pedestrian network");
                var collider=paving?paving.GetComponent<MeshCollider>():null;
                float probeZ=landmark.landmarkId=="rear_gate"?5:(portal==landmark.generalEntrance?6:3.5f);
                var probe=portal.TransformPoint(new Vector3(0,0,probeZ));
                if(collider&&collider.Raycast(new Ray(probe+Vector3.up*100,Vector3.down),out var hit,200))
                {
                    float surface=portal.InverseTransformPoint(hit.point).y+.005f;
                    Undo.RecordObject(landing,"Align gate walkway with paving");
                    var scale=landing.localScale;scale.y=surface+.02f;landing.localScale=scale;
                    var position=landing.localPosition;position.y=(surface-.02f)/2;landing.localPosition=position;
                }
            }
            bool gate = landmark.landmarkId == "main_gate" || landmark.landmarkId == "rear_gate";
            foreach(int direction in gate ? new[]{1,-1} : new[]{1})
            {
            float half = landing.localScale.x / 2;
            float startZ = landing.localPosition.z + direction * landing.localScale.z / 2;
            float top = landing.localPosition.y + landing.localScale.y / 2;
            float length = landmark.landmarkId=="main_gate" && portal==landmark.accessibleEntrance ? (direction<0 ? 10 : 8) : 6;
            // Main-building doors open onto a raised forecourt. Keep a short
            // level terrace here; a separate long ramp must reach the sidewalk.
            if(landmark.landmarkId=="main_building")length=2.5f;
            // Both 60th-anniversary facade portals face existing paving 7-8m away.
            // The surveyed extension corridor contains no road/building colliders.
            if(landmark.landmarkId=="anniversary_60")length=9;
            if(landmark.landmarkId=="building_2"&&portal==landmark.generalEntrance)length=7;
            if(landmark.landmarkId=="biryong_plaza"&&portal==landmark.generalEntrance)length=10;
            var sideLengths=new[]{length,length};
            float endOffset=.025f;
            if(landmark.landmarkId=="dorm_2")
            {
                var frontage=GameObject.Find("Dormitory 2 frontage road");
                if(!frontage)throw new Exception("Dormitory frontage road is required for entrance fitting.");
                var road=frontage.GetComponentInChildren<CampusTerrainBinding>();
                var rv=road.originalMesh.vertices;
                var wa=road.transform.TransformPoint(rv[0].x<rv[1].x?rv[0]:rv[1]);
                int last=rv.Length-2;
                var wb=road.transform.TransformPoint(rv[last].x<rv[last+1].x?rv[last]:rv[last+1]);
                var a=portal.InverseTransformPoint(wa);var b=portal.InverseTransformPoint(wb);
                if(Mathf.Abs(b.x-a.x)<.001f)throw new Exception("Frontage edge parallel to entrance approach.");
                for(int side=0;side<2;side++)
                {
                    float x=side==0?-half:half;
                    float t=(x-a.x)/(b.x-a.x);
                    if(t<0||t>1)throw new Exception("Entrance falls beyond frontage edge.");
                    sideLengths[side]=Mathf.Lerp(a.z,b.z,t)-startZ;
                    if(sideLengths[side]<1)throw new Exception("Insufficient entrance setback.");
                }
                length=Mathf.Max(sideLengths[0],sideLengths[1]);endOffset=road.surfaceOffset;
            }
            // When the whole terminal width overlaps authored paving, meet its
            // actual surface rather than leaving a terrain-to-pavement step.
            var pedestrian=GameObject.Find("Continuous pedestrian network");
            var pedestrianCollider=pedestrian?pedestrian.GetComponent<MeshCollider>():null;
            if(pedestrianCollider&&gatePavingFit&&direction==1)
            {
                var candidates=new float[2];
                bool found=true;
                for(int side=0;side<2;side++)
                {
                    bool sideFound=false;
                    for(float distance=.05f;distance<=12;distance+=.05f)
                    {
                        var point=portal.TransformPoint(new Vector3(side==0?-half:half,0,startZ+distance));
                        if(!pedestrianCollider.Raycast(new Ray(point+Vector3.up*100,Vector3.down),out _,200))continue;
                        candidates[side]=distance;sideFound=true;break;
                    }
                    found&=sideFound;
                }
                if(found){sideLengths=candidates;length=Mathf.Max(candidates[0],candidates[1]);}
            }
            // A short pavement strip can lie inside the old approach, with its
            // far endpoint back on terrain. Find that overlap before endpoint fitting.
            if(pedestrianCollider&&!gate&&landmark.landmarkId!="dorm_2")
            for(float distance=.5f;distance<length;distance+=.25f)
            {
                bool fullWidth=true;
                for(int sample=0;sample<=8;sample++)
                {
                    var point=portal.TransformPoint(new Vector3(Mathf.Lerp(-half,half,sample/8f),0,startZ+distance));
                    if(!pedestrianCollider.Raycast(new Ray(point+Vector3.up*100,Vector3.down),out var hit,200)
                        ||Mathf.Abs(portal.InverseTransformPoint(hit.point).y-top)/distance>.05f)
                    {fullWidth=false;break;}
                }
                if(!fullWidth)continue;
                sideLengths[0]=sideLengths[1]=length=distance;
                break;
            }
            var pavingEndHeights=new float[2];bool meetsPaving=pedestrianCollider;
            for(int sample=0;sample<=8&&meetsPaving;sample++)
            {
                float u=sample/8f;
                var end=portal.TransformPoint(new Vector3(Mathf.Lerp(-half,half,u),0,
                    startZ+direction*Mathf.Lerp(sideLengths[0],sideLengths[1],u)));
                if(!pedestrianCollider.Raycast(new Ray(end+Vector3.up*100,Vector3.down),out var hit,200))
                {meetsPaving=false;break;}
                if(sample==0||sample==8)
                    pavingEndHeights[sample==0?0:1]=portal.InverseTransformPoint(hit.point+Vector3.up*.005f).y;
            }
            if(meetsPaving)
            {
                // End at the near paving edge, not deep inside its footprint.
                // Otherwise the rising approach intersects the existing slab.
                for(int side=0;side<2;side++)
                {
                    float x=side==0?-half:half;
                    bool Paved(float distance,out RaycastHit hit)
                    {
                        var point=portal.TransformPoint(new Vector3(x,0,startZ+direction*distance));
                        return pedestrianCollider.Raycast(new Ray(point+Vector3.up*100,Vector3.down),out hit,200);
                    }
                    float inside=sideLengths[side],outside=inside;
                    while(outside>0&&Paved(outside,out _))
                    {inside=outside;outside=Mathf.Max(0,outside-.1f);}
                    if(outside<=0)continue; // Already paved at the landing: retain its authored length.
                    for(int iteration=0;iteration<16;iteration++)
                    {
                        float middle=(inside+outside)*.5f;
                        if(Paved(middle,out _))inside=middle;else outside=middle;
                    }
                    float terminal=Mathf.Min(sideLengths[side],inside+.02f);
                    if(Paved(terminal,out var hit))
                    {
                        sideLengths[side]=terminal;
                        pavingEndHeights[side]=portal.InverseTransformPoint(hit.point+Vector3.up*.005f).y;
                    }
                }
                length=Mathf.Max(sideLengths[0],sideLengths[1]);
            }
            int strips = Mathf.CeilToInt(length/.25f);
            var vertices = new Vector3[(strips + 1) * 2];
            var triangles = new int[strips * 6];
            float maxGrade = 0;
            for (int i = 0; i <= strips; i++)
            for (int side = 0; side < 2; side++)
            {
                float t = (float)i / strips;
                float sideLength=sideLengths[side];
                var end = portal.TransformPoint(new Vector3(side == 0 ? -half : half, 0, startZ + direction * sideLength));
                float endHeight = portal.InverseTransformPoint(new Vector3(end.x, terrain.SampleHeight(end) + terrain.transform.position.y + endOffset, end.z)).y;
                if(meetsPaving)endHeight=pavingEndHeights[side];
                if(landmark.landmarkId=="main_building")endHeight=top;
                vertices[i * 2 + side] = new Vector3(side == 0 ? -half : half, Mathf.Lerp(top, endHeight, t), startZ + direction * sideLength * t);
                maxGrade = Mathf.Max(maxGrade, Mathf.Abs(endHeight - top) / length);
                if (i < strips && side == 0)
                {
                    int v = i * 2, k = i * 6;
                    triangles[k] = v; triangles[k+1] = v+2; triangles[k+2] = v+1;
                    triangles[k+3] = v+1; triangles[k+4] = v+2; triangles[k+5] = v+3;
                }
            }
            // Keep the landing seam fixed; intermediate rows clear local sculpted humps.
            for(int i=1;i<strips;i++)
            {
                float lift=0;
                for(int side=0;side<=4;side++)
                {
                    var local=Vector3.Lerp(vertices[i*2],vertices[i*2+1],side/4f);
                    var world=portal.TransformPoint(local);
                    float required=portal.InverseTransformPoint(new Vector3(world.x,terrain.SampleHeight(world)+terrain.transform.position.y+.025f,world.z)).y;
                    lift=Mathf.Max(lift,required-local.y);
                }
                // Preserve the cross-slope interpolated toward the two terrain endpoints.
                // Flattening each interior row to its higher edge created a drop on the
                // lower edge in the final 0.25m strip, even on a smooth terrain plane.
                vertices[i*2].y+=lift;vertices[i*2+1].y+=lift;
            }
            maxGrade=0;
            for(int i=1;i<=strips;i++)for(int side=0;side<2;side++)
                maxGrade=Mathf.Max(maxGrade,Mathf.Abs(vertices[i*2+side].y-vertices[(i-1)*2+side].y)/(sideLengths[side]/strips));
            if(direction<0)for(int i=0;i<triangles.Length;i+=3){int swap=triangles[i+1];triangles[i+1]=triangles[i+2];triangles[i+2]=swap;}
            string suffix=direction<0?"_reverse":"";
            var meshPath = folder + "/" + portal.name + suffix + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh) { mesh = new Mesh { name = portal.name + " approach" }; AssetDatabase.CreateAsset(mesh, meshPath); }
            // Close the exposed edges down to terrain without altering the walking surface.
            // Keep the original top vertices first for sculpt-response and seam checks.
            var solidVertices = new System.Collections.Generic.List<Vector3>(vertices);
            var solidTriangles = new System.Collections.Generic.List<int>(triangles);
            var perimeter = new System.Collections.Generic.List<int>();
            for(int i=0;i<=strips;i++)perimeter.Add(i*2);
            for(int i=strips;i>=0;i--)perimeter.Add(i*2+1);
            if(direction<0)perimeter.Reverse();
            for(int i=0;i<perimeter.Count;i++)
            {
                var a=vertices[perimeter[i]];var b=vertices[perimeter[(i+1)%perimeter.Count]];
                Vector3 Bottom(Vector3 v)
                {
                    var w=portal.TransformPoint(v);
                    float ground=portal.InverseTransformPoint(new Vector3(w.x,terrain.SampleHeight(w)+terrain.transform.position.y-.05f,w.z)).y;
                    v.y=Mathf.Min(v.y-.08f,ground);return v;
                }
                int k=solidVertices.Count;
                solidVertices.Add(a);solidVertices.Add(Bottom(a));solidVertices.Add(b);solidVertices.Add(Bottom(b));
                solidTriangles.AddRange(new[]{k,k+1,k+2,k+2,k+1,k+3});
            }
            mesh.Clear(); mesh.SetVertices(solidVertices); mesh.SetTriangles(solidTriangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            string approachName=direction<0?"Terrain approach reverse draft":"Terrain approach draft";
            var approach = portal.Find(approachName);
            if (!approach) { approach = new GameObject(approachName).transform; approach.SetParent(portal, false); approach.gameObject.AddComponent<MeshFilter>(); approach.gameObject.AddComponent<MeshRenderer>(); approach.gameObject.AddComponent<MeshCollider>(); }
            approach.GetComponent<MeshFilter>().sharedMesh = mesh;
            approach.GetComponent<MeshRenderer>().sharedMaterial = material;
            approach.GetComponent<MeshCollider>().sharedMesh = null; approach.GetComponent<MeshCollider>().sharedMesh = mesh;
            var pedestrianArea=approach.GetComponent<CampusPedestrianArea>();
            if(!pedestrianArea)pedestrianArea=approach.gameObject.AddComponent<CampusPedestrianArea>();
            pedestrianArea.areaId=portal.name+suffix+"_approach";
            pedestrianArea.localBoundary=perimeter.Select(index=>approach.InverseTransformPoint(portal.TransformPoint(vertices[index]))).ToArray();
            pedestrianArea.excludeFromVehicleRouting=true;
            pedestrianArea.pedestrianAccessVerified=false;
            pedestrianArea.source="Synthetic entrance approach; landmark="+landmark.landmarkId+"; entrance="+portal.name+"; role="+(portal==landmark.accessibleEntrance?"accessible":"general")+". Boundary follows authored top surface; access and Stop connection unverified.";
            EditorUtility.SetDirty(pedestrianArea);
            GameObjectUtility.SetStaticEditorFlags(approach.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
            float maxBurial = 0;
            foreach (var vertex in vertices)
            {
                var world = portal.TransformPoint(vertex);
                maxBurial = Mathf.Max(maxBurial, terrain.SampleHeight(world) + terrain.transform.position.y - world.y);
            }
            float crossGrade=0;
            for(int i=0;i<=strips;i++)crossGrade=Mathf.Max(crossGrade,Mathf.Abs(vertices[i*2].y-vertices[i*2+1].y)/(2*half));
            report.AppendLine(portal.name + suffix + ": length="+length+"m, width="+(2*half)+"m, maximum longitudinal grade=" + maxGrade.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + ", maximum cross grade="+crossGrade.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+", sampled terrain above approach=" + maxBurial.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "m; route remains unverified");
            }
        }
        CampusRearGateSidewalk.Fit();
        CampusBiryongSidewalk.Fit();
        CampusHiTechSidewalk.Fit();
        CampusHiTechFrontage.Fit();
        CampusDormitoryFrontage.Fit();
        CampusDormitoryOneFrontage.Fit();
        CampusDormitoryThreeFrontage.Fit();
        CampusDormitoryThreeDriveway.Fit();
        CampusLibraryFrontage.Fit();
        CampusLibraryPavingConnection.Fit();
        CampusMainBuildingFrontage.Fit();
        CampusMainBuildingPavingConnection.Fit();
        CampusStudentCenterFrontage.Fit();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        if(saveScene){AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());}
        File.WriteAllText("Docs/MapResearch/Iterations/entrance-approaches.txt", report.ToString());
    }
    // Synthetic ramped threshold distributes the rise over the existing forecourt.
    // Door-side elevation is retained; this does not certify an accessible route.
    private static void FitBuildingTwoThreshold(Transform threshold)
    {
        const string path="Assets/CampusSim/Generated/EntranceApproaches/Building2RampedThreshold.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}
        Undo.RecordObject(threshold,"Fit ramped entrance threshold");
        var paving=GameObject.Find("Continuous pedestrian network");
        var surface=paving?paving.GetComponent<MeshCollider>():null;
        var probe=threshold.parent.TransformPoint(new Vector3(0,0,4.5f));
        float front=.27f;
        if(surface&&surface.Raycast(new Ray(probe+Vector3.up*100,Vector3.down),out var hit,200))
            front=threshold.parent.InverseTransformPoint(hit.point).y-.025f;
        var position=threshold.localPosition;position.y=front-threshold.localScale.y/2;threshold.localPosition=position;
        // Coordinates are normalized by the existing 3.2 x .12 x 2.6 transform.
        float back=(.14f-position.y)/threshold.localScale.y;
        float bottom=(-.04f-position.y)/threshold.localScale.y;
        mesh.vertices=new[]{new Vector3(-.5f,back,-.5f),new Vector3(.5f,back,-.5f),new Vector3(-.5f,.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,bottom,-.5f),new Vector3(.5f,bottom,-.5f),new Vector3(-.5f,bottom,.5f),new Vector3(.5f,bottom,.5f)};
        mesh.triangles=new[]{0,2,1,1,2,3,4,5,6,5,7,6,4,0,5,5,0,1,6,7,2,7,3,2,4,6,0,6,2,0,5,1,7,7,1,3};
        mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        threshold.GetComponent<MeshFilter>().sharedMesh=mesh;
        threshold.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Walkway.mat");
        var box=threshold.GetComponent<BoxCollider>();if(box)Undo.DestroyObjectImmediate(box);
        var collider=threshold.GetComponent<MeshCollider>();if(!collider)collider=Undo.AddComponent<MeshCollider>(threshold.gameObject);
        collider.sharedMesh=null;collider.sharedMesh=mesh;
    }
}
