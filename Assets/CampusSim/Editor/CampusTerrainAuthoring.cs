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

public static class CampusTerrainAuthoring
{
    const string Folder = "Assets/CampusSim/Generated";
    const string ScenePath = "Assets/CampusSim/Scenes/CampusTerrain.unity";
    const string TerrainName = "Campus Terrain | approximate DSM relief";
    static Terrain Terrain => GameObject.Find(TerrainName)?.GetComponent<Terrain>();
    static string ReportDir => Path.Combine(Directory.GetCurrentDirectory(), "Docs/MapResearch/Iterations");
    static void Folders()
    {
        Directory.CreateDirectory(Folder + "/Meshes");
        Directory.CreateDirectory(Folder + "/Materials");
        Directory.CreateDirectory("Assets/CampusSim/Scenes");
        Directory.CreateDirectory(ReportDir);
        AssetDatabase.Refresh();
    }

    [MenuItem("Campus/Map/Create Terrain Working Scene")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stop play/compilation first.");
        var current = SceneManager.GetActiveScene();
        if (current.path != "Assets/InhaCampus/InhaCampus.unity" || current.isDirty)
            throw new InvalidOperationException("Expected saved source InhaCampus scene; preserve user edits first.");
        if (File.Exists(ScenePath)) throw new InvalidOperationException("Working scene already exists. Use Bake, never overwrite.");
        Folders();
        if (!AssetDatabase.CopyAsset(current.path, ScenePath)) throw new IOException("Scene copy failed.");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var data = new TerrainData { heightmapResolution = 257, size = new Vector3(2048, 100, 2048), name = "Campus approximate editable relief" };
        byte[] bytes = File.ReadAllBytes("maps/inha_relief_research/terrain_south_first_u16.raw");
        if (bytes.Length != 257 * 257 * 2) throw new InvalidDataException("Heightmap dimensions mismatch.");
        var heights = new float[257,257];
        for (int z=0;z<257;z++) for(int x=0;x<257;x++)
        { int i=(z*257+x)*2; heights[z,x]=(bytes[i] | bytes[i+1]<<8)/65535f; }
        data.SetHeights(0,0,heights);
        AssetDatabase.CreateAsset(data, Folder + "/CampusTerrain.asset");
        var terrainGo = UnityEngine.Terrain.CreateTerrainGameObject(data);
        terrainGo.name = TerrainName;
        terrainGo.transform.position = new Vector3(-900,-16,-900);
        var terrain = terrainGo.GetComponent<Terrain>();
        terrain.heightmapPixelError = 4;
        terrain.drawInstanced = true;
        var texture = new Texture2D(2,2);
        texture.SetPixels(Enumerable.Repeat(new Color(.39f,.52f,.34f),4).ToArray());texture.Apply();
        AssetDatabase.CreateAsset(texture,Folder+"/Materials/GrassColor.asset");
        var layer = new TerrainLayer { diffuseTexture=texture,tileSize=new Vector2(30,30) };
        AssetDatabase.CreateAsset(layer,Folder+"/Materials/Grass.terrainlayer");
        data.terrainLayers = new[]{layer};
        var terrainShader=Shader.Find("FlatKit/Terrain");
        if (!terrainShader) throw new Exception("Flat Kit Terrain shader missing.");
        var tm=new Material(terrainShader){name="Campus Flat Kit Terrain"};
        tm.EnableKeyword("_CELPRIMARYMODE_SINGLE");
        AssetDatabase.CreateAsset(tm,Folder+"/Materials/Terrain.mat"); terrain.materialTemplate=tm;
        int serial=0;
        foreach (var transform in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray())
        {
            if (transform.name.StartsWith("Ground |") || transform.name.StartsWith("Tree trunks") || transform.name.StartsWith("Foliage") || transform.name.Contains("canopies"))
                transform.gameObject.SetActive(false);
            if (transform.name == "03 Campus buildings")
            {
                foreach(Transform building in transform)
                {
                    var renderers=building.GetComponentsInChildren<Renderer>();
                    if(renderers.Length==0) continue;
                    var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    var bind=building.gameObject.AddComponent<CampusTerrainBinding>();
                    bind.kind=CampusTerrainBinding.BindingKind.Building;bind.samplePoint=bounds.center;
                    bind.originalPosition=building.position;bind.sourceId=building.name;
                    FlattenPad(data,bounds,terrain.transform.position);
                }
            }
        }
        foreach(var mf in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>()).ToArray())
        {
            if(mf.GetComponentInParent<CampusTerrainBinding>() || !mf.sharedMesh)continue;
            var binding=mf.gameObject.AddComponent<CampusTerrainBinding>();
            binding.originalPosition=mf.transform.position;
            binding.samplePoint=mf.GetComponent<Renderer>().bounds.center;
            binding.sourceId=mf.name;
            binding.originalMesh=mf.sharedMesh;
            bool water=mf.name.Contains("인경호")||mf.name.Contains("수로");
            binding.kind=water?CampusTerrainBinding.BindingKind.Water:CampusTerrainBinding.BindingKind.Surface;
            if(water)FlattenPad(data,mf.GetComponent<Renderer>().bounds,terrain.transform.position);
            binding.sourceMesh=water?mf.sharedMesh:Subdivide(mf.sharedMesh);
            if(!water)AssetDatabase.CreateAsset(binding.sourceMesh,Folder+"/Meshes/Source_"+(serial)+".asset");
            binding.bakedMesh=UnityEngine.Object.Instantiate(binding.sourceMesh);
            AssetDatabase.CreateAsset(binding.bakedMesh,Folder+"/Meshes/Baked_"+(serial++)+".asset");
            binding.surfaceOffset=.15f;
        }
        Bake();
        var cameras=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).ToArray();
        foreach(var camera in cameras) { camera.gameObject.SetActive(false);camera.tag="Untagged"; }
        var overview=cameras.FirstOrDefault(c=>c.name=="Campus overview camera");
        if(overview){overview.gameObject.SetActive(true);overview.tag="MainCamera";overview.transform.position=new Vector3(-530,520,-700);overview.transform.LookAt(new Vector3(20,5,-90));}
        RenderSettings.fog=false;
        EditorSceneManager.SaveScene(scene);
        Capture();
        Debug.Log("Campus terrain working scene created; original scene and source meshes preserved.");
    }

    static void FlattenPad(TerrainData data,Bounds bounds,Vector3 origin)
    {
        int n=data.heightmapResolution;
        var h=data.GetHeights(0,0,n,n);
        float target=data.GetInterpolatedHeight((bounds.center.x-origin.x)/data.size.x,(bounds.center.z-origin.z)/data.size.z)/data.size.y;
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            float wx=origin.x+x*data.size.x/(n-1),wz=origin.z+z*data.size.z/(n-1);
            float dx=Mathf.Max(bounds.min.x-wx,0,wx-bounds.max.x),dz=Mathf.Max(bounds.min.z-wz,0,wz-bounds.max.z);
            float distance=Mathf.Sqrt(dx*dx+dz*dz);
            if(distance<12)h[z,x]=Mathf.Lerp(target,h[z,x],Mathf.SmoothStep(0,1,distance/12));
        }
        data.SetHeights(0,0,h);
    }

    static Mesh Subdivide(Mesh source,Transform projection=null,Terrain terrain=null)
    {
        var vertices=new List<Vector3>();var uvs=new List<Vector2>();var triangles=new List<int>();
        var v=source.vertices;var uv=source.uv;
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector2 ua,Vector2 ub,Vector2 uc,int depth)
        {
            float ab=new Vector2(a.x-b.x,a.z-b.z).sqrMagnitude,bc=new Vector2(b.x-c.x,b.z-c.z).sqrMagnitude,ca=new Vector2(c.x-a.x,c.z-a.z).sqrMagnitude;
            bool split=Mathf.Max(ab,bc,ca)>576;
            if(terrain && projection)
            {
                float Height(Vector3 p)=>terrain.SampleHeight(projection.TransformPoint(p));
                float ha=Height(a),hb=Height(b),hc=Height(c);
                // Height interpolation error is amplified across very narrow faces.
                // Refine only slivers that actually span changing terrain heights.
                float longest=Mathf.Sqrt(Mathf.Max(ab,bc,ca));
                float area2=Mathf.Abs((b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x));
                float altitude=area2/Mathf.Max(longest,.0001f);
                var worldCenter=projection.TransformPoint((a+b+c)/3);
                bool lakeRoadJoint=projection.name=="Continuous asphalt network"
                    &&worldCenter.x>244&&worldCenter.x<260&&worldCenter.z> -77&&worldCenter.z< -54;
                if(lakeRoadJoint&&longest>.25f&&longest>8*Mathf.Max(altitude,.0001f)
                    &&Mathf.Max(ha,hb,hc)-Mathf.Min(ha,hb,hc)>.02f)split=true;
                split|=Mathf.Abs(Height((a+b)*.5f)-(ha+hb)*.5f)>.025f;
                split|=Mathf.Abs(Height((b+c)*.5f)-(hb+hc)*.5f)>.025f;
                split|=Mathf.Abs(Height((c+a)*.5f)-(hc+ha)*.5f)>.025f;
                split|=Mathf.Abs(Height((a+b+c)/3)-(ha+hb+hc)/3)>.025f;
            }
            else split=Mathf.Max(ab,bc,ca)>100;
            if(depth<24&&split)
            {
                if(bc>ab&&bc>=ca){Triangle(b,c,a,ub,uc,ua,depth);return;}
                if(ca>ab&&ca>bc){Triangle(c,a,b,uc,ua,ub,depth);return;}
                var m=(a+b)*.5f;var um=(ua+ub)*.5f;
                Triangle(a,m,c,ua,um,uc,depth+1);Triangle(m,b,c,um,ub,uc,depth+1);return;
            }
            int i=vertices.Count;vertices.AddRange(new[]{a,b,c});uvs.AddRange(new[]{ua,ub,uc});triangles.AddRange(new[]{i,i+1,i+2});
        }
        var submeshes=new List<int[]>();
        for(int submesh=0;submesh<source.subMeshCount;submesh++)
        {
            triangles.Clear();var indices=source.GetTriangles(submesh);
            for(int i=0;i<indices.Length;i+=3)
            {int a=indices[i],b=indices[i+1],c=indices[i+2];Triangle(v[a],v[b],v[c],uv.Length>0?uv[a]:Vector2.zero,uv.Length>0?uv[b]:Vector2.zero,uv.Length>0?uv[c]:Vector2.zero,0);}
            submeshes.Add(triangles.ToArray());
        }
        var mesh=new Mesh{name=source.name+" subdivided",indexFormat=IndexFormat.UInt32};
        mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.subMeshCount=submeshes.Count;
        for(int i=0;i<submeshes.Count;i++)mesh.SetTriangles(submeshes[i],i);
        mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }

    static string HierarchyKey(Transform t)
    {
        string key=t.name;while(t.parent && t.parent.name!="INHA UNIVERSITY"){t=t.parent;key=t.name+"/"+key;}return key;
    }

    [MenuItem("Campus/Map/Enable Adaptive Surface Baking")]
    public static void EnableAdaptiveBaking()
    {
        var working=SceneManager.GetActiveScene();
        if(working.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Open working scene.");
        var bindings=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Where(b=>b.kind==CampusTerrainBinding.BindingKind.Surface).ToArray();
        var source=EditorSceneManager.OpenScene("Assets/InhaCampus/InhaCampus.unity",OpenSceneMode.Additive);
        try
        {
            var original=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).GroupBy(m=>HierarchyKey(m.transform)).ToDictionary(g=>g.Key,g=>g.First().sharedMesh);
            foreach(var binding in bindings)
            {
                if(!original.TryGetValue(HierarchyKey(binding.transform),out var mesh))throw new Exception("Missing source "+binding.name);
                binding.originalMesh=mesh;EditorUtility.SetDirty(binding);
            }
        }
        finally{EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(working);}
        Bake();EditorSceneManager.SaveScene(working);
    }

    [MenuItem("Campus/Map/Repair Multi Material Terrain Surfaces")]
    public static void RepairSurfaces()
    {
        if(SceneManager.GetActiveScene().path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open working scene in edit mode.");
        int repaired=0;
        foreach(var binding in UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None))
        {
            if(binding.kind!=CampusTerrainBinding.BindingKind.Surface)continue;
            // Architectural details are rigid structures, not deformable ground.
            if(binding.sourceMesh.bounds.size.y>2)continue;
            if(!AssetDatabase.GetAssetPath(binding.sourceMesh).StartsWith(Folder+"/Meshes/"))throw new Exception("Refusing to modify source asset.");
            var mesh=Subdivide(binding.sourceMesh);
            ReplaceGeneratedSurface(mesh,binding.sourceMesh);
            ReplaceGeneratedSurface(mesh,binding.bakedMesh);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(binding.sourceMesh);EditorUtility.SetDirty(binding.bakedMesh);repaired++;
        }
        Bake();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText(Path.Combine(ReportDir,"surface-repair.txt"),"Ground surfaces retessellated: "+repaired+"\nMaterial submeshes preserved; maximum requested planar triangle edge 2m.\n");
        Debug.Log("Repaired terrain surfaces: "+repaired);
    }

    // Subdivide produces only positions, UV0, normals and triangle submeshes.
    // Use Mesh setters so changes invalidate GPU geometry as well as CPU data.
    // This is deliberately not a general copier for imported/skinned meshes.
    static void ReplaceGeneratedSurface(Mesh source,Mesh destination)
    {
        if(!source || !destination || source==destination)
            throw new ArgumentException("Distinct generated surface meshes required.");
        Undo.RecordObject(destination,"Update generated terrain surface");
        destination.Clear();
        destination.indexFormat=source.indexFormat;
        destination.vertices=source.vertices;
        destination.uv=source.uv;
        destination.normals=source.normals;
        destination.subMeshCount=source.subMeshCount;
        for(int i=0;i<source.subMeshCount;i++)
            destination.SetTriangles(source.GetTriangles(i),i,false);
        destination.bounds=source.bounds;
        EditorUtility.SetDirty(destination);
    }

    [MenuItem("Campus/Map/Bake Terrain Bindings")]
    public static void Bake()
    {
        if(SceneManager.GetActiveScene().path!=ScenePath || !Terrain)throw new InvalidOperationException("Open the terrain working scene.");
        var terrain=Terrain;
        foreach(var binding in UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(binding.transform,"Bake terrain binding");
            if(binding.kind!=CampusTerrainBinding.BindingKind.Surface)
            {
                var sample=binding.heightAnchor?binding.heightAnchor.samplePoint:binding.samplePoint;
                binding.transform.position=binding.originalPosition+Vector3.up*(terrain.SampleHeight(sample)+terrain.transform.position.y+binding.surfaceOffset);
                continue;
            }
            if(binding.originalMesh && binding.originalMesh.bounds.size.y<=2)
            {
                var adaptive=Subdivide(binding.originalMesh,binding.transform,terrain);
                try
                {
                    ReplaceGeneratedSurface(adaptive,binding.sourceMesh);
                    ReplaceGeneratedSurface(adaptive,binding.bakedMesh);
                }
                finally
                {
                    // Both persistent meshes own copies. Release the temporary
                    // native mesh on every bake, including failed copies.
                    UnityEngine.Object.DestroyImmediate(adaptive);
                }
                // Keep persistent asset names consistent with their file names.
                binding.sourceMesh.name=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(binding.sourceMesh));
                binding.bakedMesh.name=Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(binding.bakedMesh));
                EditorUtility.SetDirty(binding.sourceMesh);
            }
            var vertices=binding.sourceMesh.vertices;
            for(int i=0;i<vertices.Length;i++)
            {
                var world=binding.transform.TransformPoint(vertices[i]);
                world.y=binding.transform.position.y+(world.y-binding.transform.position.y)*binding.surfaceReliefScale;
                world.y+=terrain.SampleHeight(world)+terrain.transform.position.y+binding.surfaceOffset;
                vertices[i]=binding.transform.InverseTransformPoint(world);
            }
            binding.bakedMesh.vertices=vertices;binding.bakedMesh.RecalculateNormals();binding.bakedMesh.RecalculateBounds();
            if(binding.name.StartsWith("sport ") || binding.name=="대운동장")
            {
                // Adaptive triangles have split vertices. Sample the common terrain
                // normal field so adjacent triangles do not create false hard seams.
                var normals=new Vector3[vertices.Length];
                for(int i=0;i<vertices.Length;i++)
                {
                    var world=binding.transform.TransformPoint(vertices[i]);
                    var local=terrain.transform.InverseTransformPoint(world);
                    var size=terrain.terrainData.size;
                    var normal=terrain.terrainData.GetInterpolatedNormal(Mathf.Clamp01(local.x/size.x),Mathf.Clamp01(local.z/size.z));
                    var worldNormal=terrain.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(normal).normalized;
                    normals[i]=binding.transform.localToWorldMatrix.transpose.MultiplyVector(worldNormal).normalized;
                }
                binding.bakedMesh.normals=normals;
            }
            binding.GetComponent<MeshFilter>().sharedMesh=binding.bakedMesh;
            var collider=binding.GetComponent<MeshCollider>();if(collider){collider.sharedMesh=null;collider.sharedMesh=binding.bakedMesh;}
            EditorUtility.SetDirty(binding.bakedMesh);
        }
        CampusDormitoryFoundation.Fit();
        CampusDreamCenterFoundation.Fit();
        CampusRemainingFoundations.Fit();
        CampusDormitoryThreeAuthoring.FitFoundation();
        FitRoadPaint();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        CampusHubAuthoring.FitGround();
        CampusWaterShoreAuthoring.FitAfterBake();
        CampusEntranceApproaches.FitAfterTerrainBake();
        AssetDatabase.SaveAssets();
        Debug.Log("Terrain bindings and entrance approaches baked. Access remains unverified; authoring geometry only.");
    }

    public static void FitRoadPaint()
    {
        var road=GameObject.Find("Continuous asphalt network");
        if(!road)return;
        var collider=road.GetComponent<MeshCollider>();if(!collider)return;
        int fitted=0,total=0;
        foreach(string name in new[]{"Crosswalks and lane dashes","Continuous yellow road edges"})
        {
            var go=GameObject.Find(name);if(!go)continue;
            var binding=go.GetComponent<CampusTerrainBinding>();
            if(!binding||!binding.bakedMesh)continue;
            var mesh=binding.bakedMesh;var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)
            {
                total++;var p=go.transform.TransformPoint(vertices[i]);
                if(!collider.Raycast(new Ray(new Vector3(p.x,1000,p.z),Vector3.down),out var hit,2000))continue;
                // Use the actual asphalt triangles, not a second approximation
                // of the terrain. This prevents z-fighting after sculpt/bake.
                p.y=hit.point.y+.015f;vertices[i]=go.transform.InverseTransformPoint(p);fitted++;
            }
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            var paintCollider=go.GetComponent<MeshCollider>();
            if(paintCollider){paintCollider.sharedMesh=null;paintCollider.sharedMesh=mesh;}
        }
        Directory.CreateDirectory(ReportDir);
        File.WriteAllText(Path.Combine(ReportDir,"road-paint-fit.txt"),"Paint vertices fitted to asphalt="+fitted+"/"+total+"; clearance 0.015m. Non-overlapping vertices retain terrain-baked height.\n");
    }

    [MenuItem("Campus/Map/Repair Architecture And Water Bindings")]
    public static void RepairRigidBindings()
    {
        if(SceneManager.GetActiveScene().path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Open working scene.");
        var all=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None);
        var main=all.Single(b=>b.sourceId=="본관 | OSM 217958071_0");
        int details=0,waters=0;
        foreach(var b in all.Where(b=>b.kind==CampusTerrainBinding.BindingKind.Surface && b.name.StartsWith("Main ")))
        {
            if(!b.originalMesh)throw new Exception("Original architecture mesh missing.");
            b.GetComponent<MeshFilter>().sharedMesh=b.originalMesh;
            var c=b.GetComponent<MeshCollider>();if(c)c.sharedMesh=b.originalMesh;
            b.kind=CampusTerrainBinding.BindingKind.Building;b.heightAnchor=main;
            EditorUtility.SetDirty(b);details++;
        }
        const string materialPath=Folder+"/Materials/CampusStylizedWater.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(!material)
        {
            material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Stylized Water 3/Materials/StylizedWater3_Mobile.mat")){name="Campus calm stylized water"};
            material.SetColor("_BaseColor",new Color(.05f,.34f,.39f,1));
            material.SetColor("_ShallowColor",new Color(.18f,.56f,.57f,.9f));
            material.SetFloat("_EdgeFade",.1f);material.SetFloat("_WaveHeight",0);
            material.SetFloat("_FoamBaseAmount",0);material.SetFloat("_AnimationSpeed",.15f);
            AssetDatabase.CreateAsset(material,materialPath);
        }
        foreach(var water in all.Where(b=>b.kind==CampusTerrainBinding.BindingKind.Surface && b.name=="Water").ToArray())
        {
            var parent=water.transform.parent;
            if(parent.GetComponent<CampusTerrainBinding>())continue;
            foreach(var child in parent.GetComponentsInChildren<CampusTerrainBinding>())
            {
                if(!child.originalMesh)throw new Exception("Original water/rim mesh missing.");
                child.transform.position=child.originalPosition;
                child.GetComponent<MeshFilter>().sharedMesh=child.originalMesh;
                var collider=child.GetComponent<MeshCollider>();if(collider)collider.sharedMesh=child.originalMesh;
            }
            var renderers=parent.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
            foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            var bind=parent.gameObject.AddComponent<CampusTerrainBinding>();bind.kind=CampusTerrainBinding.BindingKind.Water;
            bind.originalPosition=parent.position;bind.samplePoint=bounds.center;bind.sourceId=parent.name;
            water.GetComponent<Renderer>().sharedMaterial=material;
            foreach(var child in parent.GetComponentsInChildren<CampusTerrainBinding>().Where(b=>b!=bind).ToArray())UnityEngine.Object.DestroyImmediate(child);
            FlattenPad(Terrain.terrainData,bounds,Terrain.transform.position);waters++;
        }
        Bake();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText(Path.Combine(ReportDir,"rigid-bindings.txt"),"Architecture details bound to main building: "+details+"\nPlanar water groups: "+waters+"\nStylized Water 3 mobile material cloned; no wave displacement.\n");
    }

    [MenuItem("Campus/Map/Inspect Terrain Surface Geometry")]
    public static void InspectSurfaces()
    {
        var report=new System.Text.StringBuilder();
        var terrain=Terrain;
        foreach(var b in UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None))
        {
            if(b.kind!=CampusTerrainBinding.BindingKind.Surface)continue;
            var v=b.sourceMesh.vertices;var indices=b.sourceMesh.triangles;float longest=0,maxGap=0,minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
            foreach(var p in v){minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);}
            for(int i=0;i<indices.Length;i+=3)
            {
                for(int j=0;j<3;j++)
                {
                    var a=b.transform.TransformPoint(v[indices[i+j]]);var c=b.transform.TransformPoint(v[indices[i+(j+1)%3]]);
                    longest=Mathf.Max(longest,Vector2.Distance(new Vector2(a.x,a.z),new Vector2(c.x,c.z)));
                    var midpoint=(a+c)*.5f;
                    maxGap=Mathf.Max(maxGap,Mathf.Abs((terrain.SampleHeight(a)+terrain.SampleHeight(c))*.5f-terrain.SampleHeight(midpoint)));
                }
            }
            report.AppendLine(b.name+" vertices="+v.Length+" edge="+longest+" sourceY="+minY+".."+maxY+" midpointGap="+maxGap);
        }
        File.WriteAllText(Path.Combine(ReportDir,"surface-geometry.txt"),report.ToString());
    }

    [MenuItem("Campus/Map/Capture Working Scene")]
    public static void Capture()
    {
        Folders();var camera=Camera.main;if(!camera)throw new Exception("No active camera.");
        var rt=new RenderTexture(1600,1000,24);var previous=camera.targetTexture;var active=RenderTexture.active;
        var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(Path.Combine(ReportDir,"terrain-overview.png"),image.EncodeToPNG());}
        finally{camera.targetTexture=previous;RenderTexture.active=active;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
    }

    [MenuItem("Campus/Map/Inspect And Capture Lake")]
    public static void CaptureLake()
    {
        var water=UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None).Single(b=>b.kind==CampusTerrainBinding.BindingKind.Water && b.name.Contains("인경호"));
        var mf=water.GetComponentsInChildren<MeshFilter>().Single(m=>m.name=="Water");
        var vertices=mf.sharedMesh.vertices.Select(v=>mf.transform.TransformPoint(v)).ToArray();
        var renderer=mf.GetComponent<Renderer>();var bounds=renderer.bounds;var terrain=Terrain;
        File.WriteAllText(Path.Combine(ReportDir,"lake-geometry.txt"),"Water y range="+vertices.Min(v=>v.y)+".."+vertices.Max(v=>v.y)+"\nMinimum vertex clearance="+vertices.Min(v=>v.y-terrain.SampleHeight(v)-terrain.transform.position.y)+"\nShader="+renderer.sharedMaterial.shader.name+" supported="+renderer.sharedMaterial.shader.isSupported+"\n");
        var camera=Camera.main;var pos=camera.transform.position;var rotation=camera.transform.rotation;
        try
        {
            camera.transform.position=bounds.center+new Vector3(-90,100,-110);camera.transform.LookAt(bounds.center);
            Capture();File.Copy(Path.Combine(ReportDir,"terrain-overview.png"),Path.Combine(ReportDir,"lake.png"),true);
        }
        finally{camera.transform.SetPositionAndRotation(pos,rotation);}
    }

    [MenuItem("Campus/Map/Set Water Surface Clearance")]
    public static void SetWaterClearance()
    {
        if(SceneManager.GetActiveScene().path!=ScenePath)throw new Exception("Open working scene.");
        foreach(var b in UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None))
            if(b.kind==CampusTerrainBinding.BindingKind.Water){b.surfaceOffset=.2f;EditorUtility.SetDirty(b);}
        // Remove the old generic ground offset from architectural details.
        foreach(var b in UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None))
            if(b.heightAnchor){b.surfaceOffset=0;EditorUtility.SetDirty(b);}
        Bake();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }
}

