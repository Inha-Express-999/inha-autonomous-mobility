using System;
using UnityEditor;
using UnityEngine;

namespace InhaExpress.EditorTools
{
    /// <summary>Author only the PC crossing variant; leave the imported pedestrian assets intact.</summary>
    public static class PedestrianWaitingAnimation
    {
        private const string Folder = "Assets/CampusSim/Resources/Pedestrians/";

        [MenuItem("InhaExpress/Pedestrians/Rebuild Crossing Waiting Animation")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before authoring the crossing variant.");
            var original = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/CampusSim/Art/Pedestrian/Idle 1.anim");
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/CampusSim/Art/Pedestrian/Pedestrian.controller");
            if (original == null || controller == null) throw new InvalidOperationException("Pedestrian sources missing.");
            var clip = UnityEngine.Object.Instantiate(original);
            clip.name = "Crossing Waiting Look Around";
            // Retain the existing shoulder/body idle, with clearly visible left/right checks.
            SetCurve(clip, "Head Turn Left-Right", .32f);
            SetCurve(clip, "Neck Turn Left-Right", .12f);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0;
            settings.stopTime = 5;
            settings.loopTime = true;
            settings.loopBlend = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            var savedClip = Save(clip, Folder + "CrossingWaiting.anim");
            var variant = new AnimatorOverrideController(controller) { name = "Crossing Waiting" };
            variant[original.name] = savedClip;
            var savedVariant = Save(variant, Folder + "CrossingWaiting.overrideController");
            string prefabPath = Folder + "CrossingVisual.prefab";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                    animator.runtimeAnimatorController = savedVariant;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("CAMPUS_PEDESTRIAN_WAITING_ANIMATION built: five-second look-around idle");
        }

        private static void SetCurve(AnimationClip clip, string muscle, float amplitude)
        {
            var curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, amplitude),
                new Keyframe(1.7f, amplitude), new Keyframe(3, -amplitude),
                new Keyframe(3.7f, -amplitude), new Keyframe(5, 0));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), muscle), curve);
        }

        private static T Save<T>(T authored, string path) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(authored, path); return authored; }
            EditorUtility.CopySerialized(authored, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(authored);
            return existing;
        }
    }
}
