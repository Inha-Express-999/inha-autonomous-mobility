using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEngine;

public static class CampusMainBuildingPavingConnection
{
    public static void Fit()
    {
        var landmark=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).SingleOrDefault(l=>l.landmarkId=="main_building");
        if(!landmark)return;
        var p=landmark.generalEntrance;
        var approach=p.Find("Terrain approach draft").GetComponent<MeshCollider>();
        var paving=GameObject.Find("Continuous pedestrian network").GetComponent<MeshCollider>();
        var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();
        var obstacles=UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Where(c=>c.enabled&&(c.name.StartsWith("Road ")||c.name=="Facades")).ToArray();
        const int rows=96,columns=4;
        var vertices=new List<Vector3>();var triangles=new List<int>();float clearance=float.PositiveInfinity;
        for(int j=0;j<=columns;j++)
        {
            float offset=Mathf.Lerp(-.9f,.9f,j/(float)columns);
            var start=p.TransformPoint(new Vector3(offset,0,4.48f));
            if(!approach.Raycast(new Ray(start+Vector3.up*100,Vector3.down),out var first,200))throw new InvalidOperationException("Main approach seam missing.");
            float z=10;RaycastHit last=default;
            for(;z<16;z+=.01f)
            {
                var target=p.TransformPoint(new Vector3(-40+offset,0,z));
                if(paving.Raycast(new Ray(target+Vector3.up*100,Vector3.down),out last,200))break;
            }
            if(z>=16)throw new InvalidOperationException("Main paving seam missing.");
            var corners=new[]{new Vector2(offset,4.48f),new Vector2(offset,9.5f+offset),new Vector2(-40+offset,9.5f+offset),new Vector2(-40+offset,z)};
            var lengths=new[]{Vector2.Distance(corners[0],corners[1]),Vector2.Distance(corners[1],corners[2]),Vector2.Distance(corners[2],corners[3])};
            int[] counts={8,72,16};float total=lengths.Sum(),traveled=0;
            float startY=p.InverseTransformPoint(first.point).y,endY=p.InverseTransformPoint(last.point+Vector3.up*.005f).y;
            for(int segment=0;segment<3;segment++)
            {
                for(int i=segment==0?0:1;i<=counts[segment];i++)
                {
                    float u=i/(float)counts[segment];var point=Vector2.Lerp(corners[segment],corners[segment+1],u);
                    var local=new Vector3(point.x,Mathf.Lerp(startY,endY,(traveled+lengths[segment]*u)/total),point.y);
                    var world=p.TransformPoint(local);
                    clearance=Mathf.Min(clearance,world.y-terrain.SampleHeight(world)-terrain.transform.position.y);
                    if(obstacles.Any(c=>c.Raycast(new Ray(world+Vector3.up*100,Vector3.down),out _,200)))throw new InvalidOperationException("Main connection crosses road or building.");
                    vertices.Add(local);
                }
                traveled+=lengths[segment];
            }
        }
        for(int k=0;k<vertices.Count;k++)
        {
            if(k%(rows+1)==0||k%(rows+1)==rows)continue;
            var v=vertices[k];var world=p.TransformPoint(v);
            world.y=Mathf.Max(world.y,terrain.SampleHeight(world)+terrain.transform.position.y+.025f);
            v.y=p.InverseTransformPoint(world).y;vertices[k]=v;
        }
        float maxGrade=0;
        for(int j=0;j<columns;j++)for(int i=0;i<rows;i++)
        {int k=j*(rows+1)+i;triangles.AddRange(new[]{k,k+1,k+rows+1,k+1,k+rows+2,k+rows+1});}
        for(int k=0;k<triangles.Count;k+=3)
        {
            var n=Vector3.Cross(vertices[triangles[k+1]]-vertices[triangles[k]],vertices[triangles[k+2]]-vertices[triangles[k]]);
            if(n.y<0){int swap=triangles[k+1];triangles[k+1]=triangles[k+2];triangles[k+2]=swap;n=-n;}
            maxGrade=Mathf.Max(maxGrade,new Vector2(n.x,n.z).magnitude/n.y);
        }
        // Constrain the actual triangle gradient, not skewed row differences.
        // Keep both seams fixed and clear the ground without terrain edits.
        var minimumY=vertices.Select(v=>{var w=p.TransformPoint(v);w.y=terrain.SampleHeight(w)+terrain.transform.position.y+.025f;return p.InverseTransformPoint(w).y;}).ToArray();
        bool Fixed(int k)=>k%(rows+1)==0||k%(rows+1)==rows;
        float surfaceGradient=0;
        for(int iteration=0;iteration<4000;iteration++)
        {
            surfaceGradient=0;
            for(int t=0;t<triangles.Count;t+=3)
            {
                int ia=triangles[t],ib=triangles[t+1],ic=triangles[t+2];var a=vertices[ia];var b=vertices[ib];var c=vertices[ic];
                float dx1=b.x-a.x,dz1=b.z-a.z,dx2=c.x-a.x,dz2=c.z-a.z;
                float determinant=dx1*dz2-dx2*dz1;
                if(Mathf.Abs(determinant)<1e-8f)throw new InvalidOperationException("Degenerate walkway face.");
                var ga=new Vector2(dz1-dz2,dx2-dx1)/determinant;
                var gb=new Vector2(dz2,-dx2)/determinant;
                var gc=new Vector2(-dz1,dx1)/determinant;
                var gradient=ga*a.y+gb*b.y+gc*c.y;float magnitude=gradient.magnitude;
                surfaceGradient=Mathf.Max(surfaceGradient,magnitude);
                if(magnitude<=.045f)continue;
                var direction=gradient/magnitude;
                float ca=Fixed(ia)?0:Vector2.Dot(ga,direction),cb=Fixed(ib)?0:Vector2.Dot(gb,direction),cc=Fixed(ic)?0:Vector2.Dot(gc,direction);
                float denominator=ca*ca+cb*cb+cc*cc;
                if(denominator<1e-12f)continue;
                float correction=(magnitude-.045f)/denominator;
                a.y-=correction*ca;b.y-=correction*cb;c.y-=correction*cc;
                if(!Fixed(ia))a.y=Mathf.Max(a.y,minimumY[ia]);
                if(!Fixed(ib))b.y=Mathf.Max(b.y,minimumY[ib]);
                if(!Fixed(ic))c.y=Mathf.Max(c.y,minimumY[ic]);
                vertices[ia]=a;vertices[ib]=b;vertices[ic]=c;
            }
            if(surfaceGradient<=.0451f)break;
        }
        if(surfaceGradient>.046f)throw new InvalidOperationException("Walkway surface constraints unresolved: "+surfaceGradient);

        maxGrade=surfaceGradient;

        var boundary=new List<int>();for(int i=0;i<=rows;i++)boundary.Add(i);for(int j=1;j<=columns;j++)boundary.Add(j*(rows+1)+rows);for(int i=rows-1;i>=0;i--)boundary.Add(columns*(rows+1)+i);for(int j=columns-1;j>0;j--)boundary.Add(j*(rows+1));
        var polygon=boundary.Select(k=>vertices[k]).ToArray();
        for(int i=0;i<boundary.Count;i++)
        {
            var a=vertices[boundary[i]];var b=vertices[boundary[(i+1)%boundary.Count]];
            Vector3 Bottom(Vector3 v){var w=p.TransformPoint(v);w.y=terrain.SampleHeight(w)+terrain.transform.position.y-.05f;v.y=p.InverseTransformPoint(w).y;return v;}
            int k=vertices.Count;vertices.AddRange(new[]{a,Bottom(a),b,Bottom(b)});triangles.AddRange(new[]{k,k+1,k+2,k+2,k+1,k+3});
        }
        const string path="Assets/CampusSim/Generated/EntranceApproaches/MainBuildingPavingConnection.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}
        Undo.RecordObject(mesh,"Fit main-building paving connection");mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        var child=p.Find("Lateral pedestrian connection");
        if(!child){child=new GameObject("Lateral pedestrian connection").transform;Undo.RegisterCreatedObjectUndo(child.gameObject,"Create main-building paving connection");child.SetParent(p,false);child.gameObject.AddComponent<MeshFilter>();child.gameObject.AddComponent<MeshRenderer>();child.gameObject.AddComponent<MeshCollider>();child.gameObject.AddComponent<CampusPedestrianArea>();}
        child.GetComponent<MeshFilter>().sharedMesh=mesh;child.GetComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Walkway.mat");var collider=child.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
        var area=child.GetComponent<CampusPedestrianArea>();area.areaId="main_building_paving_connection";area.localBoundary=polygon;area.excludeFromVehicleRouting=true;area.pedestrianAccessVerified=false;area.source="Synthetic diagonal walkway to existing scene pedestrian network; actual access and Stop unverified.";
        GameObjectUtility.SetStaticEditorFlags(child.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        CampusMainRampRails.Fit();
        File.WriteAllText("Docs/MapResearch/Iterations/main-building-paving-connection.txt",$"Top vertices=485; max face gradient={maxGrade}; minimum vertex terrain clearance={clearance}; endpoint paving offset=0.005m; access unverified.");
    }
}



