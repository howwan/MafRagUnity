// 场景引导（D0 / FR-9）。
// 通过 RuntimeInitializeOnLoadMethod 在游戏启动时自动创建：持久化管理器 + 事件系统 + 按场景名构建 UI。
// 无需在 .unity 场景中放置任何脚本引用，降低资源耦合；2 个场景仅含一个相机即可。

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace MafRag
{
    public class RagBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            RagLogger.Init(); // 在所有日志产生前启动统一日志（落盘到 persistentDataPath/RagLogs）
            RagSettings.Load();                              // 读取持久化配置（含日志级别）
            RagLogger.ApplyMinLevel(RagSettings.Current.logLevel); // 应用上次设置的日志级别

            // 持久化管理器（跨场景保持对话上下文与向量库连接）
            var mgrGo = new GameObject("RagManager");
            mgrGo.AddComponent<RagManager>();
            Object.DontDestroyOnLoad(mgrGo);

            EnsureEventSystem();

            BuildForCurrentScene();
            SceneManager.sceneLoaded += (scene, mode) => BuildForCurrentScene();
        }

        // 运行时确保存在 EventSystem（uGUI 交互所需；DontDestroyOnLoad 使其跨场景存活）
        static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
                Object.DontDestroyOnLoad(es);
            }
        }

        // 按当前场景名构建对应 UI（主场景聊天 / 设置场景配置）。
        // 通过实例化组件（其 Awake 中调用 Build），使 UI 类可持有实例字段与事件处理器。
        static void BuildForCurrentScene()
        {
            // 清理上一轮可能残留的运行时 UI 对象（例如 Play 模式下保存场景后，
            // 重新编译还原出来的旧 UICanvas/场景 UI，可能带着已被删除的类引用），
            // 避免重复画布或“missing script”报错。
            foreach (var tag in new[] { "UICanvas", "MainSceneUI", "SettingsSceneUI" })
            {
                var old = GameObject.Find(tag);
                if (old != null) Object.Destroy(old);
            }

            string name = SceneManager.GetActiveScene().name;
            if (name == "MainScene") { var go = new GameObject("MainSceneUI"); go.AddComponent<MainSceneUI>(); }
            else if (name == "SettingsScene") { var go = new GameObject("SettingsSceneUI"); go.AddComponent<SettingsSceneUI>(); }
        }
    }
}
