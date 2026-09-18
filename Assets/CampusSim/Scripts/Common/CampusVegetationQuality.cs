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
            if (!initialized && mobileDefaults && Application.isMobilePlatform) quality = Detail.Medium;
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
            if (!viewCamera) viewCamera = Camera.main;
            if (!viewCamera) return;
            if (patches == null || patches.Length == 0) patches = GetComponentsInChildren<Renderer>();
            var position = viewCamera.transform.position;
            float distanceSquared = DrawDistance * DrawDistance;
            foreach (var patch in patches)
                if (patch) patch.enabled = patch.bounds.SqrDistance(position) <= distanceSquared;
        }
        void OnDisable()
        {
            if (patches == null) return;
            foreach (var patch in patches) if (patch) patch.enabled = true;
        }
    }
}
