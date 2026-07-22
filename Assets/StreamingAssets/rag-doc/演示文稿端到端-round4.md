

**三、手工复现完整操作流程**

  **环境准备（若未启动）**

  

  # 1. 启动全部依赖服务（PostGIS / Mongo / MinIO / RabbitMQ / ES / Redis）

  cd ./backend

  docker compose up -d

  

  # 2. 启动后端（端口 8080）

  ./gradlew bootRun

  

  # 3. 启动后台 dev server（端口 5174）

  cd ./admin

  pnpm dev

  

  # 4. 启动前台 dev server（端口 5173）

  cd ./frontend

  pnpm dev

  

  ▎ _当前状态：以上服务_**_已全部启动并运行正常_**_。_

  **后台录入演示文稿（带3D模型筛选）**

  

  1. **登录后台**：打开 http://localhost:5174/login

    - 账号：testuser

    - 密码：password123

  2. **选择项目**：登录后进入 Dashboard，点击顶部项目选择器，选择任意项目（如

  _Sample Project 1_）。

  3. **进入演示文稿模块**：左侧菜单 → **演示文稿**，进入列表页。

  4. **新建演示文稿**：点击右上角 **新建演示文稿** 按钮，进入四步向导。

  5. **Step 1 — 基础设置**：

    - 名称：3D Cube Demo

    - 自定义 URL：my-3d-demo（仅允许字母、数字、连字符，系统会自动校验唯一性）

    - 描述：使用 minimal-cube.glb 的真实3D模型演示

    - 点击 **下一步**

  6. **Step 2 — 内容筛选**（⚠️  关键步骤）：

    - 在空间树中，**勾选第一个楼栋**（如 _Building A_）。由于 check-strictly = 

  false，其下属楼层（包含已关联 minimal-cube.glb 的楼层）会自动被勾选。

    - 状态筛选可勾选 **可用**（可选）。

    - 点击 **下一步**

  7. **Step 3 — 主题选择**：

    - 点击 **默认主题** 卡片，使其高亮选中。

    - 点击 **下一步**

  8. **Step 4 — 隐私配置**：

    - 隐私类型选择 **公开访问**。

    - 点击 **发布**，在确认弹窗中点击 **确定**。

  9. **获取分享链接**：

    - 发布成功后，页面会显示 **分享链接**，格式类似：

  http://localhost:8080/presentation/my-3d-demo

    - 点击 **复制** 按钮复制链接。

  

  **前台查看3D模型呈现**

  

  1. **访问公开演示文稿**：

    - 将上述链接中的 localhost:8080 替换为前台地址 localhost:5173，即：

  http://localhost:5173/presentation/my-3d-demo

    - 在浏览器中打开该链接（无需登录）。

  2. **验证3D场景**：

    - 页面标题栏显示演示文稿名称。

    - 中央 canvas 区域加载 **minimal-cube.glb** 模型（一个立方体）。

    - 底部楼层导航栏可见。

    - 3D 场景中可见单元标注（CSS2DRenderer 渲染的圆形标签）。

  

  **使用已种子化的快捷链接（无需手工创建）**

  

  由于后端 TestUserInitializer 已自动注入测试数据，也可直接访问：

  http://localhost:5173/presentation/3d-cube-demo

  即可立即看到已配置好3D模型筛选的演示文稿效果。

  

  ---

  **四、Playwright 一键复测命令**

  

  # 后台 E2E（确保 localhost:5174 与 localhost:8080 已运行）

  cd ./admin

  pnpm exec playwright test tests/presentation.spec.ts

  

  # 前台 E2E（确保 localhost:5173 与 localhost:8080 已运行）

  cd ./frontend

  pnpm exec playwright test tests/presentation.spec.ts

  

  所有测试均连接**真实后端与真实依赖服务**，未使用任何 Mock。