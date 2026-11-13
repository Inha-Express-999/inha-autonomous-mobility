using System;
using System.Collections;
using System.Collections.Generic;
using InhaExpress.Client.MapClient;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using System.Text;

namespace InhaExpress.Client.Presentation
{
    /// <summary>Shared web visual language; PC transport requests, automatic missions, and mobile map.</summary>
    public sealed class WebClientHud : MonoBehaviour
    {
        private CampusServerMap map;
        private Text connection, session, metrics;
        private InputField serverAddress;
        private Text serverAddressStatus;
        private Toggle mobileStepFree;
        private string passengerId;
        private string passengerRun;
        private readonly List<Renderer> mobileHiddenRenderers = new List<Renderer>();
        private readonly Dictionary<Rigidbody, bool> mobileBodies = new Dictionary<Rigidbody, bool>();
        [Serializable] private sealed class PendingPassengerRequest { public string server, run, json; }
        private RectTransform desktopMap;
        private readonly Vector3[] mapCorners = new Vector3[4];
        private Text operationLog;
        private readonly List<string> logLines = new List<string>();
        private readonly Dictionary<string, string> observedStates = new Dictionary<string, string>();
        private string logRun;
        private Slider[] batteryIndicators;
        private Text[] batteryLabels;
        private MapPin selectedHub;
        private Image[] fleetSurfaces;
        private Text[] fleetDetails;
        private Text[] fleetRoutes;
        private CampusRouteOverlay[] routeOverlays;
        private CampusCrowdOverlay crowdOverlay;
        private Text crowdStatus, crowdToggleLabel;
        private GameObject crowdResetButton;
        private readonly List<Dropdown> stopDropdowns = new List<Dropdown>();
        private ServerMapAnchor[] dropdownStops;
        private Camera worldCamera;
        private Camera previousCamera;
        private bool previousCameraEnabled, wasActive, wasOrthographic, wasEnabled;
        private Vector3 cameraPosition;
        private Quaternion cameraRotation;
        private float cameraSize, cameraAspect;
        private Rect previousViewport;
        private GameObject canvasRoot;
        [SerializeField] private bool showWorldMarkers = false;
        private bool mobile;
        private Button disconnectButton;
        private Text disconnectLabel, disconnectStatus;
        private float nextRefresh;
        private float fittedSize;
        private readonly List<Camera> suspendedCameras = new List<Camera>();
        private sealed class MapPin
        {
            public RectTransform root, label, leader;
            public Transform target;
            public Text caption;
            public string vehicleId;
            public bool hovered;
        }
        private readonly List<MapPin> mapPins = new List<MapPin>();
        private readonly List<Rect> placedLabels = new List<Rect>();
        private static readonly Color[] VehicleColors = { new Color(.086f, .467f, 1), new Color(.486f, .361f, .906f), new Color(.063f, .659f, .584f) };
        private ServerMapMarker[] hubs;
        private RectTransform safeRoot;
        public PcOperatorControls OperatorControls { get; private set; }
        private Toggle[] vehicleButtons;
        private readonly Dictionary<string, UnityAutonomousClient> automaticClients = new Dictionary<string, UnityAutonomousClient>();
        private Button requestSubmit;
        private Text requestStatus, requestLoad, requestService;
        private Dropdown dispatchDropdown;
        private Dropdown plannerDropdown;
        private bool changingPlanner;
        private Text selectedSpeed, queueDetails;
        private RectTransform queueContent, queueViewport;
        private Toggle[] weatherToggles;
        private Button pedestrianSpawnButton;
        private Text pedestrianStatus;
        private string pedestrianFeedback, pedestrianFeedbackVehicle;
        private float pedestrianFeedbackUntil;
        private static readonly string[] WeatherKeys = { "clear", "fog", "rain", "snow" };
        private static readonly string[] WeatherLabels = { "맑음", "안개", "비", "눈" };
        private static readonly string[] WeatherRoads = { "dry", "dry", "wet", "snow" };
        private Text dispatchStatus;
        private bool changingDispatch;
        [Serializable] private sealed class DispatchControl { public string dispatch; }
        [Serializable] private sealed class PlannerControl { public string planner; }
        private ServerMapAnchor pickup, dropoff;
        private bool cargoRequest, submitting;
        private int requestAmount = 1;
        private string pendingRequestJson, pendingRequestRun, submittedRequestId;
        [Serializable] private sealed class TransportOrder
        {
            public string command_id, owner_id = "unity-operator", service_type;
            public string pickup_landmark_id, dropoff_landmark_id;
            public int party_size;
            public float cargo_kg;
            public TransportNeeds service_needs = new TransportNeeds();
        }
        [Serializable] private sealed class TransportNeeds { public bool requires_step_free; public int wheelchair_slots; public bool boarding_assistance; }
        [Serializable] private sealed class TransportAck
        {
            public bool accepted;
            public CampusServerMap.TransportRequest request;
        }
        public UnityAutonomousClient ActiveAutomaticClient => OperatorControls?.SelectedVehicle != null &&
            automaticClients.TryGetValue(OperatorControls.SelectedVehicle.OperatorVehicleId, out var client) ? client : null;
        private Bounds framingBounds;
        private Vector2Int lastScreenSize;
        private Rect lastSafeArea;
        private Rect MapViewport
        {
            get
            {
                if (mobile) return new Rect(0, .60f, 1, .32f);
                if (desktopMap == null) return new Rect(0, .49f, 1, .43f);
                desktopMap.GetWorldCorners(mapCorners);
                var safe = Screen.safeArea;
                return new Rect((mapCorners[0].x - safe.x) / safe.width,
                    (mapCorners[0].y - safe.y) / safe.height,
                    (mapCorners[2].x - mapCorners[0].x) / safe.width,
                    (mapCorners[2].y - mapCorners[0].y) / safe.height);
            }
        }

        public void Initialize(bool passenger) => StartCoroutine(WaitForWorld(passenger));

        private IEnumerator WaitForWorld(bool passenger)
        {
            while ((map = FindFirstObjectByType<CampusServerMap>()) == null) yield return null;
            Build(passenger);
        }

        private void Build(bool passenger)
        {
            mobile = passenger;
            if (mobile) passengerId = ClientServerSettings.PassengerId;
            if (mobile)
            {
                try
                {
                    var pending = JsonUtility.FromJson<PendingPassengerRequest>(PlayerPrefs.GetString("InhaMobil.Passenger.Pending", "{}"));
                    if (pending != null && pending.server == map.serverUrl)
                    { pendingRequestJson = pending.json; pendingRequestRun = pending.run; }
                }
                catch (ArgumentException) { ClearPendingRequest(); }
            }
            map = FindFirstObjectByType<CampusServerMap>();
            if (map != null) map.showStatus = false;
            canvasRoot = new GameObject("Inha Mobil Web UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);
            FixtureUiFactory.EnsureEventSystem(canvasRoot.transform);
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasRoot.GetComponent<Canvas>().sortingOrder = 110;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = mobile ? new Vector2(390, 844) : new Vector2(1280, 720);
            scaler.matchWidthOrHeight = mobile ? 0 : .5f;
            var root = FixtureUiFactory.Rect(canvasRoot.transform, "Safe Area", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            safeRoot = root;
            root.gameObject.AddComponent<SafeAreaPanel>();
            if (mobile)
            {
            Panel(root, "Header", 0, .92f, 1, 1);
            Label(root, "Brand", "Inha Mobil", mobile ? 23 : 24, mobile ? .14f : .065f, .93f, .55f, .995f, true);
            var logo=Resources.Load<Texture2D>("inha-mobil-logo");
            if(logo!=null) {
                var icon=FixtureUiFactory.Rect(root,"Brand Logo",new Vector2(.025f,.935f),new Vector2(mobile?.12f:.055f,.99f),Vector2.zero,Vector2.zero).gameObject.AddComponent<RawImage>();
                icon.texture=logo; icon.raycastTarget=false;
            }
            connection = Label(root, "Connection", "연결 확인 중", mobile ? 12 : 15, .58f, .94f, .97f, .985f);
            connection.alignment = TextAnchor.MiddleRight;
                BuildMobile(root);
                session = Label(root, "Version", $"v{Application.version}", 10, .05f, .01f, .95f, .04f);
            }
            else BuildOperator(root);
            Canvas.ForceUpdateCanvases();
            worldCamera = Camera.main;
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (camera.CompareTag("MainCamera"))
                {
                    if (worldCamera != null && worldCamera != camera)
                    {
                        previousCamera = worldCamera;
                        previousCameraEnabled = previousCamera.enabled;
                        previousCamera.enabled = false;
                    }
                    worldCamera = camera;
                    break;
                }
            if (worldCamera != null)
            {
                previousViewport = worldCamera.rect;
                wasActive = worldCamera.gameObject.activeSelf;
                wasEnabled = worldCamera.enabled;
                wasOrthographic = worldCamera.orthographic;
                cameraPosition = worldCamera.transform.position;
                cameraRotation = worldCamera.transform.rotation;
                cameraSize = worldCamera.orthographicSize;
                cameraAspect = worldCamera.aspect;
                worldCamera.gameObject.SetActive(true);
                worldCamera.enabled = true;
                foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    if (camera != worldCamera && camera.isActiveAndEnabled && camera.targetTexture == null)
                    {
                        suspendedCameras.Add(camera);
                        camera.enabled = false;
                    }
                var bounds = new Bounds(map.markers[0].transform.position, Vector3.zero);
                foreach (var marker in map.markers) bounds.Encapsulate(marker.transform.position);
                if (mobile)
                {
                    var anchors = Array.FindAll(map.markers, m => m.kind == ServerMapMarker.MarkerKind.Hub);
                    if (anchors.Length > 0)
                    {
                        bounds = new Bounds(anchors[0].transform.position, Vector3.zero);
                        foreach (var anchor in anchors) bounds.Encapsulate(anchor.transform.position);
                        bounds.Expand(new Vector3(160, 60, 160));
                    }
                }
                framingBounds = bounds;
                RefreshViewport();
                hubs = Array.FindAll(map.markers, m => m.kind == ServerMapMarker.MarkerKind.Hub);
                foreach (var hub in hubs)
                    CreateMapPin(hub.transform, hub.displayName, null, Color.white);
            }
            if (worldCamera != null)
            {
                if (crowdOverlay != null && map != null) crowdOverlay.Initialize(map, worldCamera);
                OperatorControls = gameObject.AddComponent<PcOperatorControls>();
                OperatorControls.Initialize(worldCamera, framingBounds, fittedSize, mobile ? 55 : 65, mobile ? 25 : 0);
                OperatorControls.InputEnabled = !mobile;
                foreach (var bound in OperatorControls.Vehicles)
                    if (!mobile && bound is IAutonomousVehicle target && !automaticClients.ContainsKey(target.OperatorVehicleId))
                    {
                        var client = gameObject.AddComponent<UnityAutonomousClient>();
                        automaticClients.Add(target.OperatorVehicleId, client);
                        client.Initialize(map, target);
                    }
            }
            if (OperatorControls != null)
            {
                int colorIndex = 0;
                foreach (var vehicle in OperatorControls.Vehicles)
                {
                    if (vehicle.VehicleTransform == null) continue;
                    if (mobile)
                    {
                        foreach (var renderer in vehicle.VehicleTransform.GetComponentsInChildren<Renderer>())
                            if (renderer.enabled) { mobileHiddenRenderers.Add(renderer); renderer.enabled = false; }
                        foreach (var body in vehicle.VehicleTransform.GetComponentsInChildren<Rigidbody>())
                            if (!mobileBodies.ContainsKey(body)) { mobileBodies.Add(body, body.isKinematic); body.isKinematic = true; }
                    }
                    int index = vehicle.OperatorVehicleId == "V01" ? 0 : vehicle.OperatorVehicleId == "V02" ? 1 : vehicle.OperatorVehicleId == "V03" ? 2 : colorIndex % 3;
                    CreateMapPin(vehicle.VehicleTransform, vehicle.OperatorVehicleId, vehicle.OperatorVehicleId, VehicleColors[index]);
                    colorIndex++;
                }
            }
            if (!showWorldMarkers)
                foreach (var worldCanvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (worldCanvas.renderMode == RenderMode.WorldSpace) worldCanvas.enabled = false;
            Debug.Log("CAMPUS_WEB_UI_READY role=" + (mobile ? "Mobile" : "PC") + " additive=true camera_navigation=" + !mobile);
        }

        private void RefreshViewport()
        {
            if (worldCamera == null || Screen.width <= 0 || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            var view = MapViewport;
            worldCamera.rect = new Rect((safe.x + view.x * safe.width) / Screen.width,
                (safe.y + view.y * safe.height) / Screen.height,
                view.width * safe.width / Screen.width, view.height * safe.height / Screen.height);
            float aspect = worldCamera.rect.width * Screen.width / (worldCamera.rect.height * Screen.height);
            worldCamera.aspect = aspect;
            float fit = 1;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        var corner = framingBounds.center + Vector3.Scale(framingBounds.extents, new Vector3(x, y, z));
                        var local = Quaternion.Inverse(Quaternion.Euler(mobile ? 55 : 65, mobile ? 25 : 0, 0)) * (corner - framingBounds.center);
                        fit = Mathf.Max(fit, Mathf.Abs(local.y), Mathf.Abs(local.x) / aspect);
                    }
            fittedSize = fit * (mobile ? 1.04f : 1.08f);
            if (OperatorControls != null) OperatorControls.Resize(fittedSize);
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            lastSafeArea = safe;
        }

        // The web dashboard grid: 64px header, 328px sidebar, 20px workspace inset,
        // 30/70/flexible/136px rows with 12px gaps. Only the live map remains transparent.
        private void BuildOperator(RectTransform root)
        {
            var paper = FixtureUiPalette.Canvas; paper.a = 1;
            var header = Top(root, "Header", 0, 64, 0, 0);
            Surface(header, Color.white, false);
            var logo = Resources.Load<Texture2D>("inha-mobil-logo");
            if (logo != null)
            {
                var icon = Top(header, "Logo", 12, 40, 24, 0);
                icon.anchorMax = icon.anchorMin = new Vector2(0, 1); icon.sizeDelta = new Vector2(40, 40);
                icon.anchoredPosition = new Vector2(44, -32);
                var raw = icon.gameObject.AddComponent<RawImage>(); raw.texture = logo; raw.raycastTarget = false;
            }
            var brand = TextAt(Top(header, "Brand", 0, 64, 76, 300), "Inha Mobil", 22, true);
            brand.color = FixtureUiPalette.Blue;
            session = TextAt(RightTop(header, "Version", 0, 80, 64, 24), $"v{Application.version}", 11);
            session.alignment = TextAnchor.MiddleRight; session.color = FixtureUiPalette.Muted;
            var serverUri = new Uri(map.serverUrl);
            var urls = ClientServerSettings.LanUrls(serverUri.Port);
            string address = !serverUri.IsLoopback ? map.serverUrl : urls.Length > 0 ? urls[0] : "LAN 주소 확인";
            var serverLabel = TextAt(RightTop(header, "Server Address", 0, 400, 64, 120), "서버 " + address, 11);
            serverLabel.alignment = TextAnchor.MiddleRight;
            serverLabel.color = FixtureUiPalette.Muted;

            var body = FixtureUiFactory.Rect(root, "Dashboard", Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(0, -64));
            var workspace = FixtureUiFactory.Rect(body, "Workspace", Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-328, 0));
            Surface(Top(workspace, "Workspace Top Background", 0, 184, 0, 0), paper, false);
            Surface(Bottom(workspace, "Workspace Bottom Background", 0, 190, 0, 0), paper, false);
            Surface(FixtureUiFactory.Rect(workspace, "Left Gutter", Vector2.zero, new Vector2(0, 1),
                Vector2.zero, new Vector2(20, 0)), paper, false);
            Surface(FixtureUiFactory.Rect(workspace, "Right Gutter", new Vector2(1, 0), Vector2.one,
                new Vector2(-20, 0), Vector2.zero), paper, false);
            TextAt(Top(workspace, "Title", 16, 30, 20, 260), "캠퍼스 관제", 23, true);
            connection = TextAt(Top(workspace, "Connection", 16, 30, 200, 20), "● 연결 대기", 12);
            connection.alignment = TextAnchor.MiddleRight;
            var stats = Top(workspace, "Summary", 58, 70, 20, 20);
            Surface(stats, Color.white);
            statCards = new Text[4];
            string[] statNames = { "제어 연결 차량", "대기 요청", "운송 완료", "충전 중" };
            for (int i = 0; i < 4; i++)
            {
                var cell = FixtureUiFactory.Rect(stats, "Metric " + i, new Vector2(i / 4f, 0),
                    new Vector2((i + 1) / 4f, 1), new Vector2(16, 8), new Vector2(-16, -8));
                TextAt(Top(cell, "Name", 0, 19, 0, 0), statNames[i], 12).color = FixtureUiPalette.Muted;
                statCards[i] = TextAt(Bottom(cell, "Value", 0, 32, 0, 0), "—", 24, true);
            }
            var mapHeader = Top(workspace, "Map Header", 140, 44, 20, 20);
            Surface(mapHeader, Color.white);
            TextAt(Top(mapHeader, "Map Title", 0, 44, 12, 752), "인하대학교 운행 지도", 13, true);
            BuildEnvironmentControls(mapHeader);
            // Right to left: weather choices (8..216), disconnect demo (224..360),
            // crowd prior buttons (368..744); the title keeps the remaining width.
            disconnectButton = UiButton(RightTop(mapHeader, "Disconnect Demo", 5, 136, 34, 224),
                "연결 끊김 시연", () => ActiveAutomaticClient?.ToggleDemoConnection());
            disconnectLabel = disconnectButton.GetComponentInChildren<Text>();
            desktopMap = FixtureUiFactory.Rect(workspace, "Map Viewport", Vector2.zero, Vector2.one,
                new Vector2(20, 248), new Vector2(-20, -184));
            desktopMap.gameObject.AddComponent<RectMask2D>();
            // Crowd prior sits below the vehicle routes. PC only; mobile never builds it.
            var crowdLayer = FixtureUiFactory.Rect(desktopMap, "Crowd Prior Roads", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            crowdOverlay = crowdLayer.gameObject.AddComponent<CampusCrowdOverlay>();
            crowdOverlay.raycastTarget = false;
            var crowdToggle = UiButton(RightTop(mapHeader, "Crowd Toggle", 7, 128, 30, 368), "혼잡 예상 표시", ToggleCrowd);
            crowdToggleLabel = crowdToggle.GetComponentInChildren<Text>();
            crowdToggleLabel.fontSize = 12;
            // Moves only the crowd prior's time of day; the shared simulation clock stays 1x.
            var nextPeak = UiButton(RightTop(mapHeader, "Crowd Next Peak", 7, 140, 30, 504), "다음 혼잡 시간대", NextCrowdPeak);
            nextPeak.GetComponentInChildren<Text>().fontSize = 12;
            var resetHolder = RightTop(mapHeader, "Crowd Reset Time", 7, 92, 30, 652);
            var resetTime = UiButton(resetHolder, "기본 시각", ResetCrowdTime);
            resetTime.GetComponentInChildren<Text>().fontSize = 12;
            crowdResetButton = resetHolder.gameObject;
            crowdResetButton.SetActive(false);
            routeOverlays = new CampusRouteOverlay[3];
            for (int i = 0; i < 3; i++)
            {
                var layer = FixtureUiFactory.Rect(desktopMap, "V0" + (i + 1) + " Planned Route", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                routeOverlays[i] = layer.gameObject.AddComponent<CampusRouteOverlay>();
                routeOverlays[i].color = VehicleColors[i];
                routeOverlays[i].raycastTarget = false;
            }
            var speedPanel = Bottom(desktopMap, "Selected Vehicle Speed", 12, 44, 12, 0);
            speedPanel.anchorMax = speedPanel.anchorMin = Vector2.zero;
            speedPanel.sizeDelta = new Vector2(190, 44);
            speedPanel.anchoredPosition = new Vector2(107, 34);
            selectedSpeed = TextAt(Top(speedPanel, "Speed", 0, 44, 12, 12), "", 20, true);
            selectedSpeed.color = Color.white;
            var speedOutline = selectedSpeed.gameObject.AddComponent<Outline>();
            speedOutline.effectColor = new Color(0, 0, 0, .85f);
            speedOutline.effectDistance = new Vector2(1, -1);
            var speedShadow = selectedSpeed.gameObject.AddComponent<Shadow>();
            speedShadow.effectColor = new Color(0, 0, 0, .35f);
            speedShadow.effectDistance = new Vector2(2, -2);
            speedPanel.gameObject.SetActive(false);
            var footer = Bottom(workspace, "Map Footer", 188, 60, 20, 20);
            Surface(footer, Color.white, false);
            pedestrianSpawnButton = UiButton(RightTop(footer, "Pedestrian Spawn", 4, 132, 30, 12),
                "보행자 등장", SpawnPedestrian);
            pedestrianSpawnButton.interactable = false;
            pedestrianSpawnButton.transform.parent.gameObject.SetActive(false);
            // Between the dispatch/planner selectors (12..396) and the pedestrian button.
            disconnectStatus = TextAt(Top(footer, "Vehicle Link State", 4, 30, 408, 152), "", 12, true);
            disconnectStatus.resizeTextForBestFit = true;
            disconnectStatus.resizeTextMinSize = 9;
            disconnectStatus.resizeTextMaxSize = 12;
            BuildCrowdLegend(RightTop(footer, "Crowd Legend", 34, 470, 24, 12));
            pedestrianStatus = TextAt(Top(footer, "Pedestrian Status", 38, 20, 12, 494),
                "차량을 추적하면 보행자 횡단을 실행할 수 있습니다", 12);
            pedestrianStatus.supportRichText = false;
            TextAt(Bottom(workspace, "Fleet Title", 156, 24, 20, 20), "차량 현황", 15, true);
            var fleetRow = Bottom(workspace, "Fleet", 16, 132, 20, 20);
            fleetCards = new Text[3]; fleetDetails = new Text[3]; fleetSurfaces = new Image[3];
            fleetRoutes = new Text[3];
            vehicleButtons = new Toggle[3];
            batteryIndicators = new Slider[3]; batteryLabels = new Text[3];
            var group = fleetRow.gameObject.AddComponent<ToggleGroup>(); group.allowSwitchOff = true;
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var card = FixtureUiFactory.Rect(fleetRow, "Vehicle V0" + (i + 1), new Vector2(i / 3f, 0),
                    new Vector2((i + 1) / 3f, 1), new Vector2(i == 0 ? 0 : 6, 0), new Vector2(i == 2 ? 0 : -6, 0));
                fleetSurfaces[i] = Surface(card, Color.white); fleetSurfaces[i].raycastTarget = true;
                TextAt(Top(card, "Vehicle ID", 10, 24, 14, 14), "V0" + (i + 1), 16, true).color = VehicleColors[i];
                fleetCards[i] = TextAt(Top(card, "Connection", 38, 22, 14, 14), "미연결", 12);
                fleetDetails[i] = TextAt(Top(card, "State", 58, 18, 14, 14), "—", 12);
                fleetRoutes[i] = TextAt(Top(card, "Route", 78, 30, 14, 14), "", 11);
                fleetRoutes[i].supportRichText = false;
                fleetRoutes[i].resizeTextForBestFit = true;
                fleetRoutes[i].resizeTextMinSize = 9;
                fleetRoutes[i].resizeTextMaxSize = 11;
                var track = Top(card, "Battery", 113, 7, 14, 68);
                BatterySurface(track, new Color(.87f, .90f, .94f));
                var fill = FixtureUiFactory.Rect(track, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                BatterySurface(fill, FixtureUiPalette.Green);
                batteryIndicators[i] = track.gameObject.AddComponent<Slider>();
                batteryIndicators[i].fillRect = fill;
                batteryIndicators[i].interactable = false;
                batteryIndicators[i].transition = Selectable.Transition.None;
                batteryLabels[i] = TextAt(RightTop(card, "Battery Percent", 106, 48, 20, 14), "—", 11);
                batteryLabels[i].alignment = TextAnchor.MiddleRight;
                vehicleButtons[i] = card.gameObject.AddComponent<Toggle>();
                vehicleButtons[i].targetGraphic = fleetSurfaces[i]; vehicleButtons[i].isOn = false;
                vehicleButtons[i].group = group;
                vehicleButtons[i].onValueChanged.AddListener(selected => {
                    if (OperatorControls == null) return;
                    if (selected) OperatorControls.SelectVehicle("V0" + (index + 1));
                    else if (!group.AnyTogglesOn()) OperatorControls.DeselectVehicle();
                });
            }

            var sidebar = FixtureUiFactory.Rect(body, "Sidebar", new Vector2(1, 0), Vector2.one,
                new Vector2(-328, 0), Vector2.zero);
            Surface(sidebar, paper, false);
            var tabs = Top(sidebar, "Request Tabs", 16, 32, 16, 16);
            var form = Top(sidebar, "Operation Log Card", 60, 360, 16, 16);
            var requestForm = Top(sidebar, "Request Card", 60, 360, 16, 16);
            BuildRequestForm(requestForm);
            var requestTab = FixtureUiFactory.Rect(tabs, "Request Tab", Vector2.zero, new Vector2(.5f, 1), Vector2.zero, new Vector2(-4, 0));
            var missionTab = FixtureUiFactory.Rect(tabs, "Operation Log Tab", new Vector2(.5f, 0), Vector2.one, new Vector2(4, 0), Vector2.zero);
            var tabGroup = tabs.gameObject.AddComponent<ToggleGroup>();
            tabGroup.allowSwitchOff = false;
            Surface(form, Color.white);
            TextAt(Top(form, "Heading", 16, 24, 16, 16), "운행 로그", 15, true);
            TextAt(Top(form, "Log Note", 44, 20, 16, 16), "수신한 상태 변경 · 최근 100건", 11).color = FixtureUiPalette.Muted;
            var logViewport = FixtureUiFactory.Rect(form, "Log Viewport", Vector2.zero, Vector2.one,
                new Vector2(16, 16), new Vector2(-16, -74));
            logViewport.gameObject.AddComponent<RectMask2D>();
            Surface(logViewport, Color.white).raycastTarget = true;
            var logContent = Top(logViewport, "Log Content", 0, 290, 0, 0);
            logContent.pivot = new Vector2(.5f, 1);
            operationLog = TextAt(logContent, "운행 이력 대기", 12);
            operationLog.alignment = TextAnchor.UpperLeft;
            operationLog.supportRichText = false;
            var scroll = logViewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = logViewport; scroll.content = logContent; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            CreateTab(requestTab, "운송 요청", tabGroup, requestForm, true);
            CreateTab(missionTab, "운행 로그", tabGroup, form, false);
            var policyCard = Top(footer, "Dispatch Policy", 4, 30, 12, 0);
            policyCard.anchorMax = policyCard.anchorMin = new Vector2(0, 1);
            policyCard.sizeDelta = new Vector2(384, 30);
            policyCard.anchoredPosition = new Vector2(204, -19);
            var dispatchColumn = FixtureUiFactory.Rect(policyCard, "Dispatch", Vector2.zero, new Vector2(.5f, 1), Vector2.zero, new Vector2(-4, 0));
            var plannerColumn = FixtureUiFactory.Rect(policyCard, "Planner", new Vector2(.5f, 0), Vector2.one, new Vector2(4, 0), Vector2.zero);
            dispatchStatus = TextAt(Top(dispatchColumn, "Policy Status", 0, 30, 0, 130), "배차 방식", 11, true);
            var policyRow = Top(dispatchColumn, "Policy Select", 0, 30, 58, 8);
            var policyControl=DefaultControls.CreateDropdown(new DefaultControls.Resources());
            policyControl.name="Dispatch Policy Dropdown";policyControl.transform.SetParent(policyRow,false);
            var policyRect=(RectTransform)policyControl.transform;
            policyRect.anchorMin=Vector2.zero;policyRect.anchorMax=Vector2.one;policyRect.offsetMin=policyRect.offsetMax=Vector2.zero;
            dispatchDropdown=policyControl.GetComponent<Dropdown>();
            dispatchDropdown.ClearOptions();dispatchDropdown.AddOptions(new List<string>{"FIFO","Greedy"});
            StyleDropdown(dispatchDropdown, 84);
            dispatchDropdown.onValueChanged.AddListener(index=>StartCoroutine(ChangeDispatch(index==0?"fifo":"greedy")));
            TextAt(Top(plannerColumn, "Planner Title", 0, 30, 0, 130), "경로 탐색", 11, true);
            var plannerControl = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            plannerControl.name = "Route Planner Dropdown";
            plannerControl.transform.SetParent(Top(plannerColumn, "Planner Select", 0, 30, 58, 0), false);
            var plannerRect = (RectTransform)plannerControl.transform;
            plannerRect.anchorMin = Vector2.zero; plannerRect.anchorMax = Vector2.one;
            plannerRect.offsetMin = plannerRect.offsetMax = Vector2.zero;
            plannerDropdown = plannerControl.GetComponent<Dropdown>();
            plannerDropdown.ClearOptions(); plannerDropdown.AddOptions(new List<string> { "A*", "Dijkstra" });
            StyleDropdown(plannerDropdown, 84);
            plannerDropdown.onValueChanged.AddListener(index => StartCoroutine(ChangePlanner(index == 0 ? "astar" : "dijkstra")));
            var queueCard = FixtureUiFactory.Rect(sidebar, "Queue Card", Vector2.zero, Vector2.one,
                new Vector2(16, 16), new Vector2(-16, -432));
            Surface(queueCard, Color.white);
            TextAt(Top(queueCard, "Queue Title", 8, 22, 16, 16), "요청 대기열", 15, true);
            queue = TextAt(Top(queueCard, "Queue Status", 32, 28, 16, 16), "연결 대기", 12);
            queue.color = FixtureUiPalette.Muted;
            queueViewport = FixtureUiFactory.Rect(queueCard, "Queue Viewport", Vector2.zero, Vector2.one,
                new Vector2(16, 12), new Vector2(-16, -64));
            queueViewport.gameObject.AddComponent<RectMask2D>();
            Surface(queueViewport, Color.white).raycastTarget = true;
            queueContent = Top(queueViewport, "Queue Content", 0, 0, 0, 0);
            queueContent.pivot = new Vector2(.5f, 1);
            queueDetails = TextAt(queueContent, "서버 연결 후 대기 요청을 표시합니다", 12);
            queueDetails.alignment = TextAnchor.UpperLeft;
            queueDetails.supportRichText = false;
            var queueScroll = queueViewport.gameObject.AddComponent<ScrollRect>();
            queueScroll.viewport = queueViewport; queueScroll.content = queueContent; queueScroll.horizontal = false;
            queueScroll.movementType = ScrollRect.MovementType.Clamped;
        }

        private void BuildEnvironmentControls(RectTransform panel)
        {
            weatherToggles = CreateWeatherChoices(RightTop(panel, "Weather Choices", 3, 208, 38, 8));
            RefreshEnvironmentControls();
        }

        private void SpawnPedestrian()
        {
            if (OperatorControls == null || !OperatorControls.IsFollowing) return;
            var selected = OperatorControls?.SelectedVehicle;
            if (selected == null) return;
            var message = PedestrianScenarioActions.SpawnAhead?.Invoke(selected);
            pedestrianFeedback = message ?? "보행자 기능을 준비하고 있습니다";
            pedestrianFeedbackVehicle = selected.OperatorVehicleId;
            pedestrianFeedbackUntil = Time.unscaledTime + 8;
            RefreshEnvironmentControls();
        }

        private Toggle[] CreateWeatherChoices(RectTransform groupRoot)
        {
            var group = groupRoot.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            var toggles = new Toggle[WeatherKeys.Length];
            for (int i = 0; i < WeatherKeys.Length; i++)
            {
                int index = i;
                var rect = FixtureUiFactory.Rect(groupRoot, "Weather " + WeatherKeys[i],
                    new Vector2(i / 4f, 0), new Vector2((i + 1f) / 4, 1),
                    new Vector2(2, 0), new Vector2(-2, 0));
                var background = Surface(rect, FixtureUiPalette.Canvas); background.raycastTarget = true;
                var iconRect = Top(rect, "Icon", 2, 20, 14, 14);
                var icon = iconRect.gameObject.AddComponent<OperatorWeatherIcon>();
                icon.Weather = WeatherKeys[i]; icon.raycastTarget = false;
                var label = TextAt(Bottom(rect, "Caption", 1, 14, 0, 0), WeatherLabels[i], 10, true);
                label.alignment = TextAnchor.MiddleCenter;
                var toggle = rect.gameObject.AddComponent<Toggle>();
                toggle.SetIsOnWithoutNotify(false);
                toggle.group = group; toggle.targetGraphic = background;
                toggle.transition = Selectable.Transition.None;
                toggle.onValueChanged.AddListener(on => {
                    if (!on) return;
                    PhysicalLidarSource.ApplyEnvironment?.Invoke(WeatherKeys[index], WeatherRoads[index]);
                    RefreshEnvironmentControls();
                });
                toggles[i] = toggle;
            }
            return toggles;
        }

        private void RefreshEnvironmentControls()
        {
            if (pedestrianSpawnButton != null)
            {
                bool following = OperatorControls != null && OperatorControls.IsFollowing &&
                    OperatorControls.SelectedVehicle != null;
                pedestrianSpawnButton.transform.parent.gameObject.SetActive(following);
                pedestrianSpawnButton.interactable = following && PedestrianScenarioActions.SpawnAhead != null;
                if (pedestrianStatus != null)
                    pedestrianStatus.text = !following ? "차량을 추적하면 보행자 횡단을 실행할 수 있습니다" :
                        Time.unscaledTime < pedestrianFeedbackUntil && pedestrianFeedbackVehicle == OperatorControls.SelectedVehicle.OperatorVehicleId ? pedestrianFeedback :
                        PedestrianScenarioActions.ReadStatus?.Invoke() ?? "차량 앞 도로에 보행자를 등장시킵니다";
            }
            var state = PhysicalLidarSource.ReadEnvironment?.Invoke();
            bool available = state != null && PhysicalLidarSource.ApplyEnvironment != null;
            UpdateEnvironmentChoices(weatherToggles, WeatherKeys, state?.weather, available);
        }

        private void RefreshVehicleReadout()
        {
            if (selectedSpeed == null) return;
            var vehicle = OperatorControls?.SelectedVehicle;
            bool visible = vehicle != null && OperatorControls.IsFollowing;
            selectedSpeed.transform.parent.parent.gameObject.SetActive(visible);
            if (!visible) return;
            var body = vehicle.VehicleBody;
            selectedSpeed.text =
                (body != null ? (body.linearVelocity.magnitude * 3.6f).ToString("0") : "—") + " km/h";
        }

        private void RefreshQueue(CampusServerMap.SharedState state, bool online)
        {
            if (queueDetails == null) return;
            var orders = online ? state?.queue : null;
            if (queue != null) queue.text = !online ? "연결 대기" : orders == null || orders.Length == 0 ? "대기 없음" : $"대기 요청 {orders.Length}건";
            var lines = new StringBuilder();
            if (!online) lines.Append("서버 연결 후 대기 요청을 표시합니다");
            else if (orders == null || orders.Length == 0) lines.Append("현재 대기 중인 요청이 없습니다");
            else for (int i = 0; i < orders.Length; i++)
            {
                var order = orders[i];
                if (order == null) continue;
                lines.Append(i + 1).Append(". ").Append(order.id).Append(" · 가용 차량 대기\n")
                    .Append(LandmarkName(order.pickup_landmark_id)).Append(" → ")
                    .Append(LandmarkName(order.dropoff_landmark_id)).Append("\n\n");
            }
            string details = lines.ToString();
            if (queueDetails.text != details)
            {
                queueDetails.text = details;
                queueContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Max(queueViewport.rect.height, queueDetails.preferredHeight));
            }
        }

        private static void UpdateEnvironmentChoices(Toggle[] toggles, string[] keys, string selected, bool available)
        {
            if (toggles == null) return;
            // Set the active choice first so group notification cannot clear it later.
            int active = Array.IndexOf(keys, selected);
            if (active >= 0) toggles[active].SetIsOnWithoutNotify(true);
            for (int i = 0; i < toggles.Length; i++)
            {
                bool on = i == active;
                toggles[i].SetIsOnWithoutNotify(on);
                toggles[i].interactable = available;
                toggles[i].GetComponent<Image>().color = on ? FixtureUiPalette.Blue : FixtureUiPalette.Canvas;
                toggles[i].GetComponentInChildren<Text>().color = on ? Color.white : FixtureUiPalette.Muted;
                toggles[i].GetComponentInChildren<OperatorWeatherIcon>().color = on ? Color.white : FixtureUiPalette.Muted;
            }
        }

        private static void CreateTab(RectTransform rect, string title, ToggleGroup group, RectTransform panel, bool initial)
        {
            var background = Surface(rect, FixtureUiPalette.Canvas); background.raycastTarget = true;
            var label = TextAt(rect, title, 13, true); label.alignment = TextAnchor.MiddleCenter;
            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.SetIsOnWithoutNotify(false);
            toggle.group = group; toggle.targetGraphic = background;
            toggle.transition = Selectable.Transition.None;
            toggle.onValueChanged.AddListener(on => {
                panel.gameObject.SetActive(on);
                background.color = on ? FixtureUiPalette.Blue : FixtureUiPalette.Canvas;
                label.color = on ? Color.white : FixtureUiPalette.Muted;
            });
            toggle.isOn = initial;
            toggle.onValueChanged.Invoke(initial);
        }

        private static void StyleDropdown(Dropdown dropdown, float height)
        {
            var background = dropdown.GetComponent<Image>();
            background.sprite = RoundedSprite(); background.type = Image.Type.Sliced;
            background.color = FixtureUiPalette.Canvas;
            var colors = dropdown.colors;
            colors.highlightedColor = new Color(.90f, .94f, 1);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.80f, .87f, 1);
            dropdown.colors = colors;
            foreach (var text in dropdown.GetComponentsInChildren<Text>(true))
            { text.font = FixtureUiFactory.Font; text.fontSize = 13; text.color = FixtureUiPalette.Ink; text.raycastTarget = false; }
            dropdown.captionText.rectTransform.offsetMin = new Vector2(12, 4);
            dropdown.captionText.rectTransform.offsetMax = new Vector2(-32, -4);
            var arrow = dropdown.transform.Find("Arrow");
            arrow.GetComponent<Image>().enabled = false;
            FixtureUiFactory.Text(arrow, "Chevron", "▾", 18, FixtureUiPalette.Muted, TextAnchor.MiddleCenter);
            dropdown.template.sizeDelta = new Vector2(0, height);
            var popup = dropdown.template.GetComponent<Image>();
            popup.sprite = RoundedSprite(); popup.type = Image.Type.Sliced; popup.color = Color.white;
            var shadow = dropdown.template.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.06f, .14f, .25f, .18f); shadow.effectDistance = new Vector2(0, -3);
            var item = dropdown.itemText.GetComponentInParent<Toggle>(true);
            var itemBackground = item.targetGraphic as Image;
            if (itemBackground != null)
            { itemBackground.sprite = RoundedSprite(); itemBackground.type = Image.Type.Sliced; itemBackground.color = Color.white; }
            var itemColors = item.colors;
            itemColors.normalColor = Color.white;
            itemColors.highlightedColor = new Color(.91f, .95f, 1);
            itemColors.selectedColor = itemColors.highlightedColor;
            itemColors.pressedColor = new Color(.82f, .90f, 1);
            item.colors = itemColors;
            if (item.graphic != null)
            {
                var old = item.graphic; old.enabled = false;
                item.graphic = FixtureUiFactory.Text(old.transform, "Selected", "✓", 14, FixtureUiPalette.Blue, TextAnchor.MiddleCenter);
            }
            var itemRect = (RectTransform)item.transform;
            itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, 36);
            var content = (RectTransform)itemRect.parent;
            content.sizeDelta = new Vector2(content.sizeDelta.x, 40);
            var scrollbar = dropdown.template.GetComponentInChildren<Scrollbar>(true);
            if (scrollbar != null)
            {
                var track = scrollbar.GetComponent<Image>(); track.color = FixtureUiPalette.Canvas;
                var handle = scrollbar.targetGraphic as Image;
                if (handle != null) { handle.sprite = RoundedSprite(); handle.type = Image.Type.Sliced; handle.color = new Color(.70f, .76f, .84f); }
            }
        }

        private void RefreshOperationLog(CampusServerMap.SharedState state)
        {
            if (operationLog == null) return;
            bool changed = false;
            if (logRun != state.run_id)
            { logRun = state.run_id; observedStates.Clear(); logLines.Clear(); changed = true; }
            if (state.vehicles != null) foreach (var vehicle in state.vehicles)
                changed |= ObserveLog("vehicle:" + vehicle.id, vehicle.phase + "|" + vehicle.destination,
                    VehicleLogMessage(vehicle), state.time_s);
            if (state.requests != null) foreach (var request in state.requests)
                changed |= ObserveLog("request:" + request.id, request.status + "|" + request.vehicle_id,
                    RequestLogMessage(request), state.time_s);
            if (!changed) return;
            operationLog.text = string.Join("\n\n", logLines);
            ((RectTransform)operationLog.transform.parent).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(290, operationLog.preferredHeight + 8));
        }

        private string VehicleLogMessage(CampusServerMap.Vehicle vehicle)
        {
            string destination = string.IsNullOrEmpty(vehicle.destination) ? "목적지" : LandmarkName(vehicle.destination);
            string description = vehicle.phase switch
            {
                "READY" => "배차 대기",
                "TO_PICKUP" => $"픽업 {destination} 방면 이동",
                "PICKUP_SERVICE" => "출발지 탑승/적재",
                "TO_DROPOFF" => $"{destination} 방면 운송",
                "DROPOFF_SERVICE" => "도착지 하차/하역",
                "RETURN_PENDING" => "복귀 준비",
                "RETURNING" => (string.IsNullOrEmpty(vehicle.home_name) ? "차고지" : vehicle.home_name) + " 방면 복귀",
                "TO_CHARGER" => "충전소 이동",
                "CHARGING" => "배터리 충전",
                "MANEUVER" => "방향 전환",
                "OUT_OF_SERVICE" => "운행 중단",
                _ => "운행 상태 변경"
            };
            return vehicle.id + " " + description;
        }

        private string RequestLogMessage(CampusServerMap.TransportRequest request)
        {
            string vehicle = string.IsNullOrEmpty(request.vehicle_id) ? "차량" : request.vehicle_id + " 차량";
            string description = request.status switch
            {
                "CREATED" => "요청 접수",
                "VALIDATED" => "운송 조건 확인",
                "QUEUED" => "배차 대기",
                "ASSIGNED" => vehicle + " 배정",
                "PICKUP_SERVICE" => "출발지 탑승/적재",
                "IN_TRANSIT" => "목적지 이동",
                "DROPOFF_SERVICE" => "도착지 하차/하역",
                "COMPLETED" => "운송 완료",
                "REJECTED" => "요청 거절",
                "CANCELLED" => "요청 취소",
                "EXPIRED" => "요청 기한 만료",
                "FAILED" => "운송 실패 운영자 확인 필요",
                _ => "요청 상태 변경"
            };
            return $"요청 {request.id} {LandmarkName(request.pickup_landmark_id)} → {LandmarkName(request.dropoff_landmark_id)}\n{description}";
        }

        private bool ObserveLog(string key, string value, string message, double time)
        {
            if (observedStates.TryGetValue(key, out var previous) && previous == value) return false;
            observedStates[key] = value;
            logLines.Insert(0, $"[{TimeSpan.FromSeconds(Math.Max(0, time)):hh\\:mm\\:ss}] {message}");
            if (logLines.Count > 100) logLines.RemoveAt(logLines.Count - 1);
            return true;
        }

        private void BuildRequestForm(RectTransform form)
        {
            Surface(form, Color.white);
            TextAt(Top(form, "Heading", 16, 24, 16, 16), "새 운송 요청", 15, true);
            TextAt(Top(form, "Pickup Label", 52, 18, 16, 16), "출발 거점", 12).color = FixtureUiPalette.Muted;
            CreateStopDropdown(Top(form, "Pickup", 76, 40, 16, 16), "Request Pickup", "출발지 선택", 0, 0, 1, 1, stop => pickup = stop);
            TextAt(Top(form, "Dropoff Label", 130, 18, 16, 16), "도착 거점", 12).color = FixtureUiPalette.Muted;
            CreateStopDropdown(Top(form, "Dropoff", 154, 40, 16, 16), "Request Dropoff", "도착지 선택", 0, 0, 1, 1, stop => dropoff = stop);
            var row = Top(form, "Service and Load", 218, 36, 16, 16);
            var service = FixtureUiFactory.Rect(row, "Service", Vector2.zero, new Vector2(.5f, 1), Vector2.zero, new Vector2(-4, 0));
            requestService = UiButton(service, "승객 이동", () => {
                if (submitting || pendingRequestJson != null) return;
                cargoRequest = !cargoRequest; requestAmount = 1; UpdateRequestLoad();
            }).GetComponentInChildren<Text>();
            var minus = FixtureUiFactory.Rect(row, "Less", new Vector2(.5f, 0), new Vector2(.64f, 1), Vector2.zero, Vector2.zero);
            var amount = FixtureUiFactory.Rect(row, "Amount", new Vector2(.64f, 0), new Vector2(.86f, 1), Vector2.zero, Vector2.zero);
            var plus = FixtureUiFactory.Rect(row, "More", new Vector2(.86f, 0), Vector2.one, Vector2.zero, Vector2.zero);
            requestLoad = TextAt(amount, "1명", 12); requestLoad.alignment = TextAnchor.MiddleCenter;
            UiButton(minus, "−", () => ChangeRequestAmount(-1));
            UiButton(plus, "+", () => ChangeRequestAmount(1));
            requestSubmit = UiButton(Top(form, "Submit Request", 270, 42, 16, 16), "요청 접수", () => StartCoroutine(SubmitTransport()), true);
            requestStatus = TextAt(Top(form, "Request Status", 322, 26, 16, 16), "", 12);
            requestStatus.color = FixtureUiPalette.Muted;
        }

        private IEnumerator ChangeDispatch(string policy)
        {
            if(changingDispatch || string.IsNullOrEmpty(map?.RunId)) yield break;
            changingDispatch = true;
            using(var request = new UnityWebRequest(map.serverUrl.TrimEnd('/')+"/v1/demo/control", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new DispatchControl{dispatch=policy})));
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 5;
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) requestStatus.text = "배차 방식 변경 실패 · 다시 선택하세요";
            }
            changingDispatch = false;
        }

        private IEnumerator ChangePlanner(string planner)
        {
            if (changingPlanner || string.IsNullOrEmpty(map?.RunId)) yield break;
            changingPlanner = true;
            using (var request = new UnityWebRequest(map.serverUrl.TrimEnd('/') + "/v1/demo/control", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new PlannerControl { planner = planner })));
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 5;
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) requestStatus.text = "경로 탐색 변경 실패 · 다시 선택하세요";
            }
            changingPlanner = false;
        }

        private void ChangeRequestAmount(int delta)
        {
            if (submitting || pendingRequestJson != null) return;
            requestAmount = Mathf.Clamp(requestAmount + delta, 1, cargoRequest ? 100 : 4);
            UpdateRequestLoad();
        }
        private void UpdateRequestLoad()
        {
            requestService.text = cargoRequest ? "물품 배송" : "승객 이동";
            requestLoad.text = requestAmount + (cargoRequest ? "kg" : "명");
        }
        private IEnumerator SubmitTransport()
        {
            if (submitting || map == null || string.IsNullOrEmpty(map.RunId) || !string.IsNullOrEmpty(map.LastError)) yield break;
            if (pendingRequestJson == null)
            {
                if (pickup == null || dropoff == null || string.IsNullOrEmpty(pickup.landmark_id) || string.IsNullOrEmpty(dropoff.landmark_id) || pickup.landmark_id == dropoff.landmark_id)
                { requestStatus.text = "출발과 도착 거점을 다르게 선택하세요"; yield break; }
                pendingRequestRun = map.RunId;
                pendingRequestJson = JsonUtility.ToJson(new TransportOrder { command_id = Guid.NewGuid().ToString("N"),
                    owner_id = mobile ? passengerId : "unity-operator",
                    service_type = cargoRequest ? "CARGO" : "PASSENGER", pickup_landmark_id = pickup.landmark_id,
                    dropoff_landmark_id = dropoff.landmark_id, party_size = cargoRequest ? 0 : requestAmount,
                    cargo_kg = cargoRequest ? requestAmount : 0,
                    service_needs = new TransportNeeds { requires_step_free = mobileStepFree != null && mobileStepFree.isOn,
                        boarding_assistance = mobileStepFree != null && mobileStepFree.isOn } });
                if (mobile)
                {
                    PlayerPrefs.SetString("InhaMobil.Passenger.Pending", JsonUtility.ToJson(new PendingPassengerRequest { server = map.serverUrl, run = pendingRequestRun, json = pendingRequestJson }));
                    PlayerPrefs.Save();
                }
            }
            submitting = true; submittedRequestId = null; requestStatus.text = "요청 접수 중…";
            using (var request = new UnityWebRequest(map.serverUrl.TrimEnd('/') + "/v1/demo/requests", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(pendingRequestJson));
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 5;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-Campus-Run-Id", pendingRequestRun);
                yield return request.SendWebRequest();
                if (map.RunId != pendingRequestRun)
                { ClearPendingRequest(); requestStatus.text = "서버 세션이 바뀌었습니다. 요청 상태를 확인하세요"; }
                else if (request.result == UnityWebRequest.Result.Success)
                {
                    TransportAck ack = null;
                    try { ack = JsonUtility.FromJson<TransportAck>(request.downloadHandler.text); } catch (ArgumentException) { }
                    if (ack?.request != null && !string.IsNullOrEmpty(ack.request.id))
                    {
                        submittedRequestId = ack.request.id; ClearPendingRequest();
                        requestStatus.text = TransportStatus(ack.request);
                    }
                    else requestStatus.text = "접수 응답을 확인하지 못했습니다. 같은 요청으로 재시도하세요";
                }
                else if (request.responseCode >= 400 && request.responseCode < 500)
                { ClearPendingRequest(); requestStatus.text = "요청이 거부됐습니다. 거점과 적재 인원을 확인하세요"; }
                else requestStatus.text = "접수 확인 실패 · 다시 누르면 같은 요청으로 확인합니다";
            }
            submitting = false;
        }
        private void ClearPendingRequest()
        {
            pendingRequestJson = null;
            if (mobile) { PlayerPrefs.DeleteKey("InhaMobil.Passenger.Pending"); PlayerPrefs.Save(); }
        }

        [Serializable] private sealed class PassengerCancel { public string command_id, owner_id; }
        private IEnumerator CancelPassengerRequest()
        {
            if (submitting || submittedRequestId == null || map.LatestState == null || !string.IsNullOrEmpty(map.LastError)) yield break;
            submitting = true;
            string id = submittedRequestId;
            using (var request = new UnityWebRequest(map.serverUrl + "/v1/demo/passengers/requests/" + Uri.EscapeDataString(id) + "/cancel", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new PassengerCancel { command_id = "cancel-" + id, owner_id = passengerId })));
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 5;
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();
                requestStatus.text = request.result == UnityWebRequest.Result.Success ? "요청 취소됨" : "취소 확인 실패 · 요청 상태를 확인하세요";
            }
            submitting = false;
        }
        private void MobilePrimaryAction()
        {
            if (submitting) return;
            var current = map.LatestState?.requests == null ? null : Array.Find(map.LatestState.requests, r => r.id == submittedRequestId && r.owner_id == passengerId);
            if (pendingRequestJson == null && current != null && (current.status == "QUEUED" || current.status == "ASSIGNED"))
                StartCoroutine(CancelPassengerRequest());
            else if (pendingRequestJson != null || current == null || IsTerminal(current.status))
                StartCoroutine(SubmitTransport());
        }
        private static string TransportStatus(CampusServerMap.TransportRequest request)
        {
            string phase = request.status switch {
                "QUEUED" => "가용 차량 대기", "ASSIGNED" => "출발 거점으로 이동 중", "PICKUP_SERVICE" => "승차·상차 중",
                "IN_TRANSIT" => "목적지로 이동 중", "DROPOFF_SERVICE" => "하차·하역 중", "COMPLETED" => "운송 완료",
                "REJECTED" => "요청 거부", "CANCELLED" => "요청 취소", "FAILED" => "운송 실패", "EXPIRED" => "요청 만료", _ => "요청 접수" };
            return string.IsNullOrEmpty(request.vehicle_id) ? phase : request.vehicle_id + " · " + phase;
        }
        private static bool IsTerminal(string status) => status == "COMPLETED" || status == "REJECTED" || status == "CANCELLED" || status == "FAILED" || status == "EXPIRED";

        private static RectTransform Top(Transform parent, string name, float top, float height, float left, float right) =>
            FixtureUiFactory.Rect(parent, name, new Vector2(0, 1), Vector2.one,
                new Vector2(left, -top - height), new Vector2(-right, -top));
        private static RectTransform Bottom(Transform parent, string name, float bottom, float height, float left, float right) =>
            FixtureUiFactory.Rect(parent, name, Vector2.zero, new Vector2(1, 0),
                new Vector2(left, bottom), new Vector2(-right, bottom + height));
        private static RectTransform RightTop(Transform parent, string name, float top, float width, float height, float right) =>
            FixtureUiFactory.Rect(parent, name, Vector2.one, Vector2.one,
                new Vector2(-right - width, -top - height), new Vector2(-right, -top));
        private static Text TextAt(RectTransform rect, string value, int size, bool bold = false) =>
            FixtureUiFactory.Text(rect, "Label", value, size, FixtureUiPalette.Ink, TextAnchor.MiddleLeft,
                bold ? FontStyle.Bold : FontStyle.Normal);
        private static Button UiButton(RectTransform rect, string value, UnityEngine.Events.UnityAction action, bool primary = false)
        {
            var button = FixtureUiFactory.Button(rect, "Button", value, Color.white, out var text);
            var image = button.GetComponent<Image>();
            image.sprite = RoundedSprite(); image.type = Image.Type.Sliced;
            image.color = primary ? FixtureUiPalette.Blue : FixtureUiPalette.Canvas;
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(.9f, .94f, 1);
            colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.8f, .87f, 1);
            colors.disabledColor = new Color(.75f, .78f, .82f, .65f); button.colors = colors;
            text.fontSize = 13; text.color = primary ? Color.white : FixtureUiPalette.Ink;
            button.onClick.AddListener(action);
            return button;
        }
        private static Image Surface(RectTransform rect, Color color, bool roundedCorners = true)
        {
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
            if (roundedCorners) { image.sprite = RoundedSprite(); image.type = Image.Type.Sliced; }
            return image;
        }

        private static Sprite batteryCapsule;
        private static Image BatterySurface(RectTransform rect, Color color)
        {
            const int size = 64;
            const float barHeight = 7f;
            if (batteryCapsule == null)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Battery capsule",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * (size * .5f));
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(size * .5f - distance)));
                }
                texture.Apply(false, true);
                batteryCapsule = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f,
                    100, 0, SpriteMeshType.FullRect, Vector4.one * (size * .5f));
            }
            var image = Surface(rect, color, false);
            image.sprite = batteryCapsule;
            image.type = Image.Type.Sliced;
            // Match both end caps to half the bar height; stretch only the middle.
            image.pixelsPerUnitMultiplier = size / barHeight;
            return image;
        }

        private void BuildCrowdLegend(RectTransform legend)
        {
            var caution = FixtureUiFactory.Rect(legend, "Caution Swatch", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, -2), new Vector2(18, 2));
            Surface(caution, CampusCrowdOverlay.CautionColor, false);
            var cautionText = TextAt(FixtureUiFactory.Rect(legend, "Caution Label", new Vector2(0, 0), new Vector2(0, 1), new Vector2(22, 0), new Vector2(66, 0)), "혼잡 주의", 10);
            cautionText.color = FixtureUiPalette.Muted;
            for (int i = 0; i < 3; i++)
            {
                var dash = FixtureUiFactory.Rect(legend, "Avoid Dash " + i, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(72 + i * 7, -2), new Vector2(77 + i * 7, 2));
                Surface(dash, CampusCrowdOverlay.AvoidColor, false);
            }
            var avoidText = TextAt(FixtureUiFactory.Rect(legend, "Avoid Label", new Vector2(0, 0), new Vector2(0, 1), new Vector2(96, 0), new Vector2(140, 0)), "혼잡 회피", 10);
            avoidText.color = FixtureUiPalette.Muted;
            crowdStatus = TextAt(FixtureUiFactory.Rect(legend, "Crowd Status", Vector2.zero, Vector2.one, new Vector2(146, 0), Vector2.zero), "혼잡 예상: 도로 형상 대기", 10);
            crowdStatus.color = FixtureUiPalette.Muted;
            crowdStatus.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        private void NextCrowdPeak()
        {
            if (crowdOverlay == null) return;
            if (!crowdOverlay.Visible) ToggleCrowd();
            StartCoroutine(crowdOverlay.ChangeTime("next_peak"));
        }

        private void ResetCrowdTime()
        {
            if (crowdOverlay != null) StartCoroutine(crowdOverlay.ChangeTime("reset"));
        }

        private void ToggleCrowd()
        {
            if (crowdOverlay == null) return;
            crowdOverlay.Visible = !crowdOverlay.Visible;
            crowdToggleLabel.text = crowdOverlay.Visible ? "혼잡 예상 표시" : "혼잡 예상 숨김";
        }

        private Text queue;
        private Text[] statCards;
        private void Zoom(float factor)
        {
            if(OperatorControls!=null) { OperatorControls.Zoom(factor); return; }
        }
        private void ResetView() => OperatorControls?.DeselectVehicle();
        private static void ActionButton(RectTransform root,string name,string value,float x,float y,float right,float top,UnityEngine.Events.UnityAction action)
        {
            var holder=FixtureUiFactory.Rect(root,name+" Bounds",new Vector2(x,y),new Vector2(right,top),Vector2.zero,Vector2.zero);
            var button=FixtureUiFactory.Button(holder,name,value,FixtureUiPalette.Surface,out var label);
            label.color=FixtureUiPalette.Ink; label.fontSize=value.Length>2?12:22;
            button.onClick.AddListener(action);
        }
        private Text[] fleetCards;
        private void CreateStopDropdown(RectTransform root, string name, string placeholder,
            float x, float y, float right, float top, Action<ServerMapAnchor> selected = null)
        {
            var holder = FixtureUiFactory.Rect(root, name + " Bounds", new Vector2(x, y),
                new Vector2(right, top), Vector2.zero, Vector2.zero);
            var control = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            control.name = name;
            control.transform.SetParent(holder, false);
            var rect = (RectTransform)control.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var dropdown = control.GetComponent<Dropdown>();
            StyleDropdown(dropdown, mobile ? 264 : 224);
            if (mobile)
            {
                dropdown.captionText.fontSize = 15; dropdown.itemText.fontSize = 15;
                var item = dropdown.itemText.GetComponentInParent<Toggle>(true).GetComponent<RectTransform>();
                item.sizeDelta = new Vector2(item.sizeDelta.x, 44);
            }
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string> { placeholder });
            dropdown.interactable = false;
            dropdown.onValueChanged.AddListener(index => selected?.Invoke(
                dropdownStops != null && index > 0 && index <= dropdownStops.Length ? dropdownStops[index - 1] : null));
            stopDropdowns.Add(dropdown);
        }

        private void RefreshStopDropdowns()
        {
            var stops = map.Document?.stops;
            if (ReferenceEquals(stops, dropdownStops)) return;
            var previous = dropdownStops;
            dropdownStops = stops;
            foreach (var dropdown in stopDropdowns)
            {
                string selectedId = previous != null && dropdown.value > 0 && dropdown.value <= previous.Length
                    ? previous[dropdown.value - 1].id : null;
                string placeholder = dropdown.options[0].text;
                dropdown.Hide();
                dropdown.ClearOptions();
                var options = new List<string> { placeholder };
                if (stops != null) foreach (var stop in stops) options.Add(stop.name);
                dropdown.AddOptions(options);
                int index = selectedId == null || stops == null ? -1 : Array.FindIndex(stops, stop => stop.id == selectedId);
                dropdown.SetValueWithoutNotify(index + 1);
                dropdown.RefreshShownValue();
                dropdown.interactable = stops != null && stops.Length > 0;
                dropdown.onValueChanged.Invoke(dropdown.value);
            }
        }

        private void BuildMobile(RectTransform root)
        {
            Panel(root, "Sheet Background", 0, 0, 1, .60f, FixtureUiPalette.Canvas);
            var viewport = FixtureUiFactory.Rect(root, "Passenger Sheet Viewport", new Vector2(0,.045f), new Vector2(1,.60f), Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<Image>().color = FixtureUiPalette.Surface;
            viewport.gameObject.AddComponent<RectMask2D>();
            var sheet = FixtureUiFactory.Rect(viewport, "Passenger Sheet", new Vector2(0,1), Vector2.one, Vector2.zero, Vector2.zero);
            sheet.pivot = new Vector2(.5f,1); sheet.sizeDelta = new Vector2(0,548);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = sheet; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24;
            TextAt(Top(sheet,"Title",18,30,20,20),"어디로 이동할까요?",22,true);
            TextAt(Top(sheet,"Subtitle",50,22,20,20),"출발지와 목적지를 선택하세요",12).color = FixtureUiPalette.Muted;
            CreateStopDropdown(Top(sheet,"Pickup Row",84,52,20,20),"Request Pickup","출발지 선택",0,0,1,1, stop => pickup = stop);
            CreateStopDropdown(Top(sheet,"Dropoff Row",146,52,20,20),"Request Dropoff","목적지 선택",0,0,1,1, stop => dropoff = stop);
            var needs = DefaultControls.CreateToggle(new DefaultControls.Resources());
            needs.name = "Step Free Access"; needs.transform.SetParent(Top(sheet,"Access Row",204,44,20,20), false);
            var needsRect = (RectTransform)needs.transform;
            needsRect.anchorMin = Vector2.zero; needsRect.anchorMax = Vector2.one;
            needsRect.offsetMin = needsRect.offsetMax = Vector2.zero;
            mobileStepFree = needs.GetComponent<Toggle>(); mobileStepFree.isOn = false;
            mobileStepFree.targetGraphic.color = new Color(.82f,.87f,.94f);
            mobileStepFree.graphic.color = FixtureUiPalette.Blue;
            var needsText = needs.GetComponentInChildren<Text>(); needsText.font = FixtureUiFactory.Font;
            needsText.text = "계단 없는 접근 · 승하차 도움"; needsText.fontSize = 14;
            requestStatus = TextAt(Top(sheet,"My Request Status",252,46,20,20),"거점을 선택해 호출하세요",13);
            requestSubmit = UiButton(Top(sheet,"Submit Passenger Request",304,52,20,20),"차량 호출",MobilePrimaryAction,true);
            requestSubmit.GetComponentInChildren<Text>().fontSize = 16;
            TextAt(Top(sheet,"Server Heading",370,20,20,20),"PC 서버 연결",12,true);
            var addressRow = Top(sheet,"Server Address Row",396,48,20,20);
            var addressObject = DefaultControls.CreateInputField(new DefaultControls.Resources());
            addressObject.name = "PC Server Address"; addressObject.transform.SetParent(addressRow, false);
            var addressRect = (RectTransform)addressObject.transform;
            addressRect.anchorMin = Vector2.zero; addressRect.anchorMax = new Vector2(.73f,1);
            addressRect.offsetMin = addressRect.offsetMax = Vector2.zero;
            serverAddress = addressObject.GetComponent<InputField>();
            serverAddress.textComponent.font = FixtureUiFactory.Font; serverAddress.textComponent.fontSize = 13;
            serverAddress.keyboardType = TouchScreenKeyboardType.URL;
            serverAddress.characterLimit = 200;
            ((Text)serverAddress.placeholder).font = FixtureUiFactory.Font;
            ((Text)serverAddress.placeholder).text = "PC IP:8765";
            serverAddress.text = map.serverUrl;
            var connect = FixtureUiFactory.Rect(addressRow,"Connect PC Server",new Vector2(.76f,0),Vector2.one,Vector2.zero,Vector2.zero);
            UiButton(connect,"연결",ConnectMobileServer);
            serverAddressStatus = TextAt(Top(sheet,"Server Address Status",446,22,20,20),"PC와 같은 네트워크에 연결하세요",11);
            var vegetation = DefaultControls.CreateToggle(new DefaultControls.Resources());
            vegetation.name = "Mobile Vegetation";
            vegetation.transform.SetParent(Top(sheet,"Vegetation Row",480,48,20,20), false);
            var vegetationRect = (RectTransform)vegetation.transform;
            vegetationRect.anchorMin = Vector2.zero; vegetationRect.anchorMax = Vector2.one;
            vegetationRect.offsetMin = vegetationRect.offsetMax = Vector2.zero;
            var vegetationToggle = vegetation.GetComponent<Toggle>();
            vegetationToggle.SetIsOnWithoutNotify(VegetationDisplaySettings.MobileVisible);
            vegetationToggle.onValueChanged.AddListener(VegetationDisplaySettings.SetMobileVisible);
            vegetationToggle.graphic.color = FixtureUiPalette.Blue;
            var vegetationText = vegetation.GetComponentInChildren<Text>();
            vegetationText.font = FixtureUiFactory.Font; vegetationText.fontSize = 14;
            vegetationText.text = "나무·잔디 표시";
            UiButton(FixtureUiFactory.Rect(root,"Map Overview",new Vector2(.82f,.63f),new Vector2(.95f,.69f),Vector2.zero,Vector2.zero),"전체",ResetView);
            Label(root,"Attribution","© OpenStreetMap contributors · ODbL 1.0",8,.05f,.60f,.95f,.615f);
        }

        private void ConnectMobileServer()
        {
            try
            {
                string normalized = ClientServerSettings.NormalizeUrl(serverAddress.text);
                if (normalized != map.serverUrl && (pendingRequestJson != null || submitting))
                    throw new ArgumentException("접수 확인 후 서버를 변경하세요.");
                if (submitting) return;
                map.ConnectToServer(normalized); serverAddress.text = normalized;
                submittedRequestId = null; requestStatus.text = "서버 연결 중…";
            }
            catch (ArgumentException error) { requestStatus.text = error.Message; }
        }

        private static Sprite rounded, pointSprite;
        private static void Panel(Transform root, string name, float x, float y, float right, float top, Color? color = null)
        {
            var rect = FixtureUiFactory.Panel(root, name, new Vector2(x,y), new Vector2(right,top), Vector2.zero, Vector2.zero, color ?? FixtureUiPalette.Surface);
            if (name.Contains("Background") || name == "Header" || name == "Page Title") return;
            var image=rect.GetComponent<Image>(); image.sprite=RoundedSprite(); image.type=Image.Type.Sliced;
            image.raycastTarget=false;
        }

        private static Sprite RoundedSprite()
        {
            if (rounded == null)
            {
                var texture = new Texture2D(32,32,TextureFormat.RGBA32,false);
                texture.name = "UI rounded card";
                for (int j=0;j<32;j++) for(int i=0;i<32;i++)
                {
                    float dx = Mathf.Max(8-i, i-23, 0), dy = Mathf.Max(8-j,j-23,0);
                    texture.SetPixel(i,j,new Color(1,1,1,Mathf.Clamp01(8.5f-Mathf.Sqrt(dx*dx+dy*dy))));
                }
                texture.Apply();
                rounded=Sprite.Create(texture,new Rect(0,0,32,32),Vector2.one*.5f,100,0,SpriteMeshType.FullRect,new Vector4(9,9,9,9));
            }
            return rounded;
        }

        private static Text Label(Transform root, string name, string value, int size, float x, float y, float right, float top, bool bold = false)
        {
            var holder = FixtureUiFactory.Rect(root, name + " Bounds", new Vector2(x, y), new Vector2(right, top), Vector2.zero, Vector2.zero);
            return FixtureUiFactory.Text(holder, name, value, size, FixtureUiPalette.Ink, TextAnchor.MiddleLeft, bold ? FontStyle.Bold : FontStyle.Normal);
        }

        private void RefreshDisconnectDemo()
        {
            var client = ActiveAutomaticClient;
            bool held = client != null && client.DemoDisconnected;
            disconnectButton.interactable = client != null && (client.CanRestoreDemo || client.CanDisconnectDemo);
            disconnectLabel.text = held ? "차량 연결 복구" : "연결 끊김 시연";
            if (client == null) disconnectStatus.text = "시연할 차량을 선택하세요";
            else if (held) disconnectStatus.text = $"{client.VehicleId} 단절 · {client.SpeedMps * 3.6f:F1} km/h · " +
                (client.CanRestoreDemo ? "정지 유지 · 복구 가능" : "제동 중 · 정지 후 복구");
            else if (client.DemoUsed) disconnectStatus.text = $"{client.VehicleId} · " +
                (client.Connected ? client.Status : "재연결 시도 중 · 제동 유지");
            else disconnectStatus.text = client.CanDisconnectDemo ? $"{client.VehicleId} 운행 중 · 단절 시 자동 제동" :
                $"{client.VehicleId} · 운행 중 시연 가능";
        }

        private void Update()
        {
            if (!mobile) RefreshVehicleReadout();
            if (map == null || Time.unscaledTime < nextRefresh || connection == null) return;
            nextRefresh = Time.unscaledTime + .25f;
            if (!mobile) RefreshEnvironmentControls();
            RefreshStopDropdowns();
            if (disconnectButton != null) RefreshDisconnectDemo();
            if (crowdStatus != null && crowdOverlay != null)
                crowdStatus.text = crowdOverlay.Visible ? crowdOverlay.StatusText : "혼잡 예상 숨김";
            if (crowdResetButton != null && crowdOverlay != null)
                crowdResetButton.SetActive(crowdOverlay.TimeOverridden);
            bool online = !string.IsNullOrEmpty(map.RunId) && string.IsNullOrEmpty(map.LastError);
            if (serverAddressStatus != null) serverAddressStatus.text = online ? map.serverUrl : map.LastError ?? "연결 중…";
            connection.text = online ? "● 서버 연결됨" : "● 연결 대기";
            connection.color = online ? FixtureUiPalette.Green : FixtureUiPalette.Amber;
            var state = map.LatestState;
            if (!mobile) RefreshQueue(state, online);
            if (mobile && online && passengerRun != map.RunId)
            { passengerRun = map.RunId; submittedRequestId = null; requestStatus.text = "거점을 선택해 호출하세요"; }
            foreach (var pin in mapPins)
            {
                if (pin.vehicleId == null || pin.caption == null) continue;
                var entry = state?.vehicles == null ? null : Array.Find(state.vehicles, v => v.id == pin.vehicleId);
                string phase = !online || entry == null ? "연결 대기" : entry.phase switch
                {
                    "READY" => "대기", "TO_PICKUP" => "픽업 이동", "PICKUP_SERVICE" => "탑승 중",
                    "TO_DROPOFF" => "운송 중", "DROPOFF_SERVICE" => "하차 중",
                    "RETURNING" => "차고 복귀", "CHARGING" => "충전 중", "MANEUVER" => "회차 중", _ => entry.phase
                };
                if (online && entry != null && automaticClients.TryGetValue(pin.vehicleId, out var control) &&
                    (entry.phase == "TO_PICKUP" || entry.phase == "TO_DROPOFF" || entry.phase == "RETURNING") && !control.IsDriving)
                    phase = "제동 대기";
                pin.caption.text = pin.vehicleId + " · " + phase;
            }
            if (requestSubmit != null)
            {
                if (mobile && online && submittedRequestId == null && state?.requests != null)
                    submittedRequestId = Array.FindLast(state.requests, r => r.owner_id == passengerId)?.id;
                if (!submitting && online && pendingRequestJson != null && pendingRequestRun != map.RunId)
                { ClearPendingRequest(); requestStatus.text = "서버 세션 변경 · 거점을 다시 확인하세요"; }
                requestSubmit.interactable = online && !submitting && (pendingRequestJson != null ||
                    pickup != null && dropoff != null && !string.IsNullOrEmpty(pickup.landmark_id) && !string.IsNullOrEmpty(dropoff.landmark_id) && pickup.landmark_id != dropoff.landmark_id);
                requestSubmit.GetComponentInChildren<Text>().text = submitting ? "접수 중…" : pendingRequestJson != null ? "같은 요청 재확인" : mobile ? "차량 호출" : "요청 접수";
                foreach (var dropdown in stopDropdowns)
                    if (dropdown.name == "Request Pickup" || dropdown.name == "Request Dropoff")
                        dropdown.interactable = !submitting && pendingRequestJson == null && dropdownStops != null && dropdownStops.Length > 0;
                if (!submitting && submittedRequestId != null && state?.requests != null)
                {
                    var found = Array.Find(state.requests, r => r.id == submittedRequestId);
                    if (found != null) requestStatus.text = TransportStatus(found);
                    if (mobile && found != null && !IsTerminal(found.status) && pendingRequestJson == null)
                    {
                        bool canCancel = found.status == "QUEUED" || found.status == "ASSIGNED";
                        requestSubmit.interactable = online && !submitting && pendingRequestJson == null && canCancel;
                        requestSubmit.GetComponentInChildren<Text>().text = submitting ? "처리 중…" : canCancel ? "요청 취소" : "운송 진행 중";
                        foreach (var dropdown in stopDropdowns) dropdown.interactable = false;
                        if (mobileStepFree != null) mobileStepFree.interactable = false;
                        var vehicle = state.vehicles == null ? null : Array.Find(state.vehicles, v => v.id == found.vehicle_id);
                        if (vehicle != null && vehicle.motion_source == "unity_physics" &&
                            (vehicle.physical_status == "YIELDING" || vehicle.physical_status == "EMERGENCY_STOP" || !vehicle.observation_fresh))
                            requestStatus.text += "\n센서 안전 확인 대기";
                        else if (map.ServerPaused) requestStatus.text += "\nPC에서 운행을 시작하면 진행됩니다";
                    }
                    else if (mobileStepFree != null) mobileStepFree.interactable = !submitting && pendingRequestJson == null;
                }
            }
            session.text = $"v{Application.version}";
            int connectedCount = 0;
            if (!mobile && OperatorControls != null)
            {
                var controls=OperatorControls;
                for(int i=0;i<vehicleButtons.Length;i++)
                {
                    string id="V0"+(i+1); bool bound=controls.HasVehicle(id);
                    vehicleButtons[i].interactable=bound;
                    bool selected=controls.SelectedVehicle?.OperatorVehicleId==id;
                    vehicleButtons[i].SetIsOnWithoutNotify(selected);
                    automaticClients.TryGetValue(id, out var vehicleClient);
                    bool connected = vehicleClient != null && vehicleClient.Connected;
                    if (connected) connectedCount++;
                    fleetCards[i].text = (selected ? "선택됨 · " : "") + (connected ? "제어 연결됨" : "제어 미연결");
                    if (fleetSurfaces != null) fleetSurfaces[i].color = selected ? new Color(.91f, .95f, 1) : Color.white;
                    if (fleetDetails != null) fleetDetails[i].text = vehicleClient != null
                        ? vehicleClient.Status : "—";
                    var serverVehicle=online && state?.vehicles!=null ? Array.Find(state.vehicles,v=>v.id==id) : null;
                    bool hasBattery = serverVehicle != null && !float.IsNaN(serverVehicle.soc) && !float.IsInfinity(serverVehicle.soc);
                    // A Play-mode script reload can retain the old cards while new
                    // UI fields deserialize as empty arrays. Keep those cards usable.
                    var battery = batteryIndicators != null && i < batteryIndicators.Length ? batteryIndicators[i] : null;
                    var batteryLabel = batteryLabels != null && i < batteryLabels.Length ? batteryLabels[i] : null;
                    if (battery != null) battery.gameObject.SetActive(hasBattery);
                    if (batteryLabel != null) batteryLabel.text = hasBattery ? $"{Mathf.Clamp01(serverVehicle.soc) * 100:F0}%" : "—";
                    if (hasBattery && battery != null)
                    {
                        battery.SetValueWithoutNotify(Mathf.Clamp01(serverVehicle.soc));
                        if (battery.fillRect != null && battery.fillRect.TryGetComponent<Image>(out var fillImage))
                        {
                            fillImage.enabled = battery.value > 0f;
                            fillImage.color = serverVehicle.soc < .15f ? FixtureUiPalette.Amber : FixtureUiPalette.Green;
                        }
                    }
                    if(connected && serverVehicle!=null && fleetDetails!=null)
                    {
                        var label=serverVehicle.phase=="CHARGING"?"충전 중":serverVehicle.phase=="RETURNING"?"차고 복귀":
                            serverVehicle.phase=="READY"?"대기":serverVehicle.phase=="TO_PICKUP"?"픽업 이동":
                            serverVehicle.phase=="TO_DROPOFF"?"운송 중":serverVehicle.phase=="MANEUVER"?"회차 중":serverVehicle.phase;
                        fleetDetails[i].text=label;
                        fleetDetails[i].color=serverVehicle.soc<.15f?new Color(.8f,.15f,.12f):FixtureUiPalette.Muted;
                    }
                    if (fleetRoutes != null && i < fleetRoutes.Length)
                        fleetRoutes[i].text = serverVehicle == null ? "" : VehicleJourney(serverVehicle, state);
                    if (routeOverlays != null && i < routeOverlays.Length)
                        routeOverlays[i].SetRoute(worldCamera, serverVehicle?.mission_id,
                            serverVehicle?.route_unity, serverVehicle?.route_progress_m ?? 0,
                            serverVehicle != null && serverVehicle.observation_fresh &&
                            serverVehicle.phase != "READY" && serverVehicle.phase != "CHARGING");
                    fleetCards[i].color=selected ? FixtureUiPalette.Blue : FixtureUiPalette.Ink;
                }
            }
            if (statCards != null) statCards[0].text = connectedCount + " / " + vehicleButtons.Length;
            if (state == null || !online)
            {
                if (statCards != null) for (int i = 1; i < statCards.Length; i++) statCards[i].text = "—";
                if (queue != null) queue.text = "연결 대기";
                if (metrics != null) metrics.text = "연결 대기";
                return;
            }
            RefreshOperationLog(state);
            int queued = state.queue == null ? 0 : state.queue.Length;
            if(dispatchDropdown != null)
            {
                dispatchDropdown.interactable = !changingDispatch && online;
                if(!changingDispatch) dispatchDropdown.SetValueWithoutNotify(state.dispatch=="greedy"?1:0);
            }
            if (plannerDropdown != null)
            {
                plannerDropdown.interactable = !changingPlanner && online;
                if (!changingPlanner) plannerDropdown.SetValueWithoutNotify(state.planner == "dijkstra" ? 1 : 0);
            }
            if(metrics!=null) metrics.text = mobile ? $"차량 {map.VehicleCount}대 · 대기 {queued}건 · 완료 {map.Completed}건" :
                $"차량   {map.VehicleCount} / 3       대기 요청   {queued}       운송 완료   {map.Completed}       충전 중   {Array.FindAll(state.vehicles, v => v.phase == "CHARGING").Length}";
            if (!mobile)
            {
                if(statCards!=null) { statCards[1].text=queued.ToString(); statCards[2].text=map.Completed.ToString(); statCards[3].text=state.vehicles == null ? "—" : Array.FindAll(state.vehicles,v=>v.phase=="CHARGING").Length.ToString(); }
                if(queue!=null) queue.text=queued==0 ? "대기 없음" : $"대기 요청 {queued}건";
            }
        }

        private string LandmarkName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "거점";
            var stop = map?.Document?.stops == null ? null : Array.Find(map.Document.stops, a => a.landmark_id == id);
            if (stop != null) return stop.name.Replace(" 후보", "");
            var hub = map?.Document?.hubs == null ? null : Array.Find(map.Document.hubs, a => a.id == id);
            return hub?.name ?? id;
        }

        private string VehicleJourney(CampusServerMap.Vehicle vehicle, CampusServerMap.SharedState state)
        {
            if (vehicle.phase == "READY" || vehicle.phase == "CHARGING") return vehicle.home_name;
            var order = state?.requests == null ? null : Array.Find(state.requests, r => r.id == vehicle.request_id);
            if (order != null)
            {
                if (vehicle.phase == "RETURNING" || vehicle.phase == "RETURN_PENDING" ||
                    vehicle.transport_phase == "RETURNING" || vehicle.transport_phase == "RETURN_PENDING")
                    return LandmarkName(order.dropoff_landmark_id) + " → " + vehicle.home_name;
                return LandmarkName(order.pickup_landmark_id) + " → " + LandmarkName(order.dropoff_landmark_id);
            }
            return string.IsNullOrEmpty(vehicle.destination) ? "" : "현재 위치 → " + LandmarkName(vehicle.destination);
        }

        private void CreateMapPin(Transform target, string title, string vehicleId, Color color)
        {
            var pin = new MapPin { target = target, vehicleId = vehicleId };
            pin.root = FixtureUiFactory.Rect(safeRoot, "Map Pin " + (vehicleId ?? title), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var dot = FixtureUiFactory.Rect(pin.root, "Position", Vector2.zero, Vector2.zero, new Vector2(-6, -6), new Vector2(6, 6));
            var pointImage = Surface(dot, color);
            if (pointSprite == null)
            {
                var texture = new Texture2D(24, 24, TextureFormat.RGBA32, false);
                texture.name = "Map point";
                for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++)
                    texture.SetPixel(x, y, new Color(1, 1, 1,
                        Mathf.Clamp01(11.5f - Vector2.Distance(new Vector2(x, y), new Vector2(11.5f, 11.5f)))));
                texture.Apply();
                pointSprite = Sprite.Create(texture, new Rect(0, 0, 24, 24), Vector2.one * .5f);
            }
            pointImage.sprite = pointSprite; pointImage.type = Image.Type.Simple;
            var outline = dot.gameObject.AddComponent<Outline>(); outline.effectColor = Color.white; outline.effectDistance = new Vector2(2, -2);
            pin.leader = FixtureUiFactory.Rect(pin.root, "Leader", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            Surface(pin.leader, color, false);
            pin.leader.pivot = new Vector2(0, .5f);
            bool vehicle = vehicleId != null;
            var caption = vehicle ? title : title.Replace("운동장 서쪽 비룡주차장", "운동장").Replace(" 허브", "");
            float width = vehicle ? 132 : Mathf.Clamp(caption.Length * 12 + 30, 72, 190);
            pin.label = FixtureUiFactory.Rect(pin.root, "Badge", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(width, 28));
            pin.label.pivot = Vector2.zero;
            var background = Surface(pin.label, vehicle ? color : Color.white);
            var shadow = pin.label.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(.06f, .14f, .25f, .16f); shadow.effectDistance = new Vector2(0, -2);
            var text = FixtureUiFactory.Text(pin.label, "Name", caption, 12, vehicle ? Color.white : FixtureUiPalette.Ink, TextAnchor.MiddleCenter, vehicle ? FontStyle.Bold : FontStyle.Normal);
            pin.caption = text;
            if (vehicle && !mobile)
            {
                background.raycastTarget = true;
                var button = pin.label.gameObject.AddComponent<Button>(); button.targetGraphic = background;
                button.onClick.AddListener(() => OperatorControls?.SelectVehicle(vehicleId));
            }
            if (!vehicle && !mobile)
            {
                var hit = FixtureUiFactory.Rect(pin.root, "Hub Hit Area", Vector2.zero, Vector2.zero,
                    new Vector2(-16, -16), new Vector2(16, 16));
                Surface(hit, Color.clear).raycastTarget = true;
                var button = hit.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => selectedHub = selectedHub == pin ? null : pin);
                var events = hit.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => pin.hovered = true);
                var leave = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                leave.callback.AddListener(_ => pin.hovered = false);
                events.triggers.Add(enter); events.triggers.Add(leave);
            }
            pin.label.gameObject.SetActive(vehicle);
            pin.leader.gameObject.SetActive(vehicle);
            // Vehicles get first choice of label positions; hub labels yield when colocated.
            if (vehicle) mapPins.Insert(0, pin); else mapPins.Add(pin);
        }

        private void LateUpdate()
        {
            if (worldCamera == null || safeRoot == null) return;
            if (routeOverlays != null) foreach (var overlay in routeOverlays)
                if (overlay != null) overlay.ShowOverview = OperatorControls == null || !OperatorControls.IsFollowing;
            if (crowdOverlay != null) crowdOverlay.ShowOverview = OperatorControls == null || !OperatorControls.IsFollowing;
            if (lastScreenSize != new Vector2Int(Screen.width, Screen.height) || lastSafeArea != Screen.safeArea)
            {
                Canvas.ForceUpdateCanvases();
                RefreshViewport();
            }
            var view = MapViewport;
            var size = safeRoot.rect.size;
            var bounds = new Rect(view.x * size.x + 8, view.y * size.y + 8, view.width * size.x - 16, view.height * size.y - 16);
            placedLabels.Clear();
            foreach (var pin in mapPins)
            {
                // Fleet badges remain visible in the PC overview independently
                // of optional hub/debug markers. Follow view stays unobstructed.
                bool fleetOverview = !mobile && pin.vehicleId != null && OperatorControls != null && !OperatorControls.IsFollowing;
                if ((!mobile && !showWorldMarkers && !fleetOverview) || pin.target == null)
                { pin.root.gameObject.SetActive(false); pin.hovered = false; continue; }
                var position = pin.target.position;
                if (mobile && pin.vehicleId != null)
                {
                    var entry = map.LatestState?.vehicles == null ? null : Array.Find(map.LatestState.vehicles, v => v.id == pin.vehicleId);
                    if (entry == null || !string.IsNullOrEmpty(map.LastError)) { pin.root.gameObject.SetActive(false); continue; }
                    position = entry.position_unity; position.y = pin.target.position.y;
                }
                var point = worldCamera.WorldToViewportPoint(position);
                bool visible = point.z > 0 && point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1;
                pin.root.gameObject.SetActive(visible);
                if (!visible) { pin.hovered = false; continue; }
                var anchor = view.position + Vector2.Scale(new Vector2(point.x, point.y), view.size);
                pin.root.anchorMin = pin.root.anchorMax = anchor;
                var origin = Vector2.Scale(anchor, size);
                bool showLabel = pin.vehicleId != null || (!mobile && (pin.hovered || selectedHub == pin));
                pin.label.gameObject.SetActive(showLabel);
                pin.leader.gameObject.SetActive(showLabel);
                if (!showLabel) continue;
                var labelSize = pin.label.rect.size;
                Rect candidate = default;
                // Bounded placement search, no per-frame collections or scene scans.
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    float dx = attempt % 2 == 0 ? 12 : -labelSize.x - 12;
                    float dy = 12 + (attempt / 2) * 32;
                    if (attempt % 4 >= 2) dy = -dy;
                    candidate = new Rect(Mathf.Clamp(origin.x + dx, bounds.xMin, bounds.xMax - labelSize.x),
                        Mathf.Clamp(origin.y + dy, bounds.yMin, bounds.yMax - labelSize.y), labelSize.x, labelSize.y);
                    bool overlap = false;
                    foreach (var used in placedLabels) if (used.Overlaps(candidate)) { overlap = true; break; }
                    if (!overlap) break;
                }
                pin.label.anchoredPosition = candidate.position - origin;
                placedLabels.Add(new Rect(candidate.x - 3, candidate.y - 3, candidate.width + 6, candidate.height + 6));
                var end = new Vector2(Mathf.Clamp(origin.x, candidate.xMin, candidate.xMax), Mathf.Clamp(origin.y, candidate.yMin, candidate.yMax)) - origin;
                pin.leader.sizeDelta = new Vector2(end.magnitude, 1.5f);
                pin.leader.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(end.y, end.x) * Mathf.Rad2Deg);
            }
        }

        private void OnDestroy()
        {
            foreach (var renderer in mobileHiddenRenderers) if (renderer != null) renderer.enabled = true;
            foreach (var pair in mobileBodies) if (pair.Key != null) pair.Key.isKinematic = pair.Value;
            foreach(var client in automaticClients.Values) if(client!=null) client.enabled=false;
            if (OperatorControls != null) { OperatorControls.ShowMap(); OperatorControls.enabled=false; }
            if (worldCamera != null)
            {
                worldCamera.rect = previousViewport;
                worldCamera.orthographic = wasOrthographic;
                worldCamera.orthographicSize = cameraSize;
                worldCamera.aspect = cameraAspect;
                worldCamera.enabled = wasEnabled;
                worldCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                worldCamera.gameObject.SetActive(wasActive);
            }
            if (previousCamera != null) previousCamera.enabled = previousCameraEnabled;
            foreach (var camera in suspendedCameras) if (camera != null) camera.enabled = true;
            if (map != null) map.showStatus = true;
            if (canvasRoot != null) Destroy(canvasRoot);
        }
    }
}
