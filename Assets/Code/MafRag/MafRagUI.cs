// MafRag 通用 UI 构造辅助（D0 / D7 / D8）。
// 所有界面元素在运行时用代码创建（避免手编 .unity 资源），并使用中文 CJK 字体。

using UnityEngine;
using UnityEngine.UI;

namespace MafRag
{
    public static class MafRagUI
    {
        // 中文 CJK 字体（Resources/CJKFont.ttf，由运行时加载）
        public static Font CJK
        {
            get
            {
                if (_cjk == null) _cjk = Resources.Load<Font>("CJKFont");
                return _cjk;
            }
        }
        private static Font _cjk;

        // 配色
        public static Color Bg = new Color(0.12f, 0.13f, 0.16f);
        public static Color PanelColor = new Color(0.18f, 0.20f, 0.24f);
        public static Color Accent = new Color(0.20f, 0.55f, 0.95f);
        public static Color AccentDark = new Color(0.15f, 0.42f, 0.75f);
        public static Color UserBubble = new Color(0.20f, 0.55f, 0.95f);
        public static Color BotBubble = new Color(0.24f, 0.26f, 0.30f);
        public static Color TextColor = new Color(0.95f, 0.95f, 0.96f);
        public static Color Muted = new Color(0.6f, 0.62f, 0.66f);

        // 创建铺满屏幕的 Canvas：使用固定参考分辨率 1920x1080 + ScaleWithScreenSize，
        // 不再读取屏幕分辨率、也不再随窗口尺寸变化重算（去掉分辨率检测代码）。
        // UI 缩放交由 CanvasScaler 按固定参考分辨率自适应完成。
        public static Canvas MakeCanvas()
        {
            var go = new GameObject("UICanvas");
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        // 安全区容器：自动按 Screen.safeArea 把刘海/状态栏/底部导航条区域留白（适配 Android 异形屏）。
        // 屏幕像素需 ÷ canvas.scaleFactor 换算为设计像素（Canvas 用 ScaleWithScreenSize，UI 单位即设计像素）。
        // PC/编辑器上 safeArea 通常为全屏 → inset 为 0，行为与不加一致（保留原有呼吸边距）。
        public static RectTransform MakeSafeArea(Transform parent)
        {
            var go = new GameObject("SafeArea", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.AddComponent<SafeAreaFitter>();
            return rt;
        }

        public class SafeAreaFitter : MonoBehaviour
        {
            private RectTransform _rt;
            private Canvas _canvas;
            private Rect _lastSa;
            private float _lastSf = -1f;

            private void Awake()
            {
                _rt = GetComponent<RectTransform>();
                _canvas = GetComponentInParent<Canvas>();
                Apply();
            }

            private void Update() => Apply();

            private void Apply()
            {
                if (_rt == null) return;
                var sa = Screen.safeArea;
                float sf = (_canvas != null && _canvas.scaleFactor > 0f) ? _canvas.scaleFactor : 1f;
                if (sa == _lastSa && Mathf.Approximately(sf, _lastSf)) return;
                _lastSa = sa; _lastSf = sf;

                float left = sa.x / sf;
                float bottom = sa.y / sf;
                float right = (Screen.width - (sa.x + sa.width)) / sf;
                float top = (Screen.height - (sa.y + sa.height)) / sf;
                _rt.offsetMin = new Vector2(left, bottom);
                _rt.offsetMax = new Vector2(-right, -top);
            }
        }

        public static void SetStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static GameObject Panel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.GetComponent<RectTransform>().SetParent(parent, false);
            go.GetComponent<Image>().color = PanelColor;
            return go;
        }

        public static Button MakeButton(Transform parent, string text, Vector2 size)
        {
            var go = new GameObject(text + "_btn", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = Accent;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var trt = txtGo.GetComponent<RectTransform>();
            trt.SetParent(rt, false);
            SetStretch(trt);
            var t = txtGo.GetComponent<Text>();
            t.text = text;
            t.font = CJK;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.fontSize = 34;
            return btn;
        }

        public static Text MakeText(Transform parent, string text, int fontSize, Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            var go = new GameObject("txt", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.font = CJK;
            t.color = color;
            t.alignment = align;
            t.fontSize = fontSize;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // 单行输入框（含占位符）
        public static InputField MakeInput(Transform parent, string placeholder, int fontSize)
        {
            var go = new GameObject("input", typeof(RectTransform), typeof(Image), typeof(InputField));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.95f, 0.95f, 0.96f);

            var inp = go.GetComponent<InputField>();
            inp.lineType = InputField.LineType.SingleLine;

            var phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            var phrt = phGo.GetComponent<RectTransform>();
            phrt.SetParent(rt, false); SetStretch(phrt);
            var ph = phGo.GetComponent<Text>();
            ph.text = placeholder; ph.font = CJK; ph.color = Muted; ph.fontSize = fontSize; ph.alignment = TextAnchor.MiddleLeft;

            var txGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var txrt = txGo.GetComponent<RectTransform>();
            txrt.SetParent(rt, false); SetStretch(txrt);
            var tx = txGo.GetComponent<Text>();
            tx.text = ""; tx.font = CJK; tx.color = Color.black; tx.fontSize = fontSize; tx.alignment = TextAnchor.MiddleLeft;

            inp.placeholder = ph;
            inp.textComponent = tx;
            inp.targetGraphic = go.GetComponent<Image>();
            return inp;
        }
    }
}
