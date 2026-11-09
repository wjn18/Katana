using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>为当前关卡补缺少的巡逻路线；保留已有路线、演员位姿和全部手调。</summary>
    public static class SamuraiPatrolLevelSetup
    {
        const string RootName = "Patrol routes - 小兵巡逻路径";

        [Serializable]
        public sealed class InstallReport
        {
            public string scene;
            public int ordinaryEnemies, agentsAdded, routesAdded, existingRoutesPreserved, fallbackStandingRoutes;
            public List<InstalledRoute> routes = new List<InstalledRoute>();
        }
        [Serializable]
        public sealed class InstalledRoute
        {
            public string enemy, route, mode;
            public int points;
            public bool created;
            public Vector3[] worldPoints;
        }
        sealed class Recipe
        {
            public string routeName;
            public SamuraiPatrolMode mode;
            public Color color;
            public float wait;
            public Vector2[] additionalPoints;
        }
        sealed class Plan
        {
            public SamuraiTrainingEnemy brain;
            public SamuraiPatrolAgent agent;
            public Recipe recipe;
            public Vector3[] positions;
        }

        [MenuItem("Katana/Samurai/Patrol/Install Level Patrol Routes")]
        static void InstallMenu() => Debug.Log(Install());

        public static string Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 Play 后再安装关卡巡逻路线。");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded
                || scene.name != "SamuraiCombatDemo_BlenderAssembly" && scene.name != "SamuraiCombatDemo")
                throw new InvalidOperationException("关卡巡逻安装仅支持活动场景 SamuraiCombatDemo_BlenderAssembly 或 SamuraiCombatDemo。");

            var report = new InstallReport { scene = scene.name };
            var plans = new List<Plan>();
            Physics.SyncTransforms();
            // 先采样全部候选地面。任何节点失败时，还没有创建对象或修改组件。
            foreach (var brain in UnityEngine.Object.FindObjectsByType<SamuraiTrainingEnemy>(FindObjectsInactive.Include))
            {
                if (brain.gameObject.scene != scene || !brain.isActiveAndEnabled || !brain.gameObject.activeInHierarchy) continue;
                var actor = brain.actor != null ? brain.actor : brain.GetComponent<SamuraiCombatant>();
                if (actor == null || !actor.isActiveAndEnabled || !IsOrdinaryEnemy(brain, actor)) continue;
                report.ordinaryEnemies++;
                var agent = brain.GetComponent<SamuraiPatrolAgent>();
                if (agent != null && agent.route != null)
                {
                    report.existingRoutesPreserved++;
                    report.routes.Add(Describe(brain.name, agent.route, false));
                    continue;
                }
                var recipe = RecipeFor(brain.name);
                if (recipe.additionalPoints.Length == 0) report.fallbackStandingRoutes++;
                Vector3 spawn = brain.transform.position;
                var positions = new Vector3[recipe.additionalPoints.Length + 1];
                // 起点保持当前出生坐标，同时验证其下方确有当前层地面。
                Ground(scene, brain.name, 0, spawn);
                positions[0] = spawn;
                for (int i = 0; i < recipe.additionalPoints.Length; i++)
                {
                    Vector2 xz = recipe.additionalPoints[i];
                    positions[i + 1] = Ground(scene, brain.name, i + 1, new Vector3(xz.x, spawn.y, xz.y));
                }
                plans.Add(new Plan { brain = brain, agent = agent, recipe = recipe, positions = positions });
            }
            if (plans.Count == 0) return JsonUtility.ToJson(report, true);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("补充关卡小兵巡逻路线");
            try
            {
                GameObject root = null;
                foreach (var go in scene.GetRootGameObjects()) if (go.name == RootName) { root = go; break; }
                if (root == null)
                {
                    root = new GameObject(RootName);
                    SceneManager.MoveGameObjectToScene(root, scene);
                    Undo.RegisterCreatedObjectUndo(root, "创建巡逻路线分组");
                }
                foreach (var plan in plans)
                {
                    bool addAgent = plan.agent == null;
                    var agent = plan.agent != null ? plan.agent : Undo.AddComponent<SamuraiPatrolAgent>(plan.brain.gameObject);
                    var go = new GameObject(plan.recipe.routeName);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    Undo.RegisterCreatedObjectUndo(go, "创建小兵巡逻路线");
                    Undo.SetTransformParent(go.transform, root.transform, "放入巡逻路线分组");
                    go.transform.SetPositionAndRotation(plan.brain.transform.position, Quaternion.identity);
                    var route = Undo.AddComponent<SamuraiPatrolRoute>(go);
                    Undo.RecordObject(route, "设置初始巡逻路点");
                    route.mode = plan.recipe.mode;
                    route.routeColor = plan.recipe.color;
                    route.points = new List<SamuraiPatrolPoint>();
                    for (int i = 0; i < plan.positions.Length; i++)
                    {
                        route.points.Add(new SamuraiPatrolPoint
                        {
                            localPosition = route.transform.InverseTransformPoint(plan.positions[i]),
                            waitSeconds = i == 0 || i == plan.positions.Length - 1 ? plan.recipe.wait : .5f,
                            useFacing = i == 0,
                            localYaw = plan.brain.transform.eulerAngles.y - route.transform.eulerAngles.y
                        });
                    }
                    Undo.RecordObject(agent, "绑定关卡巡逻路线");
                    agent.route = route;
                    if (addAgent)
                    {
                        agent.speedMultiplier = .6f;
                        agent.arrivalDistance = .12f;
                        report.agentsAdded++;
                    }
                    EditorUtility.SetDirty(route);
                    EditorUtility.SetDirty(agent);
                    if (PrefabUtility.IsPartOfPrefabInstance(agent)) PrefabUtility.RecordPrefabInstancePropertyModifications(agent);
                    report.routesAdded++;
                    report.routes.Add(Describe(plan.brain.name, route, true));
                }
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undoGroup);
                SceneView.RepaintAll();
                return JsonUtility.ToJson(report, true);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        static bool IsOrdinaryEnemy(SamuraiTrainingEnemy brain, SamuraiCombatant actor)
        {
            string profileName = actor.Profile != null ? actor.Profile.name : "";
            if (brain.name.StartsWith("Elite", StringComparison.OrdinalIgnoreCase)
                || profileName.IndexOf("Elite", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return profileName == "Enemy1" || brain.name.StartsWith("Enemy1", StringComparison.Ordinal);
        }

        static InstalledRoute Describe(string enemy, SamuraiPatrolRoute route, bool created)
        {
            int count = route.points != null ? route.points.Count : 0;
            var positions = new Vector3[count];
            for (int i = 0; i < count; i++) positions[i] = route.GetWorldPosition(i);
            return new InstalledRoute { enemy = enemy, route = route.name, mode = route.mode.ToString(), points = count, created = created, worldPoints = positions };
        }

        static Vector3 Ground(Scene scene, string enemy, int pointIndex, Vector3 reference)
        {
            RaycastHit best = default;
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(reference + Vector3.up * .5f, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || hit.collider.gameObject.scene != scene || hit.collider.isTrigger
                    || hit.collider.GetComponentInParent<SamuraiCombatant>() != null || hit.normal.y <= .65f || hit.distance >= nearest) continue;
                best = hit; nearest = hit.distance;
            }
            if (float.IsPositiveInfinity(nearest))
                throw new InvalidOperationException("巡逻路线安装失败：" + enemy + " 的路点 " + (pointIndex + 1)
                    + " 在 " + reference.ToString("F3") + " 附近没有当前楼层地面（仅检测参考 Y +0.5 至 -0.7 米）。");
            return best.point + Vector3.up * .01f;
        }

        static Recipe RecipeFor(string enemy)
        {
            string key = enemy.Replace(" ", "");
            switch (key)
            {
                case "Enemy1":
                    return Make("01 入口巡哨", SamuraiPatrolMode.Loop, new Color(1f, .65f, .2f), 1.5f,
                        new Vector2(14f, -7.9f), new Vector2(11f, -8.5f), new Vector2(11f, -10f), new Vector2(17f, -10f));
                case "Enemy1(1)":
                    return Make("02 中庭侧卫", SamuraiPatrolMode.PingPong, new Color(.3f, 1f, .45f), 1.5f,
                        new Vector2(10f, 9.3f), new Vector2(13f, 10f), new Vector2(16f, 9.3f), new Vector2(16f, 5f));
                case "Enemy1(2)":
                    return Make("03 下院大殿守卫", SamuraiPatrolMode.Loop, new Color(1f, .35f, .35f), 1.5f,
                        new Vector2(11f, 17.3f), new Vector2(10.5f, 15f), new Vector2(13f, 14f), new Vector2(15.5f, 15f), new Vector2(15.4f, 17.3f));
                case "Enemy1-Eaststudyguard":
                    return Make("04 东书房守卫", SamuraiPatrolMode.PingPong, new Color(.8f, .45f, 1f), 2f,
                        new Vector2(20.5f, 7f), new Vector2(17.8f, 7f), new Vector2(17.8f, 8.7f));
                case "Enemy1(3)":
                    return Make("05 观峡山屋巡哨", SamuraiPatrolMode.PingPong, new Color(.2f, .85f, 1f), 1.5f,
                        new Vector2(-7f, 25.3f), new Vector2(-10f, 25.3f), new Vector2(-12f, 25.3f));
                case "Enemy1(5)":
                    return Make("06 上院东侧守卫", SamuraiPatrolMode.PingPong, new Color(1f, .45f, .8f), 1.5f,
                        new Vector2(-19.9f, 19.3f), new Vector2(-19.9f, 21f));
                case "Enemy1(6)":
                    return Make("07 上院主殿守卫", SamuraiPatrolMode.Loop, new Color(1f, .9f, .25f), 1.5f,
                        new Vector2(-28f, 19.7f), new Vector2(-30.5f, 19.7f), new Vector2(-30.5f, 17.4f), new Vector2(-26f, 17.4f), new Vector2(-24f, 17.4f));
                default:
                    return Make(enemy + " 原位站岗（待布点）", SamuraiPatrolMode.PingPong, Color.white, 2f);
            }
        }
        static Recipe Make(string name, SamuraiPatrolMode mode, Color color, float wait, params Vector2[] points)
            => new Recipe { routeName = name, mode = mode, color = color, wait = wait, additionalPoints = points };
    }
}
