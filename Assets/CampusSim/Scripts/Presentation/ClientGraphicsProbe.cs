#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using InhaExpress.Client.MapClient;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace InhaExpress.Client.Presentation
{
    /// <summary>Opt-in graphical Player evidence. Does not send input or change server state.</summary>
    public sealed class ClientGraphicsProbe : MonoBehaviour
    {
        [Serializable] private sealed class Evidence
        {
            public string project_version, run_id;
            public int width, height, nodes, hubs, stops;
            public Rect safe_area;
            public string[] scenes, clipped_text;
            public bool vehicle_actuation;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Argument("--capture-ui") == null) return;
            var root = new GameObject("Development Graphics Probe");
            DontDestroyOnLoad(root);
            root.AddComponent<ClientGraphicsProbe>();
        }

        private static string Argument(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private IEnumerator Start()
        {
            string path = Path.GetFullPath(Argument("--capture-ui"));
            CampusServerMap map = null;
            float deadline = Time.realtimeSinceStartup + 120;
            while (Time.realtimeSinceStartup < deadline)
            {
                map = FindFirstObjectByType<CampusServerMap>();
                if (map != null && !string.IsNullOrEmpty(map.RunId) && FindFirstObjectByType<WebClientHud>() != null) break;
                yield return null;
            }
            if (map == null || string.IsNullOrEmpty(map.RunId))
            {
                Debug.LogError("CAMPUS_GRAPHICS_PROBE_FAILED: no shared session");
                yield break;
            }
            yield return new WaitForSecondsRealtime(2);
            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ScreenCapture.CaptureScreenshot(path);
            var clipped = new List<string>();
            foreach (var text in FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (!text.isActiveAndEnabled || !text.transform.IsChildOf(FindFirstObjectByType<WebClientHud>().transform)) continue;
                var corners = new Vector3[4];
                text.rectTransform.GetWorldCorners(corners);
                bool outside = false;
                foreach (var corner in corners)
                {
                    var pixel = RectTransformUtility.WorldToScreenPoint(null, corner);
                    if (pixel.x < Screen.safeArea.xMin - 1 || pixel.x > Screen.safeArea.xMax + 1 ||
                        pixel.y < Screen.safeArea.yMin - 1 || pixel.y > Screen.safeArea.yMax + 1) outside = true;
                }
                if (outside || text.preferredHeight > text.rectTransform.rect.height + 1) clipped.Add(text.name);
            }
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) scenes.Add(SceneManager.GetSceneAt(i).name);
            var evidence = new Evidence
            {
                project_version = Application.version, run_id = map.RunId,
                width = Screen.width, height = Screen.height, safe_area = Screen.safeArea,
                nodes = map.Document.nodes.Length, hubs = map.Document.hubs.Length, stops = map.Document.stops.Length,
                scenes = scenes.ToArray(), clipped_text = clipped.ToArray(), vehicle_actuation = false
            };
            File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonUtility.ToJson(evidence, true));
            Debug.Log("CAMPUS_GRAPHICS_CAPTURED " + path + " clipped_text=" + clipped.Count);
            string signal = Argument("--exit-signal");
            if (signal == null) yield break;
            while (!File.Exists(signal)) yield return new WaitForSecondsRealtime(.25f);
            Application.Quit();
        }
    }
}
#endif
