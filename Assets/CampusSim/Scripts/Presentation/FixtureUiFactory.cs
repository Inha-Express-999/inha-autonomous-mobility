using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Domain = InhaExpress.Client.Domain;

namespace InhaExpress.Client.Presentation
{
    public static class FixtureUiPalette
    {
        public static readonly Color Ink = Hex("16233B");
        public static readonly Color Muted = Hex("6C7A90");
        public static readonly Color Canvas = Hex("F4F7FB", 0.96f);
        public static readonly Color Surface = Hex("FFFFFF");
        public static readonly Color Line = Hex("DDE5F0");
        public static readonly Color Blue = Hex("1677FF");
        public static readonly Color BluePressed = Hex("0B63E5");
        public static readonly Color BlueTint = Hex("EAF3FF");
        public static readonly Color Disabled = Hex("C7D1DE");
        public static readonly Color Navy = Hex("10244A");
        public static readonly Color Green = Hex("18B86A");
        public static readonly Color Amber = Hex("F4A41D");
        public static readonly Color Red = Hex("F04444");
        public static readonly Color MapGlass = Hex("EAF0F7", 0.84f);

        public static Color Tint(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

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
                case Domain.ReasonCode.SENSOR_DATA_STALE: return "센서 관측 지연";
                case Domain.ReasonCode.SENSOR_INVALID: return "센서 관측 무효";
                case Domain.ReasonCode.OBSTACLE_STOP: return "전방 장애물 정지";
                case Domain.ReasonCode.SAFETY_RESUME_HOLD: return "안전 재확인 중";
                case Domain.ReasonCode.STALE_LOCALIZATION: return "차량 위치 정보 지연";
                case Domain.ReasonCode.RESOURCE_WAIT: return "통로 진입 순서 대기";
                case Domain.ReasonCode.RESOURCE_STATE_UNAVAILABLE: return "통로 상태 확인 필요";
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
        private static Sprite roundedSprite;
        private static Texture2D roundedTexture;
        public static Font Font => cachedFont != null ? cachedFont : cachedFont = CreateFont();

        private static Sprite RoundedSprite
        {
            get
            {
                if (roundedSprite != null) return roundedSprite;
                const int size = 64;
                const float radius = 16f;
                roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Campus UI Rounded Surface", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
                };
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        var point = new Vector2(x + 0.5f, y + 0.5f);
                        var center = new Vector2(Mathf.Clamp(point.x, radius, size - radius),
                            Mathf.Clamp(point.y, radius, size - radius));
                        pixels[y * size + x] = new Color(1, 1, 1,
                            Mathf.Clamp01(radius + 0.5f - Vector2.Distance(point, center)));
                    }
                roundedTexture.SetPixels(pixels);
                roundedTexture.Apply(false, true);
                roundedSprite = Sprite.Create(roundedTexture, new UnityEngine.Rect(0, 0, size, size),
                    new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                roundedSprite.hideFlags = HideFlags.HideAndDontSave;
                return roundedSprite;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRoundedSurface()
        {
            if (roundedSprite != null) Object.Destroy(roundedSprite);
            if (roundedTexture != null) Object.Destroy(roundedTexture);
            roundedSprite = null;
            roundedTexture = null;
        }

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
            Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color, bool rounded = true)
        {
            var rect = Rect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (rounded) { image.sprite = RoundedSprite; image.type = Image.Type.Sliced; }
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
            image.sprite = RoundedSprite;
            image.type = Image.Type.Sliced;
            // Selectable's tint multiplies Image.color; keep the base white so
            // the design token is displayed once in every interaction state.
            image.color = Color.white;
            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = background;
            colors.highlightedColor = Color.Lerp(background, Color.white, 0.12f);
            colors.pressedColor = background == FixtureUiPalette.Blue
                ? FixtureUiPalette.BluePressed : Color.Lerp(background, Color.black, 0.12f);
            colors.selectedColor = background;
            colors.disabledColor = FixtureUiPalette.Disabled;
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
            var bundled = Resources.Load<Font>("CampusUI/Pretendard-Regular");
            if (bundled != null) return bundled;
            string[] paths = Font.GetPathsToOSFonts();
            // OS font file names do not always contain their display/family name
            // (e.g. malgun.ttf is the Malgun Gothic family on Windows).
            string[] names = { "Pretendard", "Malgun Gothic", "Noto Sans CJK KR", "Apple SD Gothic Neo" };
            string[] fileHints = { "Pretendard", "malgun", "NotoSansCJK", "AppleSDGothicNeo" };
            for (int i = 0; i < names.Length; i++)
                if (System.Array.Exists(paths, path => path.IndexOf(fileHints[i], System.StringComparison.OrdinalIgnoreCase) >= 0))
                    return Font.CreateDynamicFontFromOSFont(names[i], 18);
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
