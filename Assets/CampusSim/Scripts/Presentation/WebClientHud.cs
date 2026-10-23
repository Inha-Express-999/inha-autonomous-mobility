using System;
using System.Collections;
using System.Collections.Generic;
using InhaExpress.Client.MapClient;
using UnityEngine;
using UnityEngine.UI;

namespace InhaExpress.Client.Presentation
{
    /// <summary>Shared web visual language; PC automatic missions and read-only mobile map.</summary>
    public sealed class WebClientHud : MonoBehaviour
    {
        private CampusServerMap map;
        private Text connection, session, fleet, metrics, policy;
        private Camera worldCamera;
        private Camera previousCamera;
        private bool previousCameraEnabled, wasActive, wasOrthographic, wasEnabled;
        private Vector3 cameraPosition;
        private Quaternion cameraRotation;
        private float cameraSize, cameraAspect;
        private Rect previousViewport;
        private GameObject canvasRoot;
        private bool mobile;
        private float nextRefresh;
        private float fittedSize;
        private readonly List<Camera> suspendedCameras = new List<Camera>();
        private RectTransform[] hubLabels;
        private ServerMapMarker[] hubs;
        private RectTransform safeRoot;
        public PcOperatorControls OperatorControls { get; private set; }
        private Text vehicleStatus;
        private Button[] vehicleButtons;
        private readonly Dictionary<string, UnityAutonomousClient> automaticClients = new Dictionary<string, UnityAutonomousClient>();
        private string automaticStopId;
        private Button automaticStart;
        public UnityAutonomousClient ActiveAutomaticClient => OperatorControls?.SelectedVehicle != null &&
            automaticClients.TryGetValue(OperatorControls.SelectedVehicle.OperatorVehicleId, out var client) ? client : null;
        private Bounds framingBounds;
        private Vector2Int lastScreenSize;
        private Rect lastSafeArea;
        private Rect MapViewport => mobile ? new Rect(0, .49f, 1, .43f) : new Rect(.025f, .29f, .685f, .42f);

        public void Initialize(bool passenger) => StartCoroutine(WaitForWorld(passenger));

        private IEnumerator WaitForWorld(bool passenger)
        {
            while ((map = FindFirstObjectByType<CampusServerMap>()) == null) yield return null;
            Build(passenger);
        }

        private void Build(bool passenger)
        {
            mobile = passenger;
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
            scaler.matchWidthOrHeight = .5f;
            var root = FixtureUiFactory.Rect(canvasRoot.transform, "Safe Area", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            safeRoot = root;
            root.gameObject.AddComponent<SafeAreaPanel>();
            if (!mobile)
            {
                Panel(root, "Top Background", 0, .71f, 1, 1, FixtureUiPalette.Canvas);
                Panel(root, "Bottom Background", 0, 0, 1, .29f, FixtureUiPalette.Canvas);
                Panel(root, "Right Background", .71f, .23f, 1, .75f, FixtureUiPalette.Canvas);
                Panel(root, "Left Background", 0, .23f, .025f, .75f, FixtureUiPalette.Canvas);
            }
            Panel(root, "Header", 0, .92f, 1, 1);
            Label(root, "Brand", "Inha Mobil", mobile ? 23 : 24, mobile ? .14f : .065f, .93f, .55f, .995f, true);
            var logo=Resources.Load<Texture2D>("inha-mobil-logo");
            if(logo!=null) {
                var icon=FixtureUiFactory.Rect(root,"Brand Logo",new Vector2(.025f,.935f),new Vector2(mobile?.12f:.055f,.99f),Vector2.zero,Vector2.zero).gameObject.AddComponent<RawImage>();
                icon.texture=logo; icon.raycastTarget=false;
            }
            connection = Label(root, "Connection", "연결 확인 중", mobile ? 12 : 15, .58f, .94f, .97f, .985f);
            connection.alignment = TextAnchor.MiddleRight;
            if (mobile) BuildMobile(root); else BuildOperator(root);
            session = Label(root, "Session", "서버 세션 확인 중", mobile ? 10 : 12, .03f, .005f, .97f, .043f);
            worldCamera = Camera.main;
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (camera.name == "Campus overview camera")
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
                worldCamera.orthographic = true;
                var rotation = Quaternion.Euler(mobile ? 55 : 65, mobile ? 25 : 0, 0);
                worldCamera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * 1000, rotation);
                framingBounds = bounds;
                RefreshViewport();
                hubs = Array.FindAll(map.markers, m => m.kind == ServerMapMarker.MarkerKind.Hub);
                hubLabels = new RectTransform[hubs.Length];
                for (int i = 0; i < hubs.Length; i++)
                {
                    float halfWidth = mobile ? 56 : 68;
                    var label = FixtureUiFactory.Panel(root, "Hub " + hubs[i].serverId, Vector2.zero, Vector2.zero,
                        new Vector2(-halfWidth, 0), new Vector2(halfWidth, 28), Color.white);
                    label.GetComponent<Image>().raycastTarget = false;
                    var text = FixtureUiFactory.Text(label, "Name", "● " + hubs[i].displayName, mobile ? 10 : 13,
                        FixtureUiPalette.Blue, TextAnchor.MiddleCenter, FontStyle.Bold);
                    hubLabels[i] = label;
                }
            }
            if (!mobile && worldCamera != null)
            {
                OperatorControls = gameObject.AddComponent<PcOperatorControls>();
                OperatorControls.Initialize(worldCamera, framingBounds, fittedSize);
                foreach (var bound in OperatorControls.Vehicles)
                    if (bound is IAutonomousVehicle target && !automaticClients.ContainsKey(target.OperatorVehicleId))
                    {
                        var client = gameObject.AddComponent<UnityAutonomousClient>();
                        automaticClients.Add(target.OperatorVehicleId, client);
                        client.Initialize(map, target);
                    }
            }
            Debug.Log("CAMPUS_WEB_UI_READY role=" + (mobile ? "Mobile" : "PC") + " additive=true camera_navigation=" + !mobile);
        }

        private void RefreshViewport()
        {
            if (worldCamera == null || Screen.width <= 0 || Screen.height <= 0) return;
            float zoom = fittedSize > 0 ? worldCamera.orthographicSize / fittedSize : 1f;
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
            else worldCamera.orthographicSize = fittedSize * zoom;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            lastSafeArea = safe;
        }

        private void BuildOperator(RectTransform root)
        {
            Panel(root, "Page Title", 0, .84f, .74f, .92f, FixtureUiPalette.Canvas);
            Label(root, "Title", "캠퍼스 관제", 23, .025f, .85f, .28f, .91f, true);
            statCards = new Text[4];
            string[] statNames={"운행 차량","대기 요청","운송 완료","충전 중"};
            for(int i=0;i<4;i++) {
                float x=.025f+i*.173f;
                Panel(root,"Metric Card "+i,x,.765f,x+.166f,.84f);
                Label(root,"Metric Name "+i,statNames[i],11,x+.012f,.815f,x+.15f,.84f);
                statCards[i]=Label(root,"Metric Value "+i,"—",23,x+.012f,.765f,x+.15f,.817f,true);
            }
            Panel(root, "Map Header", .025f, .71f, .71f, .75f);
            Label(root, "Map Title", "인하대학교 운영 지도", 15, .04f, .71f, .47f, .75f, true);
            Label(root, "Map Tools", "노드 187 · 허브 3", 12, .40f, .71f, .58f, .75f);
            ActionButton(root,"Zoom In","+",.665f,.53f,.70f,.59f,()=>Zoom(.8f));
            ActionButton(root,"Zoom Out","−",.665f,.47f,.70f,.53f,()=>Zoom(1.25f));
            ActionButton(root,"Overview","전체 보기",.59f,.715f,.70f,.75f,ResetView);
            ActionButton(root,"Rotate Left","↶",.625f,.36f,.66f,.42f,()=>OperatorControls?.Rotate(new Vector2(-15,0)));
            ActionButton(root,"Rotate Right","↷",.665f,.36f,.70f,.42f,()=>OperatorControls?.Rotate(new Vector2(15,0)));
            Panel(root, "Map Footer", .025f, .23f, .71f, .29f);
            policy = Label(root, "Map Legend", "배차 · 경로 상태 수신 대기  |  차량 조작 보류", 15, .04f, .24f, .70f, .285f);
            Label(root, "Attribution", "© OpenStreetMap contributors · ODbL 1.0", 10, .39f, .295f, .70f, .32f);
            Panel(root, "Vehicle Detail Card", .73f, .44f, .975f, .895f);
            Label(root, "Vehicle Detail Title", "선택 차량", 19, .75f, .83f, .96f, .885f, true);
            vehicleStatus = Label(root, "Vehicle Detail", "차량 현황에서 차량을 선택하세요", 14, .75f, .715f, .95f, .825f);
            var goalHolder=FixtureUiFactory.Rect(root,"Automatic Goal Bounds",new Vector2(.75f,.645f),new Vector2(.955f,.705f),Vector2.zero,Vector2.zero);
            var goal=FixtureUiFactory.Button(goalHolder,"Automatic Goal","목적 거점 선택",FixtureUiPalette.Canvas,out var goalText);
            goalText.color=FixtureUiPalette.Ink; goalText.fontSize=13;
            goal.onClick.AddListener(()=>OpenStops(root,goalText,stop=>automaticStopId=stop.id));
            var startHolder=FixtureUiFactory.Rect(root,"Automatic Start Bounds",new Vector2(.75f,.57f),new Vector2(.955f,.63f),Vector2.zero,Vector2.zero);
            automaticStart=FixtureUiFactory.Button(startHolder,"Start Automatic Mission","자동 운행 시작",FixtureUiPalette.Blue,out var startText);
            startText.fontSize=14;
            automaticStart.onClick.AddListener(()=>ActiveAutomaticClient?.StartMission(automaticStopId));
            ActionButton(root,"Release Follow","추적 해제",.75f,.50f,.955f,.555f,()=>OperatorControls?.ReleaseFollow());
            ActionButton(root,"Return To Map","운영지도 보기",.75f,.445f,.955f,.49f,()=>OperatorControls?.ShowMap(true));
            Panel(root, "Queue Card", .73f, .23f, .975f, .42f);
            Label(root, "Queue Title", "요청 대기열", 18, .75f, .36f, .95f, .41f, true);
            queue = Label(root, "Queue Status", "서버 상태 수신 대기", 14, .75f, .25f, .95f, .35f);
            Panel(root, "Notes", .73f, .05f, .975f, .21f);
            Label(root, "Notes Text", "드래그 이동 · 우클릭 회전 · 휠 확대\n전체 보기 / Esc: 지도 복귀\n웹 시연과 Unity 차량 상태는 별도", 13, .75f, .065f, .95f, .20f);
            Label(root, "Fleet Title", "차량 현황", 17, .025f, .18f, .71f, .23f, true);
            fleetCards = new Text[3];
            vehicleButtons = new Button[3];
            for(int i=0;i<3;i++)
            {
                float left=.025f+i*.233f;
                int index=i;
                var holder=FixtureUiFactory.Rect(root,"Vehicle Card "+i,new Vector2(left,.05f),new Vector2(left+.219f,.175f),Vector2.zero,Vector2.zero);
                vehicleButtons[i]=FixtureUiFactory.Button(holder,"Select V0"+(i+1),"V0"+(i+1)+" · 연결 확인 중",FixtureUiPalette.Surface,out fleetCards[i]);
                fleetCards[i].color=FixtureUiPalette.Ink; fleetCards[i].fontSize=14;
                vehicleButtons[i].onClick.AddListener(()=>OperatorControls?.SelectVehicle("V0"+(index+1)));
            }
        }

        private Text queue;
        private Text[] statCards;
        private void Zoom(float factor)
        {
            if(OperatorControls!=null) { OperatorControls.Zoom(factor); return; }
            if(worldCamera != null) worldCamera.orthographicSize = Mathf.Clamp(worldCamera.orthographicSize*factor,20,fittedSize*2);
        }
        private void ResetView() { if(OperatorControls!=null) OperatorControls.ShowMap(true); else if(worldCamera != null) worldCamera.orthographicSize=fittedSize; }
        private static void ActionButton(RectTransform root,string name,string value,float x,float y,float right,float top,UnityEngine.Events.UnityAction action)
        {
            var holder=FixtureUiFactory.Rect(root,name+" Bounds",new Vector2(x,y),new Vector2(right,top),Vector2.zero,Vector2.zero);
            var button=FixtureUiFactory.Button(holder,name,value,FixtureUiPalette.Surface,out var label);
            label.color=FixtureUiPalette.Ink; label.fontSize=value.Length>2?12:22;
            button.onClick.AddListener(action);
        }
        private Text[] fleetCards;
        private void Field(RectTransform root,string name,string value,float x,float y,float right,float top)
        {
            Panel(root,name,x,y,right,top,FixtureUiPalette.Canvas);
            var label = Label(root,name+" Text",value,14,x+.01f,y,right-.01f,top);
            if (name != "Origin" && name != "Destination") return;
            var hit=FixtureUiFactory.Rect(root,name+" Selection",new Vector2(x,y),new Vector2(right,top),Vector2.zero,Vector2.zero);
            var image=hit.gameObject.AddComponent<Image>(); image.color=Color.clear;
            hit.gameObject.AddComponent<Button>().onClick.AddListener(()=>OpenStops(root,label));
        }

        private void OpenStops(RectTransform root, Text target, Action<ServerMapAnchor> selected = null)
        {
            if (map.Document == null) return;
            var modal=FixtureUiFactory.Panel(root,"Stop Selection",new Vector2(.08f,.1f),new Vector2(.92f,.89f),Vector2.zero,Vector2.zero,FixtureUiPalette.Surface);
            var title=FixtureUiFactory.Text(modal,"Title","거점 선택 · 통행 검증 전 후보",mobile?16:20,FixtureUiPalette.Ink);
            ((RectTransform)title.transform).anchorMin=new Vector2(.04f,.93f);
            ((RectTransform)title.transform).anchorMax=new Vector2(.96f,.99f);
            var stops=map.Document.stops;
            for(int i=0;i<stops.Length;i++)
            {
                var stop=stops[i];
                var button=FixtureUiFactory.Button(modal,"Stop "+stop.id,stop.name,FixtureUiPalette.Canvas,out var text);
                text.color=FixtureUiPalette.Ink; text.fontSize=mobile?13:16;
                var rect=(RectTransform)button.transform;
                rect.anchorMin=new Vector2(.04f,.88f-i*.057f); rect.anchorMax=new Vector2(.96f,.93f-i*.057f);
                button.onClick.AddListener(()=>{target.text=stop.name; selected?.Invoke(stop); Destroy(modal.gameObject);});
            }
            var close=FixtureUiFactory.Button(modal,"Close","닫기",FixtureUiPalette.Blue,out _);
            var closeRect=(RectTransform)close.transform;
            closeRect.anchorMin=new Vector2(.04f,.015f); closeRect.anchorMax=new Vector2(.96f,.065f);
            close.onClick.AddListener(()=>Destroy(modal.gameObject));
        }

        private void BuildMobile(RectTransform root)
        {
            Panel(root, "Sheet Background", 0, 0, 1, .49f, FixtureUiPalette.Canvas);
            Panel(root, "Passenger Sheet", 0, 0, 1, .49f);
            Label(root, "Title", "셔틀 호출", 23, .05f, .435f, .95f, .485f, true);
            Field(root,"Origin","●  출발지 선택                  ›",.05f,.355f,.95f,.425f);
            Field(root,"Destination","●  목적지 선택                  ›",.05f,.275f,.95f,.345f);
            Panel(root, "General", .05f, .215f, .48f, .265f, new Color(.89f,.95f,1));
            Label(root, "General Text", "일반 이동", 14, .08f, .215f, .46f, .265f, true);
            Panel(root, "Accessible", .52f, .215f, .95f, .265f, FixtureUiPalette.Canvas);
            Label(root, "Accessible Text", "이동지원", 14, .55f, .215f, .93f, .265f);
            metrics = Label(root, "Status", "서버 상태 수신 대기", 11, .05f, .175f, .95f, .21f);
            Panel(root, "Deferred Action", .05f, .105f, .95f, .165f, FixtureUiPalette.Blue);
            var action = Label(root, "Action Text", "차량 호출 준비 중", 17, .05f, .105f, .95f, .165f, true);
            action.color = Color.white; action.alignment = TextAnchor.MiddleCenter;
            string[] navigation={"홈","이용내역","알림","더보기"};
            for(int i=0;i<navigation.Length;i++)
            {
                var tab=Label(root,"Navigation "+i,navigation[i],12,.025f+i*.25f,.045f,.225f+i*.25f,.095f);
                tab.alignment=TextAnchor.MiddleCenter;
                tab.color=i==0?FixtureUiPalette.Blue:FixtureUiPalette.Muted;
            }
            fleet = Label(root, "Map Legend", "노드 187 · 허브 3 · 차량 조작 보류", 11, .05f, .50f, .95f, .535f);
            ActionButton(root,"Map Overview","◎",.84f,.55f,.95f,.60f,ResetView);
            Label(root,"Attribution","© OpenStreetMap contributors · ODbL 1.0",8,.05f,.49f,.95f,.505f);
        }

        private static Sprite rounded;
        private static void Panel(Transform root, string name, float x, float y, float right, float top, Color? color = null)
        {
            var rect = FixtureUiFactory.Panel(root, name, new Vector2(x,y), new Vector2(right,top), Vector2.zero, Vector2.zero, color ?? FixtureUiPalette.Surface);
            if (name.Contains("Background") || name == "Header" || name == "Page Title") return;
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
            var image=rect.GetComponent<Image>(); image.sprite=rounded; image.type=Image.Type.Sliced;
            image.raycastTarget=false;
        }

        private static Text Label(Transform root, string name, string value, int size, float x, float y, float right, float top, bool bold = false)
        {
            var holder = FixtureUiFactory.Rect(root, name + " Bounds", new Vector2(x, y), new Vector2(right, top), Vector2.zero, Vector2.zero);
            return FixtureUiFactory.Text(holder, name, value, size, FixtureUiPalette.Ink, TextAnchor.MiddleLeft, bold ? FontStyle.Bold : FontStyle.Normal);
        }

        private void Update()
        {
            if (map == null || Time.unscaledTime < nextRefresh || connection == null) return;
            nextRefresh = Time.unscaledTime + .25f;
            bool online = !string.IsNullOrEmpty(map.RunId) && string.IsNullOrEmpty(map.LastError);
            connection.text = online ? "● 서버 연결됨" : "● 연결 대기";
            connection.color = online ? FixtureUiPalette.Green : FixtureUiPalette.Amber;
            var state = map.LatestState;
            session.text = online ? $"v{Application.version} · {map.SimulationTime:F1}s · Run {(mobile ? map.RunId.Substring(0, Math.Min(8, map.RunId.Length)) : map.RunId)}" : "서버 미연결 · 저장된 지도 표시";
            if (OperatorControls != null)
            {
                var controls=OperatorControls;
                if(vehicleStatus!=null) vehicleStatus.text=controls.SelectionMessage +
                    (controls.SelectedVehicle?.VehicleTransform != null ? $"\n속도 {controls.SelectedVehicle.VehicleBody.linearVelocity.magnitude*3.6f:F1} km/h\n{ActiveAutomaticClient?.Status}" : "");
                if(automaticStart!=null) automaticStart.interactable=ActiveAutomaticClient?.Connected == true && !string.IsNullOrEmpty(automaticStopId) && controls.SelectedVehicle.VehicleBody.linearVelocity.magnitude <= .2f;
                for(int i=0;i<vehicleButtons.Length;i++)
                {
                    string id="V0"+(i+1); bool bound=controls.HasVehicle(id);
                    vehicleButtons[i].interactable=bound;
                    bool selected=controls.IsFollowing && controls.SelectedVehicle?.OperatorVehicleId==id;
                    fleetCards[i].text=bound ? id+" · Unity 차량\n"+(selected?"선택됨 · 추적 중":"클릭하여 추적") : id+" · 웹 시연 차량\nUnity 미연결";
                    fleetCards[i].color=selected ? FixtureUiPalette.Blue : FixtureUiPalette.Ink;
                }
            }
            if (state == null) return;
            if (policy != null) policy.text = $"배차 {state.dispatch}  |  경로 탐색 {state.planner}  |  드래그 이동 · 우클릭 회전 · 휠 확대";
            int queued = state.queue == null ? 0 : state.queue.Length;
            if(metrics!=null) metrics.text = mobile ? $"차량 {map.VehicleCount}대 · 대기 {queued}건 · 완료 {map.Completed}건" :
                $"차량   {map.VehicleCount} / 3       대기 요청   {queued}       운송 완료   {map.Completed}       충전 중   {Array.FindAll(state.vehicles, v => v.phase == "CHARGING").Length}";
            if (!mobile)
            {
                int moving=Array.FindAll(state.vehicles,v=>v.phase!="READY" && v.phase!="CHARGING").Length;
                if(statCards!=null) { statCards[0].text=moving+" / "+map.VehicleCount; statCards[1].text=queued.ToString(); statCards[2].text=map.Completed.ToString(); statCards[3].text=Array.FindAll(state.vehicles,v=>v.phase=="CHARGING").Length.ToString(); }
                string value = "";
                foreach (var vehicle in state.vehicles)
                    value += $"{vehicle.id}   {vehicle.home_name}   ·   {vehicle.phase}   ·   배터리 {vehicle.soc:P0}        ";
                if (fleet != null) fleet.text = value;
                if(queue!=null) queue.text=queued==0 ? "대기 중인 요청이 없습니다." : $"대기 요청 {queued}건";
            }
        }

        private void LateUpdate()
        {
            if (worldCamera == null || hubLabels == null) return;
            if (lastScreenSize != new Vector2Int(Screen.width, Screen.height) || lastSafeArea != Screen.safeArea)
                RefreshViewport();
            for (int i = 0; i < hubs.Length; i++)
            {
                var point = worldCamera.WorldToViewportPoint(hubs[i].transform.position);
                var view = MapViewport;
                var anchor = view.position + Vector2.Scale(new Vector2(point.x, point.y), view.size);
                float halfLabel = hubLabels[i].rect.width * .5f / safeRoot.rect.width;
                float labelHeight = hubLabels[i].rect.height / safeRoot.rect.height;
                anchor.x = Mathf.Clamp(anchor.x, view.xMin + halfLabel, view.xMax - halfLabel);
                anchor.y = Mathf.Clamp(anchor.y, view.yMin, view.yMax - labelHeight);
                hubLabels[i].anchorMin = hubLabels[i].anchorMax = anchor;
                hubLabels[i].gameObject.SetActive(point.z > 0 && point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1);
            }
        }

        private void OnDestroy()
        {
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
