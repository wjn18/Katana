using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>只初始化Elite1逐窗攻击区域；不重建动作、Prefab、控制器或战斗数值。</summary>
    public static class Elite1AttackVolumeSetup
    {
        const int Version = 1;
        const float SamplesPerSecond = 120f;
        const float MaximumOuterReach = 4.3f;
        static readonly string[] AllStates = { "Heavy", "Sweep", "SweepReverse", "Strike", "Combo01", "Combo02", "Combo03", "Combo04" };
        public static string ReportFolder => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../镇门鬼将/动画资源/elite1-combat/analysis/attack-volumes-20261028"));

        public static void InstallPilot() => Install(new[] { "Heavy", "Sweep" }, "横扫重劈安装参数.json");

        [MenuItem("Katana/Samurai/Enemies/Install Elite1 Attack Volumes All")]
        public static void InstallAll() => Install(AllStates, "八招安装参数.json");

        static void Install(string[] states, string reportName)
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先退出Play再初始化精英攻击区域。");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Elite1CombatSetup.PrefabPath);
            if (prefab == null) throw new InvalidOperationException("当前正式Elite1 Prefab缺失。");
            var actions = states.Select(LoadAction).ToArray();
            var missing = actions.Where(a => a.attackVolumeConfigurationVersion < Version).ToArray();
            if (missing.Length > 0) InitializeMissing(prefab, missing);
            Elite1AttackTimingSetup.InstallForStates(states);
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(Path.Combine(ReportFolder, reportName), Newtonsoft.Json.JsonConvert.SerializeObject(
                new
                {
                    date = "2026-10-28", version = Version, sampleHz = SamplesPerSecond,
                    maximumMainOuterReach = MaximumOuterReach,
                    initialized = missing.Select(a => a.name).ToArray(),
                    preserved = actions.Except(missing).Select(a => a.name).ToArray(),
                    actions = actions.Select(ActionReport).ToArray()
                }, Newtonsoft.Json.Formatting.Indented));
            Debug.Log("Elite1 attack volumes: initialized " + missing.Length + ", preserved " + (actions.Length - missing.Length)
                + ". Animation, range/height keys, combat tuning and scene retained; default combo impact timing/axes maintained separately.");
        }

        static SamuraiActionDefinition LoadAction(string state)
        {
            string path = Elite1CombatSetup.ConfigFolder + "/Elite1_" + state + ".asset";
            var action = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(path);
            if (action == null || action.clip == null) throw new InvalidOperationException("正式攻击资产或片段缺失：" + path);
            return action;
        }

        static void InitializeMissing(GameObject prefab, SamuraiActionDefinition[] actions)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject actor = null;
            var graph = default(PlayableGraph);
            // 先完整生成全部数据，确认成功后再提交，避免采样错误时留下半套配置。
            var generated = new Dictionary<SamuraiActionDefinition, SamuraiAttackVolume[]>();
            try
            {
                actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                foreach (var behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = actor.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
                    throw new InvalidOperationException("当前精英缺少有效Humanoid Avatar。");
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var transforms = actor.GetComponentsInChildren<Transform>(true);
                var bladeBase = transforms.Single(t => t.name == "BladeBase");
                var bladeTip = transforms.Single(t => t.name == "BladeTip");
                var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Elite1CombatSetup.ProfilePath);
                if (profile == null) throw new InvalidOperationException("正式Elite1战斗配置缺失。");
                foreach (var action in actions)
                {
                    animator.Rebind();
                    graph = PlayableGraph.Create("Elite1 attack volume initialization");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, action.clip);
                    playable.SetApplyFootIK(true);
                    AnimationPlayableOutput.Create(graph, "pose", animator).SetSourcePlayable(playable);
                    graph.Play();
                    var regions = new SamuraiAttackVolume[action.DamageWindowCount];
                    for (int index = 0; index < regions.Length; index++)
                    {
                        var samples = SampleWindow(actor.transform, bladeBase, bladeTip, graph, playable, action, index);
                        regions[index] = BuildRegion(action, index, samples, action.hitRadius > 0f ? action.hitRadius : profile.bladeRadius);
                        if (!regions[index].IsUsable) throw new InvalidOperationException("生成区域无效：" + action.name + " / " + index);
                    }
                    graph.Destroy();
                    generated.Add(action, regions);
                }
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            foreach (var pair in generated)
            {
                pair.Key.useAttackVolumes = true;
                pair.Key.attackVolumes = pair.Value;
                pair.Key.attackVolumeConfigurationVersion = Version;
                EditorUtility.SetDirty(pair.Key);
                AssetDatabase.SaveAssetIfDirty(pair.Key);
            }
        }

        struct HeadSample
        {
            public float phase, yaw;
            public Vector3 from, to, center;
        }

        static HeadSample[] SampleWindow(Transform root, Transform bladeBase, Transform bladeTip,
            PlayableGraph graph, AnimationClipPlayable playable, SamuraiActionDefinition action, int index)
        {
            var window = action.GetDamageWindow(index);
            if (window.x < 0f || window.y > 1f || window.y <= window.x)
                throw new InvalidOperationException("命中窗无效：" + action.name + " / " + index);
            int steps = Mathf.Max(1, Mathf.CeilToInt((window.y - window.x) * action.clip.length * SamplesPerSecond));
            var samples = new HeadSample[steps + 1];
            float previousYaw = 0f, unwrappedYaw = 0f;
            for (int step = 0; step <= steps; step++)
            {
                float phase = Mathf.Lerp(window.x, window.y, (float)step / steps);
                playable.SetTime(action.clip.length * phase);
                graph.Evaluate(.000001f);
                Vector3 from = root.InverseTransformPoint(bladeBase.position), to = root.InverseTransformPoint(bladeTip.position);
                Vector3 center = (from + to) * .5f;
                float yaw = Mathf.Atan2(center.x, center.z) * Mathf.Rad2Deg;
                unwrappedYaw = step == 0 ? yaw : unwrappedYaw + Mathf.DeltaAngle(previousYaw, yaw);
                previousYaw = yaw;
                samples[step] = new HeadSample { phase = phase, yaw = unwrappedYaw, from = from, to = to, center = center };
            }
            return samples;
        }

        static SamuraiAttackVolume BuildRegion(SamuraiActionDefinition action, int index, HeadSample[] samples, float bladeRadius)
        {
            bool capsule = IsDownwardStrike(action.stateName, index);
            var region = new SamuraiAttackVolume
            {
                enabled = true,
                shape = capsule ? SamuraiAttackVolumeShape.Capsule : SamuraiAttackVolumeShape.Sector,
                innerReach = .65f,
                halfAngle = 14f,
                radius = capsule ? .80f : .5f,
                minHeight = .10f,
                maxHeight = 2.1f,
                frames = new SamuraiAttackVolumeFrame[samples.Length]
            };
            // 纵劈使用接近地面阶段的真实落锤方向，避免头顶径向很短时yaw快速摆动变成横扫。
            float fixedYaw = LowStrikeYaw(samples);
            for (int i = 0; i < samples.Length; i++)
            {
                var sample = samples[i];
                float endpointReach = Mathf.Max(Horizontal(sample.from), Horizontal(sample.to));
                float outerReach = Mathf.Min(MaximumOuterReach, endpointReach + Mathf.Max(.01f, bladeRadius) + .10f);
                // 胶囊的半径属于自身外沿，不能在4.3米之外再额外增加0.8米。
                float reach = Mathf.Max(region.innerReach, outerReach - (capsule ? region.radius : 0f));
                region.frames[i] = new SamuraiAttackVolumeFrame
                {
                    phase = sample.phase,
                    yaw = capsule ? fixedYaw : sample.yaw,
                    reach = reach,
                    // 高位阶段将整个区域抬升，保留有限的垂直容差，避免锤头在6~8米高时投影到地面伤害。
                    lift = Mathf.Max(0f, sample.center.y - 2.5f)
                };
            }
            return region;
        }

        static bool IsDownwardStrike(string state, int index)
        {
            switch (state)
            {
                case "Heavy": return true;
                case "Combo02": return index == 2;
                case "Combo03": return index == 0 || index == 1 || index == 3;
                case "Combo04": return index == 1 || index == 2;
                default: return false;
            }
        }

        static float LowStrikeYaw(HeadSample[] samples)
        {
            float threshold = Mathf.Lerp(samples[0].phase, samples[samples.Length - 1].phase, .65f);
            var low = samples.Where(s => s.phase >= threshold && s.center.y <= 2.1f).Select(s => s.yaw).OrderBy(v => v).ToArray();
            if (low.Length == 0) low = samples.OrderBy(s => s.center.y).Take(Mathf.Max(1, samples.Length / 5)).Select(s => s.yaw).OrderBy(v => v).ToArray();
            return low[low.Length / 2];
        }

        static float Horizontal(Vector3 point) => new Vector2(point.x, point.z).magnitude;

        static object ActionReport(SamuraiActionDefinition action)
        {
            var regions = action.attackVolumes ?? new SamuraiAttackVolume[0];
            return new
            {
                action.name, action.stateName, path = AssetDatabase.GetAssetPath(action), action.useAttackVolumes,
                action.attackVolumeConfigurationVersion,
                windows = Enumerable.Range(0, action.DamageWindowCount).Select(i =>
                {
                    var window = action.GetDamageWindow(i);
                    var region = i < regions.Length ? regions[i] : null;
                    var frames = region != null ? region.frames : null;
                    return new
                    {
                        index = i, start = window.x, end = window.y,
                        enabled = region != null && region.enabled, shape = region != null ? region.shape.ToString() : "None",
                        keyCount = frames != null ? frames.Length : 0,
                        innerReach = region != null ? region.innerReach : 0f, halfAngle = region != null ? region.halfAngle : 0f,
                        radius = region != null ? region.radius : 0f,
                        minHeight = region != null ? region.minHeight : 0f, maxHeight = region != null ? region.maxHeight : 0f,
                        yawStart = frames != null && frames.Length > 0 ? frames[0].yaw : 0f,
                        yawEnd = frames != null && frames.Length > 0 ? frames[frames.Length - 1].yaw : 0f,
                        reachMin = frames != null && frames.Length > 0 ? frames.Min(f => f.reach) : 0f,
                        reachMax = frames != null && frames.Length > 0 ? frames.Max(f => f.reach) : 0f,
                        liftMax = frames != null && frames.Length > 0 ? frames.Max(f => f.lift) : 0f
                    };
                }).ToArray()
            };
        }
    }
}
