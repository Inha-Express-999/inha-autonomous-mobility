using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampusSim;
using UnityEditor;
using UnityEngine;

public static class CampusDormitoryThreeDriveway
{
    // Synthetic site walkway to the mapped driveway edge, not a validated Stop.
    public static void Fit()
    {
        var landmark=UnityEngine.Object.FindObjectsByType<CampusLandmarkEntrances>(FindObjectsSortMode.None).SingleOrDefault(l=>l.landmarkId=="dorm_3");
        if(!landmark)return;
        var portal=landmark.generalEntrance;
        var approach=landmark.accessibleEntrance.Find("Terrain approach draft").GetComponent<MeshCollider>();
        var terrain=UnityEngine.Object.FindFirstObjectByType<Terrain>();
        var roads=UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Where(c=>c.enabled&&c.name.StartsWith("Road ")).ToArray();
        var driveway=roads.Where(c=>c.name.StartsWith("Road 471332807")).ToArray();
        if(driveway.Length==0)throw new InvalidOperationException("Mapped dormitory driveway missing.");
        bool Road(float x,float z,out RaycastHit hit)
        {
            var p=portal.TransformPoint(new Vector3(x,0,z));
            foreach(var c in driveway)if(c.Raycast(new Ray(p+Vector3.up*100,Vector3.down),out hit,200))return true;
            hit=default;return false;
        }
        var startX=portal.InverseTransformPoint(landmark.accessibleEntrance.position).x-1.65f;
        const int columns=4,steps=64;
        const float width=1.8f;
        var verts=new List<Vector3>();var tris=new List<int>();
        float maximumBurial=0,maximumLongitudinal=0,maximumCross=0,maximumSeam=0;
        var survey=new System.Text.StringBuilder("row,worldX,worldZ,designY,terrainY\n");
        var obstacles=UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Where(c=>c.enabled&&(c.name=="Facades"||c.GetComponent<CampusLandmarkEntrances>())).ToArray();
        for(int j=0;j<=columns;j++)
        {
            float offset=Mathf.Lerp(-width/2,width/2,j/(float)columns);
            float z0=3.15f+offset,z1=-8+offset;
            var start=portal.TransformPoint(new Vector3(startX+.002f,0,z0));
            if(!approach.Raycast(new Ray(start+Vector3.up*100,Vector3.down),out var startHit,200))throw new InvalidOperationException("Dormitory approach seam missing.");
            float inside=0,outside=-18;bool found=false;
            for(float x=-18.1f;x>=-45;x-=.1f){if(Road(x,z1,out _)){inside=x;found=true;break;}outside=x;}
            if(!found)throw new InvalidOperationException("Driveway edge not reached.");
            for(int k=0;k<18;k++){float m=(inside+outside)*.5f;if(Road(m,z1,out _))inside=m;else outside=m;}
            float endX=inside-.002f;
            if(!Road(endX,z1,out var endHit))throw new InvalidOperationException("Driveway terminal missing.");
            var corners=new[]{new Vector2(startX,z0),new Vector2(-18-offset,z0),new Vector2(-18-offset,z1),new Vector2(endX,z1)};
            float[] lengths={Vector2.Distance(corners[0],corners[1]),Vector2.Distance(corners[1],corners[2]),Vector2.Distance(corners[2],corners[3])};
            float total=lengths.Sum();float startY=portal.InverseTransformPoint(startHit.point).y,endY=portal.InverseTransformPoint(endHit.point+Vector3.up*.005f).y;
            // Fixed segment sample counts keep the corner cross-sections aligned.
            int[] counts={18,22,24};int rowStart=verts.Count;
            float traveled=0;
            for(int segment=0;segment<3;segment++)
            {
                for(int i=segment==0?0:1;i<=counts[segment];i++)
                {
                    float u=i/(float)counts[segment];var p=Vector2.Lerp(corners[segment],corners[segment+1],u);
                    float progress=(traveled+lengths[segment]*u)/total;
                    // A smooth 9cm crown clears the surveyed ridge without a
                    // short, steep kink; both endpoint seams stay unchanged.
                    float lift=.09f*Mathf.Pow(Mathf.Sin(Mathf.PI*progress),2);
                    var v=new Vector3(p.x,Mathf.Lerp(startY,endY,progress)+lift,p.y);
                    var world=portal.TransformPoint(v);float ground=terrain.SampleHeight(world)+terrain.transform.position.y;
                    survey.AppendLine(FormattableString.Invariant($"{j},{world.x},{world.z},{world.y},{ground}"));
                    if(world.y<ground+.025f&&!(segment==0&&i==0)){world.y=ground+.025f;v=portal.InverseTransformPoint(world);}
                    maximumBurial=Mathf.Max(maximumBurial,ground-world.y);
                    if(obstacles.Any(c=>c.Raycast(new Ray(world+Vector3.up*100,Vector3.down),out _,200)))throw new InvalidOperationException("Driveway sidewalk crosses a building.");
                    if(verts.Count>rowStart){var prior=verts[verts.Count-1];maximumLongitudinal=Mathf.Max(maximumLongitudinal,Mathf.Abs(v.y-prior.y)/new Vector2(v.x-prior.x,v.z-prior.z).magnitude);}
                    if(j>0){var prior=verts[verts.Count-(steps+1)];maximumCross=Mathf.Max(maximumCross,Mathf.Abs(v.y-prior.y)/new Vector2(v.x-prior.x,v.z-prior.z).magnitude);}
                    if(segment==2&&i==counts[segment])maximumSeam=Mathf.Max(maximumSeam,Mathf.Abs(world.y-endHit.point.y));
                    verts.Add(v);
                }
                traveled+=lengths[segment];
            }
        }
        File.WriteAllText("Docs/MapResearch/Iterations/dorm3-driveway-design-survey.csv",survey.ToString());
        if(maximumBurial>.001f||maximumLongitudinal>.05f||maximumCross>.05f)
            throw new InvalidOperationException($"Walkway needs site grading: burial={maximumBurial}, longitudinal={maximumLongitudinal}, cross={maximumCross}");
        for(int j=0;j<columns;j++)for(int i=0;i<steps;i++)
        {int k=j*(steps+1)+i;tris.AddRange(new[]{k,k+1,k+steps+1,k+1,k+steps+2,k+steps+1});}
        // Constrain the actual triangle gradient, not skewed row differences.
        // Keep both seams fixed and clear the ground without terrain edits.
        var minimumY=verts.Select(v=>{var w=portal.TransformPoint(v);w.y=terrain.SampleHeight(w)+terrain.transform.position.y+.025f;return portal.InverseTransformPoint(w).y;}).ToArray();
        bool Fixed(int k)=>k%(steps+1)==0||k%(steps+1)==steps;
        float surfaceGradient=0;
        for(int iteration=0;iteration<4000;iteration++)
        {
            surfaceGradient=0;
            for(int t=0;t<tris.Count;t+=3)
            {
                int ia=tris[t],ib=tris[t+1],ic=tris[t+2];var a=verts[ia];var b=verts[ib];var c=verts[ic];
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
                verts[ia]=a;verts[ib]=b;verts[ic]=c;
            }
            if(surfaceGradient<=.0451f)break;
        }
        if(surfaceGradient>.046f)throw new InvalidOperationException("Walkway surface constraints unresolved: "+surfaceGradient);
        maximumLongitudinal=maximumCross=maximumBurial=0;
        for(int j=0;j<=columns;j++)for(int i=0;i<=steps;i++)
        {
            int k=j*(steps+1)+i;var v=verts[k];var w=portal.TransformPoint(v);
            maximumBurial=Mathf.Max(maximumBurial,terrain.SampleHeight(w)+terrain.transform.position.y-w.y);
            if(i>0){var d=v-verts[k-1];maximumLongitudinal=Mathf.Max(maximumLongitudinal,Mathf.Abs(d.y)/new Vector2(d.x,d.z).magnitude);}
            if(j>0){var d=v-verts[k-steps-1];maximumCross=Mathf.Max(maximumCross,Mathf.Abs(d.y)/new Vector2(d.x,d.z).magnitude);}
        }
        // Orient top faces upward regardless of the portal's horizontal direction.
        for(int k=0;k<tris.Count;k+=3)if(Vector3.Cross(verts[tris[k+1]]-verts[tris[k]],verts[tris[k+2]]-verts[tris[k]]).y<0)
        {int swap=tris[k+1];tris[k+1]=tris[k+2];tris[k+2]=swap;}
        var boundary=new List<int>();for(int i=0;i<=steps;i++)boundary.Add(i);for(int j=1;j<=columns;j++)boundary.Add(j*(steps+1)+steps);for(int i=steps-1;i>=0;i--)boundary.Add(columns*(steps+1)+i);for(int j=columns-1;j>0;j--)boundary.Add(j*(steps+1));
        var polygon=boundary.Select(i=>verts[i]).ToArray();
        foreach(var pair in boundary.Select((v,i)=>(a:v,b:boundary[(i+1)%boundary.Count])))
        {
            var a=verts[pair.a];var b=verts[pair.b];Vector3 Bottom(Vector3 v){var w=portal.TransformPoint(v);w.y=terrain.SampleHeight(w)+terrain.transform.position.y-.05f;v.y=portal.InverseTransformPoint(w).y;return v;}
            int k=verts.Count;verts.AddRange(new[]{a,Bottom(a),b,Bottom(b)});tris.AddRange(new[]{k,k+1,k+2,k+2,k+1,k+3});
        }
        const string path="Assets/CampusSim/Generated/EntranceApproaches/Dormitory3Driveway.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!mesh){mesh=new Mesh{name="Dormitory three driveway walkway"};AssetDatabase.CreateAsset(mesh,path);}
        Undo.RecordObject(mesh,"Fit dormitory driveway walkway");mesh.Clear();mesh.SetVertices(verts);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
        var child=portal.Find("Driveway pedestrian connection");if(!child){child=new GameObject("Driveway pedestrian connection").transform;Undo.RegisterCreatedObjectUndo(child.gameObject,"Create dormitory walkway");child.SetParent(portal,false);child.gameObject.AddComponent<MeshFilter>();child.gameObject.AddComponent<MeshRenderer>();child.gameObject.AddComponent<MeshCollider>();child.gameObject.AddComponent<CampusPedestrianArea>();}
        child.GetComponent<MeshFilter>().sharedMesh=mesh;child.GetComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusSim/Generated/Hub/Walkway.mat");var collider=child.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
        var area=child.GetComponent<CampusPedestrianArea>();area.areaId="dormitory3_driveway_connection";area.localBoundary=polygon;area.excludeFromVehicleRouting=true;area.pedestrianAccessVerified=false;area.source="Synthetic site walkway ending at OSM 471332807 driveway edge; access, crossing and Stop approval unverified.";
        GameObjectUtility.SetStaticEditorFlags(child.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic);
        File.WriteAllText("Docs/MapResearch/Iterations/dorm3-driveway-walkway.txt",$"Width={width}; top samples={columns+1}x{steps+1}; max longitudinal={maximumLongitudinal}; max cross={maximumCross}; burial={maximumBurial}; driveway terminal vertical offset={maximumSeam}; route unverified.");
    }
}
