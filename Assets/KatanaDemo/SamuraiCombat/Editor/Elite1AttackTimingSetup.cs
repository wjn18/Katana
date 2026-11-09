using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>只迁移尚未修正的默认下砸窗；区域和真实锤轨使用同一个门控，动画/根轨迹保持。</summary>
    public static class Elite1AttackTimingSetup
    {
        // 区域配置v2修正落锤时序与默认中心轴；v1范围/高度轨迹保持，后续重装不覆盖人工调参。
        const int Version = 2;
        static readonly string[] States = { "Combo02", "Combo03", "Combo04" };
        struct Correction
        {
            public int index;
            public Vector2 before, after;
            public float beforeYaw, afterYaw;
            public Correction(int index, float start, float end, float nextStart, float nextEnd, float beforeYaw, float afterYaw)
            { this.index = index; before = new Vector2(start, end); after = new Vector2(nextStart, nextEnd);
                this.beforeYaw = beforeYaw; this.afterYaw = afterYaw; }
        }
        static Correction[] Corrections(string state)
        {
            switch (state)
            {
                // 原身体120Hz落锤样本；轴取新有效窗后35%中低位样本的中位方向，保留展开角圈数。
                case "Combo02": return new[] { new Correction(2, .49f, .59f, .553f, .567f, -382.2456f, -383.99283f) };
                case "Combo03": return new[] { new Correction(0, .23f, .30f, .272f, .289f, -397.11014f, -396.2666f),
                    new Correction(1, .34f, .41f, .377f, .388f, -409.86624f, -376.1476f),
                    new Correction(3, .73f, .82f, .787f, .797f, 370.7612f, 364.42496f) };
                case "Combo04": return new[] { new Correction(1, .34f, .42f, .397f, .408f, 369.93484f, 371.04813f) };
                default: return Array.Empty<Correction>();
            }
        }

        [MenuItem("Katana/Samurai/Enemies/Fix Elite1 Combo Impact Windows")]
        public static void InstallAll() => InstallForStates(States);

        internal static void InstallForStates(IEnumerable<string> requested)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请退出Play后维护攻击窗。");
            var reports = new List<object>();
            foreach (var state in requested.Where(s => States.Contains(s)).Distinct())
            {
                string path = Elite1CombatSetup.ConfigFolder + "/Elite1_" + state + ".asset";
                var action = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(path);
                if (action == null) throw new InvalidOperationException("缺少正式攻击资产：" + path);
                if (action.attackVolumeConfigurationVersion < 1)
                    throw new InvalidOperationException("请先安装原精英攻击区域：" + path);
                var previous = action.damageWindows != null ? action.damageWindows.ToArray() : Array.Empty<Vector2>();
                bool changed = Apply(action);
                if (changed) { EditorUtility.SetDirty(action); AssetDatabase.SaveAssetIfDirty(action); }
                reports.Add(new { path, state, changed, action.attackVolumeConfigurationVersion,
                    before = previous.Select(w => new[] { w.x, w.y }).ToArray(),
                    after = (action.damageWindows ?? Array.Empty<Vector2>()).Select(w => new[] { w.x, w.y }).ToArray() });
            }
            if (reports.Count == 0) return;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../镇门鬼将/动画资源/elite1-combat/analysis/combo-recovery-20261028"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "impact-window-install.json"), Newtonsoft.Json.JsonConvert.SerializeObject(
                new { date = "2026-10-28", version = Version,
                    policy = "Only old default windows migrate once; unchanged default capsule axes recalibrate from the actual impact. User-edited windows/axes and explicit empty arrays remain; range/height keys, animation, root curves and combat values retained.", actions = reports }, Newtonsoft.Json.Formatting.Indented));
        }

        // Pure asset mutation kept separate from saving so cloned-asset tuning protection can be verified.
        static bool Apply(SamuraiActionDefinition action)
        {
            if (action == null || action.attackVolumeConfigurationVersion >= Version) return false;
            var corrections = Corrections(action.stateName);
            if (corrections.Length == 0) return false;
            Undo.RecordObject(action, "Fix Elite1 combo impact windows");
            if (action.damageWindows != null)
            {
                var windows = action.damageWindows.ToArray();
                foreach (var correction in corrections)
                {
                    if (correction.index >= windows.Length) continue;
                    var old = windows[correction.index];
                    // A manually tuned endpoint belongs to the user, including an explicit zero window.
                    if (Mathf.Abs(old.x - correction.before.x) > .000001f || Mathf.Abs(old.y - correction.before.y) > .000001f) continue;
                    windows[correction.index] = correction.after;
                    if (action.attackVolumes == null || correction.index >= action.attackVolumes.Length) continue;
                    var region = action.attackVolumes[correction.index];
                    if (region == null || region.shape != SamuraiAttackVolumeShape.Capsule || region.frames == null
                        || region.frames.Length == 0 || region.frames.Any(f => Mathf.Abs(f.yaw - correction.beforeYaw) > .0001f)) continue;
                    var frames = region.frames.ToArray();
                    for (int i = 0; i < frames.Length; i++) { frames[i].yaw = correction.afterYaw; }
                    region.frames = frames;
                }
                action.damageWindows = windows;
            }
            action.attackVolumeConfigurationVersion = Version;
            return true;
        }
    }
}
