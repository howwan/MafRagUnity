// 主场景 UI（D7 / FR-1 / FR-2 / FR-5 / FR-9）。RAG 对话：输入框/发送不出框，顶部三按钮紧挨。
// 进入设置场景、显示来源、关闭应用。对话历史由 RagManager 跨场景保持。

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MafRag
{
    public class MainSceneUI : MonoBehaviour
    {
        private Canvas _canvas;
        private ScrollRect _scroll;
        private Transform _content;
        private InputField _input;
        private Text _status;
        private Text _botTyping;

        private void Awake() { Build(); }

        private void Build()
        {
            _canvas = MafRagUI.MakeCanvas();
            var root = _canvas.transform;

            // 全屏背景（兜底铺满）：任何栏/面板之间的缝隙只会露出深色背景，不会透出相机天空盒
            var rootBg = MafRagUI.Panel(root, "RootBg");
            var rbgRt = rootBg.GetComponent<RectTransform>();
            rbgRt.anchorMin = Vector2.zero; rbgRt.anchorMax = Vector2.one;
            rbgRt.offsetMin = Vector2.zero; rbgRt.offsetMax = Vector2.zero;
            rootBg.GetComponent<Image>().color = MafRagUI.Bg;

            // 安全区容器：自动避开刘海/状态栏/底部导航条（Android 异形屏）；PC 上为全屏，行为不变
            var area = MafRagUI.MakeSafeArea(root);

            // ---------- 顶部栏 ----------
            var topBar = MafRagUI.Panel(area, "TopBar");
            var tbr = topBar.GetComponent<RectTransform>();
            tbr.anchorMin = new Vector2(0, 1); tbr.anchorMax = new Vector2(1, 1);
            tbr.sizeDelta = new Vector2(0, 110);
            tbr.anchoredPosition = new Vector2(0, -40); // 整体向下移动半个按钮高度（顶部按钮高 80 → 40），远离上边缘避免裁切
            topBar.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.14f);

            var title = MafRagUI.MakeText(topBar.transform, "MAF · RAG 对话", 38, MafRagUI.TextColor, TextAnchor.MiddleLeft);
            var trt = title.gameObject.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0.5f); trt.anchorMax = new Vector2(0, 0.5f); trt.pivot = new Vector2(0, 0.5f);
            trt.sizeDelta = new Vector2(440, 60); trt.anchoredPosition = new Vector2(24, 0);

            // 顶部三按钮：紧挨右侧排列
            var btnSettings = MafRagUI.MakeButton(topBar.transform, "设置", new Vector2(150, 80));
            SetBtn(btnSettings, new Vector2(-405, 0));
            btnSettings.onClick.AddListener(() => SceneManager.LoadScene("SettingsScene"));

            var btnSources = MafRagUI.MakeButton(topBar.transform, "来源", new Vector2(150, 80));
            SetBtn(btnSources, new Vector2(-245, 0));
            btnSources.onClick.AddListener(() => ShowAllSources());

            var btnClose = MafRagUI.MakeButton(topBar.transform, "关闭", new Vector2(150, 80));
            SetBtn(btnClose, new Vector2(-85, 0));
            btnClose.onClick.AddListener(() =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;   // 编辑器内停止播放
#else
                Application.Quit();                                // 打包后退出应用
#endif
            });

            // ---------- 聊天面板 ----------
            var chatPanel = MafRagUI.Panel(area, "ChatPanel");
            var cpr = chatPanel.GetComponent<RectTransform>();
            cpr.anchorMin = Vector2.zero; cpr.anchorMax = Vector2.one;
            // 与上下两栏保持贴合：底栏上移 80 → 状态栏顶位于 328；对话底贴住状态栏顶（328），顶栏下移 40 → 距顶 150
            cpr.offsetMin = new Vector2(0, 328); cpr.offsetMax = new Vector2(0, -150);
            chatPanel.GetComponent<Image>().color = MafRagUI.Bg;

            _scroll = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image)).GetComponent<ScrollRect>();
            var sr = _scroll.GetComponent<RectTransform>(); sr.SetParent(chatPanel.transform, false); MafRagUI.SetStretch(sr);
            _scroll.GetComponent<Image>().color = MafRagUI.Bg;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask)).GetComponent<RectTransform>();
            viewport.SetParent(_scroll.transform, false); MafRagUI.SetStretch(viewport); viewport.GetComponent<Image>().color = MafRagUI.Bg;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero; content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 14; vlg.padding = new RectOffset(18, 18, 18, 18);
            vlg.childControlWidth = true; vlg.childControlHeight = false; vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = content; _scroll.viewport = viewport; _scroll.vertical = true; _scroll.horizontal = false;

            // 垂直滚动条：常驻可见（不再需滚动鼠标才出现）
            var sbGo = new GameObject("VScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            sbGo.transform.SetParent(chatPanel.transform, false);
            sbGo.GetComponent<Image>().color = new Color(0.22f, 0.24f, 0.28f);
            var vsb = sbGo.GetComponent<Scrollbar>();
            vsb.direction = Scrollbar.Direction.BottomToTop;
            var sbrt = sbGo.GetComponent<RectTransform>();
            sbrt.anchorMin = new Vector2(1, 0); sbrt.anchorMax = new Vector2(1, 1);
            sbrt.offsetMin = new Vector2(-14, 6); sbrt.offsetMax = new Vector2(-4, -6);
            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(sbGo.transform, false);
            var hImg = handleGo.GetComponent<Image>();
            hImg.color = new Color(0.55f, 0.57f, 0.62f);
            var hrt = handleGo.GetComponent<RectTransform>();
            MafRagUI.SetStretch(hrt);
            vsb.handleRect = hrt; vsb.targetGraphic = hImg;
            _scroll.verticalScrollbar = vsb;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _content = content;

            // ---------- 底部栏（状态 + 输入 + 发送） ----------
            var bottomBar = MafRagUI.Panel(area, "BottomBar");
            var bbr = bottomBar.GetComponent<RectTransform>();
            bbr.anchorMin = new Vector2(0, 0); bbr.anchorMax = new Vector2(1, 0);
            bbr.sizeDelta = new Vector2(0, 260); bbr.anchoredPosition = new Vector2(0, 80); // 整体上移 80，给底部发送按钮留出屏幕底边安全间距，避免裁切
            bottomBar.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.14f);

            // 状态栏：加高到 110 以容纳多行来源信息（左上对齐，多行不裁切）
            _status = MafRagUI.MakeText(bottomBar.transform, "就绪", 22, MafRagUI.Muted, TextAnchor.UpperLeft);
            var srt = _status.gameObject.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1);
            srt.sizeDelta = new Vector2(0, 110); srt.anchoredPosition = new Vector2(0, -12);
            srt.offsetMin = new Vector2(20, 0); srt.offsetMax = new Vector2(-20, 0);

            // 输入框：高 90，与发送按钮同垂直中心（bar 中心下移 45 → 本地 y=85），上方留 12px 避开状态栏
            _input = MafRagUI.MakeInput(bottomBar.transform, "输入你的问题（中文）…", 34);
            var irt = _input.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0, 0.5f); irt.anchorMax = new Vector2(1, 0.5f); irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(0, 90); irt.anchoredPosition = new Vector2(0, -45);
            // 竖向 offset 须与 anchoredPosition 一致（中心 85：底 -90 / 顶 0），水平内缩避开发送按钮
            irt.offsetMin = new Vector2(20, -90); irt.offsetMax = new Vector2(-220, 0);

            // 回车提交（Enter 直接发送，无需点按钮）
            _input.onEndEdit.AddListener(t => { if (string.IsNullOrWhiteSpace(t)) _input.ActivateInputField(); else SendAsync(); });

            // 发送按钮：右对齐、垂直居中（不再跳出窗口底边）
            var btnSend = MafRagUI.MakeButton(bottomBar.transform, "发送", new Vector2(180, 90));
            var sendrt = btnSend.GetComponent<RectTransform>();
            sendrt.anchorMin = new Vector2(1, 0.5f); sendrt.anchorMax = new Vector2(1, 0.5f); sendrt.pivot = new Vector2(1, 0.5f);
            sendrt.sizeDelta = new Vector2(180, 90); sendrt.anchoredPosition = new Vector2(-20, -45);
            btnSend.onClick.AddListener(() => SendAsync());

            // 恢复历史对话气泡
            if (RagManager.Instance != null)
                foreach (var m in RagManager.Instance.History)
                    AddBubble(m.role == "user", m.text);

            RefreshStatusSummary();   // 初始即在状态栏显示一行简介（向量库 + 文档数）
        }

        private void SetBtn(Button b, Vector2 pos)
        {
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(150, 80); rt.anchoredPosition = pos;
        }

        // ---------- 对话渲染 ----------
        // 气泡高度由 Text 经 ContentSizeFitter 自动撑开：文字永远不会溢出气泡，
        // 也就不会被视口 Mask 裁掉（此前表现为“过了一会文字就消失”）。
        private RectTransform MakeBubble(string text, int fontSize, Color textColor, Color bg, float minH)
        {
            var go = new GameObject("bubble", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(_content, false);
            go.GetComponent<Image>().color = bg;

            var brt = go.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1);

            var vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(18, 18, 14, 14);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;

            go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = MafRagUI.MakeText(go.transform, text, fontSize, textColor, TextAnchor.UpperLeft);
            var trt = t.gameObject.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = minH;
            return brt;
        }

        private void AddBubble(bool isUser, string text)
        {
            MakeBubble(text, 32, MafRagUI.TextColor, isUser ? MafRagUI.UserBubble : MafRagUI.BotBubble, 60);
            ScrollToBottom();
        }

        private void AddSources(List<(string source, float sim, string content)> srcs)
        {
            if (srcs == null || srcs.Count == 0) return;
            var sb = new StringBuilder();
            sb.AppendLine("参考来源：");
            foreach (var s in srcs) sb.AppendLine($"· {s.source}（相似度 {s.sim:F3}）");
            MakeBubble(sb.ToString(), 26, new Color(0.7f, 0.95f, 0.8f), new Color(0.16f, 0.30f, 0.22f), 50);
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        // ---------- 交互 ----------
        private async void SendAsync()
        {
            string q = _input.text.Trim();
            if (string.IsNullOrEmpty(q)) return;
            _input.text = "";
            AddBubble(true, "提问：" + q);

            await RagManager.Instance.EnsureKnowledgeAsync(s => { if (_status != null) _status.text = s; });

            BeginTyping();
            string acc = "";
            bool hadError = false;
            await RagManager.Instance.AskStreamingAsync(q,
                tok => { acc += tok; UpdateTyping(acc); },
                srcs => AddSources(srcs),
                err => { if (_status != null) _status.text = err; hadError = true; });
            EndTyping();
            if (hadError)
            {
                _input.ActivateInputField();
                return;
            }
            AddBubble(false, "回答：" + acc);
            RefreshStatusSummary();        // 完成后恢复状态栏简介行

            _input.ActivateInputField();   // 发送后重新聚焦，便于连续输入
        }

        private void BeginTyping()
        {
            var brt = MakeBubble("正在思考…", 30, MafRagUI.Muted, MafRagUI.BotBubble, 50);
            _botTyping = brt.GetComponentInChildren<Text>();
            ScrollToBottom();
        }
        private void UpdateTyping(string s) { if (_botTyping != null) _botTyping.text = s; ScrollToBottom(); }
        private void EndTyping() { if (_botTyping != null) { Destroy(_botTyping.transform.parent.gameObject); _botTyping = null; } }

        // 状态栏初始/空闲时显示的一行简介；提问完成后也会恢复此行
        private async void RefreshStatusSummary()
        {
            if (_status == null) return;
            await RagManager.Instance.EnsureInitializedAsync();
            if (_status == null) return;          // 场景可能在 await 期间被卸载
            int docs = await RagManager.Instance.StoreDocCountAsync();
            if (_status == null) return;
            _status.text = $"向量库：{RagManager.Instance.BackendLocation}  ·  文档数：{(docs < 0 ? "—（远程后端）" : docs.ToString())}";
        }

        // 点「来源」：展开三行详细统计到状态栏（不进对话框）
        private async void ShowAllSources()
        {
            if (_status == null) return;
            _status.text = "正在统计来源…";
            await RagManager.Instance.EnsureInitializedAsync();
            if (_status == null) return;          // 场景可能在 await 期间被卸载
            int docs = await RagManager.Instance.StoreDocCountAsync();
            if (_status == null) return;
            var sb = new StringBuilder();
            sb.AppendLine($"向量库位置：{RagManager.Instance.BackendLocation}");
            sb.AppendLine($"文档数：{(docs < 0 ? "—（远程后端）" : docs.ToString())}");
            sb.AppendLine("（在「设置」中可入库 / 重置 / 查看详细统计）");
            _status.text = sb.ToString();
        }
    }
}
