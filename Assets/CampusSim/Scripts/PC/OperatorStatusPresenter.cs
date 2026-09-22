using System.Globalization;
using System.Text;
using InhaExpress.Client.Domain;
using InhaExpress.Client.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace InhaExpress.Client.PC
{
    public sealed class OperatorStatusPresenter : FixtureStatusPresenter
    {
        private Text fleetCount, requestCount, landmarkCount, fleetList, requestList;
        private Text inspectorTitle, inspectorBody, zoneTitle, zoneBody, timelineText, mapCaption;
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
        }

        private void BuildLeftColumn(RectTransform root)
        {
            var panel = FixtureUiFactory.Panel(root, "Operations Panel", new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(16, 16), new Vector2(332, -16), FixtureUiPalette.Surface);
            AddHeading(panel, "운영 현황", "Fixture 실시간 요약", 18);
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
            FixtureUiFactory.Text(tag, "Text", "캠퍼스 3D · LIVE", 11, FixtureUiPalette.Blue,
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
                "Fixture 데이터 전용\nPhysics·센서·배차 및 실제 캠퍼스 좌표와 연결되지 않았습니다.", 12,
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

        protected override void RenderSnapshot(WorldSnapshotDto snapshot)
        {
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
