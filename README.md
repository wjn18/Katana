# Katana

斗笠剑士的 Unity 演示项目，包含打刀 Generic 动画与平地操控，以及薙刀 Humanoid 第三人称战斗。

## 打开项目

1. 安装 Unity **6000.6.5f1**，在 Unity Hub 中添加此项目目录。
2. 首次打开等待 Unity 还原 `Packages/` 中声明的依赖并导入资源。Git 包依赖需要联网访问 GitHub。
3. 打刀入口为 `Assets/KatanaDemo/UchigatanaCombat/Scenes/UchigatanaCombatDemo.unity`；薙刀入口为 `Assets/KatanaDemo/NaginataCombat/Scenes/NaginataThirdPersonCombat.unity`。

打刀进入 Play 后自动待机，WASD / 方向键慢走，Shift 快走，Ctrl 奔跑，左键单次攻击，R 复位。完整玩家为 `UchigatanaCombat/Prefabs/UchigatanaCombatPlayer.prefab`，独立武器为 `Weapons/Uchigatana/Prefabs/InitialUchigatana.prefab`；两者均有实际引用。

薙刀支持 WASD 移动、Shift 奔跑、鼠标环绕、左键逐次连招、右键重击和数字 1–6 单招。完整操作见 `Assets/KatanaDemo/NaginataCombat/使用说明.txt`。

打刀移动用于平地测试，无重力或碰撞；两种武器当前均无敌人或命中扣血，角色大幅抬腿仍可能出现衣物穿模。当前目录与维护规则见 `Assets/KatanaDemo/资源目录规范.md`。

## 仓库内容

- `Assets/`：模型、贴图、动画、场景、预制体、脚本及对应 `.meta` 文件。
- `Packages/`：包依赖与锁定文件。
- `ProjectSettings/`：项目配置与 Unity 版本。

Unity 缓存、构建输出、IDE 配置和本地验证截图不纳入版本控制。外部 Blender 制作文件和角色设定资料位于此仓库之外。
