using UnityEngine;

namespace CampusSim
{
    /// <summary>Grass mesh density only; visibility remains owned by CampusVegetationQuality.</summary>
    [RequireComponent(typeof(MeshFilter))]
    public sealed class CampusMeadowDetail : MonoBehaviour
    {
        public Mesh denseMesh, sparseMesh;
        public float denseDistance = 65;
        MeshFilter filter;
        float nextUpdate;
        void LateUpdate()
        {
            if(Time.unscaledTime<nextUpdate)return;
            nextUpdate=Time.unscaledTime+.25f;
            var camera=Camera.main;if(!camera)return;
            bool mobile=Application.isMobilePlatform || InhaExpress.Client.MapClient.ClientServerSettings.MobileMode;
            ApplyDensity(camera,mobile);
        }
        public void ApplyDensity(Camera camera,bool mobile)
        {
            if(!denseMesh || !sparseMesh)return;
            if(!filter)filter=GetComponent<MeshFilter>();
            var quality=GetComponentInParent<CampusVegetationQuality>();
            bool high=!mobile && camera && !camera.orthographic && (!quality || quality.quality==CampusVegetationQuality.Detail.High)
                && (transform.TransformPoint(denseMesh.bounds.center)-camera.transform.position).sqrMagnitude<denseDistance*denseDistance;
            var mesh=high?denseMesh:sparseMesh;
            if(mesh && filter.sharedMesh!=mesh)filter.sharedMesh=mesh;
        }
        void OnDisable(){if(filter && denseMesh)filter.sharedMesh=denseMesh;}
    }
}
