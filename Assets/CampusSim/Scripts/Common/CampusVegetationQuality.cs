using UnityEngine;

namespace CampusSim
{
    // Rendering only. Never remove simulation entities or change authoritative state.
    public sealed class CampusVegetationQuality : MonoBehaviour
    {
        public enum Detail { Low, Medium, High }
        public Detail quality = Detail.High;
        public bool mobileDefaults = true;
        public Camera viewCamera;
        Renderer[] patches;
        float nextUpdate;
        bool initialized;
        void OnEnable()
        {
            patches = GetComponentsInChildren<Renderer>();
            if (!initialized && mobileDefaults && (Application.isMobilePlatform || InhaExpress.Client.MapClient.ClientServerSettings.MobileMode)) quality = Detail.Medium;
            initialized = true;
            // Editor authoring/captures must not serialize the last runtime camera's visibility.
            if (Application.isPlaying) RefreshVisibility();
        }
        public void SetQuality(Detail value) { quality = value; RefreshVisibility(); }
        public float DrawDistance => quality == Detail.Low ? 70 : quality == Detail.Medium ? 140 : 450;
        void LateUpdate()
        {
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + .2f;
            RefreshVisibility();
        }
        public void RefreshVisibility()
        {
            if (patches == null || patches.Length == 0) patches = GetComponentsInChildren<Renderer>(true);
            if (!InhaExpress.Client.MapClient.VegetationDisplaySettings.Visible)
            {
                foreach (var patch in patches) if (patch) patch.enabled = false;
                return;
            }
            if (!viewCamera) viewCamera = Camera.main;
            if (!viewCamera) return;
            if (patches == null || patches.Length == 0) patches = GetComponentsInChildren<Renderer>();
            var position = viewCamera.transform.position;
            float distanceSquared = DrawDistance * DrawDistance;
            foreach (var patch in patches)
            {
                if (!patch) continue;
                var origin = position;
                var limit = distanceSquared;
                // Overview cameras sit high above the map. Measure their visible ground
                // footprint so enabling vegetation does not still hide the entire campus.
                if (viewCamera.orthographic)
                {
                    var ray = viewCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
                    var plane = new Plane(Vector3.up, patch.bounds.center);
                    if (plane.Raycast(ray, out float enter)) origin = ray.GetPoint(enter);
                    float radius = Mathf.Max(DrawDistance, viewCamera.orthographicSize * Mathf.Max(1, viewCamera.aspect) * 1.2f);
                    limit = radius * radius;
                }
                patch.enabled = patch.bounds.SqrDistance(origin) <= limit;
            }
        }
        void OnDisable()
        {
            if (patches == null) return;
            foreach (var patch in patches) if (patch) patch.enabled = !Application.isPlaying || InhaExpress.Client.MapClient.VegetationDisplaySettings.Visible;
        }
    }
}
