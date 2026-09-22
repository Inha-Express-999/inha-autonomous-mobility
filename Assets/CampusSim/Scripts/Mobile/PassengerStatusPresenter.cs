using System.Text;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace InhaExpress.Client.Mobile
{
    public sealed class PassengerStatusPresenter : FixtureStatusPresenter
    {
        private Text statusLabel, statusTitle, routeText, etaValue, vehicleText, reasonText, progressText, mapHint;
        private Image statusAccent;
        public override ClientRole Role => ClientRole.Mobile_Passenger;
        protected override string Format(WorldSnapshotDto snapshot) => Describe(snapshot);

        protected override void BuildRoleView(RectTransform root)
        {
            var mapGlass = FixtureUiFactory.Panel(root, "Passenger Map", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Color(0.86f, 0.92f, 0.97f, 0.12f));
            var mapChip = FixtureUiFactory.Panel(mapGlass, "Map Chip", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(16, -48), new Vector2(150, -14), FixtureUiPalette.Surface);
            FixtureUiFactory.Text(mapChip, "Text", "캠퍼스 보기", 11, FixtureUiPalette.Blue,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            mapHint = FixtureUiFactory.Text(mapGlass, "Map Hint", "요청 위치를 확인하는 중…", 12,
                FixtureUiPalette.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            var hintRect = (RectTransform)mapHint.transform;
            hintRect.anchorMin = new Vector2(0, 1); hintRect.anchorMax = new Vector2(1, 1);
            hintRect.offsetMin = new Vector2(18, -92); hintRect.offsetMax = new Vector2(-18, -54);

            var marker = FixtureUiFactory.Panel(mapGlass, "Vehicle Marker", new Vector2(0.62f, 0.66f),
                new Vector2(0.62f, 0.66f), new Vector2(-20, -20), new Vector2(20, 20), FixtureUiPalette.Blue);
            FixtureUiFactory.Text(marker, "Text", "V1", 11, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);

            var sheet = FixtureUiFactory.Panel(root, "Trip Bottom Sheet", Vector2.zero, new Vector2(1, 0),
                new Vector2(12, 14), new Vector2(-12, 470), FixtureUiPalette.Surface);
            var handle = FixtureUiFactory.Panel(sheet, "Handle", new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-24, -14), new Vector2(24, -10), FixtureUiPalette.Line);
            handle.GetComponent<Image>().raycastTarget = false;
            statusAccent = FixtureUiFactory.Panel(sheet, "Status Accent", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(16, -76), new Vector2(22, -28), FixtureUiPalette.Blue).GetComponent<Image>();
            statusLabel = FixtureUiFactory.Text(sheet, "Status Label", "요청 상태", 10,
                FixtureUiPalette.Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(statusLabel, 32, -48, -18, -26);
            statusTitle = FixtureUiFactory.Text(sheet, "Status", "연결 중…", 22,
                FixtureUiPalette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(statusTitle, 32, -82, -18, -48);
            routeText = FixtureUiFactory.Text(sheet, "Route", "출발지  →  목적지", 15,
                FixtureUiPalette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(routeText, 18, -132, -18, -92);

            var etaCard = FixtureUiFactory.Panel(sheet, "ETA Card", new Vector2(0, 1), new Vector2(0.45f, 1),
                new Vector2(18, -204), new Vector2(-5, -142), FixtureUiPalette.Canvas);
            FixtureUiFactory.Text(etaCard, "Label", "ETA", 10, FixtureUiPalette.Muted,
                TextAnchor.UpperCenter, FontStyle.Bold);
            etaValue = FixtureUiFactory.Text(etaCard, "Value", "—", 20, FixtureUiPalette.Blue,
                TextAnchor.LowerCenter, FontStyle.Bold);
            var vehicleCard = FixtureUiFactory.Panel(sheet, "Vehicle Card", new Vector2(0.45f, 1), Vector2.one,
                new Vector2(5, -204), new Vector2(-18, -142), FixtureUiPalette.Canvas);
            FixtureUiFactory.Text(vehicleCard, "Label", "배정 차량", 10, FixtureUiPalette.Muted,
                TextAnchor.UpperCenter, FontStyle.Bold);
            vehicleText = FixtureUiFactory.Text(vehicleCard, "Value", "미배정", 16,
                FixtureUiPalette.Ink, TextAnchor.LowerCenter, FontStyle.Bold);

            progressText = FixtureUiFactory.Text(sheet, "Progress", "●  ○  ○  ○", 15,
                FixtureUiPalette.Blue, TextAnchor.MiddleCenter, FontStyle.Bold);
            SetRect(progressText, 18, -250, -18, -214);
            reasonText = FixtureUiFactory.Text(sheet, "Reason", "Fixture 경로를 준비하는 중입니다.", 13,
                FixtureUiPalette.Ink, TextAnchor.UpperLeft);
            SetRect(reasonText, 18, -330, -18, -262);
            var warning = FixtureUiFactory.Panel(sheet, "Fixture Notice", Vector2.zero, new Vector2(1, 0),
                new Vector2(18, 18), new Vector2(-18, 108), new Color(0.94f, 0.62f, 0.12f, 0.10f));
            var warningText = FixtureUiFactory.Text(warning, "Text",
                "데모 전용\n 위치·접근성·ETA를 사용하는 고정 데이터 화면이며 실제 차량 호출이 아닙니다.", 11,
                FixtureUiPalette.Amber, TextAnchor.MiddleLeft, FontStyle.Bold);
            ((RectTransform)warningText.transform).offsetMin = new Vector2(12, 8);
            ((RectTransform)warningText.transform).offsetMax = new Vector2(-12, -8);
        }

        protected override void RenderSnapshot(WorldSnapshotDto snapshot)
        {
            mapHint.text = $"{snapshot.MapVersion}  ·  project {snapshot.ProjectVersion}";
            if (snapshot.Requests.Count == 0)
            {
                statusLabel.text = "활성 요청 없음";
                statusTitle.text = "목적지를 선택하세요";
                routeText.text = "이 Fixture 사용자에게 연결된 요청이 없습니다";
                etaValue.text = "—";
                vehicleText.text = "미배정";
                reasonText.text = "현재는 검색 가능한 Fixture 거점만 선택할 수 있습니다.";
                progressText.text = "○  ○  ○  ○";
                return;
            }
            var request = snapshot.Requests[0];
            statusLabel.text = FixtureUiText.RequestStatus(request.Status);
            statusTitle.text = StatusText(request.Status);
            routeText.text = $"{DisplayLandmark(request.PickupLandmarkId)}   →   {DisplayLandmark(request.DropoffLandmarkId)}\n" +
                $"{request.PickupStopId ?? "Stop 결정 중"}  ·  {request.DropoffStopId ?? "Stop 결정 중"}";
            etaValue.text = request.EtaS.HasValue ? $"{request.EtaS.Value:F0}초" : "—";
            vehicleText.text = request.VehicleId ?? "미배정";
            reasonText.text = Guidance(snapshot, request);
            progressText.text = Progress(request.Status);
            statusAccent.color = IsComplete(request.Status) ? FixtureUiPalette.Green : FixtureUiPalette.Blue;
        }

        public static string Describe(WorldSnapshotDto snapshot)
        {
            var text = new StringBuilder();
            text.AppendLine($"Project {snapshot.ProjectVersion}\nMap {snapshot.MapVersion}");
            if (snapshot.Requests.Count == 0) text.AppendLine("이 Fixture 사용자에게 연결된 요청이 없습니다.");
            foreach (var request in snapshot.Requests)
            {
                text.AppendLine($"\n{request.Id}: {StatusText(request.Status)}");
                text.AppendLine($"{request.PickupLandmarkId} -> {request.DropoffLandmarkId}");
                text.AppendLine($"승차 Stop: {request.PickupStopId ?? "미결정"}\n하차 Stop: {request.DropoffStopId ?? "미결정"}");
                text.AppendLine($"차량: {request.VehicleId ?? "미배정"}");
                text.AppendLine($"ETA: {request.EtaS?.ToString("F1") ?? "미확인"}초");
            }
            foreach (var vehicle in snapshot.Vehicles)
            {
                if (vehicle.MotionState == VehicleMotionState.YIELDING && vehicle.Reason == ReasonCode.PEDESTRIAN)
                    text.AppendLine("보행자에게 양보 중입니다(기록 예시).");
                else if (vehicle.MissionState == VehicleMissionState.TO_DROPOFF && vehicle.Reason == ReasonCode.CROWD_AVOIDANCE)
                    text.AppendLine("혼잡 구역을 우회 중입니다(기록 예시).");
            }
            return text.ToString();
        }

        public static string StatusText(RequestStatus status)
        {
            switch (status)
            {
                case RequestStatus.CREATED: return "요청을 만들었어요";
                case RequestStatus.VALIDATED: return "승차 위치가 확인됐어요";
                case RequestStatus.QUEUED: return "차량을 찾고 있어요";
                case RequestStatus.ASSIGNED: return "차량이 오고 있어요";
                case RequestStatus.PICKUP_SERVICE: return "탑승을 준비해 주세요";
                case RequestStatus.IN_TRANSIT: return "목적지로 이동 중이에요";
                case RequestStatus.DROPOFF_SERVICE: return "도착했어요 · 아직 완료 전";
                case RequestStatus.COMPLETED: return "이용 완료 · 최종 보행은 미확인";
                default: return FixtureUiText.RequestStatus(status);
            }
        }

        private static string Guidance(WorldSnapshotDto snapshot, RequestDto request)
        {
            foreach (var vehicle in snapshot.Vehicles)
            {
                if (vehicle.MotionState == VehicleMotionState.YIELDING && vehicle.Reason == ReasonCode.PEDESTRIAN)
                    return "보행자에게 양보 중입니다. 안전이 확인되면 다시 출발합니다.";
                if (vehicle.MissionState == VehicleMissionState.TO_DROPOFF && vehicle.Reason == ReasonCode.CROWD_AVOIDANCE)
                    return "혼잡 구역을 피해 이동하고 있습니다.";
            }
            switch (request.Status)
            {
                case RequestStatus.CREATED: return "Fixture 요청이 생성되었습니다.";
                case RequestStatus.VALIDATED: return "계단 없는 접근 경로와 Fixture Stop을 확인했습니다.";
                case RequestStatus.QUEUED: return "요청 조건에 맞는 차량을 배정하고 있습니다.";
                case RequestStatus.ASSIGNED: return "선택된 승차 Stop에서 차량을 기다려 주세요.";
                case RequestStatus.PICKUP_SERVICE: return "차량이 정차해 탑승을 진행하고 있습니다.";
                case RequestStatus.IN_TRANSIT: return "하차 Stop까지 Fixture 운행 경로를 표시합니다.";
                case RequestStatus.DROPOFF_SERVICE: return "하차를 진행 중이며 아직 이용 완료 전입니다.";
                case RequestStatus.COMPLETED: return "하차가 완료되었습니다. 최종 보행 도착은 확인되지 않았습니다.";
                default: return request.Reason == ReasonCode.UNKNOWN ? "사유가 제공되지 않았습니다." : FixtureUiText.Reason(request.Reason);
            }
        }

        private static string Progress(RequestStatus status)
        {
            if (status == RequestStatus.COMPLETED) return "●  ●  ●  ●   완료";
            if (status == RequestStatus.DROPOFF_SERVICE || status == RequestStatus.IN_TRANSIT) return "●  ●  ●  ○   이동 중";
            if (status == RequestStatus.PICKUP_SERVICE || status == RequestStatus.ASSIGNED) return "●  ●  ○  ○   승차";
            return "●  ○  ○  ○   배차";
        }

        private static bool IsComplete(RequestStatus status) => status == RequestStatus.COMPLETED;
        private static string DisplayLandmark(string id) => id == "landmark-0" ? "Fixture A" : id == "landmark-1" ? "Fixture B" : id;

        private static void SetRect(Text text, float left, float bottom, float right, float top)
        {
            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1);
            rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(right, top);
        }
    }
}
