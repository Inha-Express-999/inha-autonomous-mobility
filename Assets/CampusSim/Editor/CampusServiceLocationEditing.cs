using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using InhaExpress.Client.MapClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Edit saved Stop/Hub markers; validate and synchronize before entering Play.</summary>
[InitializeOnLoad]
public static class CampusServiceLocationEditing
{
    [Serializable] public sealed class Anchor { public string id; public double x, z; }
    [Serializable] public sealed class Locations
    {
        public int schema_version = 1;
        public string base_geometry_hash;
        public Anchor[] stops = Array.Empty<Anchor>(), hubs = Array.Empty<Anchor>();
    }
    [Serializable] sealed class Outcome { public bool success; public string error, geometry_hash; public Locations locations; }
    static Process process;
    static Task<string> stdout, stderr;
    static bool continuePlay, bypass;
    static bool checkingPreview;
    static Scene temporaryScene;
    static string resultFile;
    const string PendingPlay = "CampusSim.ServiceLocations.PendingPlay";
    public static string LastStatus { get; private set; } = "정류장/허브 이동 후 저장하고 Play하면 서버와 동기화합니다.";

    static CampusServiceLocationEditing()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.update += ResumePendingPlay;
    }

    static void ResumePendingPlay()
    {
        if (!SessionState.GetBool(PendingPlay,false) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        SessionState.SetBool(PendingPlay,false);
        Begin(true);
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && !Application.isBatchMode)
        {
            bool bootstrap = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s => s.name == "PC_Bootstrap");
            if (!bootstrap && SceneManager.GetSceneByPath(CampusServerMapAuthoring.ScenePath).isLoaded &&
                UnityEngine.Object.FindFirstObjectByType<DesktopServerLauncher>() == null)
            {
                var launcher = new GameObject("Editor location session server").AddComponent<DesktopServerLauncher>();
                launcher.StartCoroutine(launcher.EnsureRunning());
            }
            return;
        }
        if (state != PlayModeStateChange.ExitingEditMode || Application.isBatchMode) return;
        if (bypass) { bypass = false; return; }
        if (Environment.GetEnvironmentVariable("CAMPUS_SIM_SKIP_EDITOR_LOCATION_SYNC") == "1") return;
        bool applies = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .Any(s => s.path == CampusServerMapAuthoring.ScenePath || s.name == "PC_Bootstrap");
        if (!applies) return;
        SessionState.SetBool(PendingPlay,true);
        EditorApplication.isPlaying = false;
    }

    [MenuItem("Campus/Server Map/Apply Edited Stops And Hubs")]
    public static void Apply() => Begin(false);

    public static Locations Collect(CampusServerMap map, Locations previous)
    {
        var document = ServerMapDocument.Parse(map.authoredMap.text);
        if (map.geometryHash != document.geometry_hash) throw new InvalidOperationException("씬 지도가 오래됐습니다. 서버 지도를 다시 가져오세요.");
        var markers = map.GetComponentsInChildren<ServerMapMarker>(true);
        if (markers.Length != document.nodes.Length + document.stops.Length + document.hubs.Length)
            throw new InvalidOperationException("마커가 누락/추가됐습니다. 위치 이동만 지원합니다.");
        foreach (var marker in markers)
        {
            var p = marker.transform.position;
            if (!float.IsFinite(p.x) || !float.IsFinite(p.z)) throw new InvalidOperationException("유효하지 않은 좌표: " + marker.serverId);
            if (marker.kind == ServerMapMarker.MarkerKind.RoadNode && HorizontalDistance(p, marker.serverPosition) > .02f)
                throw new InvalidOperationException("도로 노드는 위치 편집 대상이 아닙니다: " + marker.serverId);
        }
        Anchor[] Gather(ServerMapMarker.MarkerKind kind, ServerMapAnchor[] source, Anchor[] old)
        {
            var result = (old ?? Array.Empty<Anchor>()).ToDictionary(a => a.id);
            foreach (var anchor in source)
            {
                var marker = markers.Single(m => m.kind == kind && m.serverId == anchor.id);
                if (HorizontalDistance(marker.serverPosition, anchor.position) > .01f)
                    throw new InvalidOperationException("마커의 서버 메타데이터가 다릅니다: " + anchor.id);
                if (HorizontalDistance(marker.transform.position, anchor.position) > .02f)
                    result[anchor.id] = new Anchor { id = anchor.id, x = marker.transform.position.x, z = marker.transform.position.z };
            }
            if (result.Keys.Except(source.Select(a => a.id)).Any()) throw new InvalidOperationException("이전 위치 설정에 없는 정류장/허브 ID가 있습니다.");
            return result.Values.OrderBy(a => a.id).ToArray();
        }
        return new Locations { base_geometry_hash = map.geometryHash,
            stops = Gather(ServerMapMarker.MarkerKind.Stop, document.stops, previous.stops),
            hubs = Gather(ServerMapMarker.MarkerKind.Hub, document.hubs, previous.hubs) };
    }

    static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x-b.x, a.z-b.z).magnitude;

    static void Begin(bool play)
    {
        if (process != null || EditorApplication.isPlaying) return;
        continuePlay = play;
        try
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            var scene = SceneManager.GetSceneByPath(CampusServerMapAuthoring.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = temporaryScene = EditorSceneManager.OpenScene(CampusServerMapAuthoring.ScenePath, OpenSceneMode.Additive);
            var map = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CampusServerMap>(true)).Single();
            string config = Path.Combine(root, "configs/service_locations.json");
            var previous = File.Exists(config) ? JsonUtility.FromJson<Locations>(File.ReadAllText(config)) : new Locations();
            var locations = Collect(map, previous);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("CampusTerrain 저장 실패");
            string work = Path.Combine(root, "artifacts/service_locations"); Directory.CreateDirectory(work);
            string input = Path.Combine(work, "editor-request.json"); resultFile = Path.Combine(work, "editor-result.json");
            File.WriteAllText(input, JsonUtility.ToJson(locations, true));
            if (File.Exists(resultFile)) File.Delete(resultFile);
            var info = new ProcessStartInfo { FileName = DesktopServerLauncher.ResolvePython(root, null),
                Arguments = "-m campus_sim.editor_locations --input \"" + input + "\" --result \"" + resultFile +
                    "\" --project-root \"" + root + "\" --server-url \"" + map.serverUrl + "\" --preview",
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            string paths = Path.Combine(root, "backend/src");
            string dependencies = Path.Combine(root, ".utmp/fifo-dijkstra-deps");
            if (Directory.Exists(dependencies)) paths += Path.PathSeparator + dependencies;
            string inherited = Environment.GetEnvironmentVariable("PYTHONPATH");
            if (!string.IsNullOrEmpty(inherited)) paths += Path.PathSeparator + inherited;
            info.EnvironmentVariables["PYTHONPATH"] = paths;
            process = Process.Start(info) ?? throw new InvalidOperationException("Python 실행 실패");
            checkingPreview=true;
            stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
            LastStatus = "정류장·허브 도로 연결 및 서버 동기화 검사 중…";
            EditorApplication.update += Poll;
        }
        catch (Exception e) { Fail(e.Message); }
    }

    static void Poll()
    {
        if (process == null || !process.HasExited) return;
        EditorApplication.update -= Poll;
        try
        {
            string error = stderr.GetAwaiter().GetResult(); stdout.GetAwaiter().GetResult();
            var outcome = File.Exists(resultFile) ? JsonUtility.FromJson<Outcome>(File.ReadAllText(resultFile)) : null;
            if (process.ExitCode != 0 || outcome == null || !outcome.success)
                throw new InvalidOperationException(outcome?.error ?? error);
            if(checkingPreview)
            {
                if(outcome.locations==null)throw new InvalidOperationException("도로 투영 결과가 없습니다.");
                var scene=SceneManager.GetSceneByPath(CampusServerMapAuthoring.ScenePath);
                var roads=RoadColliders(scene);
                Physics.SyncTransforms();
                foreach(var a in outcome.locations.stops.Concat(outcome.locations.hubs))
                    if(!roads.Any(c=>c.Raycast(new Ray(new Vector3((float)a.x,1000,(float)a.z),Vector3.down),out _,2000)))
                        throw new InvalidOperationException(a.id+": 도로에 투영한 위치가 아스팔트 밖/잘린 도로 끝입니다. Inspector의 '도로에 맞추기'로 도로 안쪽에 배치하세요.");
                var info=process.StartInfo;
                info.Arguments=info.Arguments.Substring(0,info.Arguments.Length-" --preview".Length)+" --expected-preview-hash "+outcome.geometry_hash;
                process.Dispose();process=null;
                File.Delete(resultFile);
                process=Process.Start(info)??throw new InvalidOperationException("지도 반영 프로세스 실행 실패");
                stdout=process.StandardOutput.ReadToEndAsync();stderr=process.StandardError.ReadToEndAsync();
                checkingPreview=false;LastStatus="도로 투영·아스팔트 검사 통과, 서버 지도 반영 중…";
                EditorApplication.update+=Poll;
                return;
            }
            process.Dispose(); process = null;
            AssetDatabase.ImportAsset(CampusServerMapAuthoring.SnapshotPath, ImportAssetOptions.ForceUpdate);
            CampusServerMapAuthoring.Import();
            LastStatus = "정류장·허브 동기화 완료: " + outcome.geometry_hash;
            UnityEngine.Debug.Log("SERVICE_LOCATIONS_APPLIED " + outcome.geometry_hash);
            CloseTemporaryScene();
            if (continuePlay) { bypass = true; EditorApplication.isPlaying = true; }
        }
        catch (Exception e) { Fail(e.Message); }
    }

    static void CloseTemporaryScene()
    {
        if (temporaryScene.IsValid() && temporaryScene.isLoaded) EditorSceneManager.CloseScene(temporaryScene, true);
        temporaryScene = default;
    }

    static MeshCollider[] RoadColliders(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(g=>g.GetComponentsInChildren<MeshCollider>())
        .Where(c=>c.enabled && c.gameObject.activeInHierarchy && (c.name=="Continuous asphalt network" || c.name.StartsWith("Road "))).ToArray();

    public static void SnapToRoad(ServerMapMarker marker)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || marker.kind==ServerMapMarker.MarkerKind.RoadNode)return;
        var map=marker.GetComponentInParent<CampusServerMap>();
        var document=ServerMapDocument.Parse(map.authoredMap.text);
        var nodes=document.nodes.ToDictionary(n=>n.id,n=>n.position);
        var roads=RoadColliders(marker.gameObject.scene);Physics.SyncTransforms();
        var p=marker.transform.position;var flat=new Vector2(p.x,p.z);
        float best=9.0001f;Vector3 closest=default;bool found=false;
        foreach(var edge in document.edges)
        {
            var a=nodes[edge.u];var b=nodes[edge.v];var d=new Vector2(b.x-a.x,b.z-a.z);
            float length=d.magnitude;if(length<.01f)continue;
            float t=Mathf.Clamp01(Vector2.Dot(flat-new Vector2(a.x,a.z),d)/d.sqrMagnitude);
            // Sample slightly inside clipped ends as an exact endpoint may have
            // zero asphalt area and cannot support a vehicle arrival anchor.
            foreach(float shift in new[]{0f,-.25f,.25f,-.5f,.5f,-1f,1f,-2f,2f})
            {
                var q=Vector3.Lerp(a,b,Mathf.Clamp01(t+shift/length));
                float distance=(flat-new Vector2(q.x,q.z)).sqrMagnitude;
                if(distance>=best)continue;
                foreach(var road in roads)
                    if(road.Raycast(new Ray(new Vector3(q.x,1000,q.z),Vector3.down),out var hit,2000))
                    {best=distance;closest=hit.point;found=true;break;}
            }
        }
        if(!found){LastStatus="3m 이내에 연결 가능한 실제 도로가 없습니다. 포인트를 도로 가까이 이동하세요.";return;}
        Undo.RecordObject(marker.transform,"Snap service point onto road");
        marker.transform.position=closest+Vector3.up*(marker.kind==ServerMapMarker.MarkerKind.Hub?1.2f:.8f);
        EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
        LastStatus="도로 안쪽에 맞췄습니다. 저장 후 Play 또는 위치 검증/반영을 실행하세요.";
    }
    static void Fail(string error)
    {
        EditorApplication.update -= Poll;
        if (process != null && process.HasExited) { process.Dispose(); process = null; }
        LastStatus = "실행 중단: " + error;
        UnityEngine.Debug.LogError("SERVICE_LOCATIONS_BLOCKED " + error);
        CloseTemporaryScene();
    }
}

[CustomEditor(typeof(ServerMapMarker))]
public sealed class ServiceLocationMarkerInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var marker = (ServerMapMarker)target;
        EditorGUILayout.HelpBox(marker.kind == ServerMapMarker.MarkerKind.RoadNode ?
            "도로 노드는 편집하지 마세요. 정류장/허브만 Scene View에서 이동할 수 있습니다." :
            "이 포인트의 X/Z가 운행 위치입니다. 자식 쉼터/차고도 함께 이동하며, 저장 후 Play하면 지도에 반영합니다. 모델 배치만 조정할 때는 자식 모델의 로컬 위치를 바꾸세요.", MessageType.Info);
        using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
        EditorGUILayout.HelpBox(CampusServiceLocationEditing.LastStatus, MessageType.None);
        if(marker.kind!=ServerMapMarker.MarkerKind.RoadNode && GUILayout.Button("도로에 맞추기") && !EditorApplication.isPlaying)
            CampusServiceLocationEditing.SnapToRoad(marker);
        if (GUILayout.Button("위치 검증 및 서버 반영") && !EditorApplication.isPlaying) CampusServiceLocationEditing.Apply();
    }
}

public sealed class ServiceLocationEditingWindow : EditorWindow
{
    [MenuItem("Campus/Server Map/Edit Stops And Hubs")]
    public static void Open() => GetWindow<ServiceLocationEditingWindow>("정류장·허브 위치");

    Vector2 scroll;
    void OnGUI()
    {
        EditorGUILayout.HelpBox("포인트를 선택해 이동하면 자식 정류장/차고 모델도 따라갑니다. 저장 후 Play하면 도로 연결을 검사하고 지도에 반영합니다. 모델만 배치할 때는 자식 모델을 편집하세요.", MessageType.Info);
        if (GUILayout.Button("CampusTerrain 열기") && !EditorApplication.isPlaying)
        {
            var scene=SceneManager.GetSceneByPath(CampusServerMapAuthoring.ScenePath);
            if(!scene.IsValid() || !scene.isLoaded)EditorSceneManager.OpenScene(CampusServerMapAuthoring.ScenePath,OpenSceneMode.Additive);
        }
        scroll=EditorGUILayout.BeginScrollView(scroll);
        foreach(var marker in UnityEngine.Object.FindObjectsByType<ServerMapMarker>(FindObjectsSortMode.None)
            .Where(m=>m.gameObject.scene.path==CampusServerMapAuthoring.ScenePath && m.kind!=ServerMapMarker.MarkerKind.RoadNode)
            .OrderBy(m=>m.kind).ThenBy(m=>m.serverId))
        {
            if(GUILayout.Button((marker.kind==ServerMapMarker.MarkerKind.Hub?"허브 · ":"정류장 · ")+marker.displayName))
            {Selection.activeGameObject=marker.gameObject;Tools.current=Tool.Move;SceneView.lastActiveSceneView?.FrameSelected();}
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.HelpBox(CampusServiceLocationEditing.LastStatus,MessageType.None);
        using(new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            if(GUILayout.Button("검증 및 서버 반영"))CampusServiceLocationEditing.Apply();
    }
}
