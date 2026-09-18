using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusWaterShoreAuthoring
{
    const string Folder="Assets/CampusSim/Generated/WaterShore";
    [MenuItem("Campus/Map/Fit Water And Build Lakeside Pavement")]
    public static void Apply()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity"||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Open working scene.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var high=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/CampusWaterHigh.mat");
        if(!high)
        {
            high=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusStylizedWater.mat")){name="Campus water high quality"};
            high.EnableKeyword("_NORMALMAP");high.EnableKeyword("_REFRACTION");
            high.DisableKeyword("_UNLIT");high.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");high.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            high.SetFloat("_LightingMode",1);high.SetFloat("_DepthVertical",2);
            high.SetFloat("_NormalStrength",.22f);high.SetFloat("_WaveHeight",0);
            high.SetFloat("_IntersectionLength",.3f);high.SetFloat("_ReflectionStrength",.3f);
            high.SetFloat("_SunReflectionStrength",.8f);high.SetFloat("_RefractionStrength",.035f);
            AssetDatabase.CreateAsset(high,Folder+"/CampusWaterHigh.mat");
        }
        foreach(var b in UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Where(b=>b.kind==CampusTerrainBinding.BindingKind.Water))
        {
            var r=b.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Water");var q=r.GetComponent<CampusWaterQuality>()??r.gameObject.AddComponent<CampusWaterQuality>();
            q.mobileQuality=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Materials/CampusStylizedWater.mat");q.highQuality=high;q.SetHighQuality(true);EditorUtility.SetDirty(q);
        }
        if(!GameObject.Find("Inkyung continuous lakeside pavement"))
        {
            var go=new GameObject("Inkyung continuous lakeside pavement");go.AddComponent<MeshFilter>();go.AddComponent<MeshRenderer>();go.AddComponent<MeshCollider>();
            var material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/LakeFurniture/Warm grey stone.mat")){name="Pale lakeside paving"};material.SetColor("_BaseColor",new Color(.66f,.64f,.56f));AssetDatabase.CreateAsset(material,Folder+"/Pavement.mat");go.GetComponent<Renderer>().sharedMaterial=material;
            GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        }
        FitAfterBake();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());CampusTerrainAuthoring.CaptureLake();
    }
    public static void FitAfterBake()
    {
        if(!Directory.Exists(Folder))return;
        var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();if(!terrain)return;
        float Ground(Vector3 p)=>terrain.SampleHeight(p)+terrain.transform.position.y;
        var report=new System.Text.StringBuilder("Synthetic water/pavement fitting, not measured elevations or validated vehicle/pedestrian routes.\n");
        float lakeLevel=0;
        foreach(var b in UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Where(b=>b.kind==CampusTerrainBinding.BindingKind.Water))
        {
            var filter=b.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="Water");
            var points=filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
            float old=points[0].y;float level=points.Max(Ground)+.12f;
            // A pond stays level below its banks. Raising it to the highest bank
            // vertex flooded the connecting paths and forced a tall sidewalk slab.
            const string authoredLevel="Assets/CampusSim/Data/inkyung-water-level.txt";
            if(b.name.Contains("인경호")&&File.Exists(authoredLevel))
                level=float.Parse(File.ReadAllText(authoredLevel),System.Globalization.CultureInfo.InvariantCulture);
            else
            {
                // The long reflecting pool has only a few perimeter vertices.
                // Terrain peaks inside its triangles must also remain submerged.
                // One-metre samples resolve the current two-metre terrain grid.
                float highest=points.Max(Ground);
                var triangles=filter.sharedMesh.triangles;
                for(int k=0;k<triangles.Length;k+=3)
                {
                    var a=points[triangles[k]];var c=points[triangles[k+1]];var d=points[triangles[k+2]];
                    int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Max(Vector3.Distance(a,c),Vector3.Distance(c,d),Vector3.Distance(d,a))));
                    for(int i=0;i<=steps;i++)for(int j=0;j<=steps-i;j++)
                        highest=Mathf.Max(highest,Ground(a+(c-a)*(i/(float)steps)+(d-a)*(j/(float)steps)));
                }
                level=highest+.12f;
            }
            b.transform.position+=Vector3.up*(level-old);
            b.surfaceOffset=b.transform.position.y-b.originalPosition.y-Ground(b.samplePoint);EditorUtility.SetDirty(b);
            if(b.name.Contains("본관 앞 수로")) FitPoolEdge(b,terrain);
            if(b.name.Contains("인경호"))lakeLevel=level;
            report.AppendLine(b.name+": old="+old+", new="+level+"; authored pond level when supplied, otherwise boundary fitting");
        }
        var pavement=GameObject.Find("Inkyung continuous lakeside pavement");
        var pavilion=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).FirstOrDefault(x=>x.name=="Lakeside pavilion | photo guided proposal");
        if(pavilion)
        {
            float floorBase=Mathf.Max(Ground(pavilion.samplePoint)+.15f,lakeLevel+.12f);
            pavilion.surfaceOffset=floorBase-pavilion.originalPosition.y-Ground(pavilion.samplePoint);
            pavilion.transform.position=new Vector3(pavilion.originalPosition.x,floorBase,pavilion.originalPosition.z);
            EditorUtility.SetDirty(pavilion);
            FitPavilionSteps(pavilion);
            report.AppendLine("Pavilion deck base="+floorBase+"; shore connection grade remains unverified.");
        }
        if(pavement) FitPromenade(pavement,terrain,lakeLevel,report);
        File.WriteAllText("Docs/MapResearch/Iterations/water-shore-fit.txt",report.ToString());
    }

    static void FitPoolEdge(CampusTerrainBinding pool,Terrain terrain)
    {
        var edge=pool.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="Stone water edge");
        // Rebuild from the immutable source so raising/lowering terrain is reversible.
        var original=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/InhaCampus/Meshes/0038.asset");
        if(!original)throw new Exception("Reflecting pool edge source missing.");
        string path=Folder+"/PoolGroundedEdge.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(!mesh){mesh=UnityEngine.Object.Instantiate(original);AssetDatabase.CreateAsset(mesh,path);}
        else EditorUtility.CopySerialized(original,mesh);
        mesh.name="PoolGroundedEdge";
        var vertices=original.vertices;float bottom=original.bounds.min.y;
        float foundation=float.PositiveInfinity;
        var triangles=original.triangles;
        for(int k=0;k<triangles.Length;k+=3)for(int side=0;side<3;side++)
        {
            var a=vertices[triangles[k+side]];var c=vertices[triangles[k+(side+1)%3]];
            if(Mathf.Abs(a.y-bottom)>.001f||Mathf.Abs(c.y-bottom)>.001f)continue;
            a=edge.transform.TransformPoint(a);c=edge.transform.TransformPoint(c);
            int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,c)*2));
            for(int j=0;j<=steps;j++)
            {
                var p=Vector3.Lerp(a,c,j/(float)steps);
                foundation=Mathf.Min(foundation,terrain.SampleHeight(p)+terrain.transform.position.y-.08f);
            }
        }
        if(float.IsPositiveInfinity(foundation))throw new Exception("Pool foundation boundary missing.");
        for(int i=0;i<vertices.Length;i++)
        {
            if(Mathf.Abs(vertices[i].y-bottom)>.001f)continue;
            var world=edge.transform.TransformPoint(vertices[i]);
            world.y=Mathf.Min(world.y,foundation);
            vertices[i]=edge.transform.InverseTransformPoint(world);
        }
        mesh.vertices=vertices;mesh.RecalculateBounds();mesh.RecalculateNormals();EditorUtility.SetDirty(mesh);
        edge.sharedMesh=mesh;EditorUtility.SetDirty(edge);
        var collider=edge.GetComponent<MeshCollider>();
        if(collider){collider.sharedMesh=null;collider.sharedMesh=mesh;EditorUtility.SetDirty(collider);}
    }

    // Designed promenade: preserves the full asphalt carriageway, with curved
    // returns to the existing stone walk. Dimensions remain synthetic.
    static void FitPromenade(GameObject pavement,Terrain terrain,float lakeLevel,System.Text.StringBuilder report)
    {
        float Ground(Vector3 p)=>terrain.SampleHeight(p)+terrain.transform.position.y;
        var furniture=GameObject.Find("Inkyung stone path and roadside benches");
        if(!furniture)throw new Exception("Existing lakeside furniture missing");
        var first=furniture.transform.Find("Stone 0").GetComponent<Renderer>().bounds;
        var last=furniture.transform.Find("Stone 196").GetComponent<Renderer>().bounds;
        var a=new Vector3(255.18f,0,-58.46f);var b=new Vector3(212.44f,0,-147.88f);
        var normal=new Vector3(.902f,0,-.431f).normalized;var direction=(b-a).normalized;
        var north=a-normal*1.9f;var south=b-normal*1.9f;
        var start=first.center;start.y=first.max.y+.005f;
        var finish=last.center;finish.y=last.max.y+.005f;
        float level=Mathf.Max(lakeLevel+.22f,Enumerable.Range(0,51).Max(i=>Ground(Vector3.Lerp(a,b,i/50f)+normal*3f))+.30f);
        var points=new System.Collections.Generic.List<Vector3>();
        var widths=new System.Collections.Generic.List<float>();
        void Curve(Vector3 p0,Vector3 p1,Vector3 p2,Vector3 p3,int count,float y0,float y1,bool widening)
        {
            for(int i=0;i<count;i++)
            {
                float t=i/(float)count,u=1-t;var p=u*u*u*p0+3*u*u*t*p1+3*u*t*t*p2+t*t*t*p3;
                p.y=Mathf.Lerp(y0,y1,Mathf.SmoothStep(0,1,t));points.Add(p);
                widths.Add(Mathf.Lerp(widening?1.8f:3f,widening?3f:1.8f,Mathf.SmoothStep(0,1,t)));
            }
        }
        Curve(start,start+new Vector3(9,0,-4),north-direction*3-normal*4,north,32,start.y,level,true);
        const int straight=124;
        for(int i=0;i<=straight;i++){var p=Vector3.Lerp(north,south,i/(float)straight);p.y=level;points.Add(p);widths.Add(3f);}
        // Skip duplicate start station at the return bend.
        int previous=points.Count;
        Curve(south,south+direction*6,finish+new Vector3(10,0,-6),finish,48,level,finish.y,false);
        points.RemoveAt(previous);widths.RemoveAt(previous);
        points.Add(finish);widths.Add(1.8f);
        var v=new System.Collections.Generic.List<Vector3>();var tri=new System.Collections.Generic.List<int>();
        var edgeL=new Vector3[points.Count];var edgeR=new Vector3[points.Count];
        for(int i=0;i<points.Count;i++)
        {
            var tangent=points[Mathf.Min(i+1,points.Count-1)]-points[Mathf.Max(i-1,0)];tangent.y=0;tangent.Normalize();
            var side=Vector3.Cross(Vector3.up,tangent);
            edgeL[i]=points[i]-side*widths[i]/2;edgeR[i]=points[i]+side*widths[i]/2;
            v.Add(edgeL[i]);v.Add(edgeR[i]);
            if(i<points.Count-1){int k=i*2;tri.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3});}
        }
        // Independent wall vertices keep paving normals flat and the bank closed.
        var boundary=edgeL.Concat(edgeR.Reverse()).ToArray();
        for(int i=0;i<boundary.Length;i++)
        {
            var p=boundary[i];var q=boundary[(i+1)%boundary.Length];var lowP=p;var lowQ=q;
            lowP.y=Mathf.Min(p.y-.12f,Ground(p)-.06f);lowQ.y=Mathf.Min(q.y-.12f,Ground(q)-.06f);
            int k=v.Count;v.AddRange(new[]{p,lowP,q,lowQ});tri.AddRange(new[]{k,k+1,k+2,k+2,k+1,k+3});
        }
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Pavement.asset");
        mesh.Clear();mesh.SetVertices(v);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        pavement.GetComponent<MeshFilter>().sharedMesh=mesh;pavement.GetComponent<MeshCollider>().sharedMesh=null;pavement.GetComponent<MeshCollider>().sharedMesh=mesh;
        var material=pavement.GetComponent<Renderer>().sharedMaterial;
        material.SetColor("_BaseColor",new Color(.57f,.59f,.56f));material.SetColor("_ColorDim",new Color(.42f,.46f,.43f));EditorUtility.SetDirty(material);
        var area=pavement.GetComponent<CampusPedestrianArea>()??pavement.AddComponent<CampusPedestrianArea>();
        area.areaId="inkyung_roadside_pavement";area.localBoundary=boundary;area.excludeFromVehicleRouting=true;area.pedestrianAccessVerified=false;
        area.source="Synthetic 3m shore promenade with curved stone-path returns; road footprint preserved; not measured accessibility";EditorUtility.SetDirty(area);
        File.WriteAllText("Docs/MapResearch/Iterations/inkyung-pedestrian-area.json",JsonUtility.ToJson(area,true));
        // Benches sit in the inner 0.8m furnishing strip, leaving 2.2m behind them.
        var benches=furniture.GetComponentsInChildren<CampusTerrainBinding>().Where(x=>x.name.StartsWith("Lakeside bench")).OrderBy(x=>int.Parse(x.name.Substring("Lakeside bench ".Length))).ToArray();
        for(int i=0;i<benches.Length;i++)
        {
            var binding=benches[i];var point=Vector3.Lerp(a,b,(i+.5f)/benches.Length)-normal*2.9f;
            // Feet start at local Y=0. Embed them 5mm into the paving rather
            // than using the positive overlay offset reserved for road paint.
            binding.originalPosition=point;binding.samplePoint=point;binding.surfaceOffset=level-.005f-Ground(point);
            binding.transform.SetPositionAndRotation(new Vector3(point.x,level-.005f,point.z),Quaternion.LookRotation(-normal));EditorUtility.SetDirty(binding);
        }
        // A restrained border band separates the promenade from the dark asphalt.
        var border=pavement.transform.Find("Stone edge bands");
        if(!border){border=new GameObject("Stone edge bands").transform;border.SetParent(pavement.transform,false);border.gameObject.AddComponent<MeshFilter>();border.gameObject.AddComponent<MeshRenderer>();}
        var bv=new System.Collections.Generic.List<Vector3>();var bt=new System.Collections.Generic.List<int>();
        for(int side=0;side<2;side++)for(int i=0;i<points.Count-1;i++)
        {
            var e0=side==0?edgeL[i]:edgeR[i];var e1=side==0?edgeL[i+1]:edgeR[i+1];
            var in0=(points[i]-e0).normalized*.14f;var in1=(points[i+1]-e1).normalized*.14f;
            int k=bv.Count;bv.AddRange(new[]{e0+Vector3.up*.015f,e0+in0+Vector3.up*.015f,e1+Vector3.up*.015f,e1+in1+Vector3.up*.015f});
            if(side==0)bt.AddRange(new[]{k,k+2,k+1,k+1,k+2,k+3});else bt.AddRange(new[]{k,k+1,k+2,k+1,k+3,k+2});
        }
        var bm=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/PromenadeBorder.asset");if(!bm){bm=new Mesh();AssetDatabase.CreateAsset(bm,Folder+"/PromenadeBorder.asset");}
        bm.Clear();bm.SetVertices(bv);bm.SetTriangles(bt,0);bm.RecalculateNormals();bm.RecalculateBounds();EditorUtility.SetDirty(bm);
        border.GetComponent<MeshFilter>().sharedMesh=bm;border.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Pearl.mat");
        GameObjectUtility.SetStaticEditorFlags(border.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        var asphalt=GameObject.Find("Continuous asphalt network");var mask=new CampusVegetationFootprints(new[]{asphalt.GetComponent<MeshFilter>()});int overlap=0,total=0;float maxGrade=0;
        for(int i=0;i<points.Count;i++)
        {
            for(int j=0;j<=6;j++){var p=Vector3.Lerp(edgeL[i],edgeR[i],j/6f);total++;if(mask.Overlaps(new Vector2(p.x,p.z),0))overlap++;}
            if(i>0){var delta=points[i]-points[i-1];maxGrade=Mathf.Max(maxGrade,Mathf.Abs(delta.y)/new Vector2(delta.x,delta.z).magnitude);}
        }
        report.AppendLine("Redesigned promenade: 3m main width, 1.8m stone-path returns, bench strip .8m; asphalt overlap="+overlap+"/"+total+"; maximum centerline grade="+maxGrade+". Profile and access synthetic/unverified.");
    }

    static void FitPavilionSteps(CampusTerrainBinding pavilion)
    {
        var furniture=GameObject.Find("Inkyung stone path and roadside benches");if(!furniture)return;
        // The old three stones lie underneath the raised deck, not between path and deck.
        foreach(Transform child in furniture.transform)if(child.name=="Pavilion connecting stone"){child.gameObject.SetActive(false);child.gameObject.tag="EditorOnly";}
        var deck=pavilion.transform.Find("Deck").GetComponent<Renderer>();
        var end=pavilion.transform.TransformPoint(new Vector3(-3,.5f,0));
        var outward=-pavilion.transform.right;
        var stones=furniture.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("Stone "));
        var stone=stones.Where(r=>Vector3.Dot(r.bounds.center-end,outward)>2.5f).OrderBy(r=>(r.bounds.center-end).sqrMagnitude).FirstOrDefault();
        if(!stone)return;
        var start=stone.bounds.center;start.y=stone.bounds.max.y;
        var delta=end-start;float run=new Vector2(delta.x,delta.z).magnitude;float rise=end.y-start.y;
        if(rise<=.05f||run<1)return;
        var root=GameObject.Find("Pavilion stone stair connection")??new GameObject("Pavilion stone stair connection");
        var area=root.GetComponent<CampusPedestrianArea>()??root.AddComponent<CampusPedestrianArea>();area.areaId="inkyung_pavilion_steps";area.excludeFromVehicleRouting=true;area.pedestrianAccessVerified=false;area.source="synthetic stairs; not step-free; no validated accessible route";
        int count=Mathf.CeilToInt(rise/.16f);var direction=new Vector3(delta.x,0,delta.z).normalized;var rotation=Quaternion.LookRotation(direction);
        var material=stone.sharedMaterial;
        for(int i=0;i<count;i++)
        {
            var child=root.transform.Find("Step "+i);if(!child){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Step "+i;go.transform.SetParent(root.transform,false);child=go.transform;}
            child.gameObject.SetActive(true);float t=(i+.5f)/count;float top=Mathf.Lerp(start.y,end.y,(i+1f)/count);float bottom=Mathf.Lerp(start.y,end.y,i/(float)count)-.18f;
            var point=Vector3.Lerp(start,end,t);point.y=(top+bottom)/2;child.SetPositionAndRotation(point,rotation);child.localScale=new Vector3(1.8f,top-bottom,run/count+.015f);child.GetComponent<Renderer>().sharedMaterial=material;
            GameObjectUtility.SetStaticEditorFlags(child.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        }
        foreach(Transform child in root.transform)if(child.name.StartsWith("Step ")&&int.TryParse(child.name.Substring(5),out int index)&&index>=count)child.gameObject.SetActive(false);
        File.WriteAllText("Docs/MapResearch/Iterations/pavilion-connection.txt","Synthetic general pedestrian stairs: steps="+count+", rise="+rise+"m, run="+run+"m, width=1.8m. Starts on existing stone, ends at deck side. Not step-free; no accessible route approval. Old submerged connector stones archived.\n");
    }
}
