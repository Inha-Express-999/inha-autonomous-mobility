using System;
using System.Collections.Generic;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Photo-inspired proportions, not surveyed architecture or accessible-route geometry.
public static class CampusLibraryAccent
{
    const string Folder = "Assets/CampusSim/Generated/LibraryAccent";
    [MenuItem("Campus/Map/Apply Library Photo Accent")]
    public static void Apply()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/CampusSim/Scenes/CampusTerrain.unity")
            throw new InvalidOperationException("Open the working campus scene.");
        var parent = UnityEngine.Object.FindObjectsByType<CampusTerrainBinding>(FindObjectsSortMode.None)
            .Single(b => b.name.Contains("정석학술정보관"));
        
        System.IO.Directory.CreateDirectory(Folder);
        var shader = Shader.Find("FlatKit/Stylized Surface");
        if (!shader) throw new InvalidOperationException("FlatKit shader missing.");
        Material Make(string name, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat");
            if (existing) return existing;
            var m = new Material(shader) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor", color);
            m.SetColor("_ColorDim", new Color(.64f,.71f,.74f,1));
            m.EnableKeyword("_CELPRIMARYMODE_SINGLE");
            AssetDatabase.CreateAsset(m, Folder + "/" + name + ".mat");
            return m;
        }
        var glass = Make("Library blue curtain glass", new Color(.30f,.52f,.61f,1));
        var frame = Make("Library silver frame", new Color(.68f,.73f,.73f,1));
        var roof = Make("Library roof canopy", new Color(.48f,.51f,.53f,1));
        var vertices = new List<Vector3>();
        var groups = new[] {new List<int>(),new List<int>(),new List<int>()};
        Vector3 P(float x,float y,float z) => new Vector3(-69.44f,0,-148.38f)
            + new Vector3(.4847f,0,.8747f)*x + Vector3.up*y + new Vector3(.8747f,0,-.4847f)*z;
        void Quad(int material, Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            int n=vertices.Count; vertices.AddRange(new[]{a,b,c,d});
            groups[material].AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        }
        void Front(int material, Vector3 a,Vector3 b,Vector3 c,Vector3 d) => Quad(material,d,c,b,a);
        float Z(float x) => .35f + 2.8f*(1-x*x/144f);
        for(int i=0;i<12;i++)
        {
            float a=-12+i*2,b=a+2;
            // Tile glass and frames on the same faceted plane. Overlaid narrow
            // strips previously produced unstable depth/shadow edges at distance.
            Vector3 Surface(float x,float y) => P(x,y,Mathf.Lerp(Z(a),Z(b),(x-a)/(b-a)));
            void Tile(int material,float x0,float x1,float y0,float y1)
                => Front(material,Surface(x0,y0),Surface(x1,y0),Surface(x1,y1),Surface(x0,y1));
            const float trim=.16f;
            Tile(1,a,a+trim,4,39);
            for(int j=0;j<7;j++)
            {
                float y=4+j*5;
                Tile(1,a+trim,b,y,y+trim);
                Tile(0,a+trim,b,y+trim,y+5-(j==6?trim:0));
            }
            Tile(1,a+trim,b,39-trim,39);
        }
        // Sloping oversailing cap: six closed faces, one shared mesh/material set.
        var a0=P(-15,42,-7); var b0=P(15,42,-7); var c0=P(15,45,5); var d0=P(-15,45,5);
        var down=Vector3.down*.65f;
        Quad(2,a0,b0,c0,d0); Quad(2,a0+down,d0+down,c0+down,b0+down);
        Quad(1,d0+down,c0+down,c0,d0); Quad(1,b0+down,a0+down,a0,b0);
        Quad(1,a0+down,d0+down,d0,a0); Quad(1,c0+down,b0+down,b0,c0);
        // Two visible roof supports terminate in the existing roof.
        foreach(float x in new[]{-9f,9f})
        {
            float lo=x-.22f,hi=x+.22f;
            Front(1,P(lo,38.5f,0),P(hi,38.5f,0),P(hi,43.6f,0),P(lo,43.6f,0));
        }
        const string meshPath=Folder+"/LibraryAtriumTiled.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        bool isNew=!mesh;
        if(isNew) mesh=new Mesh{name="LibraryAtriumTiled"};
        else {Undo.RecordObject(mesh,"Update library mesh");mesh.Clear();}
        mesh.SetVertices(vertices);mesh.subMeshCount=3;
        for(int i=0;i<3;i++)mesh.SetTriangles(groups[i],i);
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        if(isNew) AssetDatabase.CreateAsset(mesh,meshPath);
        else EditorUtility.SetDirty(mesh);
        var child=parent.transform.Find("Photo inspired library atrium");
        var go=child?child.gameObject:new GameObject("Photo inspired library atrium");
        if(!child){Undo.RegisterCreatedObjectUndo(go,"Library photo accent");go.transform.SetParent(parent.transform,false);go.AddComponent<MeshFilter>();go.AddComponent<MeshRenderer>();go.AddComponent<MeshCollider>();}
        Undo.RecordObject(go.GetComponent<MeshFilter>(),"Library mesh");
        Undo.RecordObject(go.GetComponent<MeshRenderer>(),"Library materials");
        Undo.RecordObject(go.GetComponent<MeshCollider>(),"Library collider");
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        go.GetComponent<MeshRenderer>().sharedMaterials=new[]{glass,frame,roof};
        go.GetComponent<MeshCollider>().sharedMesh=null;
        go.GetComponent<MeshCollider>().sharedMesh=mesh;
        GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("Library accent: "+vertices.Count+" vertices, "+groups.Sum(g=>g.Count/3)+" triangles. Existing building and entrances preserved.");
    }
}
