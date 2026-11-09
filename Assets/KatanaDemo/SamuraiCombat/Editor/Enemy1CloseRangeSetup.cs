using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>只初始化Enemy1三招的原窗近身前扇区；不重建动画、根运动或其他战斗数值。</summary>
    public static class Enemy1CloseRangeSetup
    {
        const int Version = 1;
        static readonly string[] States = { "Light01", "Light02", "Heavy" };

        [MenuItem("Katana/Samurai/Enemies/Install Enemy1 Close Range Contacts")]
        public static void InstallAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出Play再初始化Enemy1近身判定。");
            var actions = States.Select(state =>
            {
                string path = Enemy1CombatSetup.ConfigFolder + "/Enemy1_" + state + ".asset";
                var action = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(path);
                if (action == null || action.stateName != state || action.clip == null || action.hitSource != SamuraiHitSource.Weapon)
                    throw new InvalidOperationException("正式Enemy1武器动作缺失或状态不符：" + path);
                Vector2 window = action.GetDamageWindow(0);
                if (float.IsNaN(window.x) || float.IsNaN(window.y) || window.x < 0f || window.y > 1f || window.y <= window.x)
                    throw new InvalidOperationException("Enemy1首个原命中窗无效：" + path);
                return action;
            }).ToArray();
            var missing = actions.Where(a => a.enemyCloseRangeConfigurationVersion < Version).ToArray();
            foreach (var action in missing)
            {
                Vector2 window = action.GetDamageWindow(0);
                // 当前标准胶囊贴边中心距约0.61米，玩家最近表面约0.35米；额外留0.10米接触余量。
                action.closeRangeReach = .45f;
                action.closeRangeHalfAngle = 35f;
                action.closeRangeStart = window.x;
                action.closeRangeEnd = window.y;
                action.enemyCloseRangeConfigurationVersion = Version;
                EditorUtility.SetDirty(action);
                AssetDatabase.SaveAssetIfDirty(action);
            }
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../锈甲杂兵/动画资源/enemy1-combat/analysis/close-contact-20261028"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "安装参数.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                date = "2026-10-28", version = Version,
                initialized = missing.Select(a => a.name).ToArray(),
                preserved = actions.Except(missing).Select(a => a.name).ToArray(),
                actions = actions.Select(a => new
                {
                    a.name, path = AssetDatabase.GetAssetPath(a), a.enemyCloseRangeConfigurationVersion,
                    a.closeRangeReach, a.closeRangeHalfAngle, a.closeRangeStart, a.closeRangeEnd,
                    originalWindow = new[] { a.GetDamageWindow(0).x, a.GetDamageWindow(0).y }
                }).ToArray()
            }, Newtonsoft.Json.Formatting.Indented));
            Debug.Log("Enemy1 close contacts initialized " + missing.Length + ", preserved " + (actions.Length - missing.Length)
                + "; original body/root/windows/damage/radius retained.");
        }
    }
}
