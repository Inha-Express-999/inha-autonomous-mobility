using InhaExpress.Client.Domain;
using UnityEngine;

namespace InhaExpress.Client.Presentation
{
    // Temporary English developer HUD; not the shipping passenger UI. No per-frame scene searches.
    public abstract class FixtureStatusPresenter : MonoBehaviour, IClientView
    {
        protected ClientRuntimeHost Host { get; private set; }
        private WorldStateStore subscribedStore;
        private string body = "Waiting for Bootstrap...";
        private string header = "FIXTURE DATA - NO SERVER";
        private double nextStatusRefresh;
        private Vector2 scroll;
        private GUIStyle textStyle, buttonStyle;
        public abstract ClientRole Role { get; }
        public string Body => body;

        public void Bind(ClientRuntimeHost host)
        {
            if (host == null || host.Role != Role) throw new System.ArgumentException("View role mismatch.");
            Unsubscribe();
            Host = host;
            if (isActiveAndEnabled) Subscribe();
        }

        private void OnEnable() { if (Host != null) Subscribe(); }
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (subscribedStore != null) return;
            subscribedStore = Host.Store;
            subscribedStore.SnapshotChanged += OnSnapshot;
            if (subscribedStore.Current != null) OnSnapshot(subscribedStore.Current);
        }

        private void Unsubscribe()
        {
            if (subscribedStore == null) return;
            subscribedStore.SnapshotChanged -= OnSnapshot;
            subscribedStore = null;
        }

        private void OnSnapshot(WorldSnapshotDto snapshot) => body = Format(snapshot);
        protected abstract string Format(WorldSnapshotDto snapshot);

        private void Update()
        {
            if (Host == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextStatusRefresh) return;
            nextStatusRefresh = now + 0.1;
            bool stale = Host.Store.IsStale(now);
            header = "FIXTURE DATA - NO SERVER\n" + Role + " | " + Host.ConnectionState +
                (stale ? " | STALE: last received state, ETA frozen" : " | FRESH") +
                (Host.Fixture != null && Host.Fixture.IsPaused ? " | REPLAY PAUSED" : "");
        }

        private void OnGUI()
        {
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = false };
                buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fixedHeight = 44 };
            }
            var safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) return;
            float scale = Role == ClientRole.Mobile_Passenger ? Mathf.Max(1, safe.width / 420f) :
                Mathf.Max(1, Mathf.Min(safe.width / 1280f, safe.height / 720f));
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(safe.x, Screen.height - safe.yMax, 0),
                Quaternion.identity, Vector3.one * scale);
            float width = Mathf.Min(Role == ClientRole.Mobile_Passenger ? 420 : 620, safe.width / scale);
            var previousColor = GUI.color;
            GUI.color = new Color(0.08f, 0.10f, 0.12f, 0.96f);
            GUI.DrawTexture(new Rect(0, 0, width, safe.height / scale), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUILayout.BeginArea(new Rect(0, 0, width, safe.height / scale), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(header, textStyle);
            GUILayout.Space(8);
            GUILayout.Label(body, textStyle);
            if (Host != null && Host.Fixture != null && (Application.isEditor || Debug.isDebugBuild))
            {
                GUILayout.Space(8);
                GUILayout.Label("Developer-only LOCAL replay controls (not service commands)", textStyle);
                var fixture = Host.Fixture;
                double now = Time.realtimeSinceStartupAsDouble;
                if (GUILayout.Button(fixture.IsPaused ? "Resume replay" : "Pause replay", buttonStyle))
                    fixture.SetPaused(!fixture.IsPaused, now);
                if (GUILayout.Button("Restart with a new run", buttonStyle)) fixture.Restart(now);
                if (GUILayout.Button(fixture.DeliveryEnabled ? "Interrupt delivery" : "Restore delivery", buttonStyle))
                    fixture.SetDeliveryEnabled(!fixture.DeliveryEnabled, now);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = previous;
        }
    }
}
