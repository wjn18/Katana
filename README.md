# Katana

斗笠剑士的 Unity 演示项目，包含混元 V3 角色、持刀呼吸与行走动画，以及平地移动测试。

## 打开项目

1. 安装 Unity **6000.6.5f1**，在 Unity Hub 中添加仓库根目录。
2. 首次打开等待 Unity 还原 `Packages/` 中声明的依赖并导入资源。Git 包依赖需要联网访问 GitHub。
3. 打开 `Assets/KatanaDemo/Scenes/KatanaCombatDemo.unity`，进入 Play。

WASD / 方向键移动，R 复位。当前移动用于平地测试，尚未包含重力、碰撞或攻击；角色大幅抬腿仍存在衣物穿模。

## 仓库内容

- `Assets/`：模型、贴图、动画、场景、预制体、脚本及对应 `.meta` 文件。
- `Packages/`：包依赖与锁定文件。
- `ProjectSettings/`：项目配置与 Unity 版本。

Unity 缓存、构建输出、IDE 配置和本地验证截图不纳入版本控制。外部 Blender 制作文件和角色设定资料位于此仓库之外。
