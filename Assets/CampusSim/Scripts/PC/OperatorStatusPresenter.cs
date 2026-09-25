using System.Globalization;
using System.Collections.Generic;
using System.Text;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace InhaExpress.Client.PC
{
    public sealed class OperatorStatusPresenter : FixtureStatusPresenter
    {
        private const string OperatorOwnerId = "pc-operator";
        private Text fleetCount, requestCount, landmarkCount, fleetList, requestList;
        private Text inspectorTitle, inspectorBody, zoneTitle, zoneBody, timelineText, mapCaption;
        private Text requestServiceChoice, requestPickupChoice, requestDropoffChoice;
        private Text requestQuantityChoice, requestStepFreeChoice, requestFeedback;
        private Button requestServiceButton, requestPickupButton, requestDropoffButton;
        private Button requestQuantityButton, requestStepFreeButton, submitRequestButton, cancelRequestButton;
        private readonly List<LandmarkDto> requestLandmarks = new List<LandmarkDto>();
        private WorldSnapshotDto currentSnapshot;
        private RequestDto cancellableOperatorRequest;
        private int pickupIndex;
        private int dropoffIndex = 1;
        private int partySize = 1;
        private double cargoKg = 5.0;
        private bool requiresStepFree;
        private ServiceType selectedServiceType = ServiceType.PASSENGER;
        private string pendingRequestMessageId;
        public override ClientRole Role => ClientRole.PC_Operator;
        protected override string Format(WorldSnapshotDto snapshot) => Describe(snapshot);

        protected override void BuildRoleView(RectTransform root)
        {
            var shade = FixtureUiFactory.Panel(root, "Dashboard Tint", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Color(0.94f, 0.96f, 0.98f, 0.42f));
            BuildLeftColumn(shade);
            BuildMapOverlay(shade);
            BuildRightColumn(shade);
            BuildTimeline(shade);
            BuildRequestPanel(shade);
        }

        private void BuildLeftColumn(RectTransform root)
        {
            var panel = FixtureUiFactory.Panel(root, "Operations Panel", new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(16, 16), new Vector2(332, -16), FixtureUiPalette.Surface);
            AddHeading(panel, "운영 현황", "Python 서버 · 합성 서비스", 18);
            var stats = FixtureUiFactory.Panel(panel, "Stats", new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(14, -158), new Vector2(-14, -72), FixtureUiPalette.Canvas);
            fleetCount = AddStat(stats, "차량", 0, 0.333f);
            requestCount = AddStat(stats, "요청", 0.333f, 0.666f);
            landmarkCount = AddStat(stats, "거점", 0.666f, 1f);
            var fleetHeader = FixtureUiFactory.Text(panel, "Fleet Header", "차량", 12, FixtureUiPalette.Muted,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(fleetHeader, 14, -190, -14, -160);
            fleetList = FixtureUiFactory.Text(panel, "Fleet List", "차량 데이터를 기다리는 중", 14,
                FixtureUiPalette.Ink);
            SetRect(fleetList, 14, -370, -14, -192);
            var requestHeader = FixtureUiFactory.Text(panel, "Request Header", "요청", 12,
                FixtureUiPalette.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(requestHeader, 14, -406, -14, -376);
            requestList = FixtureUiFactory.Text(panel, "Request List", "요청 데이터를 기다리는 중", 13,
                FixtureUiPalette.Ink);
            SetRect(requestList, 14, -620, -14, -408);
        }

        private void BuildMapOverlay(RectTransform root)
        {
            var map = FixtureUiFactory.Panel(root, "Map Frame", Vector2.zero, Vector2.one,
                new Vector2(348, 118), new Vector2(-376, -16), new Color(0.89f, 0.94f, 0.98f, 0.18f));
            var tag = FixtureUiFactory.Panel(map, "Map Tag", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(18, -55), new Vector2(180, -18), FixtureUiPalette.Surface);
            FixtureUiFactory.Text(tag, "Text", "합성 지도 · SERVER", 11, FixtureUiPalette.Blue,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            mapCaption = FixtureUiFactory.Text(map, "Caption", "첫 Snapshot을 기다리는 중", 13,
                FixtureUiPalette.Ink, TextAnchor.LowerLeft, FontStyle.Bold);
            ((RectTransform)mapCaption.transform).offsetMin = new Vector2(20, 18);
            ((RectTransform)mapCaption.transform).offsetMax = new Vector2(-20, 70);
        }

        private void BuildRightColumn(RectTransform root)
        {
            var panel = FixtureUiFactory.Panel(root, "Inspector", new Vector2(1, 0), Vector2.one,
                new Vector2(-360, 16), new Vector2(-16, -16), FixtureUiPalette.Surface);
            AddHeading(panel, "상세 정보", "선택 차량 및 안전 상태", 18);
            inspectorTitle = SectionTitle(panel, "차량", -122);
            inspectorBody = BodyText(panel, "Vehicle Body", -284, -126);
            zoneTitle = SectionTitle(panel, "Zone", -328);
            zoneBody = BodyText(panel, "Zone Body", -535, -332);
            var notice = FixtureUiFactory.Panel(panel, "Authority Notice", Vector2.zero, new Vector2(1, 0),
                new Vector2(14, 16), new Vector2(-14, 112), new Color(0.16f, 0.39f, 0.86f, 0.08f));
            FixtureUiFactory.Text(notice, "Text",
                "합성 경로·ETA·서비스 상태입니다.\nUnity Physics·센서·실제 캠퍼스 경로는 미연결입니다.", 12,
                FixtureUiPalette.Blue, TextAnchor.MiddleLeft);
            ((RectTransform)notice.GetChild(0)).offsetMin = new Vector2(12, 8);
            ((RectTransform)notice.GetChild(0)).offsetMax = new Vector2(-12, -8);
        }

        private void BuildTimeline(RectTransform root)
        {
            var panel = FixtureUiFactory.Panel(root, "Timeline", new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(348, 16), new Vector2(-376, 104), FixtureUiPalette.Surface);
            var label = FixtureUiFactory.Text(panel, "Label", "운행 기록", 11, FixtureUiPalette.Muted,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            ((RectTransform)label.transform).offsetMin = new Vector2(16, 44);
            timelineText = FixtureUiFactory.Text(panel, "Event", "Fixture 이벤트를 기다리는 중", 14,
                FixtureUiPalette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            ((RectTransform)timelineText.transform).offsetMin = new Vector2(16, 4);
            ((RectTransform)timelineText.transform).offsetMax = new Vector2(-16, -34);
        }

        private void BuildRequestPanel(RectTransform root)
        {
            if (Host.Commands == null) return;
            var panel = FixtureUiFactory.Panel(root, "Operator Request Panel",
                new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-210, -400), new Vector2(210, -12), FixtureUiPalette.Surface);
            var heading = FixtureUiFactory.Text(panel, "Request Heading", "요청 입력 · 합성 서비스",
                16, FixtureUiPalette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(heading, 14, -38, -14, -12);

            requestServiceButton = RequestButton(panel, "Service Type", -44, -78,
                FixtureUiPalette.Ink, out requestServiceChoice);
            requestServiceButton.onClick.AddListener(CycleServiceType);
            requestPickupButton = RequestButton(panel, "Pickup Landmark", -82, -116,
                FixtureUiPalette.Canvas, out requestPickupChoice);
            requestPickupButton.onClick.AddListener(() => CycleLandmark(true));
            requestDropoffButton = RequestButton(panel, "Dropoff Landmark", -120, -154,
                FixtureUiPalette.Canvas, out requestDropoffChoice);
            requestDropoffButton.onClick.AddListener(() => CycleLandmark(false));
            requestQuantityButton = RequestButton(panel, "Request Quantity", -158, -192,
                FixtureUiPalette.Canvas, out requestQuantityChoice);
            requestQuantityButton.onClick.AddListener(CycleQuantity);
            requestStepFreeButton = RequestButton(panel, "Step Free", -196, -230,
                FixtureUiPalette.Canvas, out requestStepFreeChoice);
            requestStepFreeButton.onClick.AddListener(ToggleStepFree);
            submitRequestButton = RequestButton(panel, "Submit Request", -234, -270,
                FixtureUiPalette.Blue, out _);
            submitRequestButton.onClick.AddListener(CreateOperatorRequest);
            cancelRequestButton = RequestButton(panel, "Cancel Operator Request", -274, -310,
                FixtureUiPalette.Red, out _);
            cancelRequestButton.onClick.AddListener(CancelOperatorRequest);
            requestFeedback = FixtureUiFactory.Text(panel, "Request Feedback",
                "요청 값은 서버에서 검증됩니다.", 11, FixtureUiPalette.Muted, TextAnchor.MiddleLeft);
            SetRect(requestFeedback, 14, -344, -14, -314);
            UpdateRequestControls();
        }

        private static Button RequestButton(RectTransform parent, string name, float top, float bottom,
            Color color, out Text label)
        {
            var button = FixtureUiFactory.Button(parent, name, "선택", color, out label);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.offsetMin = new Vector2(14, bottom);
            rect.offsetMax = new Vector2(-14, top);
            return button;
        }

        protected override void RenderSnapshot(WorldSnapshotDto snapshot)
        {
            currentSnapshot = snapshot;
            UpdateRequestLandmarks(snapshot.Landmarks);
            cancellableOperatorRequest = null;
            for (int i = snapshot.Requests.Count - 1; i >= 0; i--)
            {
                var request = snapshot.Requests[i];
                if (request.OwnerId == OperatorOwnerId &&
                    (request.Status == RequestStatus.QUEUED || request.Status == RequestStatus.ASSIGNED))
                {
                    cancellableOperatorRequest = request;
                    break;
                }
            }
            UpdateRequestControls();
            fleetCount.text = snapshot.Vehicles.Count.ToString(CultureInfo.InvariantCulture);
            requestCount.text = snapshot.Requests.Count.ToString(CultureInfo.InvariantCulture);
            landmarkCount.text = snapshot.Landmarks.Count.ToString(CultureInfo.InvariantCulture);
            var fleet = new StringBuilder();
            foreach (var vehicle in snapshot.Vehicles)
                fleet.AppendLine($"●  {vehicle.Id}   {FixtureUiText.Mission(vehicle.MissionState)}\n    {FixtureUiText.Motion(vehicle.MotionState)}  ·  {vehicle.SpeedMps:F1} m/s");
            fleetList.text = fleet.ToString();
            var requests = new StringBuilder();
            foreach (var request in snapshot.Requests)
                requests.AppendLine($"{request.Id}  ·  {FixtureUiText.RequestStatus(request.Status)}\n{request.PickupLandmarkId}  →  {request.DropoffLandmarkId}\n");
            requestList.text = requests.ToString();
            if (snapshot.Vehicles.Count > 0)
            {
                var vehicle = snapshot.Vehicles[0];
                inspectorTitle.text = vehicle.Id.ToUpperInvariant();
                inspectorBody.text = $"임무      {FixtureUiText.Mission(vehicle.MissionState)}\n운행      {FixtureUiText.Motion(vehicle.MotionState)}\n" +
                    $"속도      {vehicle.SpeedMps:F1} m/s\n배터리    {vehicle.BatteryWh?.ToString("F0") ?? "미확인"} Wh\n" +
                    $"경로      {vehicle.RouteId ?? "미배정"}\n사유      {FixtureUiText.Reason(vehicle.Reason)}";
            }
            if (snapshot.Zones.Count > 0)
            {
                var zone = snapshot.Zones[0];
                zoneTitle.text = $"ZONE · {FixtureUiText.Zone(zone.Status)}";
                zoneBody.text = $"관측 밀도     {Value(zone.ObservedDensity)}\n" +
                    $"EMA 밀도      {Value(zone.EmaDensity)}\n예측 밀도     {Value(zone.PriorDensity)}\n" +
                    $"사유          {FixtureUiText.Reason(zone.Reason)}\n\n이 Fixture에서 관측값과 EMA는 미확인 상태입니다.";
            }
            mapCaption.text = $"run  {snapshot.RunId}\nmap  {snapshot.MapVersion}   ·   schema {snapshot.SchemaVersion}";
            timelineText.text = Timeline(snapshot);
        }

        protected override void OnCommandAcknowledged(ServiceCommandAckDto acknowledgement)
        {
            if (acknowledgement.MessageId != pendingRequestMessageId) return;
            pendingRequestMessageId = null;
            requestFeedback.text = acknowledgement.Accepted
                ? acknowledgement.CommandType == "cancel_request"
                    ? "운영자 취소 요청을 접수했습니다."
                    : "요청을 서버가 접수했습니다."
                : $"요청 실패: {acknowledgement.ErrorCode}";
            requestFeedback.color = acknowledgement.Accepted ? FixtureUiPalette.Green : FixtureUiPalette.Red;
            UpdateRequestControls();
        }

        protected override void RefreshConnectionState() => UpdateRequestControls();

        private void UpdateRequestLandmarks(IReadOnlyList<LandmarkDto> landmarks)
        {
            string pickupId = SelectedLandmarkId(pickupIndex);
            string dropoffId = SelectedLandmarkId(dropoffIndex);
            requestLandmarks.Clear();
            foreach (var landmark in landmarks) requestLandmarks.Add(landmark);
            pickupIndex = LandmarkIndex(pickupId, 0);
            dropoffIndex = LandmarkIndex(dropoffId, requestLandmarks.Count > 1 ? 1 : 0);
            if (requestLandmarks.Count > 1 && pickupIndex == dropoffIndex)
                dropoffIndex = (pickupIndex + 1) % requestLandmarks.Count;
        }

        private int LandmarkIndex(string id, int fallback)
        {
            if (id != null)
                for (int i = 0; i < requestLandmarks.Count; i++)
                    if (requestLandmarks[i].Id == id) return i;
            return requestLandmarks.Count == 0 ? 0 : Mathf.Clamp(fallback, 0, requestLandmarks.Count - 1);
        }

        private string SelectedLandmarkId(int index) =>
            index >= 0 && index < requestLandmarks.Count ? requestLandmarks[index].Id : null;

        private string SelectedLandmarkName(int index) =>
            index >= 0 && index < requestLandmarks.Count ? requestLandmarks[index].Name : "거점 없음";

        private void CycleLandmark(bool pickup)
        {
            if (requestLandmarks.Count < 2) return;
            int next = ((pickup ? pickupIndex : dropoffIndex) + 1) % requestLandmarks.Count;
            if (pickup)
            {
                if (next == dropoffIndex) next = (next + 1) % requestLandmarks.Count;
                pickupIndex = next;
            }
            else
            {
                if (next == pickupIndex) next = (next + 1) % requestLandmarks.Count;
                dropoffIndex = next;
            }
            UpdateRequestControls();
        }

        private void CycleServiceType()
        {
            selectedServiceType = selectedServiceType == ServiceType.PASSENGER
                ? ServiceType.CARGO : ServiceType.PASSENGER;
            UpdateRequestControls();
        }

        private void CycleQuantity()
        {
            if (selectedServiceType == ServiceType.PASSENGER)
                partySize = partySize >= 4 ? 1 : partySize + 1;
            else
                cargoKg = cargoKg >= 20.0 ? 1.0 : cargoKg + 1.0;
            UpdateRequestControls();
        }

        private void ToggleStepFree()
        {
            requiresStepFree = !requiresStepFree;
            UpdateRequestControls();
        }

        private void UpdateRequestControls()
        {
            if (requestServiceChoice == null) return;
            requestServiceChoice.text = selectedServiceType == ServiceType.PASSENGER
                ? "서비스 유형: 승객" : "서비스 유형: 배송";
            requestPickupChoice.text = "출발: " + SelectedLandmarkName(pickupIndex);
            requestDropoffChoice.text = "목적지: " + SelectedLandmarkName(dropoffIndex);
            requestQuantityChoice.text = selectedServiceType == ServiceType.PASSENGER
                ? $"승객 그룹: {partySize}명 (누르면 변경)"
                : $"화물: {cargoKg:F0} kg (누르면 변경)";
            requestStepFreeChoice.text = "계단 없는 승하차: " + (requiresStepFree ? "예" : "아니요");
            requestStepFreeButton.gameObject.SetActive(selectedServiceType == ServiceType.PASSENGER);

            bool syntheticMap = currentSnapshot != null &&
                currentSnapshot.MapVersion.StartsWith("synthetic-", System.StringComparison.Ordinal);
            bool choicesValid = requestLandmarks.Count > 1 &&
                SelectedLandmarkId(pickupIndex) != null && SelectedLandmarkId(dropoffIndex) != null &&
                SelectedLandmarkId(pickupIndex) != SelectedLandmarkId(dropoffIndex);
            bool connected = Host != null && Host.ConnectionState == ConnectionState.Connected;
            bool canSubmit = Host?.Commands != null && connected && syntheticMap && choicesValid &&
                pendingRequestMessageId == null;
            requestServiceButton.interactable = canSubmit;
            requestPickupButton.interactable = canSubmit;
            requestDropoffButton.interactable = canSubmit;
            requestQuantityButton.interactable = canSubmit;
            requestStepFreeButton.interactable = canSubmit && selectedServiceType == ServiceType.PASSENGER;
            submitRequestButton.interactable = canSubmit;
            cancelRequestButton.interactable = connected && Host?.Commands != null &&
                cancellableOperatorRequest != null && pendingRequestMessageId == null;
            if (!syntheticMap)
            {
                requestFeedback.text = "합성 map_version에서만 이 요청 입력을 사용할 수 있습니다.";
                requestFeedback.color = FixtureUiPalette.Amber;
            }
            else if (requestFeedback.text == "합성 map_version에서만 이 요청 입력을 사용할 수 있습니다.")
            {
                requestFeedback.text = "요청 값은 서버에서 검증됩니다.";
                requestFeedback.color = FixtureUiPalette.Muted;
            }
        }

        private void CreateOperatorRequest()
        {
            if (Host?.Commands == null) return;
            string pickupId = SelectedLandmarkId(pickupIndex);
            string dropoffId = SelectedLandmarkId(dropoffIndex);
            if (pickupId == null || dropoffId == null || pickupId == dropoffId) return;
            try
            {
                string messageId;
                if (selectedServiceType == ServiceType.PASSENGER)
                {
                    var command = new PassengerRequestCommandDto(pickupId, dropoffId,
                        new ServiceNeedsDto(requiresStepFree, 0, false), partySize);
                    messageId = command.MessageId;
                    Host.Commands.SendPassengerRequest(command);
                }
                else
                {
                    var command = new CargoRequestCommandDto(pickupId, dropoffId, cargoKg);
                    messageId = command.MessageId;
                    Host.Commands.SendCargoRequest(command);
                }
                pendingRequestMessageId = messageId;
                requestFeedback.text = "서버 응답을 기다리는 중…";
                requestFeedback.color = FixtureUiPalette.Muted;
                UpdateRequestControls();
            }
            catch (System.Exception error)
            {
                pendingRequestMessageId = null;
                requestFeedback.text = "요청 전송 실패: " + error.Message;
                requestFeedback.color = FixtureUiPalette.Red;
                UpdateRequestControls();
            }
        }

        private void CancelOperatorRequest()
        {
            if (Host?.Commands == null || cancellableOperatorRequest == null || pendingRequestMessageId != null)
                return;
            try
            {
                var command = new CancelRequestCommandDto(cancellableOperatorRequest.Id);
                pendingRequestMessageId = command.MessageId;
                Host.Commands.SendCancelRequest(command);
                requestFeedback.text = "취소 응답을 기다리는 중…";
                requestFeedback.color = FixtureUiPalette.Muted;
                UpdateRequestControls();
            }
            catch (System.Exception error)
            {
                pendingRequestMessageId = null;
                requestFeedback.text = "취소 전송 실패: " + error.Message;
                requestFeedback.color = FixtureUiPalette.Red;
                UpdateRequestControls();
            }
        }

        public static string Describe(WorldSnapshotDto snapshot)
        {
            var text = new StringBuilder();
            text.AppendLine($"Project {snapshot.ProjectVersion} | schema {snapshot.SchemaVersion}");
            text.AppendLine($"Map {snapshot.MapVersion}\nRun {snapshot.RunId}");
            text.AppendLine($"Tick {snapshot.SimulationTick} | seq {snapshot.Sequence} | t={snapshot.SimulationTimeS.ToString("F1", CultureInfo.InvariantCulture)} s");
            text.AppendLine($"Vehicles {snapshot.Vehicles.Count} | Requests {snapshot.Requests.Count} | Landmarks {snapshot.Landmarks.Count}");
            foreach (var vehicle in snapshot.Vehicles)
                text.AppendLine($"\n{vehicle.Id}: {vehicle.MissionState} / {vehicle.MotionState}\n{vehicle.SpeedMps:F1} m/s | battery {vehicle.BatteryWh?.ToString("F0") ?? "unknown"} Wh | reason {vehicle.Reason}");
            foreach (var request in snapshot.Requests)
                text.AppendLine($"\n{request.Id}: {request.Status}\n{request.PickupLandmarkId} -> {request.DropoffLandmarkId}");
            foreach (var zone in snapshot.Zones)
                text.AppendLine($"\n{zone.Id}: {zone.Status}\nObserved {zone.ObservedDensity?.ToString("F2") ?? "unknown"} | EMA {zone.EmaDensity?.ToString("F2") ?? "unknown"} | prior {zone.PriorDensity?.ToString("F2") ?? "unknown"} persons/m2");
            return text.ToString();
        }

        private static string Timeline(WorldSnapshotDto snapshot)
        {
            if (snapshot.Requests.Count == 0) return "진행 중인 Fixture 요청 없음";
            var request = snapshot.Requests[0];
            if (snapshot.Vehicles.Count > 0 && snapshot.Vehicles[0].MotionState == VehicleMotionState.YIELDING)
                return "24–27초  ·  보행자 감지로 차량 양보 중";
            if (snapshot.Vehicles.Count > 0 && snapshot.Vehicles[0].Reason == ReasonCode.CROWD_AVOIDANCE)
                return "20초  ·  혼잡 회피 경로 선택";
            return $"요청 진행  ·  {FixtureUiText.RequestStatus(request.Status)}";
        }

        private static string Value(double? value) => value.HasValue ? $"{value.Value:F2}명/m²" : "미확인";

        private static void AddHeading(RectTransform panel, string title, string subtitle, int size)
        {
            var heading = FixtureUiFactory.Text(panel, "Heading", title, size, FixtureUiPalette.Ink,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(heading, 14, -44, -14, -10);
            var secondary = FixtureUiFactory.Text(panel, "Subtitle", subtitle, 12, FixtureUiPalette.Muted, TextAnchor.MiddleLeft);
            SetRect(secondary, 14, -68, -14, -44);
        }

        private static Text AddStat(RectTransform parent, string label, float min, float max)
        {
            var value = FixtureUiFactory.Text(parent, label + " Value", "0", 24, FixtureUiPalette.Ink,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            var rect = (RectTransform)value.transform;
            rect.anchorMin = new Vector2(min, 0.28f); rect.anchorMax = new Vector2(max, 1); rect.offsetMin = rect.offsetMax = Vector2.zero;
            var caption = FixtureUiFactory.Text(parent, label, label, 11, FixtureUiPalette.Muted, TextAnchor.MiddleCenter);
            rect = (RectTransform)caption.transform;
            rect.anchorMin = new Vector2(min, 0); rect.anchorMax = new Vector2(max, 0.35f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            return value;
        }

        private static Text SectionTitle(RectTransform panel, string title, float top)
        {
            var text = FixtureUiFactory.Text(panel, title + " Title", title, 15, FixtureUiPalette.Blue,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(text, 14, top, -14, top + 36);
            return text;
        }

        private static Text BodyText(RectTransform panel, string name, float bottom, float top)
        {
            var text = FixtureUiFactory.Text(panel, name, "데이터를 기다리는 중", 13, FixtureUiPalette.Ink);
            SetRect(text, 14, bottom, -14, top);
            return text;
        }

        private static void SetRect(Text text, float left, float bottom, float right, float top)
        {
            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1);
            rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(right, top);
        }
    }
}
