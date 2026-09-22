using InhaExpress.Client.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace InhaExpress.Client.Presentation
{
    public abstract class FixtureStatusPresenter : MonoBehaviour, IClientView
    {
        protected ClientRuntimeHost Host { get; private set; }
        protected RectTransform ContentRoot { get; private set; }
        protected Text SimulationText { get; private set; }
        private WorldStateStore subscribedStore;
        private GameObject viewRoot;
        private Text connectionText, pauseButtonText, deliveryButtonText;
        private Image connectionPill;
        private Canvas canvas;
        private string body = "Waiting for Bootstrap...";
        private double nextStatusRefresh;
        public abstract ClientRole Role { get; }
        public string Body => body;

        public void Bind(ClientRuntimeHost host)
        {
            if (host == null || host.Role != Role) throw new System.ArgumentException("View role mismatch.");
            Unsubscribe();
            Host = host;
            EnsureView();
            if (isActiveAndEnabled) Subscribe();
        }

        private void OnEnable()
        {
            if (viewRoot != null) viewRoot.SetActive(true);
            if (Host != null) Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (viewRoot != null) viewRoot.SetActive(false);
        }

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

        private void OnSnapshot(WorldSnapshotDto snapshot)
        {
            body = Format(snapshot);
            SimulationText.text = $"{snapshot.SimulationTimeS:F1}s  ·  tick {snapshot.SimulationTick}";
            RenderSnapshot(snapshot);
        }

        protected abstract string Format(WorldSnapshotDto snapshot);
        protected abstract void BuildRoleView(RectTransform root);
        protected abstract void RenderSnapshot(WorldSnapshotDto snapshot);

        private void EnsureView()
        {
            if (viewRoot != null) return;
            viewRoot = new GameObject("Fixture Application UI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            viewRoot.transform.SetParent(transform, false);
            canvas = viewRoot.GetComponent<Canvas>();
            AttachCameraIfAvailable();
            canvas.sortingOrder = 100;
            var scaler = viewRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Role == ClientRole.Mobile_Passenger ? new Vector2(390, 844) : new Vector2(1440, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = Role == ClientRole.Mobile_Passenger ? 0.5f : 0f;
            FixtureUiFactory.EnsureEventSystem(viewRoot.transform);

            var safe = FixtureUiFactory.Rect(viewRoot.transform, "Safe Area", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);
            safe.gameObject.AddComponent<SafeAreaPanel>();
            BuildHeader(safe);
            ContentRoot = FixtureUiFactory.Rect(safe, "Role Content", Vector2.zero, Vector2.one,
                new Vector2(0, 70), new Vector2(0, -72));
            BuildRoleView(ContentRoot);
            BuildControls(safe);
        }

        private void BuildHeader(RectTransform safe)
        {
            var bar = FixtureUiFactory.Panel(safe, "Top Bar", new Vector2(0, 1), Vector2.one,
                new Vector2(12, -64), new Vector2(-12, -10), FixtureUiPalette.Surface);
            var brand = FixtureUiFactory.Text(bar, "Brand", "INHA  EXPRESS", 20, FixtureUiPalette.Ink,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            ((RectTransform)brand.transform).offsetMin = new Vector2(16, 0);
            ((RectTransform)brand.transform).offsetMax = new Vector2(-250, 0);
            bool mobile = Role == ClientRole.Mobile_Passenger;
            if (mobile)
            {
                brand.fontSize = 14;
                brand.text = "INHA EXPRESS";
                ((RectTransform)brand.transform).offsetMin = new Vector2(12, 0);
                ((RectTransform)brand.transform).offsetMax = new Vector2(-242, 0);
            }
            var badge = FixtureUiFactory.Panel(bar, "Fixture Badge", new Vector2(mobile ? 0.52f : 0.5f, 0.18f),
                new Vector2(mobile ? 0.52f : 0.5f, 0.82f), new Vector2(mobile ? -56 : -74, 0),
                new Vector2(mobile ? 56 : 74, 0), new Color(0.94f, 0.62f, 0.12f, 0.16f));
            FixtureUiFactory.Text(badge, "Text", mobile ? "FIXTURE" : "FIXTURE · 서버 미연결", mobile ? 10 : 12,
                FixtureUiPalette.Amber,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SimulationText = FixtureUiFactory.Text(bar, "Simulation Clock", "0.0s", 13,
                FixtureUiPalette.Muted, TextAnchor.MiddleRight);
            ((RectTransform)SimulationText.transform).offsetMax = new Vector2(-154, 0);
            SimulationText.gameObject.SetActive(!mobile);
            connectionPill = FixtureUiFactory.Panel(bar, "Connection", new Vector2(1, 0.2f), Vector2.one,
                new Vector2(mobile ? -112 : -140, 0), new Vector2(-14, -10), new Color(0.11f, 0.61f, 0.41f, 0.12f)).GetComponent<Image>();
            connectionText = FixtureUiFactory.Text(connectionPill.transform, "Text", "● CONNECTING", 11,
                FixtureUiPalette.Green, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        private void BuildControls(RectTransform safe)
        {
            var tray = FixtureUiFactory.Panel(safe, "Demo Controls", Vector2.zero, new Vector2(1, 0),
                new Vector2(12, 10), new Vector2(-12, 62), FixtureUiPalette.Surface);
            var hint = FixtureUiFactory.Text(tray, "Hint", "로컬 데모 제어", 11,
                FixtureUiPalette.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            ((RectTransform)hint.transform).offsetMin = new Vector2(14, 0);
            ((RectTransform)hint.transform).offsetMax = new Vector2(-340, 0);
            hint.gameObject.SetActive(Role != ClientRole.Mobile_Passenger);
            var pause = FixtureUiFactory.Button(tray, "Pause", "일시정지", FixtureUiPalette.Blue, out pauseButtonText);
            SetButtonRect(pause, -322, -222);
            pause.onClick.AddListener(TogglePause);
            var restart = FixtureUiFactory.Button(tray, "Restart", "다시 시작", FixtureUiPalette.Ink, out _);
            SetButtonRect(restart, -212, -112);
            restart.onClick.AddListener(Restart);
            var delivery = FixtureUiFactory.Button(tray, "Delivery", "연결 끊기", FixtureUiPalette.Red, out deliveryButtonText);
            SetButtonRect(delivery, -102, -12);
            delivery.onClick.AddListener(ToggleDelivery);
            tray.gameObject.SetActive(Application.isEditor || Debug.isDebugBuild);
        }

        private static void SetButtonRect(Button button, float left, float right)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(1, 0.15f);
            rect.anchorMax = new Vector2(1, 0.85f);
            rect.offsetMin = new Vector2(left, 0);
            rect.offsetMax = new Vector2(right, 0);
        }

        private void TogglePause()
        {
            var fixture = Host?.Fixture;
            if (fixture != null) fixture.SetPaused(!fixture.IsPaused, Time.realtimeSinceStartupAsDouble);
        }

        private void Restart() => Host?.Fixture?.Restart(Time.realtimeSinceStartupAsDouble);

        private void ToggleDelivery()
        {
            var fixture = Host?.Fixture;
            if (fixture != null) fixture.SetDeliveryEnabled(!fixture.DeliveryEnabled, Time.realtimeSinceStartupAsDouble);
        }

        private void Update()
        {
            if (Host == null || viewRoot == null) return;
            AttachCameraIfAvailable();
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextStatusRefresh) return;
            nextStatusRefresh = now + 0.1;
            bool stale = Host.Store.IsStale(now);
            bool paused = Host.Fixture != null && Host.Fixture.IsPaused;
            connectionText.text = stale ? "● 상태 지연" : "● " + ConnectionLabel(Host.ConnectionState);
            connectionText.color = stale ? FixtureUiPalette.Red : FixtureUiPalette.Green;
            connectionPill.color = stale ? new Color(0.85f, 0.34f, 0.34f, 0.12f) :
                new Color(0.11f, 0.61f, 0.41f, 0.12f);
            pauseButtonText.text = paused ? "계속" : "일시정지";
            deliveryButtonText.text = Host.Fixture != null && Host.Fixture.DeliveryEnabled ? "연결 끊기" : "다시 연결";
        }

        private static string ConnectionLabel(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Connecting: return "연결 중";
                case ConnectionState.Connected: return "연결됨";
                case ConnectionState.Reconnecting: return "재연결 중";
                default: return "연결 끊김";
            }
        }

        private void AttachCameraIfAvailable()
        {
            if (canvas == null || canvas.worldCamera != null) return;
            var main = Camera.main;
            if (main == null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                return;
            }
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = main;
            canvas.planeDistance = Mathf.Max(main.nearClipPlane + 0.1f, 1f);
            canvas.sortingOrder = 100;
        }
    }
}
