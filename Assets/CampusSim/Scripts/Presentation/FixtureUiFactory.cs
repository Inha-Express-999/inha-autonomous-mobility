using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Domain = InhaExpress.Client.Domain;

namespace InhaExpress.Client.Presentation
{
    public static class FixtureUiPalette
    {
        public static readonly Color Ink = Hex("172033");
        public static readonly Color Muted = Hex("6E7787");
        public static readonly Color Canvas = Hex("F4F6FA", 0.96f);
        public static readonly Color Surface = Hex("FFFFFF");
        public static readonly Color Line = Hex("E4E8F0");
        public static readonly Color Blue = Hex("2864DC");
        public static readonly Color Green = Hex("1C9B68");
        public static readonly Color Amber = Hex("E39B22");
        public static readonly Color Red = Hex("D95757");
        public static readonly Color MapGlass = Hex("EAF0F7", 0.84f);

        private static Color Hex(string value, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString("#" + value, out var color);
            color.a = alpha;
            return color;
        }
    }

    public static class FixtureUiText
    {
        public static string RequestStatus(Domain.RequestStatus status)
        {
            switch (status)
            {
                case Domain.RequestStatus.CREATED: return "요청 생성";
                case Domain.RequestStatus.VALIDATED: return "요청 확인";
                case Domain.RequestStatus.QUEUED: return "배차 대기";
                case Domain.RequestStatus.ASSIGNED: return "차량 배정";
                case Domain.RequestStatus.PICKUP_SERVICE: return "승차 진행";
                case Domain.RequestStatus.IN_TRANSIT: return "운행 중";
                case Domain.RequestStatus.DROPOFF_SERVICE: return "하차 진행";
                case Domain.RequestStatus.COMPLETED: return "운행 완료";
                case Domain.RequestStatus.REJECTED: return "요청 거부";
                case Domain.RequestStatus.CANCELLED: return "요청 취소";
                case Domain.RequestStatus.EXPIRED: return "요청 만료";
                case Domain.RequestStatus.FAILED: return "요청 실패";
                default: return status.ToString();
            }
        }

        public static string Mission(Domain.VehicleMissionState state)
        {
            switch (state)
            {
                case Domain.VehicleMissionState.IDLE: return "대기";
                case Domain.VehicleMissionState.TO_PICKUP: return "승차 지점 이동";
                case Domain.VehicleMissionState.PICKUP_SERVICE: return "승차 지원";
                case Domain.VehicleMissionState.TO_DROPOFF: return "목적지 이동";
                case Domain.VehicleMissionState.DROPOFF_SERVICE: return "하차 지원";
                case Domain.VehicleMissionState.TO_CHARGER: return "충전소 이동";
                case Domain.VehicleMissionState.CHARGING: return "충전 중";
                case Domain.VehicleMissionState.OUT_OF_SERVICE: return "운행 불가";
                default: return state.ToString();
            }
        }

        public static string Motion(Domain.VehicleMotionState state)
        {
            switch (state)
            {
                case Domain.VehicleMotionState.DRIVING: return "주행 중";
                case Domain.VehicleMotionState.YIELDING: return "양보 중";
                case Domain.VehicleMotionState.REPLANNING: return "경로 재탐색";
                case Domain.VehicleMotionState.EMERGENCY_STOP: return "비상 정지";
                case Domain.VehicleMotionState.WAITING_RESOURCE: return "대기 중";
                default: return state.ToString();
            }
        }

        public static string Zone(Domain.ZoneStatus status)
        {
            switch (status)
            {
                case Domain.ZoneStatus.NORMAL: return "정상";
                case Domain.ZoneStatus.CAUTION: return "주의";
                case Domain.ZoneStatus.AVOID: return "우회 권고";
                case Domain.ZoneStatus.CLOSED: return "진입 금지";
                default: return status.ToString();
            }
        }

        public static string Reason(Domain.ReasonCode reason)
        {
            switch (reason)
            {
                case Domain.ReasonCode.UNKNOWN: return "미제공";
                case Domain.ReasonCode.CROWD_AVOIDANCE: return "혼잡 회피";
                case Domain.ReasonCode.ZONE_CLOSED: return "구역 폐쇄";
                case Domain.ReasonCode.NO_ACCESSIBLE_ALTERNATIVE: return "접근 가능한 대안 없음";
                case Domain.ReasonCode.PEDESTRIAN: return "보행자";
                case Domain.ReasonCode.ROAD_CLOSED: return "도로 폐쇄";
                case Domain.ReasonCode.VEHICLE_FAILURE: return "차량 고장";
                default: return reason.ToString();
            }
        }
    }

    public sealed class SafeAreaPanel : MonoBehaviour
    {
        private Rect lastSafeArea;
        private Vector2Int lastScreen;

        private void OnEnable() => Apply();
        private void Update()
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (lastSafeArea != Screen.safeArea || lastScreen != size) Apply();
        }

        private void Apply()
        {
            var safe = Screen.safeArea;
            lastSafeArea = safe;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            rect.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }

    public static class FixtureUiFactory
    {
        private static Font cachedFont;
        public static Font Font => cachedFont != null ? cachedFont : cachedFont = CreateFont();

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        public static RectTransform Panel(Transform parent, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var rect = Rect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }

        public static Text Text(Transform parent, string name, string value, int size, Color color,
            TextAnchor alignment = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(Transform parent, string name, string label, Color background,
            out Text labelText)
        {
            var rect = Rect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = background;
            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = background;
            colors.highlightedColor = Color.Lerp(background, Color.white, 0.12f);
            colors.pressedColor = Color.Lerp(background, Color.black, 0.12f);
            button.colors = colors;
            labelText = Text(rect, "Label", label, 15, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            return button;
        }

        public static void EnsureEventSystem(Transform parent)
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("Fixture UI EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(parent, false);
        }

        private static Font CreateFont()
        {
            string[] paths = Font.GetPathsToOSFonts();
            foreach (var name in new[] { "Malgun Gothic", "Noto Sans CJK KR", "Apple SD Gothic Neo", "sans-serif", "Arial" })
                if (System.Array.Exists(paths, path => path.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0))
                    return Font.CreateDynamicFontFromOSFont(name, 18);
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
