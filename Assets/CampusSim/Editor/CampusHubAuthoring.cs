using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CampusSim;

public static class CampusHubAuthoring
{
    const string RootName = "West Field | Autonomous Mobility Hub";
    const string Folder = "Assets/CampusSim/Generated/Hub";
    static Material white, dark, glass, cyan, asphalt, pale, solar;

    public static void FitGround()
    {
        var hub=UnityEngine.Object.FindFirstObjectByType<CampusVehicleHub>();
        var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();
        if(!hub||!terrain)return;
        var root=hub.transform;var binding=hub.GetComponent<CampusTerrainBinding>();
        float Ground(Vector3 p)=>terrain.SampleHeight(p)+terrain.transform.position.y;
        float maximum=float.NegativeInfinity;
        for(int x=-27;x<=27;x+=3)for(int z=-18;z<=18;z+=3)
            maximum=Mathf.Max(maximum,Ground(root.TransformPoint(new Vector3(x,0,z))));
        // Apron top is local y=.66. Give it 10cm ground clearance at the highest
        // site sample, then close its foundation to the actual terrain perimeter.
        var position=root.position;position.y=maximum-.56f;root.position=position;
        binding.surfaceOffset=position.y-binding.originalPosition.y-Ground(binding.samplePoint);EditorUtility.SetDirty(binding);
        var slab=root.Find("Floating plinth")??root.Find("Grounded foundation");
        slab.name="Grounded foundation";slab.localPosition=Vector3.zero;slab.localRotation=Quaternion.identity;slab.localScale=Vector3.one;
        var corners=new[]{new Vector3(-27,.56f,-18),new Vector3(-27,.56f,18),new Vector3(27,.56f,18),new Vector3(27,.56f,-18)};
        var vertices=new System.Collections.Generic.List<Vector3>();var triangles=new System.Collections.Generic.List<int>();
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {int k=vertices.Count;vertices.AddRange(new[]{a,b,c,d});triangles.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});}
        Quad(corners[0],corners[1],corners[2],corners[3]);
        for(int edge=0;edge<4;edge++)for(int i=0;i<18;i++)
        {
            var a=Vector3.Lerp(corners[edge],corners[(edge+1)%4],i/18f);var b=Vector3.Lerp(corners[edge],corners[(edge+1)%4],(i+1)/18f);
            var lowA=a;var lowB=b;lowA.y=Mathf.Min(a.y-.08f,Ground(root.TransformPoint(a))-position.y-.08f);lowB.y=Mathf.Min(b.y-.08f,Ground(root.TransformPoint(b))-position.y-.08f);
            Quad(a,lowA,lowB,b);
        }
        void Store(Transform target,string filename)
        {
            var path=Folder+"/"+filename+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh){mesh=new Mesh{name=filename};AssetDatabase.CreateAsset(mesh,path);}
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            target.GetComponent<MeshFilter>().sharedMesh=mesh;
            var box=target.GetComponent<BoxCollider>();if(box)UnityEngine.Object.DestroyImmediate(box);
            var collider=target.GetComponent<MeshCollider>();if(!collider)collider=target.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
            GameObjectUtility.SetStaticEditorFlags(target.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        }
        Store(slab,"GroundedFoundation");
        float maxGrade=0;
        foreach(int sign in new[]{-1,1})
        {
            var ramp=root.Find(sign<0?"West entry apron":"East exit apron");ramp.localPosition=Vector3.zero;ramp.localRotation=Quaternion.identity;ramp.localScale=Vector3.one;
            float endX=sign<0?49:48;
            float rampLength=endX-26.5f;
            int segments=Mathf.CeilToInt(rampLength/.5f);
            var road=GameObject.Find("Continuous asphalt network").GetComponent<MeshCollider>();
            vertices.Clear();triangles.Clear();var rows=new Vector3[(segments+1)*2];
            for(int i=0;i<=segments;i++)for(int side=0;side<2;side++)
            {
                float t=i/(float)segments;var end=new Vector3(sign*endX,0,-10.5f+(side==0?-4:4));
                var worldEnd=root.TransformPoint(end);
                if(!road.Raycast(new Ray(worldEnd+Vector3.up*100,Vector3.down),out var roadHit,200))
                    throw new InvalidOperationException("Hub ramp endpoint no longer intersects the authored road. Refit its horizontal alignment.");
                float endY=roadHit.point.y-position.y;
                rows[i*2+side]=new Vector3(sign*Mathf.Lerp(26.5f,endX,t),Mathf.Lerp(.66f,endY,t),end.z);
            }
            for(int i=1;i<segments;i++)
            {
                float lift=0;for(int j=0;j<=4;j++){var p=Vector3.Lerp(rows[i*2],rows[i*2+1],j/4f);lift=Mathf.Max(lift,Ground(root.TransformPoint(p))-position.y+.025f-p.y);}
                rows[i*2].y+=lift;rows[i*2+1].y+=lift;
            }
            for(int i=0;i<segments;i++)
            {
                var a=rows[i*2];var b=rows[i*2+1];var c=rows[(i+1)*2+1];var d=rows[(i+1)*2];
                if(sign>0)Quad(a,b,c,d);else Quad(a,d,c,b);
                for(int side=0;side<2;side++)maxGrade=Mathf.Max(maxGrade,Mathf.Abs(rows[(i+1)*2+side].y-rows[i*2+side].y)/(rampLength/segments));
            }
            // Close ramp edges to terrain; an open top-only mesh looked suspended
            // from low vehicle/passenger cameras even when every top sample fit.
            var perimeter=new System.Collections.Generic.List<Vector3>();
            for(int i=0;i<=segments;i++)perimeter.Add(rows[i*2]);
            for(int i=segments;i>=0;i--)perimeter.Add(rows[i*2+1]);
            if(sign<0)perimeter.Reverse();
            for(int i=0;i<perimeter.Count;i++)
            {
                var a=perimeter[i];var b=perimeter[(i+1)%perimeter.Count];
                var lowA=a;var lowB=b;
                lowA.y=Mathf.Min(a.y-.04f,Ground(root.TransformPoint(a))-position.y-.05f);
                lowB.y=Mathf.Min(b.y-.04f,Ground(root.TransformPoint(b))-position.y-.05f);
                Quad(a,lowA,lowB,b);
            }
            Store(ramp,sign<0?"GroundedEntryRamp":"GroundedExitRamp");
            (sign<0?hub.entry:hub.exit).localPosition=(rows[segments*2]+rows[segments*2+1])*.5f;
        }
        EditorUtility.SetDirty(hub);
        VerifyRoadSeams(hub);
        File.WriteAllText("Docs/MapResearch/Iterations/hub-ground-fit.txt","Foundation follows terrain perimeter; apron clearance .10m at maximum site sample; entry/exit ramps 22.5m/21.5m terminating on the existing road collider. Root="+root.position+" maximum ramp grade="+maxGrade+". Road graph and operation remain unverified.\n");
    }

    public static void VerifyRoadSeams(CampusVehicleHub hub)
    {
        var road=GameObject.Find("Continuous asphalt network").GetComponent<MeshCollider>();
        var report=new System.Text.StringBuilder("Geometry-only hub seam test; not vehicle route or access approval. Samples inset 1mm to avoid exact triangle-edge raycast ambiguity.\n");
        foreach(bool incoming in new[]{true,false})
        {
            string name=incoming?"West entry apron":"East exit apron";
            var ramp=hub.transform.Find(name).GetComponent<MeshCollider>();
            var endpoint=(incoming?hub.entry:hub.exit).localPosition;
            endpoint.x+=incoming?.001f:-.001f;
            int hits=0;float maximum=0;
            for(int i=0;i<=32;i++)
            {
                var p=endpoint;p.z+=Mathf.Lerp(-3.999f,3.999f,i/32f);
                var world=hub.transform.TransformPoint(p);
                var ray=new Ray(world+Vector3.up*100,Vector3.down);
                if(road.Raycast(ray,out var roadHit,200)&&ramp.Raycast(ray,out var rampHit,200))
                {hits++;maximum=Mathf.Max(maximum,Mathf.Abs(roadHit.point.y-rampHit.point.y));}
            }
            report.AppendLine(name+": coverage="+hits+"/33, maximum seam="+maximum+"m");
            if(hits!=33||maximum>.02f)
                throw new InvalidOperationException("Hub road seam failed: "+name+" hits="+hits+" max="+maximum);
        }
        Directory.CreateDirectory("Docs/MapResearch/Iterations");
        File.WriteAllText("Docs/MapResearch/Iterations/hub-seam-validation.txt",report.ToString());
    }

    [MenuItem("Campus/Map/Build West Field Hub")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != "Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new InvalidOperationException("Open CampusTerrain in edit mode first.");
        if (GameObject.Find(RootName)) throw new InvalidOperationException("Hub already exists; preserve existing edits.");
        var map = GameObject.Find("INHA UNIVERSITY").transform;
        var terrain = GameObject.Find("Campus Terrain | approximate DSM relief").GetComponent<Terrain>();
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        white=Mat("Pearl",new Color(.84f,.91f,.91f));
        dark=Mat("Graphite",new Color(.075f,.12f,.16f));
        glass=Mat("Blue glass",new Color(.10f,.29f,.36f));
        cyan=Mat("Signal teal",new Color(.1f,.88f,.79f));
        asphalt=Mat("Apron",new Color(.20f,.26f,.29f));
        pale=Mat("Walkway",new Color(.58f,.69f,.68f));
        solar=Mat("Solar panels",new Color(.09f,.19f,.28f));
        var root=new GameObject(RootName).transform;
        root.position=map.TransformPoint(new Vector3(-300,0,15)); root.rotation=map.rotation;
        var binding=root.gameObject.AddComponent<CampusTerrainBinding>();
        binding.kind=CampusTerrainBinding.BindingKind.Building;
        binding.sourceId="proposed hub on OSM way/568420971";
        binding.originalPosition=root.position; binding.samplePoint=root.position;
        root.position+=Vector3.up*(terrain.SampleHeight(root.position)+terrain.transform.position.y);
        Box(root,"Floating plinth",new Vector3(0,.28f,0),new Vector3(54,.56f,36),dark);
        Box(root,"Vehicle apron",new Vector3(0,.6f,0),new Vector3(53,.12f,35),asphalt);
        Box(root,"North pedestrian concourse",new Vector3(0,.72f,14),new Vector3(52,.22f,6),pale);
        Box(root,"Accessible east concourse",new Vector3(23,.72f,1),new Vector3(6,.22f,22),pale);
        Box(root,"Canopy graphite soffit",new Vector3(-6,6.65f,6),new Vector3(40,.35f,19),dark);
        Box(root,"Pearl floating canopy",new Vector3(-6,7.0f,6),new Vector3(41,.45f,20),white);
        Box(root,"Continuous teal fascia",new Vector3(-6,6.8f,-4.05f),new Vector3(40,.16f,.12f),cyan);
        for(int i=0;i<3;i++)
        {
            float x=-19+i*12;
            Box(root,"Canopy fin "+i,new Vector3(x,3.5f,12),new Vector3(.5f,5.7f,1),white);
            Box(root,"Solar array "+i,new Vector3(x,7.28f,7),new Vector3(10,.13f,12),solar);
            for(int line=0;line<5;line++) Box(root,"Solar seam",new Vector3(x,7.36f,2+line*2.4f),new Vector3(9.8f,.02f,.045f),glass);
        }
        var hub=root.gameObject.AddComponent<CampusVehicleHub>(); hub.departureBays=new Transform[3];
        for(int i=0;i<3;i++)
        {
            float x=-19+i*12;
            Box(root,"Bay "+(i+1)+" inset",new Vector3(x,.685f,4),new Vector3(9,.025f,12),glass);
            foreach(float dx in new[]{-4.5f,4.5f})Box(root,"Bay guidance",new Vector3(x+dx,.71f,4),new Vector3(.12f,.025f,12),cyan);
            Box(root,"Charging pedestal "+(i+1),new Vector3(x,1.65f,10),new Vector3(.7f,2,.55f),white);
            Box(root,"Charger status",new Vector3(x,1.9f,9.71f),new Vector3(.42f,.65f,.045f),cyan);
            Text(root,"Bay number "+i,"0"+(i+1),new Vector3(x,.74f,-1.6f),new Vector3(90,0,0),1.2f,Color.white);
            hub.departureBays[i]=Anchor(root,"Spawn_Bay_0"+(i+1),new Vector3(x,.78f,4),180);
        }
        Box(root,"Operations pavilion",new Vector3(18,2.6f,7),new Vector3(8,3.8f,12),glass);
        Box(root,"Pavilion roof",new Vector3(18,4.65f,7),new Vector3(9,.4f,13),white);
        for(int i=0;i<4;i++)Box(root,"Pavilion mullion",new Vector3(14-.06f,2.6f,2+i*3.3f),new Vector3(.12f,3.7f,.15f),white);
        Box(root,"Pavilion doorway",new Vector3(18,2,0.95f),new Vector3(2.2f,2.7f,.12f),dark);
        Box(root,"Entry portal light",new Vector3(18,3.4f,.84f),new Vector3(2.4f,.1f,.08f),cyan);
        Box(root,"Hub sign backing",new Vector3(-6,6.15f,-4.08f),new Vector3(20,.9f,.14f),dark);
        Text(root,"Hub fascia title","INHA / MOBILITY HUB",new Vector3(-6,6.15f,-4.17f),Vector3.zero,1.4f,Color.white);
        Text(root,"Pavilion title","CONTROL",new Vector3(18,3.9f,.9f),Vector3.zero,.42f,Color.white);
        for(int i=0;i<9;i++)Box(root,"Aisle dashes",new Vector3(-23+i*5.6f,.705f,-11),new Vector3(2.3f,.025f,.12f),white);
        foreach(float x in new[]{-16f,0f,16f})
        {
            var arrow=Box(root,"Eastbound arrow",new Vector3(x,.74f,-8.5f),new Vector3(2,.04f,.15f),cyan);
            var a=Box(root,"Arrow wing",new Vector3(x+.6f,.74f,-8.2f),new Vector3(.85f,.04f,.15f),cyan);a.localRotation=Quaternion.Euler(0,45,0);
            var b=Box(root,"Arrow wing",new Vector3(x+.6f,.74f,-8.8f),new Vector3(.85f,.04f,.15f),cyan);b.localRotation=Quaternion.Euler(0,-45,0);
        }
        Box(root,"Departure hold line",new Vector3(24,.74f,-10.5f),new Vector3(.22f,.04f,7),white);
        // Shallow aprons join the conceptual platform to the parking surface.
        Ramp(root,"West entry apron",-29.5f,true,asphalt);
        Ramp(root,"East exit apron",29.5f,false,asphalt);
        hub.entry=Anchor(root,"Entry_Unverified",new Vector3(-32,.1f,-10.5f),90);
        hub.exit=Anchor(root,"Exit_Unverified",new Vector3(32,.1f,-10.5f),90);
        hub.departureHold=Anchor(root,"Departure_Hold",new Vector3(23,.78f,-10.5f),90);
        hub.passengerWaiting=Anchor(root,"General_Waiting",new Vector3(15,.85f,15),0);
        hub.assistanceWaiting=Anchor(root,"Assistance_Waiting",new Vector3(23,.85f,8),0);
        for(int i=0;i<3;i++)
        {
            Box(root,"Concourse bench",new Vector3(-19+i*12,1.15f,15),new Vector3(3,.25f,.8f),white);
            Box(root,"Bench pedestal",new Vector3(-19+i*12,.95f,15),new Vector3(2,.3f,.4f),dark);
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Selection.activeGameObject=root.gameObject;
        Capture();
        Debug.Log("West field mobility hub built: 3 departure bays; access and routing remain synthetic/unverified.");
    }

    static Material Mat(string name,Color color)
    {
        var shader=Shader.Find("FlatKit/Stylized Surface"); if(!shader)throw new Exception("Flat Kit shader missing");
        var m=new Material(shader){name="Hub "+name};
        m.SetColor("_BaseColor",color);m.SetColor("_Color",color);
        m.EnableKeyword("_CELPRIMARYMODE_SINGLE");
        AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat");return m;
    }
    static Transform Box(Transform parent,string name,Vector3 position,Vector3 size,Material material)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);
        g.transform.localPosition=position;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=material;return g.transform;
    }
    static Transform Anchor(Transform root,string name,Vector3 pos,float yaw)
    {
        var t=new GameObject(name).transform;t.SetParent(root,false);t.localPosition=pos;t.localRotation=Quaternion.Euler(0,yaw,0);return t;
    }
    static void Text(Transform root,string name,string text,Vector3 pos,Vector3 rotation,float size,Color color)
    {
        var t=Anchor(root,name,pos,0);t.localRotation=Quaternion.Euler(rotation);
        var label=t.gameObject.AddComponent<TextMesh>();label.text=text;label.fontSize=80;label.characterSize=size;
        label.characterSize=size/8f;
        label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=color;
    }
    static void Ramp(Transform parent,string name,float x,bool entry,Material material)
    {
        var t=Box(parent,name,new Vector3(x,.35f,-10.5f),new Vector3(5,.15f,8),material);
        t.localRotation=Quaternion.Euler(0,0,entry?7:-7);
    }

    [Serializable] class AnchorRecord { public string id; public Vector3 unityPosition; public Vector3 legacyMapPosition; public float unityHeadingDegrees; }
    [Serializable] class HubReport
    {
        public string hubId="west_field_autonomous_hub";
        public string parkingOsmId="way/568420971";
        public string status="synthetic_design_unverified_access";
        public string coordinateNote="legacy map x=east,z=north; unityPosition includes user map rotation; not AEQD graph coordinates";
        public string runtimeNote="authoring anchors only; Python spawning, occupancy, charging, graph connections and exit reservations not implemented";
        public bool terrainOneMeterRebakePassed;
        public int departureBayCount;
        public AnchorRecord[] anchors;
    }

    [MenuItem("Campus/Map/Polish And Validate West Field Hub")]
    public static void PolishAndValidate()
    {
        if(SceneManager.GetActiveScene().path!="Assets/CampusSim/Scenes/CampusTerrain.unity" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open working scene in edit mode.");
        var hub=GameObject.Find(RootName).GetComponent<CampusVehicleHub>();
        if(!hub.transform.Find("Hub sign backing"))
            Box(hub.transform,"Hub sign backing",new Vector3(-6,6.15f,-4.08f),new Vector3(20,.9f,.14f),AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Graphite.mat"));
        foreach(var text in hub.GetComponentsInChildren<TextMesh>())
        {
            text.characterSize=text.name.StartsWith("Bay number")?.15f:text.name=="Hub fascia title"?.175f:.0525f;
            if(text.name=="Hub fascia title")text.transform.localPosition=new Vector3(-6,6.15f,-4.17f);
        }
        if(hub.departureBays.Length!=3 || hub.departureBays.Any(t=>!t))throw new Exception("Three departure bay anchors required.");
        if(hub.GetComponentsInChildren<Renderer>().Any(r=>!r.sharedMaterial || !r.sharedMaterial.shader.isSupported))throw new Exception("Unsupported hub material.");
        var terrain=GameObject.Find("Campus Terrain | approximate DSM relief").GetComponent<Terrain>();
        int n=terrain.terrainData.heightmapResolution;var original=terrain.terrainData.GetHeights(0,0,n,n);
        var raised=(float[,])original.Clone();for(int z=0;z<n;z++)for(int x=0;x<n;x++)raised[z,x]+=.01f;
        var baseline=hub.departureBays.Select(t=>t.position).ToArray();bool passed=false;
        try
        {
            terrain.terrainData.SetHeights(0,0,raised);CampusTerrainAuthoring.Bake();
            passed=hub.departureBays.Select((t,i)=>Vector3.Distance(t.position,baseline[i]+Vector3.up)<.002f).All(b=>b);
            if(!passed)throw new Exception("Hub terrain rebake mismatch.");
        }
        finally {terrain.terrainData.SetHeights(0,0,original);CampusTerrainAuthoring.Bake();}
        if(hub.departureBays.Select((t,i)=>Vector3.Distance(t.position,baseline[i])>.002f).Any(b=>b))throw new Exception("Terrain restoration mismatch.");
        var map=GameObject.Find("INHA UNIVERSITY").transform;
        var anchors=hub.departureBays.Concat(new[]{hub.entry,hub.exit,hub.departureHold,hub.passengerWaiting,hub.assistanceWaiting});
        var report=new HubReport{terrainOneMeterRebakePassed=passed,departureBayCount=3,anchors=anchors.Select(t=>new AnchorRecord{id=t.name,unityPosition=t.position,legacyMapPosition=map.InverseTransformPoint(t.position),unityHeadingDegrees=t.eulerAngles.y}).ToArray()};
        Directory.CreateDirectory("Docs/MapResearch/Iterations");
        File.WriteAllText("Docs/MapResearch/Iterations/west-field-hub-validation.json",JsonUtility.ToJson(report,true));
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Selection.activeGameObject=hub.gameObject;
        if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(hub.transform.position,Quaternion.Euler(35,hub.transform.eulerAngles.y+35,0),70);
        Debug.Log("Hub validation passed: 3 bays; +1m terrain rebake follows all bay anchors; original heights restored.");
    }

    [MenuItem("Campus/Map/Finish West Field Hub")]
    public static void Finish()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != "Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new InvalidOperationException("Open CampusTerrain in edit mode first.");
        var hub = GameObject.Find(RootName).GetComponent<CampusVehicleHub>();
        var root = hub.transform;
        white = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Pearl.mat");
        dark = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Graphite.mat");
        cyan = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Signal teal.mat");
        if (!root.Find("Mobility beacon"))
        {
            var beacon = Anchor(root, "Mobility beacon", new Vector3(24, .83f, 14), 0);
            Box(beacon, "Beacon body", new Vector3(0, 3, 0), new Vector3(1.8f, 6, 1), dark);
            Box(beacon, "Beacon cap", new Vector3(0, 6.1f, 0), new Vector3(2, .2f, 1.2f), white);
            Box(beacon, "Teal spine", new Vector3(-.8f, 3, -.53f), new Vector3(.12f, 5.7f, .06f), cyan);
            Text(beacon, "Beacon name", "INHA\nMOBILITY\nHUB", new Vector3(.1f, 4.3f, -.56f), Vector3.zero, .65f, Color.white);
            Text(beacon, "Beacon bays", "01 / 02 / 03", new Vector3(.1f, 2.7f, -.56f), Vector3.zero, .38f, Color.white);
            for (int i = 0; i < 3; i++)
                Box(root, "Pedestrian edge marker", new Vector3(10 + i * 2, 1.2f, 11.2f), new Vector3(.18f, .75f, .18f), white);
            Text(root, "Waiting zone label", "WAITING", new Vector3(-6, .86f, 14.5f), new Vector3(90, 0, 0), .8f, Color.white);
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Selection.activeGameObject = hub.gameObject;
        if (SceneView.lastActiveSceneView)
            SceneView.lastActiveSceneView.LookAt(root.TransformPoint(new Vector3(0, 2, 0)), Quaternion.Euler(30, root.eulerAngles.y + 35, 0), 65);
        Debug.Log("West field hub finish saved; three authoring departure bays retained. Runtime dispatch remains unimplemented.");
    }

    [MenuItem("Campus/Map/Capture West Field Hub")]
    public static void Capture()
    {
        var hub=GameObject.Find(RootName).transform;
        var go=new GameObject("Temporary hub capture camera");var cam=go.AddComponent<Camera>();
        cam.transform.position=hub.TransformPoint(new Vector3(-52,29,-64));cam.transform.LookAt(hub.TransformPoint(new Vector3(0,2,1)));
        cam.fieldOfView=43;cam.farClipPlane=2000;cam.clearFlags=CameraClearFlags.Skybox;
        var rt=new RenderTexture(1600,1000,24);var active=RenderTexture.active;var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        try
        {
            cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();
            Directory.CreateDirectory("Docs/MapResearch/Iterations");File.WriteAllBytes("Docs/MapResearch/Iterations/west-field-hub.png",image.EncodeToPNG());
        }
        finally {RenderTexture.active=active;cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(go);}
    }
}
