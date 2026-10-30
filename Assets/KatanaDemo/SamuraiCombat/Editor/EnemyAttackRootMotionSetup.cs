using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>敌人独立水平根轨迹的定向接入；不改身体动画、命中窗或战斗参数。</summary>
    public static class EnemyAttackRootMotionSetup
    {
        const int Version = 1;
        // 目标/原双手锤角色worldHumanScale比；与支撑脚滑动独立校准一致，源JSON仍为原米制1倍。
        public const float EliteSourceDistanceMultiplier = 2.0808494f;
        public const string DataPath = "Assets/KatanaDemo/SamuraiCombat/Editor/EnemyRootMotion.json";
        static readonly string[] EliteStates = { "Heavy", "Sweep", "SweepReverse", "Strike", "Combo01", "Combo02", "Combo03", "Combo04" };
        static readonly string[] EnemyStates = { "Light01", "Light02", "Heavy" };
        public static string ReportFolder => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../镇门鬼将/动画资源/elite1-combat/analysis/enemy-root-motion-20261028"));

        [Serializable]
        public sealed class RootMotionLibrary
        {
            public int version;
            public RootMotionClip[] clips;
        }

        [Serializable]
        public sealed class RootMotionClip
        {
            public string actor, state, source, sourceSha256, notes;
            public float duration, sourceScale;
            public float[] t, x, z;
        }

        sealed class PendingAction
        {
            public SamuraiActionDefinition action;
            public RootMotionClip source;
            public AnimationCurve x, z;
            public float multiplier;
            public SamuraiWeaponRangeFrame[] weaponRangeFrames;
            public float weaponRangeRadius;
        }

        [MenuItem("Tools/OC Design/Samurai Combat/Install Enemy Attack Root Motion Pilot")]
        public static void InstallPilot() => InstallPilotWithMultiplier(EliteSourceDistanceMultiplier);

        public static void InstallPilotWithMultiplier(float eliteMultiplier) => Install(
            new[] { "Elite1/Heavy", "Elite1/Sweep" }, eliteMultiplier, 1f, "横扫重劈根运动安装.json");

        [MenuItem("Tools/OC Design/Samurai Combat/Install Enemy Attack Root Motion All")]
        public static void InstallAll() => InstallAllWithMultipliers(EliteSourceDistanceMultiplier, 1f);

        public static void InstallAllWithMultipliers(float eliteMultiplier, float enemyMultiplier) => Install(
            EliteStates.Select(s => "Elite1/" + s).Concat(EnemyStates.Select(s => "Enemy1/" + s)).ToArray(),
            eliteMultiplier, enemyMultiplier, "全部敌人根运动安装.json");

        /// <summary>完整角色维护入口调用；版本标记保护已初始化的开关、倍率和曲线。</summary>
        public static void InstallElite1() => Install(EliteStates.Select(s => "Elite1/" + s).ToArray(),
            EliteSourceDistanceMultiplier, 1f, "精英根运动维护.json");

        public static void InstallEnemy1() => Install(EnemyStates.Select(s => "Enemy1/" + s).ToArray(),
            1f, 1f, "普通怪根运动维护.json");

        static void Install(string[] keys, float eliteMultiplier, float enemyMultiplier, string reportName)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出Play再接入敌人根运动。");
            ValidateMultiplier(eliteMultiplier); ValidateMultiplier(enemyMultiplier);
            var library = LoadLibrary();
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var staged = new List<PendingAction>();
            var all = new List<PendingAction>();
            var actors = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                var parts = key.Split('/');
                string actor = parts[0], state = parts[1];
                var action = LoadAction(actor, state);
                var matches = library.clips.Where(c => c != null && c.actor == actor && c.state == state).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("根运动数据须唯一对应动作：" + key);
                var clip = matches[0];
                ValidateClip(clip, action, hashes);
                var pending = new PendingAction { action = action, source = clip, multiplier = actor == "Elite1" ? eliteMultiplier : enemyMultiplier };
                if (action.enemyRootMotionConfigurationVersion < Version)
                {
                    pending.x = LinearCurve(clip.t, clip.x, clip.duration); pending.z = LinearCurve(clip.t, clip.z, clip.duration);
                    staged.Add(pending);
                }
                all.Add(pending); actors.Add(actor);
            }
            // 所有源指纹、时间和Prefab结构先通过，再写任何动作资产。
            foreach (string actor in actors) ValidatePrefab(actor);
            SampleEnemyWeaponRanges(staged.Where(p => p.source.actor == "Enemy1").ToArray());
            foreach (var item in staged)
            {
                item.action.rootMotionX = item.x; item.action.rootMotionZ = item.z;
                item.action.rootMotionDistanceMultiplier = item.multiplier;
                item.action.useRootMotion = true;
                if (item.source.actor == "Enemy1")
                {
                    item.action.weaponRangeFrames = item.weaponRangeFrames;
                    item.action.weaponRangeRadius = item.weaponRangeRadius;
                }
                item.action.enemyRootMotionConfigurationVersion = Version;
                EditorUtility.SetDirty(item.action); AssetDatabase.SaveAssetIfDirty(item.action);
            }
            var prefabChanges = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string actor in actors) prefabChanges.Add(actor, EnsurePrefabDriver(actor));
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(Path.Combine(ReportFolder, reportName), Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                date = "2026-10-28", version = Version, library = DataPath,
                initialized = staged.Select(p => p.action.name).ToArray(),
                preserved = all.Except(staged).Select(p => p.action.name).ToArray(),
                prefabChanges,
                actions = all.Select(ActionReport).ToArray()
            }, Newtonsoft.Json.Formatting.Indented));
            Debug.Log("Enemy attack root motion: initialized " + staged.Count + ", preserved " + (all.Count - staged.Count)
                + ". Body animations, damage windows, combat values and scene retained.");
        }

        public static RootMotionLibrary LoadLibrary()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
            if (text == null) throw new InvalidOperationException("根轨迹数据缺失：" + DataPath);
            var library = JsonUtility.FromJson<RootMotionLibrary>(text.text);
            if (library == null || library.version != Version || library.clips == null)
                throw new InvalidOperationException("敌人根轨迹数据版本或结构无效。");
            return library;
        }

        static SamuraiActionDefinition LoadAction(string actor, string state)
        {
            string config = actor == "Elite1" ? Elite1CombatSetup.ConfigFolder : actor == "Enemy1" ? Enemy1CombatSetup.ConfigFolder : null;
            if (config == null) throw new InvalidOperationException("未知敌人：" + actor);
            var action = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(config + "/" + actor + "_" + state + ".asset");
            if (action == null || action.clip == null || action.stateName != state)
                throw new InvalidOperationException("正式敌人动作缺失或状态不符：" + actor + "/" + state);
            return action;
        }

        static void ValidateClip(RootMotionClip clip, SamuraiActionDefinition action, Dictionary<string, string> hashes)
        {
            if (!Finite(clip.duration) || clip.duration <= 0f || Mathf.Abs(clip.duration - action.clip.length) > .002f)
                throw new InvalidOperationException("根轨迹时长与正式身体片段不一致：" + action.name);
            if (!Finite(clip.sourceScale) || clip.sourceScale <= 0f || string.IsNullOrEmpty(clip.source) || string.IsNullOrEmpty(clip.sourceSha256))
                throw new InvalidOperationException("根轨迹来源或尺度缺失：" + action.name);
            if (clip.t == null || clip.x == null || clip.z == null || clip.t.Length < 2
                || clip.t.Length != clip.x.Length || clip.t.Length != clip.z.Length)
                throw new InvalidOperationException("根轨迹采样数组无效：" + action.name);
            if (Mathf.Abs(clip.t[0]) > .000001f || Mathf.Abs(clip.t[clip.t.Length - 1] - 1f) > .000001f
                || Mathf.Abs(clip.x[0]) > .0001f || Mathf.Abs(clip.z[0]) > .0001f)
                throw new InvalidOperationException("根轨迹必须完整覆盖0..1并减去初始水平位置：" + action.name);
            for (int i = 0; i < clip.t.Length; i++)
                if (!Finite(clip.t[i]) || !Finite(clip.x[i]) || !Finite(clip.z[i]) || clip.t[i] < 0f || clip.t[i] > 1f
                    || (i > 0 && clip.t[i] <= clip.t[i - 1]))
                    throw new InvalidOperationException("根轨迹包含非有限值或非递增时间：" + action.name);
            string path = Path.IsPathRooted(clip.source) ? Path.GetFullPath(clip.source)
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", clip.source));
            if (!File.Exists(path)) throw new FileNotFoundException("原根轨迹来源文件缺失。", path);
            if (!hashes.TryGetValue(path, out string hash))
            {
                using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                    hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                hashes.Add(path, hash);
            }
            if (!string.Equals(hash, clip.sourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("原根轨迹来源指纹已改变，请重新核对并生成数据：" + clip.source);
        }

        static AnimationCurve LinearCurve(float[] phases, float[] values, float duration)
        {
            // AnimationCurve极短时间段有内部最小间隔。以动画秒存储，并合并几乎重合的采样/源键。
            var indices = new List<int>();
            for (int i = 0; i < phases.Length; i++)
            {
                if (indices.Count > 0 && (phases[i] - phases[indices[indices.Count - 1]]) * duration < .00012f)
                {
                    if (i == phases.Length - 1) indices[indices.Count - 1] = i;
                    continue;
                }
                indices.Add(i);
            }
            var times = indices.Select(i => phases[i] * duration).ToArray();
            var samples = indices.Select(i => values[i]).ToArray();
            var keys = new Keyframe[times.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                float before = i > 0 ? (samples[i] - samples[i - 1]) / (times[i] - times[i - 1]) : 0f;
                float after = i + 1 < keys.Length ? (samples[i + 1] - samples[i]) / (times[i + 1] - times[i]) : 0f;
                keys[i] = new Keyframe(times[i], samples[i], before, after);
            }
            var curve = new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyBroken(curve, i, true);
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            return curve;
        }

        static void SampleEnemyWeaponRanges(PendingAction[] actions)
        {
            if (actions.Length == 0) return;
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(Enemy1CombatSetup.PrefabPath), scene);
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = root.GetComponentInChildren<Animator>(true);
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.enabled = false;
                animator.Rebind();
                var transforms = root.GetComponentsInChildren<Transform>(true);
                var bladeBase = transforms.Single(t => t.name == "BladeBase");
                var bladeTip = transforms.Single(t => t.name == "BladeTip");
                var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Enemy1CombatSetup.ProfilePath);
                if (profile == null) throw new InvalidOperationException("Enemy1正式配置缺失，无法采样AI首击范围。");
                foreach (var item in actions)
                {
                    var window = item.action.GetDamageWindow(0);
                    if (window.x < 0f || window.y > 1f || window.y <= window.x)
                        throw new InvalidOperationException("Enemy1首击命中窗无效：" + item.action.name);
                    int steps = Mathf.Max(1, Mathf.CeilToInt((window.y - window.x) * item.action.Duration * 120f));
                    var frames = new SamuraiWeaponRangeFrame[steps + 1];
                    for (int i = 0; i <= steps; i++)
                    {
                        float phase = Mathf.Lerp(window.x, window.y, (float)i / steps);
                        item.action.clip.SampleAnimation(animator.gameObject, item.action.Duration * phase);
                        frames[i] = new SamuraiWeaponRangeFrame
                        {
                            phase = phase,
                            from = root.transform.InverseTransformPoint(bladeBase.position),
                            to = root.transform.InverseTransformPoint(bladeTip.position)
                        };
                    }
                    item.weaponRangeFrames = frames;
                    item.weaponRangeRadius = item.action.hitRadius > 0f ? item.action.hitRadius : profile.bladeRadius;
                }
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void ValidateMultiplier(float multiplier)
        {
            if (!Finite(multiplier) || multiplier < 0f) throw new InvalidOperationException("根运动倍率须为有限非负数。");
        }

        public static bool ActorHasInstalledMotion(string actor)
        {
            var states = actor == "Elite1" ? EliteStates : actor == "Enemy1" ? EnemyStates : null;
            if (states == null) return false;
            return states.Any(state => LoadAction(actor, state).enemyRootMotionConfigurationVersion >= Version);
        }

        /// <summary>旧完整安装器保存Prefab前调用；只对已经接入过根运动的角色维护驱动。</summary>
        public static bool ConfigureInstalledDriver(GameObject root, string actor)
        {
            return ActorHasInstalledMotion(actor) && ConfigureLoadedDriver(root);
        }

        static string PrefabPath(string actor) => actor == "Elite1" ? Elite1CombatSetup.PrefabPath
            : actor == "Enemy1" ? Enemy1CombatSetup.PrefabPath : throw new InvalidOperationException("未知敌人：" + actor);

        static void ValidatePrefab(string actor)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(actor));
            if (root == null || root.GetComponent<SamuraiCombatant>() == null || root.GetComponentInChildren<Animator>(true) == null)
                throw new InvalidOperationException("正式完整Prefab缺失角色入口或Animator：" + actor);
        }

        public static bool EnsurePrefabDriver(string actor)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath(actor));
            try
            {
                bool changed = ConfigureLoadedDriver(root);
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(actor));
                return changed;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static bool ConfigureLoadedDriver(GameObject root)
        {
            var actor = root.GetComponent<SamuraiCombatant>();
            var animator = root.GetComponentInChildren<Animator>(true);
            if (actor == null || animator == null) throw new InvalidOperationException("根运动驱动需要完整角色及Animator。");
            var driver = animator.GetComponent<SamuraiRootMotionDriver>();
            bool changed = driver == null;
            if (driver == null) driver = animator.gameObject.AddComponent<SamuraiRootMotionDriver>();
            var serialized = new SerializedObject(driver);
            changed |= serialized.FindProperty("actor").objectReferenceValue != actor || !animator.applyRootMotion;
            if (changed)
            {
                driver.Configure(actor);
                EditorUtility.SetDirty(driver); EditorUtility.SetDirty(animator);
            }
            return changed;
        }

        static object ActionReport(PendingAction item)
        {
            var action = item.action;
            return new
            {
                action.name, path = AssetDatabase.GetAssetPath(action), item.source.actor, item.source.state,
                item.source.source, item.source.sourceSha256, item.source.sourceScale, item.source.duration,
                sourceSamples = item.source.t.Length, action.enemyRootMotionConfigurationVersion,
                action.useRootMotion, action.rootMotionDistanceMultiplier,
                xKeys = action.rootMotionX != null ? action.rootMotionX.length : 0,
                zKeys = action.rootMotionZ != null ? action.rootMotionZ.length : 0,
                curveTimeUnit = "animation seconds",
                weaponRangeSamples = action.weaponRangeFrames != null ? action.weaponRangeFrames.Length : 0,
                action.weaponRangeRadius,
                net = new[] { action.RootMotionPosition(1f).x, action.RootMotionPosition(1f).z },
                retainedFixedStep = new[] { action.stepDistance, action.stepStart, action.stepEnd },
                item.source.notes
            };
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
