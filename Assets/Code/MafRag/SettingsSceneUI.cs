// 设置场景 UI（D8 / FR-3 / FR-6 / FR-8 / 统一抽象层）。
// 修复：滚动内容顶部锚定（ContentSizeFitter 生效，参数输入框可见）；
// 向量库后端选择（SQLite / Qdrant / pgvector（仅 PC））+ 配置；
// 数据管理：入库、重置、统计；顶部按钮紧挨；可返回主场景。

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MafRag
{
    public class SettingsSceneUI : MonoBehaviour
    {
        private Canvas _canvas;
        private Transform _content;
        private Text _status;

        // 日志区域
        private List<Button> _logBtns;
        private string _logLevel;
        private GameObject _logOverlay;
        private GameObject _remoteGroup;   // Qdrant 字段
        private GameObject _pgGroup;        // pgvector 字段（仅 PC / NPGSQL）

        // 配置字段引用
        private InputField _fEndpoint, _fApiKey, _fCollection;
        private InputField _fPgHost, _fPgPort, _fPgDb, _fPgUser, _fPgPass, _fPgTable;
        private InputField _fEmbEndpoint, _fEmbKey, _fEmbModel;
        private InputField _fChatEndpoint, _fChatKey, _fChatModel;
        private InputField _fChunk, _fOverlap, _fTopK, _fDir;

        // 后端选择
        private string _backend;
        private readonly List<Button> _backendBtns = new List<Button>();

        // 重置二次确认
        private bool _resetArmed;
        private Button _btnReset;
        private Button _btnResetCancel;

        // 重置为出厂二次确认
        private bool _factoryArmed;
        private Button _btnFactory;
        private Button _btnFactoryCancel;

        // 入库进度
        private IngestProgress _prog;

        private void Awake() { Build(); }

        private void Build()
        {
            RagSettings.Load();
            _backend = (RagSettings.Current.backend ?? "sqlite").ToLowerInvariant();

            _canvas = MafRagUI.MakeCanvas();
            var root = _canvas.transform;

            // 全屏背景（兜底铺满）：任何缝隙只会露出深色背景，不会透出相机天空盒
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
            tbr.sizeDelta = new Vector2(0, 110); tbr.anchoredPosition = new Vector2(0, -40); // 整体向下移动半个按钮高度（按钮高 80 → 40），远离上边缘避免裁切
            topBar.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.14f);

            var title = MafRagUI.MakeText(topBar.transform, "RAG 参数 / 数据管理", 36, MafRagUI.TextColor, TextAnchor.MiddleLeft);
            var trt = title.gameObject.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0.5f); trt.anchorMax = new Vector2(0, 0.5f); trt.pivot = new Vector2(0, 0.5f);
            trt.sizeDelta = new Vector2(520, 60); trt.anchoredPosition = new Vector2(24, 0);

            // 顶部按钮紧挨右侧：保存 / 返回
            var btnSave = MafRagUI.MakeButton(topBar.transform, "保存", new Vector2(150, 80));
            SetTopBtn(btnSave, -245);
            btnSave.onClick.AddListener(() => Save());

            var btnBack = MafRagUI.MakeButton(topBar.transform, "返回", new Vector2(150, 80));
            SetTopBtn(btnBack, -85);
            btnBack.onClick.AddListener(() => SceneManager.LoadScene("MainScene"));

            // ---------- 滚动内容 ----------
            var scrollGO = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            var srt = scrollGO.GetComponent<RectTransform>(); srt.SetParent(area, false);
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(0, 70); srt.offsetMax = new Vector2(0, -150); // 顶栏下移 40 → 距顶 150，保持贴合
            scrollGO.GetComponent<Image>().color = MafRagUI.Bg;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask)).GetComponent<RectTransform>();
            viewport.SetParent(scrollGO.transform, false); MafRagUI.SetStretch(viewport); viewport.GetComponent<Image>().color = Color.white;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            content.SetParent(viewport.transform, false);
            // 关键修复：滚动内容顶部锚定，ContentSizeFitter 才能正确撑高（否则塌陷为 0，字段不可见）
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero; content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 12; vlg.padding = new RectOffset(18, 18, 18, 18);
            vlg.childControlWidth = true; vlg.childControlHeight = false; vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = scrollGO.GetComponent<ScrollRect>(); sr.content = content; sr.viewport = viewport; sr.vertical = true; sr.horizontal = false;
            // 鼠标滚轮支持：部分环境下 EventSystem 未把滚轮事件派发到 ScrollRect，
            // 这里直接复用 ScrollRect 自带 OnScroll，方向与拖拽一致。
            scrollGO.AddComponent<WheelScroll>().scroll = sr;
            _content = content;

            // ---------- ① 向量库后端 ----------
            var g1 = AddSection("① 向量库后端");
            AddDesc(g1, "选择向量数据的存储位置：SQLite（本机文件·离线可用）/ Qdrant（远程向量服务）/ pgvector（PostgreSQL·生产级，仅 PC）");
            AddBackendSelector(g1);

            _remoteGroup = new GameObject("RemoteGroup", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _remoteGroup.transform.SetParent(g1, false);
            {
                var rg = _remoteGroup.GetComponent<VerticalLayoutGroup>();
                rg.spacing = 10; rg.padding = new RectOffset(0, 0, 4, 4);
                rg.childControlWidth = true; rg.childControlHeight = true; rg.childForceExpandWidth = false; rg.childForceExpandHeight = false;
                _remoteGroup.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            _fEndpoint = AddField(_remoteGroup.transform, "远端地址", "http://localhost:6333", RagSettings.Current.vectorStoreEndpoint);
            _fApiKey = AddField(_remoteGroup.transform, "API Key（可选）", "留空", RagSettings.Current.vectorStoreApiKey);
            _fCollection = AddField(_remoteGroup.transform, "集合名", "maf_rag", RagSettings.Current.collectionName);
            _remoteGroup.SetActive(_backend == "qdrant");

#if NPGSQL
            _pgGroup = new GameObject("PgGroup", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _pgGroup.transform.SetParent(g1, false);
            {
                var pg = _pgGroup.GetComponent<VerticalLayoutGroup>();
                pg.spacing = 10; pg.padding = new RectOffset(0, 0, 4, 4);
                pg.childControlWidth = true; pg.childControlHeight = true; pg.childForceExpandWidth = false; pg.childForceExpandHeight = false;
                _pgGroup.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            _fPgHost = AddField(_pgGroup.transform, "主机", "localhost", RagSettings.Current.pgHost);
            _fPgPort = AddField(_pgGroup.transform, "端口", "5432", RagSettings.Current.pgPort.ToString());
            _fPgDb = AddField(_pgGroup.transform, "数据库", "postgres", RagSettings.Current.pgDatabase);
            _fPgUser = AddField(_pgGroup.transform, "用户名", "postgres", RagSettings.Current.pgUser);
            _fPgPass = AddField(_pgGroup.transform, "密码", "留空", RagSettings.Current.pgPassword);
            _fPgTable = AddField(_pgGroup.transform, "表名", "rag_chunks", RagSettings.Current.pgTable);
            _pgGroup.SetActive(_backend == "pgvector");
#endif

            // ---------- ②/③/④ 三列：Embedding / 对话 LLM / 分块检索 ----------
            var cols = AddColumns(_content);
            var colEmb = AddColumn(cols, "② Embedding 配置");
            _fEmbEndpoint = AddField(colEmb, "Embedding 地址", "http://localhost:11434/v1", RagSettings.Current.embeddingEndpoint);
            _fEmbKey = AddField(colEmb, "API Key", "ollama", RagSettings.Current.embeddingApiKey);
            _fEmbModel = AddField(colEmb, "Embedding 模型", "qwen3-embedding:0.6b", RagSettings.Current.embeddingModel);

            var colChat = AddColumn(cols, "③ 对话 LLM 配置");
            _fChatEndpoint = AddField(colChat, "Chat 地址", "http://localhost:11434/v1", RagSettings.Current.chatEndpoint);
            _fChatKey = AddField(colChat, "API Key", "ollama", RagSettings.Current.chatApiKey);
            _fChatModel = AddField(colChat, "Chat 模型", "qwen3:4b", RagSettings.Current.chatModel);

            var colChunk = AddColumn(cols, "④ 分块 / 检索");
            _fChunk = AddField(colChunk, "分块大小", "1000", RagSettings.Current.chunkSize.ToString());
            _fOverlap = AddField(colChunk, "重叠字符", "100", RagSettings.Current.overlap.ToString());
            _fTopK = AddField(colChunk, "检索 Top-K", "3", RagSettings.Current.topK.ToString());

            // ---------- ⑤ 数据管理 ----------
            var g5 = AddSection("⑤ 数据管理");
            AddDesc(g5, "对知识库执行操作：把文档分块入库、重置清空、查看统计。");
            _fDir = AddField(g5, "默认 Markdown 目录", "目录路径", RagSettings.Current.defaultMarkdownDir);
            if (string.IsNullOrEmpty(RagSettings.Current.defaultMarkdownDir))
            {
                string auto = DetectDefaultMarkdownDir();
                if (!string.IsNullOrEmpty(auto)) _fDir.text = auto;
            }
            // 浏览 / 入库 放在目录输入框之后同一行；入库留空会自动回退到内置知识库 rag-doc，无需单独的“入库内置”按钮
            AddButtonPair(g5, "浏览", OnBrowse, "入库", OnIngestDir);
            _btnReset = AddButtonRow(g5, "重置向量库", OnReset);
            _btnResetCancel = AddButtonRow(g5, "取消", OnResetCancel);   // 武装时显示，点此取消
            _btnResetCancel.gameObject.SetActive(false);
            _btnFactory = AddButtonRow(g5, "重置为出厂", OnFactoryReset);
            _btnFactoryCancel = AddButtonRow(g5, "取消", OnFactoryCancel);   // 武装时显示，点此取消
            _btnFactoryCancel.gameObject.SetActive(false);
            AddButtonRow(g5, "刷新统计", OnStats);

            // ---------- ⑥ 日志 ----------
            AddLogSection();
            _status = MafRagUI.MakeText(g5, "就绪（当前后端：" + BackendLabel() + "）", 24, MafRagUI.Muted, TextAnchor.UpperLeft);
            var stt = _status.gameObject.GetComponent<RectTransform>();
            stt.anchorMin = new Vector2(0, 1); stt.anchorMax = new Vector2(1, 1);
            stt.sizeDelta = new Vector2(0, 0); stt.anchoredPosition = Vector2.zero;
            stt.offsetMin = new Vector2(4, 0); stt.offsetMax = new Vector2(-4, 0);
            // 高度随文本行数自增，多行不再被裁切；外层分组/滚动区已开启 PreferredSize，整体可滚动。
            var sf = _status.gameObject.AddComponent<ContentSizeFitter>();
            sf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void SetTopBtn(Button b, float x)
        {
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(150, 80); rt.anchoredPosition = new Vector2(x, 0);
        }

        // ---------- 构建辅助 ----------
        // 带标题的分组卡片：标题用强调色、字号更大，与组内文字形成清晰主次
        private Transform AddSection(string title)
        {
            var sec = new GameObject("section", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            sec.transform.SetParent(_content, false);
            sec.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.20f);
            var vlg = sec.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 10; vlg.padding = new RectOffset(18, 18, 14, 16);
            vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;
            sec.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = MafRagUI.MakeText(sec.transform, title, 34, MafRagUI.Accent, TextAnchor.MiddleLeft);
            var trt = t.gameObject.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
            trt.sizeDelta = new Vector2(0, 46); trt.anchoredPosition = Vector2.zero;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 46;
            return sec.transform;
        }

        // 分组下的说明文字（浅灰、小一号）
        private void AddDesc(Transform parent, string text)
        {
            var t = MafRagUI.MakeText(parent, text, 24, MafRagUI.Muted, TextAnchor.UpperLeft);
            var rt = t.gameObject.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(0, 56); rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
        }

        // 水平三列容器
        private Transform AddColumns(Transform parent)
        {
            var cols = new GameObject("cols", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            cols.transform.SetParent(parent, false);
            var hlg = cols.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16; hlg.padding = new RectOffset(0, 0, 4, 4);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            cols.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            cols.AddComponent<LayoutElement>().minHeight = 220;
            return cols.transform;
        }

        // 单列面板（含列标题）
        private Transform AddColumn(Transform parent, string title)
        {
            var col = new GameObject("col", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            col.transform.SetParent(parent, false);
            col.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.20f);
            var vlg = col.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 10; vlg.padding = new RectOffset(14, 14, 12, 14);
            vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;
            col.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            col.AddComponent<LayoutElement>().flexibleWidth = 1;
            var t = MafRagUI.MakeText(col.transform, title, 30, MafRagUI.TextColor, TextAnchor.MiddleLeft);
            var trt = t.gameObject.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1);
            trt.sizeDelta = new Vector2(0, 40); trt.anchoredPosition = Vector2.zero;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            return col.transform;
        }

        private void AddBackendSelector(Transform parent)
        {
            var row = new GameObject("backendRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 80;
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12; hlg.padding = new RectOffset(0, 0, 4, 4);
            hlg.childControlWidth = true; hlg.childControlHeight = true; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

#if NPGSQL
            string[] labels = { "SQLite（本地·离线）", "Qdrant（远程·高性能）", "pgvector（生产·PC）" };
            string[] vals = { "sqlite", "qdrant", "pgvector" };
#else
            string[] labels = { "SQLite（本地·离线）", "Qdrant（远程·高性能）" };
            string[] vals = { "sqlite", "qdrant" };
#endif
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var btn = MafRagUI.MakeButton(row.transform, labels[i], Vector2.zero);
                btn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                _backendBtns.Add(btn);
                btn.onClick.AddListener(() => { _backend = vals[idx]; RefreshBackendHighlight(); });
            }
            RefreshBackendHighlight();
        }

        private void RefreshBackendHighlight()
        {
#if NPGSQL
            string[] vals = { "sqlite", "qdrant", "pgvector" };
#else
            string[] vals = { "sqlite", "qdrant" };
#endif
            for (int i = 0; i < _backendBtns.Count; i++)
            {
                var img = _backendBtns[i].GetComponent<Image>();
                img.color = (vals[i] == _backend) ? MafRagUI.Accent : new Color(0.28f, 0.30f, 0.35f);
            }
            if (_remoteGroup != null) _remoteGroup.SetActive(_backend == "qdrant");
#if NPGSQL
            if (_pgGroup != null) _pgGroup.SetActive(_backend == "pgvector");
#endif
        }

        private InputField AddField(Transform parent, string label, string placeholder, string value)
        {
            var row = new GameObject("row", typeof(RectTransform), typeof(Image));
            row.transform.SetParent(parent, false);
            row.GetComponent<Image>().color = new Color(0.22f, 0.24f, 0.29f);
            row.AddComponent<LayoutElement>().preferredHeight = 60;
            // 行高 60，上下内边距各 15 → 录入框内容高 30 = 蓝框的 50%，垂直居中更协调
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12; hlg.padding = new RectOffset(14, 14, 15, 15);
            hlg.childControlWidth = true; hlg.childControlHeight = false; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

            var lbl = MafRagUI.MakeText(row.transform, label, 26, new Color(0.82f, 0.84f, 0.88f), TextAnchor.MiddleLeft);
            var lle = lbl.gameObject.AddComponent<LayoutElement>(); lle.preferredWidth = 200; lle.preferredHeight = 30; lle.flexibleHeight = 0;

            var inp = MafRagUI.MakeInput(row.transform, placeholder, 26);
            var ile = inp.gameObject.AddComponent<LayoutElement>(); ile.flexibleWidth = 1; ile.minWidth = 160; ile.preferredHeight = 30; ile.flexibleHeight = 0;
            if (!string.IsNullOrEmpty(value)) inp.text = value;
            return inp;
        }

        private Button AddButtonRow(Transform parent, string text, Action onClick)
        {
            var row = new GameObject("btnrow", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 64;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12; hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            var btn = MafRagUI.MakeButton(row.transform, text, Vector2.zero);
            btn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            btn.onClick.AddListener(() => onClick());
            return btn;
        }

        // 同一行放两个等宽按钮（如“浏览”“入库”）
        private void AddButtonPair(Transform parent, string textA, Action onClickA, string textB, Action onClickB)
        {
            var row = new GameObject("btnrow", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 64;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12; hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            var btnA = MafRagUI.MakeButton(row.transform, textA, Vector2.zero);
            btnA.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            btnA.onClick.AddListener(() => onClickA());
            var btnB = MafRagUI.MakeButton(row.transform, textB, Vector2.zero);
            btnB.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            btnB.onClick.AddListener(() => onClickB());
        }

        // ---------- ⑥ 日志：级别切换 + 查看 + 导出 ----------
        private void AddLogSection()
        {
            var g = AddSection("⑥ 日志");
            AddDesc(g, "切换日志级别（Debug/Info/Warn/Error，越低越详细）；查看实时日志内容，或将日志导出为独立文件。");

            // 日志级别选择行
            var row = new GameObject("logRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(g, false);
            row.AddComponent<LayoutElement>().preferredHeight = 80;
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12; hlg.padding = new RectOffset(0, 0, 4, 4);
            hlg.childControlWidth = true; hlg.childControlHeight = true; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            string[] levels = { "Debug", "Info", "Warn", "Error" };
            _logBtns = new List<Button>();
            _logLevel = (RagSettings.Current.logLevel ?? "Info");
            foreach (var lv in levels)
            {
                string cur = lv;
                var btn = MafRagUI.MakeButton(row.transform, lv, Vector2.zero);
                btn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                _logBtns.Add(btn);
                btn.onClick.AddListener(() => SetLogLevel(cur));
            }
            RefreshLogHighlight();

            AddButtonPair(g, "查看日志", ShowLogOverlay, "导出日志", OnExportLog);
        }

        private void SetLogLevel(string lv)
        {
            _logLevel = lv;
            RagLogger.ApplyMinLevel(lv);
            RagSettings.Current.logLevel = lv;
            RagSettings.Save();
            RefreshLogHighlight();
            _status.text = "日志级别已设为：" + lv + "（立即生效，已保存）";
        }

        private void RefreshLogHighlight()
        {
            string[] levels = { "Debug", "Info", "Warn", "Error" };
            for (int i = 0; i < _logBtns.Count; i++)
            {
                var img = _logBtns[i].GetComponent<Image>();
                img.color = (levels[i] == _logLevel) ? MafRagUI.Accent : new Color(0.28f, 0.30f, 0.35f);
            }
        }

        // 导出日志：复制为带时间戳文件，并在 PC 上尝试用默认程序打开
        private void OnExportLog()
        {
            string path = RagLogger.ExportCopy();
            if (string.IsNullOrEmpty(path))
            {
                _status.text = "暂无日志可导出。";
                return;
            }
            _status.text = "日志已导出：" + path;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            try { Application.OpenURL(path); } catch { }
#endif
        }

        // 查看日志：弹出铺满屏幕的半透明遮罩，内含可滚动文本（显示最近 3000 行）
        private void ShowLogOverlay()
        {
            if (_logOverlay != null) { _logOverlay.SetActive(true); return; }

            var ov = new GameObject("LogOverlay", typeof(RectTransform), typeof(Image));
            ov.transform.SetParent(_canvas.transform, false);
            SetStretch(ov.GetComponent<RectTransform>());
            ov.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
            _logOverlay = ov;

            // 点遮罩空白处关闭 + 按 ESC 关闭
            var bgBtn = ov.AddComponent<Button>();
            bgBtn.transition = Selectable.Transition.None;
            bgBtn.onClick.AddListener(HideLogOverlay);
            ov.AddComponent<OverlayEscCloser>().onClose = HideLogOverlay;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(ov.transform, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.05f, 0.05f); prt.anchorMax = new Vector2(0.95f, 0.95f);
            prt.offsetMin = Vector2.zero; prt.offsetMax = Vector2.zero; prt.pivot = new Vector2(0.5f, 0.5f);
            panel.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.14f);
            // 面板拦截点击，避免穿透到背景直接关闭
            var panelBtn = panel.AddComponent<Button>();
            panelBtn.transition = Selectable.Transition.None;

            // 顶部标题栏（容器）：标题占满整条栏并居中显示（不换行），关闭按钮覆盖在右侧
            var header = new GameObject("Header", typeof(RectTransform));
            var hrt = header.GetComponent<RectTransform>(); hrt.SetParent(panel.transform, false);
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1);
            hrt.anchoredPosition = new Vector2(0, -40); hrt.sizeDelta = new Vector2(0, 80);
            hrt.offsetMin = new Vector2(12, 0); hrt.offsetMax = new Vector2(-12, 0);

            var title = MafRagUI.MakeText(header.transform, "日志（最近内容）", 30, MafRagUI.Accent, TextAnchor.MiddleCenter);
            var trt = title.gameObject.GetComponent<RectTransform>();
            // 占满整条栏宽度，行高取字体高度，再把标题整体下移一个字体高度（避免顶部溢出）
            trt.anchorMin = new Vector2(0, 0.5f); trt.anchorMax = new Vector2(1, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(0, 30);            // 行高 = 字体高度
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            trt.anchoredPosition = new Vector2(0, -30);    // 整体下移一个字体高度

            var scrollGO = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            var srt = scrollGO.GetComponent<RectTransform>(); srt.SetParent(panel.transform, false);
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            // 顶部留出标题栏高度（80px），避免不透明背景遮住右上角「关闭」按钮
            srt.offsetMin = new Vector2(12, 12); srt.offsetMax = new Vector2(-12, -80);
            scrollGO.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.09f);

            // 「关闭」按钮覆盖在标题栏右侧（创建于标题之后，层级在上），垂直居中
            var close = MafRagUI.MakeButton(header.transform, "关闭", new Vector2(120, 50));
            var crt = close.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(1, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(1, 1);
            crt.anchoredPosition = new Vector2(-12, -15); // header 高 80，中心 y=-40 → 按钮与标题垂直居中对齐
            close.onClick.AddListener(HideLogOverlay);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask)).GetComponent<RectTransform>();
            viewport.SetParent(scrollGO.transform, false); SetStretch(viewport); viewport.GetComponent<Image>().color = Color.white;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero; content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 0; vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlWidth = true; vlg.childControlHeight = false; vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var txt = MafRagUI.MakeText(content, RagLogger.ReadTail(3000), 20, MafRagUI.Muted, TextAnchor.UpperLeft);
            var txtRT = txt.gameObject.GetComponent<RectTransform>();
            txtRT.anchorMin = new Vector2(0, 1); txtRT.anchorMax = new Vector2(1, 1);
            txtRT.sizeDelta = new Vector2(0, 0); txtRT.anchoredPosition = Vector2.zero;
            txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
            txt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scrollGO.GetComponent<ScrollRect>(); sr.content = content; sr.viewport = viewport; sr.vertical = true; sr.horizontal = false;
            scrollGO.AddComponent<WheelScroll>().scroll = sr; // 鼠标滚轮同样可滚动日志面板
        }

        private void HideLogOverlay()
        {
            if (_logOverlay != null) _logOverlay.SetActive(false);
        }

        private void SetStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.anchoredPosition = Vector2.zero; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private string BackendLabel()
        {
#if NPGSQL
            return _backend == "qdrant" ? "Qdrant" : _backend == "pgvector" ? "pgvector" : "SQLite";
#else
            return _backend == "qdrant" ? "Qdrant" : "SQLite";
#endif
        }

        // ---------- 交互 ----------
        private void Save()
        {
            var d = RagSettings.Current;
            d.backend = _backend;
            d.vectorStoreEndpoint = _fEndpoint != null ? _fEndpoint.text.Trim() : d.vectorStoreEndpoint;
            d.vectorStoreApiKey = _fApiKey != null ? _fApiKey.text.Trim() : d.vectorStoreApiKey;
            d.collectionName = _fCollection != null ? _fCollection.text.Trim() : d.collectionName;
#if NPGSQL
            d.pgHost = _fPgHost != null ? _fPgHost.text.Trim() : d.pgHost;
            d.pgPort = ParseInt(_fPgPort != null ? _fPgPort.text : "", 5432);
            d.pgDatabase = _fPgDb != null ? _fPgDb.text.Trim() : d.pgDatabase;
            d.pgUser = _fPgUser != null ? _fPgUser.text.Trim() : d.pgUser;
            d.pgPassword = _fPgPass != null ? _fPgPass.text : d.pgPassword; // 密码不 Trim
            d.pgTable = _fPgTable != null ? _fPgTable.text.Trim() : d.pgTable;
#endif
            d.embeddingEndpoint = _fEmbEndpoint.text.Trim();
            d.embeddingApiKey = _fEmbKey.text.Trim();
            d.embeddingModel = _fEmbModel.text.Trim();
            d.chatEndpoint = _fChatEndpoint.text.Trim();
            d.chatApiKey = _fChatKey.text.Trim();
            d.chatModel = _fChatModel.text.Trim();
            d.chunkSize = ParseInt(_fChunk.text, 1000);
            d.overlap = ParseInt(_fOverlap.text, 100);
            d.topK = ParseInt(_fTopK.text, 3);
            d.defaultMarkdownDir = _fDir != null ? _fDir.text.Trim() : d.defaultMarkdownDir;
            RagSettings.Save();
            RagManager.Instance.ApplySettings();
            _status.text = "已保存（后端：" + BackendLabel() + "）。返回主场景生效。";
        }

        private static int ParseInt(string s, int def)
        {
            return int.TryParse(s, out var v) ? v : def;
        }

        private void OnBrowse()
        {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            string picked = WindowsFolderPicker.Show();
            if (!string.IsNullOrEmpty(picked) && _fDir != null) _fDir.text = picked;
#endif
        }

        // 自动探测一个含 Markdown 的默认目录：优先内置知识库 rag-doc（随包发布、必然存在）
        private string DetectDefaultMarkdownDir()
        {
            string rag = System.IO.Path.Combine(Application.streamingAssetsPath, "rag-doc");
            if (System.IO.Directory.Exists(rag))
            {
                try
                {
                    if (System.IO.Directory.GetFiles(rag, "*.md", System.IO.SearchOption.AllDirectories).Length > 0)
                        return rag;
                }
                catch (System.Exception) { }
            }
            return "";
        }

        private async void OnIngestDir()
        {
            string folder = _fDir != null ? _fDir.text.Trim() : "";
            if (string.IsNullOrEmpty(folder)) folder = DetectDefaultMarkdownDir();   // 空则自动探测（内置知识库目录）
            if (string.IsNullOrEmpty(folder))
            {
                _status.text = "未找到默认 Markdown 目录，请填写路径或点击「浏览」选择目录。";
                return;
            }
            _status.text = "正在入库：" + folder;
            _prog = new IngestProgress();
            var t = IngestLoop();
            int n = await RagManager.Instance.IngestFolderAsync(folder, _prog);
            await t;
            _status.text = $"入库完成：新增 {n} 块，跳过 {_prog.Skipped} 个未变更。";
        }

        private async void OnReset()
        {
            if (!_resetArmed)
            {
                _resetArmed = true;
                if (_btnReset != null) _btnReset.GetComponentInChildren<Text>().text = "确认重置？";
                if (_btnResetCancel != null) _btnResetCancel.gameObject.SetActive(true);
                _status.text = "再次点击「确认重置？」将清空向量库；若不想执行，点击「取消」。";
                return;
            }
            _resetArmed = false;
            if (_btnReset != null) _btnReset.GetComponentInChildren<Text>().text = "重置向量库";
            if (_btnResetCancel != null) _btnResetCancel.gameObject.SetActive(false);
            _status.text = "正在重置…";
            await RagManager.Instance.ResetAsync();
            await RefreshStatsAsync();   // 重置后立即刷新统计，立即显示清空后的真实数字，避免误以为没清
        }

        // 取消重置向量库：解除武装、隐藏取消按钮、恢复原标签，库不改动
        private void OnResetCancel()
        {
            _resetArmed = false;
            if (_btnReset != null) _btnReset.GetComponentInChildren<Text>().text = "重置向量库";
            if (_btnResetCancel != null) _btnResetCancel.gameObject.SetActive(false);
            _status.text = "已取消，向量库未改动。";
        }

        // 重置为出厂：清空持久化的 rag.db / rag-config.json，恢复随包出厂副本，
        // 并使所有输入框重新反映出厂默认值（重载本场景）。
        private async void OnFactoryReset()
        {
            if (!_factoryArmed)
            {
                _factoryArmed = true;
                if (_btnFactory != null) _btnFactory.GetComponentInChildren<Text>().text = "确认重置为出厂？";
                if (_btnFactoryCancel != null) _btnFactoryCancel.gameObject.SetActive(true);
                _status.text = "再次点击将删除持久化的 rag.db / rag-config.json 并恢复出厂文件；若不想执行，点击「取消」。";
                return;
            }
            _factoryArmed = false;
            if (_btnFactory != null) _btnFactory.GetComponentInChildren<Text>().text = "重置为出厂";
            if (_btnFactoryCancel != null) _btnFactoryCancel.gameObject.SetActive(false);
            _status.text = "正在重置为出厂…";
            await RagManager.Instance.ResetToFactoryAsync();
            // 不重载场景（否则界面会跳回顶端），改为直接把各输入框同步成出厂默认值
            SyncFieldsFromSettings();
            await RefreshStatsAsync();   // 同步显示清空后的统计
            _status.text = "已重置为出厂（配置与向量库恢复初始），输入框已更新。";
        }

        // 取消重置为出厂：解除武装、隐藏取消按钮、恢复原标签，文件不改动
        private void OnFactoryCancel()
        {
            _factoryArmed = false;
            if (_btnFactory != null) _btnFactory.GetComponentInChildren<Text>().text = "重置为出厂";
            if (_btnFactoryCancel != null) _btnFactoryCancel.gameObject.SetActive(false);
            _status.text = "已取消，出厂文件与配置未改动。";
        }

        // 工厂重置后不重载场景，直接把各输入框同步成当前（出厂）配置，避免界面跳动
        private void SyncFieldsFromSettings()
        {
            var s = RagSettings.Current;
            _backend = (s.backend ?? "sqlite").ToLowerInvariant();
            RefreshBackendHighlight();   // 同步后端高亮与远程/PG 分组显隐
            _fEndpoint.text = s.vectorStoreEndpoint ?? "";
            _fApiKey.text = s.vectorStoreApiKey ?? "";
            _fCollection.text = s.collectionName ?? "";
            _fPgHost.text = s.pgHost ?? "";
            _fPgPort.text = s.pgPort.ToString();
            _fPgDb.text = s.pgDatabase ?? "";
            _fPgUser.text = s.pgUser ?? "";
            _fPgPass.text = s.pgPassword ?? "";
            _fPgTable.text = s.pgTable ?? "";
            _fEmbEndpoint.text = s.embeddingEndpoint ?? "";
            _fEmbKey.text = s.embeddingApiKey ?? "";
            _fEmbModel.text = s.embeddingModel ?? "";
            _fChatEndpoint.text = s.chatEndpoint ?? "";
            _fChatKey.text = s.chatApiKey ?? "";
            _fChatModel.text = s.chatModel ?? "";
            _fChunk.text = s.chunkSize.ToString();
            _fOverlap.text = s.overlap.ToString();
            _fTopK.text = s.topK.ToString();
            string dir = s.defaultMarkdownDir ?? "";
            if (string.IsNullOrEmpty(dir)) dir = DetectDefaultMarkdownDir() ?? "";
            _fDir.text = dir;
        }

        private async void OnStats()
        {
            _status.text = "统计中…";
            await RefreshStatsAsync();
        }

        // 统计并写到状态栏（重置后也复用，立即呈现真实计数）
        private async Task RefreshStatsAsync()
        {
            await RagManager.Instance.EnsureInitializedAsync();
            var st = await RagManager.Instance.GetStatsAsync();
            _status.text = $"后端：{st.path}\n" +
                           $"文档数：{(st.docs < 0 ? "—（远程后端）" : st.docs.ToString())}\n" +
                           $"分块数：{st.chunks}\n" +
                           $"维度：{(st.dim.HasValue ? st.dim.Value.ToString() : "—（空库）")}";
        }

        // 进度轮询：将 IngestProgress 反映到状态文本（仅在已知总数后更新，避免首次读到 Total==0 而提前退出卡住状态）
        private async Task IngestLoop()
        {
            if (_prog == null) return;
            while (true)
            {
                if (_prog.Total > 0)
                {
                    _status.text = $"入库中：{_prog.Current}（{_prog.Done}/{_prog.Total}）\n{_prog.Log}";
                    if (_prog.Done >= _prog.Total) break;
                }
                await Task.Delay(200);
            }
        }

        // 鼠标滚轮滚动设置面板：复用 ScrollRect 自带 OnScroll，方向与拖拽一致。
        private class WheelScroll : MonoBehaviour
        {
            public ScrollRect scroll;
            private void Update()
            {
                float d = Input.mouseScrollDelta.y;
                if (d == 0f || scroll == null) return;
                var pd = new PointerEventData(EventSystem.current);
                pd.scrollDelta = new Vector2(0f, d);
                scroll.OnScroll(pd);
            }
        }

        // 日志遮罩按 ESC 关闭
        private class OverlayEscCloser : MonoBehaviour
        {
            public System.Action onClose;
            private void Update()
            {
                if (Input.GetKeyDown(KeyCode.Escape)) onClose?.Invoke();
            }
        }
    }
}
