# MafRagUnity —— 基于 Microsoft Agent Framework 的 Unity RAG 应用

在 Unity 中实现检索增强生成（RAG）：本地 Markdown 知识库 → 向量化（SQLite/Qdrant/pgvector）→ Top-K 检索 → MAF（Microsoft Agent Framework）智能体生成答案。支持 **PC（Windows）** 与 **Android**，含 **2 个场景**（主场景对话 / 子场景配置与数据管理）。

---

## 1. 前置条件
- **Unity 2022.3.17f1c1**。
- Android 构建：通过 Unity Hub 安装 **Android Build Support**（含 SDK/NDK/JDK）。
- **Ollama 服务**：默认端点 `http://localhost:11434/v1`（可在设置中修改）。
  - 需拉取模型：`qwen3-embedding:0.6b`（向量化）与对话模型（默认 `qwen3.6:35b-a3b-mtp-q4_K_M`）。
  - 命令行示例：`ollama pull qwen3-embedding:0.6b` 、 `ollama pull qwen3.6:35b-a3b-mtp-q4_K_M`。
- **Docker容器服务**：执行如下命令
  ```bash
  cd MafRagUnity
  docker compose up -d
  ```
  向量数据库URL：
    - **PostgreSQL + pgvector**: `localhost:5432`
    - **pgweb（数据库管理 UI）**: `http://localhost:8081`
    - **Qdrant**: `http://localhost:6333`
    - **Qdrant（数据库管理 UI）**: `http://localhost:6333/dashboard#/welcome`
---

## 2. 打开与运行（PC）
1. 用 Unity Hub 打开本工程目录 `MafRagUnity`（首次打开会自动导入资源并编译，约 1–2 分钟）。
2. **两个场景已自动配置**到 Build Settings（`MainScene` 为启动场景；若需重建，菜单 `MafRag > Setup Scenes & Build Settings` 一键生成）。
3. 确认 Ollama 已启动且模型已拉取。如果要访问远程向量数据库，需启动docker容器服务。
4. 点击 **Play**：
   - 主场景：底部输入框用中文提问，回车或“发送”。首次提问若知识库为空会自动入库内置知识库。
   - 点击“来源”查看后台向量数据库的情况。
   - “设置”进入子场景；“关闭”退出应用。

---

## 3. 设置场景（参数 + 数据管理）
- **① 向量库后端（统一抽象层，可无缝切换）**：三选一按钮（pgvector 仅在 PC 构建 + 已定义 `NPGSQL` 符号时显示）
  - **SQLite（本地·离线）**：默认。向量存于设备 `persistentDataPath/rag.db`，无需任何外部服务，PC / Android 均可离线运行。
  - **Qdrant（远程·高性能）**：选它后填写远端地址（如 `http://192.168.x.x:6333`，即 docker 容器服务地址）、可选 APIKey、集合名。后端经 REST 读写，**Android 可直连宿主机 docker 容器**。
  - **pgvector（生产·仅 PC）**：选它后填写主机 / 端口 / 数据库 / 用户名 / 密码 / 表名，后端经 Npgsql 访问 Postgres。**首次连接会自动建库并初始化**：若目标库不存在会自动 `CREATE DATABASE`，随后自动执行 `CREATE EXTENSION IF NOT EXISTS vector` 并建表/索引，仅当账号无 CREATE EXTENSION / CREATEDB 权限时才需超级用户先建一次。该后端**仅 PC 构建可用**（条件编译 `NPGSQL`），不会进入 Android 包。启用方法见文末“pgvector 启用步骤”。
  - 所有后端**共享同一套 Embedding / 文本分块 / MAF 生成 / UI 交互逻辑**。
- **② Embedding 配置**：向量化端点 / ApiKey / 模型（与对话完全独立）。
- **③ 对话 LLM 配置**：端点 / ApiKey / 模型。
- **④ 检索 / 分块**：TopK、ChunkSize、Overlap。
- **⑤ 默认 Markdown 目录**：可空（= 内置知识库`StreamingAssets/rag-doc/*.md`）；PC 端可点“浏览”用系统文件夹对话框选择。
- **⑥ 向量数据管理**：
  - “浏览”：PC 弹文件夹对话框，Android 用 ⑤ 的路径输入。
  - “入库”：将选择目录下的所有Markdown(*.md)文件入库。
  - “重置向量库”：二次确认后清空。
  - “刷新统计”：后端位置 / 文档数 / 分块数 / 已存向量维度。
  - 状态栏显示操作进展。
- **⑦ 日志**：
  - **日志级别**：`Debug / Info / Warn / Error` 四档切换（越低越详细），立即生效并持久化到 `rag-config.json`，下次启动自动应用。
  - **查看日志**：弹出可滚动面板，显示 `persistentDataPath/RagLogs/rag.log` 最近内容（支持鼠标滚轮）。
  - **导出日志**：复制为 `rag-log-<时间戳>.txt` 独立文件，PC 下用默认程序直接打开，便于排查/反馈。
- “保存”写入持久化配置并即时重建后端（返回主场景即生效）；“返回”回主场景。对话上下文跨场景保持。

---

## 4. Android 构建
1. `File > Build Settings > Android > Switch Platform`（需已装 Android Build Support）。
2. `Player Settings`：`Scripting Backend = IL2CPP`（默认），`Target Architectures` 勾选 `ARM64`（移动设备）。
3. 已内置 `Assets/Plugins/Android/AndroidManifest.xml`（含 `INTERNET` 权限，访问局域网 Ollama）。
4. `Build` / `Build and Run`。SQLite 原生库（`libsqlite3.so`）已覆盖 4 个 ABI。
5. 中文渲染：已捆绑 `Assets/Resources/CJKFont.ttf`（黑体）。

---

## 5. 工程结构
```
Assets/
 ├─ Packages/            MAF 等托管 DLL
 ├─ Plugins/             SQLite 原生库（x86_64/Android 四 ABI）+ Mono.Data.Sqlite.dll
 │                          （另：pgvector 启用时把 Npgsql.dll + 依赖放到 Assets/Plugins/Npgsql，仅 PC 平台）
 ├─ Fonts/Resources/CJKFont.ttf   中文 CJK 字体
 ├─ StreamingAssets/rag-doc/       内置知识库 .md + rag-config.json + manifest.txt
├─ Code/
│   ├─ MafRag/        
│   │   ├─ Core/           
│   │   │   ├─ IVectorStoreBackend.cs   统一向量库抽象接口
│   │   │   ├─ SqliteVectorStore.cs     SQLite 默认后端
│   │   │   ├─ EmbeddingFactory.cs      Embedding 向量化
│   │   │   ├─ MarkdownChunker.cs       文本分块
│   │   │   ├─ RagIngestor.cs           入库
│   │   │   ├─ RagRetriever.cs          检索
│   │   │   ├─ RagConfig.cs             配置 + 后端工厂 CreateBackend()
│   │   │   ├─ RagSettings.cs           配置 JSON 持久化（含 logLevel）
│   │   │   ├─ RagLogger.cs             统一日志：落盘 + 5MB 轮转×3 + 级别切换 + 查看/导出
│   │   │   └─ IsExternalInit.cs        init 访问器支持
│   │   ├─ VectorStores/    远程后端与辅助：
│   │   │   ├─ QdrantVectorStore.cs    远程 Qdrant（REST，PC/Android）
│   │   │   ├─ PgVectorStore.cs        远程 pgvector（Npgsql，仅 PC / NPGSQL）
│   │   │   ├─ RagMiniJson.cs          极简 JSON 解析（无外部依赖）
│   │   │   ├─ VectorStoreUtil.cs      float[]/JSON、数值转换
│   │   │   └─ RemoteHashStore.cs      远程后端增量入库哈希（本地文件）
│   │   ├─ RagBoot.cs            场景引导（RuntimeInitializeOnLoadMethod）
│   │   ├─ RagManager.cs        跨场景状态单例（DontDestroyOnLoad）
│   │   ├─ RagAgentCore.cs       MAF 智能体封装（AsAIAgent + AgentSession + RunStreamingAsync）
│   │   ├─ MainSceneUI.cs        主场景聊天 UI（代码化构建）
│   │   ├─ SettingsSceneUI.cs    设置场景 UI
│   │   ├─ MafRagUI.cs           UI 构造辅助 + 字体
│   │   └─ WindowsFolderPicker.cs PC 原生文件夹对话框（P/Invoke）
│   └─ ChatClientFactory.cs  统一 IChatClient 工厂（Ollama OpenAI 兼容）
├─ Editor/MafRagMenu.cs  一键创建场景菜单（MafRag.Editor.asmdef）
├─ Editor/RagDocManifestGenerator.cs  构建期自动生成 rag-doc/manifest.txt + 菜单
└─ Scenes/MainScene.unity, SettingsScene.unity
```

## 6. 已知限制 / 注意
- **PC 文件夹选择**用原生对话框（Windows）；Android 无系统文件夹选择器，请用“默认目录”路径输入或“仅入库内置知识库”。
- Android 读取 `StreamingAssets` 经 `UnityWebRequest`；若内置知识库使用中文文件名在个别机型读取异常，可将 `rag-doc` 下文件改名（英文/数字）并更新 `manifest.txt`。
- **向量库后端**：已实现 **SQLite / Qdrant / pgvector** 三后端通过统一抽象层（`IVectorStoreBackend`）无缝切换。
  - **SQLite**：本地·离线，无需外部服务，PC / Android 均可。
  - **Qdrant**：走 REST，无需额外 DLL，**Android 可直连宿主机 docker 容器**。
  - **pgvector**：走 Npgsql（Postgres + `vector` 扩展），**仅 PC 构建**（`#if NPGSQL` 包裹），不进入 Android 包、不引用 Npgsql，因此不影响移动端。

### pgvector 启用步骤（仅 PC）
pgvector 后端以 `NPGSQL` 条件编译符号包裹，未定义时整个 `PgVectorStore` 不参与编译，项目始终可编译。要启用：
1. 下载 Npgsql 6.0.9 及其依赖（netstandard2.0）：`Npgsql.dll`、`Microsoft.Extensions.Logging.Abstractions.dll`、`System.Diagnostics.DiagnosticSource.dll`、`System.Memory.dll`、`System.Text.Json.dll`、`System.Threading.Tasks.Extensions.dll`、`System.Threading.Channels.dll`、`System.Runtime.CompilerServices.Unsafe.dll`。
2. 放入 `Assets/Plugins/Npgsql/`，并在 Inspector 中将平台设为 **PC（Editor + Standalone Windows）**，取消勾选 Android / WebGL / iOS（避免进入移动端包）。
3. `Edit > Project Settings > Player`：在 **PC Standalone** 与 **Editor** 的 `Scripting Define Symbols` 中加入 `NPGSQL`；Android 不添加（pgvector 按钮将不显示）。
4. **数据库与扩展自动创建**：`PgVectorStore.InitializeAsync` 在连接成功前会先连 `postgres` 库，若目标库不存在则自动 `CREATE DATABASE`；随后自动执行 `CREATE EXTENSION IF NOT EXISTS vector`，建表与 HNSW 余弦索引，**无需手动 `createdb` 或前置**。仅当你的数据库账号**不是超级用户、且没有 CREATEDB / CREATE EXTENSION 权限**时，才需要先用超级用户手动建库、建扩展一次（扩展文件需已由 pgvector 安装包提供）。
5. 确保账号可访问目标库（该账号需有表的读写权限）。
6. 重新编译后，设置场景即出现“pgvector（生产·PC）”按钮，填写连接信息即可。

> 注：未定义 `NPGSQL` 时，pgvector 分支被编译器移除，SQLite / Qdrant 完全不受影响；Android 构建也不会引用 Npgsql。
- 若 Ollama 未启动 / 模型未拉取，应用会给出友好提示且不崩溃。
- **内置知识库清单**：`rag-doc/manifest.txt` 供 Android 读取随包 `.md` 列表（PC 端入库直接递归扫描目录，无需清单）。其一致性由 **构建期自动保证**——打包前 `RagDocManifestGenerator`（`IPreprocessBuildWithReport`）会把 `rag-doc/*.md`（递归）重写进 `manifest.txt`；也可在 Editor 下用菜单 **`MafRag > 重新生成知识库清单`** 手动触发。注意该脚本位于 `MafRag.Editor.asmdef`，切勿移到运行时 asmdef，否则 `using UnityEditor;` 会与运行时程序集冲突导致全部编辑器菜单失效。
- **日志**：全部 Unity 日志落 `persistentDataPath/RagLogs/rag.log`（5MB 轮转×3），非 StreamingAssets（移动端只读）。可在设置场景「⑦ 日志」切换级别、查看或导出。
