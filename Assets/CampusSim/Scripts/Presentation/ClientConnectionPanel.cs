using System;
using UnityEngine;
using UnityEngine.UI;

namespace InhaExpress.Client.Presentation
{
    // Address configuration only. Connection success comes from the transport.
    public sealed class ClientConnectionPanel : MonoBehaviour
    {
        public bool Submitted { get; private set; }
        public string Endpoint { get; private set; }
        private InputField address;
        private Text feedback;

        public void Initialize(string endpoint)
        {
            var canvasRoot = new GameObject("Server Connection", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390, 844);
            scaler.matchWidthOrHeight = 0.5f;
            FixtureUiFactory.EnsureEventSystem(canvasRoot.transform);
            var background = FixtureUiFactory.Panel(canvasRoot.transform, "Background", Vector2.zero,
                Vector2.one, Vector2.zero, Vector2.zero, FixtureUiPalette.Canvas);
            background.gameObject.AddComponent<SafeAreaPanel>();
            var card = FixtureUiFactory.Panel(background, "Connection Card", new Vector2(0, .5f),
                new Vector2(1, .5f), new Vector2(24, -180), new Vector2(-24, 180), FixtureUiPalette.Surface);
            Label(card, "Brand", "INHA Campus Shuttle", 24, -60, -20, FixtureUiPalette.Navy);
            Label(card, "Version", "앱 " + Application.version, 13, -90, -62, FixtureUiPalette.Muted);
            Label(card, "Instruction", "PC 서버와 같은 Wi-Fi에 연결한 뒤\n서버 WebSocket 주소를 입력하세요.",
                15, -154, -98, FixtureUiPalette.Ink);
            var input = FixtureUiFactory.Panel(card, "Server Address", new Vector2(0, 1), Vector2.one,
                new Vector2(20, -214), new Vector2(-20, -166), FixtureUiPalette.Canvas);
            address = input.gameObject.AddComponent<InputField>();
            var text = FixtureUiFactory.Text(input, "Value", "", 14, FixtureUiPalette.Ink, TextAnchor.MiddleLeft);
            ((RectTransform)text.transform).offsetMin = new Vector2(12, 4);
            ((RectTransform)text.transform).offsetMax = new Vector2(-12, -4);
            address.textComponent = text;
            address.targetGraphic = input.GetComponent<Image>();
            address.contentType = InputField.ContentType.Standard;
            address.lineType = InputField.LineType.SingleLine;
            address.characterLimit = 512;
            address.text = endpoint;
            var button = FixtureUiFactory.Button(card, "Connect", "서버에 연결", FixtureUiPalette.Blue, out _);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(20, -282); rect.offsetMax = new Vector2(-20, -230);
            button.onClick.AddListener(Submit);
            feedback = Label(card, "Feedback", "예: ws://192.168.0.10:8765/v1/client/ws", 12,
                -338, -290, FixtureUiPalette.Muted);
        }

        private void Submit()
        {
            string value = address.text.Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "ws" && uri.Scheme != "wss") || string.IsNullOrEmpty(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            {
                feedback.text = "유효한 ws:// 또는 wss:// 서버 주소를 입력하세요.";
                feedback.color = FixtureUiPalette.Red;
                return;
            }
            Endpoint = value;
            Submitted = true;
        }

        private static Text Label(RectTransform parent, string name, string value, int size,
            float bottom, float top, Color color)
        {
            var label = FixtureUiFactory.Text(parent, name, value, size, color, TextAnchor.MiddleLeft);
            var rect = (RectTransform)label.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(20, bottom); rect.offsetMax = new Vector2(-20, top);
            return label;
        }
    }
}
