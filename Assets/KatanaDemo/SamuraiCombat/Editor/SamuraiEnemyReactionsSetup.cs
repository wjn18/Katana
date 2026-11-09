using System;
using System.Collections.Generic;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>
    /// 为 TrainingOpponent 增量接入普通/格挡受击；独立玩家入口复用已有十二个普通反应。
    /// 来源映射：Front 轻/中=Hit3、重=Hit4；Right=Hit2；Back=Hit5；Left=Hit1。
    /// 轻/中/重幅度=0.60/0.82/1.00，时长=0.48/0.65/0.8166667秒；120Hz派生。
    /// 原始 ARPG 包只读；普通 Install 保留现存派生片段、非空配置和所有强度阈值。
    /// </summary>
    public static class SamuraiEnemyReactionsSetup
    {
        const string Root = "Assets/KatanaDemo/SamuraiCombat";
        const string SourceRoot = "Assets/ARPGPack/ARPGSamurai/Animations/Humanoid";
        public const string AnimationFolder = Root + "/Animations/EnemyReactions";
        public const string ProfilePath = Root + "/Config/TrainingOpponent.asset";
        const string ControllerPath = Root + "/Animations/SamuraiCombat.controller";
        const float BakeRate = 120f;
        static readonly float[] Amplitudes = { .60f, .82f, 1f };
        static readonly float[] Durations = { .48f, .65f, .8166667f };
        static readonly string[] Directions = { "Front", "Right", "Back", "Left" };
        static readonly string[] GuardStates =
            { "GuardHit", "GuardHit2", "GuardHit3", "GuardBHit1", "GuardBHit2", "GuardBHit3", "GuardB" };

        sealed class PreparedAsset
        {
            public string Path;
            public AnimationClip Clip;
            public bool IsNew;
        }

        [MenuItem("Katana/Samurai/Enemies/Install Enemy Hit And Guard Reactions")]
        public static void Install()
        {
            RequireEditMode();
            var profile = Required<SamuraiCombatProfile>(ProfilePath);
            var controller = Required<AnimatorController>(ControllerPath);
            if (controller.layers.Length == 0 || controller.layers[0].stateMachine == null)
                throw new InvalidOperationException("SamuraiCombat 控制器缺少基础状态机。");
            if (!AssetDatabase.IsValidFolder(Root + "/Animations"))
                throw new InvalidOperationException("缺少 SamuraiCombat 动画目录。");
            if (!AssetDatabase.IsValidFolder(AnimationFolder)
                && AssetDatabase.LoadMainAssetAtPath(AnimationFolder) != null)
                throw new InvalidOperationException("敌人受击目录被其他资产占用：" + AnimationFolder);

            // 所有源和现有格挡资源先验证，任何缺失都发生在写入之前。
            var sources = LoadSources();
            var guards = GuardStates.Select(name => Required<AnimationClip>(
                Root + "/Animations/Samurai_" + name + ".anim")).ToArray();
            if (guards.Any(clip => !clip.humanMotion))
                throw new InvalidOperationException("现有格挡动作必须是 Humanoid 片段。");
            var prepared = new List<PreparedAsset>();
            try
            {
                var defaults = new AnimationClip[4][];
                for (int direction = 0; direction < 4; direction++)
                {
                    defaults[direction] = new AnimationClip[3];
                    for (int tier = 0; tier < 3; tier++)
                    {
                        string path = ClipPath((SamuraiHitDirection)direction, tier);
                        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                        if (existing == null && AssetDatabase.LoadMainAssetAtPath(path) != null)
                            throw new InvalidOperationException("敌人受击目标路径被其他资产占用：" + path);
                        if (existing != null && !existing.humanMotion)
                            throw new InvalidOperationException("现有敌人受击片段不是 Humanoid：" + path);
                        var clip = existing != null ? existing : CreatePreparedClip(sources[SourceNumber(
                            (SamuraiHitDirection)direction, tier) - 1], (SamuraiHitDirection)direction,
                            tier, Amplitudes[tier], Durations[tier]);
                        prepared.Add(new PreparedAsset { Path = path, Clip = clip, IsNew = existing == null });
                        defaults[direction][tier] = clip;
                    }
                }

                bool profileChanged = false;
                var front = FillMissing(profile.hitFrontReactions, defaults[0], ref profileChanged);
                var right = FillMissing(profile.hitRightReactions, defaults[1], ref profileChanged);
                var back = FillMissing(profile.hitBackReactions, defaults[2], ref profileChanged);
                var left = FillMissing(profile.hitLeftReactions, defaults[3], ref profileChanged);
                var guardA = FillMissing(profile.guardHitReactions, guards.Take(3).ToArray(), ref profileChanged);
                var guardB = FillMissing(profile.guardBHitReactions, guards.Skip(3).Take(3).ToArray(), ref profileChanged);
                var guardIdleB = profile.guardB != null ? profile.guardB : guards[6];
                var hitArrays = new[] { front, right, back, left };
                var machine = controller.layers[0].stateMachine;

                // 共享状态不会被另一套 motion 静默覆盖；全部冲突先检查再创建资产。
                for (int direction = 0; direction < 4; direction++)
                    for (int tier = 0; tier < 3; tier++)
                        ValidateState(machine, StateName((SamuraiHitDirection)direction, tier),
                            hitArrays[direction][tier], false);
                var effectiveGuards = guardA.Take(3).Concat(guardB.Take(3)).Concat(new[] { guardIdleB }).ToArray();
                for (int i = 0; i < GuardStates.Length; i++)
                    ValidateState(machine, GuardStates[i], effectiveGuards[i], true);

                if (!AssetDatabase.IsValidFolder(AnimationFolder))
                    AssetDatabase.CreateFolder(Root + "/Animations", "EnemyReactions");
                foreach (var item in prepared)
                    if (item.IsNew)
                    {
                        AssetDatabase.CreateAsset(item.Clip, item.Path);
                        AssetDatabase.SaveAssetIfDirty(item.Clip);
                    }

                bool controllerChanged = false;
                for (int direction = 0; direction < 4; direction++)
                    for (int tier = 0; tier < 3; tier++)
                        controllerChanged |= EnsureState(machine, StateName((SamuraiHitDirection)direction, tier),
                            hitArrays[direction][tier], direction * 3 + tier);
                if (controllerChanged)
                {
                    EditorUtility.SetDirty(machine);
                    EditorUtility.SetDirty(controller);
                    AssetDatabase.SaveAssetIfDirty(controller);
                }

                if (profile.guardB == null) profileChanged = true;
                if (!profile.directionalHitReactionsEnabled) profileChanged = true;
                if (profileChanged)
                {
                    profile.hitFrontReactions = front;
                    profile.hitRightReactions = right;
                    profile.hitBackReactions = back;
                    profile.hitLeftReactions = left;
                    profile.guardHitReactions = guardA;
                    profile.guardBHitReactions = guardB;
                    profile.guardB = guardIdleB;
                    profile.directionalHitReactionsEnabled = true;
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssetIfDirty(profile);
                }
            }
            finally
            {
                foreach (var item in prepared)
                    if (item.IsNew && item.Clip != null && !EditorUtility.IsPersistent(item.Clip))
                        UnityEngine.Object.DestroyImmediate(item.Clip);
            }
        }

        /// <summary>仅补玩家普通受击引用；复用已安装片段和状态，不触碰格挡、敌人或动画资产。</summary>
        [MenuItem("Katana/Samurai/Player/Install Player Hit Reactions")]
        public static void InstallPlayerHitReactions()
        {
            RequireEditMode();
            var profile = Required<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            var controller = Required<AnimatorController>(ControllerPath);
            if (controller.layers.Length == 0 || controller.layers[0].stateMachine == null)
                throw new InvalidOperationException("SamuraiCombat 控制器缺少基础状态机。");
            var defaults = new AnimationClip[4][];
            for (int direction = 0; direction < 4; direction++)
            {
                defaults[direction] = new AnimationClip[3];
                for (int tier = 0; tier < 3; tier++)
                {
                    var clip = Required<AnimationClip>(ClipPath((SamuraiHitDirection)direction, tier));
                    if (!clip.humanMotion)
                        throw new InvalidOperationException("共享受击片段不是 Humanoid：" + clip.name);
                    defaults[direction][tier] = clip;
                }
            }
            bool changed = false;
            var front = FillMissing(profile.hitFrontReactions, defaults[0], ref changed);
            var right = FillMissing(profile.hitRightReactions, defaults[1], ref changed);
            var back = FillMissing(profile.hitBackReactions, defaults[2], ref changed);
            var left = FillMissing(profile.hitLeftReactions, defaults[3], ref changed);
            var arrays = new[] { front, right, back, left };
            for (int direction = 0; direction < 4; direction++)
                for (int tier = 0; tier < 3; tier++)
                    ValidateState(controller.layers[0].stateMachine,
                        StateName((SamuraiHitDirection)direction, tier), arrays[direction][tier], true);
            if (!profile.directionalHitReactionsEnabled) changed = true;
            if (!changed) return;
            profile.hitFrontReactions = front;
            profile.hitRightReactions = right;
            profile.hitBackReactions = back;
            profile.hitLeftReactions = left;
            profile.directionalHitReactionsEnabled = true;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        /// <summary>只在内存创建派生片段，调用方负责销毁；不保存或修改任何源资产。</summary>
        public static AnimationClip CreatePreparedClip(SamuraiHitDirection direction, int tier)
        {
            ValidateSelection(direction, tier);
            return CreatePreparedClip(direction, tier, Amplitudes[tier], Durations[tier]);
        }

        /// <summary>显式幅度/时长入口，便于真实 Humanoid 预览后调节派生结果。</summary>
        public static AnimationClip CreatePreparedClip(SamuraiHitDirection direction, int tier,
            float amplitude, float duration)
        {
            ValidateSelection(direction, tier);
            var source = Required<AnimationClip>(SourcePath(direction, tier));
            ValidateSource(source);
            return CreatePreparedClip(source, direction, tier, amplitude, duration);
        }

        /// <summary>显式重烘十二个派生片段并保留 GUID；不修改配置、控制器或格挡片段。</summary>
        public static void RebuildPreparedReactions()
        {
            RequireEditMode();
            var sources = LoadSources();
            var targets = new List<AnimationClip>();
            var prepared = new List<AnimationClip>();
            try
            {
                for (int direction = 0; direction < 4; direction++)
                    for (int tier = 0; tier < 3; tier++)
                    {
                        targets.Add(Required<AnimationClip>(ClipPath((SamuraiHitDirection)direction, tier)));
                        prepared.Add(CreatePreparedClip(sources[SourceNumber((SamuraiHitDirection)direction, tier) - 1],
                            (SamuraiHitDirection)direction, tier, Amplitudes[tier], Durations[tier]));
                    }
                for (int i = 0; i < targets.Count; i++)
                {
                    EditorUtility.CopySerialized(prepared[i], targets[i]);
                    EditorUtility.SetDirty(targets[i]);
                    AssetDatabase.SaveAssetIfDirty(targets[i]);
                }
            }
            finally
            {
                foreach (var clip in prepared)
                    if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        public static string SourcePath(SamuraiHitDirection direction, int tier) =>
            SourceRoot + "/ARPG_Samurai_Hit" + SourceNumber(direction, tier) + ".anim";

        public static string ClipPath(SamuraiHitDirection direction, int tier) =>
            AnimationFolder + "/Samurai_Enemy" + StateName(direction, tier) + ".anim";

        static string StateName(SamuraiHitDirection direction, int tier)
        {
            ValidateSelection(direction, tier);
            return "Hit" + Directions[(int)direction] + (tier + 1);
        }

        static int SourceNumber(SamuraiHitDirection direction, int tier)
        {
            ValidateSelection(direction, tier);
            switch (direction)
            {
                case SamuraiHitDirection.Right: return 2;
                case SamuraiHitDirection.Back: return 5;
                case SamuraiHitDirection.Left: return 1;
                default: return tier == 2 ? 4 : 3;
            }
        }

        static AnimationClip[] LoadSources()
        {
            var sources = Enumerable.Range(1, 5).Select(i => Required<AnimationClip>(
                SourceRoot + "/ARPG_Samurai_Hit" + i + ".anim")).ToArray();
            foreach (var source in sources) ValidateSource(source);
            return sources;
        }

        static void ValidateSource(AnimationClip source)
        {
            if (!source.humanMotion || source.length <= 0f || !Finite(source.length))
                throw new InvalidOperationException("受击源必须是有效的 Humanoid 片段：" + source.name);
            var bindings = AnimationUtility.GetCurveBindings(source);
            foreach (string axis in new[] { "x", "y", "z", "w" })
                if (!bindings.Any(binding => binding.type == typeof(Animator) && binding.propertyName == "RootQ." + axis))
                    throw new InvalidOperationException("受击源缺少完整 RootQ 四元数：" + source.name);
        }

        static AnimationClip CreatePreparedClip(AnimationClip source, SamuraiHitDirection direction, int tier,
            float amplitude, float duration)
        {
            if (!Finite(amplitude) || amplitude < 0f || amplitude > 1f || !Finite(duration) || duration <= 0f)
                throw new ArgumentOutOfRangeException("受击派生要求 0–1 的幅度和正的有限时长。");
            var bindings = AnimationUtility.GetCurveBindings(source);
            var sourceCurves = bindings.Select(binding => AnimationUtility.GetEditorCurve(source, binding)).ToArray();
            var muscles = new HashSet<string>(HumanTrait.MuscleName, StringComparer.Ordinal);
            int[] rotationIndices = { -1, -1, -1, -1 };
            for (int i = 0; i < bindings.Length; i++)
                if (bindings[i].type == typeof(Animator))
                    switch (bindings[i].propertyName)
                    {
                        case "RootQ.x": rotationIndices[0] = i; break;
                        case "RootQ.y": rotationIndices[1] = i; break;
                        case "RootQ.z": rotationIndices[2] = i; break;
                        case "RootQ.w": rotationIndices[3] = i; break;
                    }
            if (rotationIndices.Any(index => index < 0))
                throw new InvalidOperationException("受击源缺少 RootQ 分量：" + source.name);
            var times = new List<float>();
            int wholeSamples = Mathf.FloorToInt(duration * BakeRate);
            for (int i = 0; i <= wholeSamples; i++)
                if (i / BakeRate < duration - .0000001f) times.Add(i / BakeRate);
            times.Add(duration);
            var sourceTimes = times.Select(time =>
            {
                float u = Mathf.Clamp01(time / duration);
                // 导数 1 + 0.25*pi*cos(pi*u) 始终大于0，提前起势并保留结尾。
                return source.length * Mathf.Clamp01(u + .25f * Mathf.Sin(Mathf.PI * u));
            }).ToArray();
            sourceTimes[0] = 0f;
            sourceTimes[sourceTimes.Length - 1] = source.length;

            var rotations = new Quaternion[times.Count];
            Quaternion initial = RootRotation(sourceCurves, rotationIndices, 0f, source.name);
            for (int i = 0; i < rotations.Length; i++)
            {
                Quaternion current = RootRotation(sourceCurves, rotationIndices, sourceTimes[i], source.name);
                Quaternion rotation = Normalize(Quaternion.Slerp(initial, current, amplitude), source.name);
                if (i > 0 && Quaternion.Dot(rotations[i - 1], rotation) < 0f)
                    rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                rotations[i] = rotation;
            }

            var clip = UnityEngine.Object.Instantiate(source);
            try
            {
                clip.name = "Samurai_Enemy" + StateName(direction, tier);
                clip.frameRate = BakeRate;
                for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                {
                    var binding = bindings[bindingIndex];
                    var curve = sourceCurves[bindingIndex];
                    bool animator = binding.type == typeof(Animator);
                    bool planar = animator && (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z");
                    bool scale = animator && (binding.propertyName == "RootT.y" || muscles.Contains(binding.propertyName));
                    int rotationComponent = Array.IndexOf(rotationIndices, bindingIndex);
                    float initialValue = curve.Evaluate(0f);
                    var values = new float[times.Count];
                    for (int sample = 0; sample < values.Length; sample++)
                    {
                        float value = curve.Evaluate(sourceTimes[sample]);
                        if (planar) value = 0f;
                        else if (rotationComponent >= 0) value = rotations[sample][rotationComponent];
                        else if (scale) value = initialValue + (value - initialValue) * amplitude;
                        if (!Finite(value))
                            throw new InvalidOperationException("受击源曲线含非有限采样：" + source.name + "/" + binding.propertyName);
                        values[sample] = value;
                    }
                    AnimationUtility.SetEditorCurve(clip, binding, LinearSampleCurve(times, values));
                }
                // 当前源无事件；派生反应不复制第三方未来可能加入的调用事件。
                AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.startTime = 0f;
                settings.stopTime = duration;
                settings.loopTime = false;
                settings.loopBlend = false;
                settings.loopBlendPositionXZ = true;
                settings.keepOriginalPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                return clip;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(clip);
                throw;
            }
        }

        static AnimationCurve LinearSampleCurve(IReadOnlyList<float> times, IReadOnlyList<float> values)
        {
            var keys = new Keyframe[times.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                float before = i > 0 ? (values[i] - values[i - 1]) / (times[i] - times[i - 1]) : 0f;
                float after = i + 1 < keys.Length ? (values[i + 1] - values[i]) / (times[i + 1] - times[i]) : 0f;
                keys[i] = new Keyframe(times[i], values[i], i == 0 ? after : before,
                    i == keys.Length - 1 ? before : after);
            }
            return new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        }

        static Quaternion RootRotation(AnimationCurve[] curves, int[] indices, float time, string sourceName) =>
            Normalize(new Quaternion(curves[indices[0]].Evaluate(time), curves[indices[1]].Evaluate(time),
                curves[indices[2]].Evaluate(time), curves[indices[3]].Evaluate(time)), sourceName);

        static Quaternion Normalize(Quaternion rotation, string sourceName)
        {
            float magnitude = Mathf.Sqrt(Quaternion.Dot(rotation, rotation));
            if (!Finite(magnitude) || magnitude < .000001f)
                throw new InvalidOperationException("受击源包含无效 RootQ 四元数：" + sourceName);
            return new Quaternion(rotation.x / magnitude, rotation.y / magnitude, rotation.z / magnitude, rotation.w / magnitude);
        }

        static AnimationClip[] FillMissing(AnimationClip[] current, AnimationClip[] defaults, ref bool changed)
        {
            if (current != null && current.Length >= defaults.Length
                && Enumerable.Range(0, defaults.Length).All(i => current[i] != null)) return current;
            var expanded = new AnimationClip[Mathf.Max(current != null ? current.Length : 0, defaults.Length)];
            if (current != null) Array.Copy(current, expanded, current.Length);
            for (int i = 0; i < defaults.Length; i++)
                if (expanded[i] == null) expanded[i] = defaults[i];
            changed = true;
            return expanded;
        }

        static AnimatorState FindState(AnimatorStateMachine machine, string name)
        {
            var matches = machine.states.Where(item => item.state.name == name).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("共享控制器有重复状态：" + name);
            return matches.Length == 0 ? null : matches[0].state;
        }

        static void ValidateState(AnimatorStateMachine machine, string name, AnimationClip clip, bool required)
        {
            var state = FindState(machine, name);
            if (state == null)
            {
                if (required) throw new InvalidOperationException("缺少已接入的格挡状态：" + name);
                return;
            }
            if ((state.motion != null && state.motion != clip) || (required && state.motion == null))
                throw new InvalidOperationException("共享控制器状态 " + name + " 与敌人配置的片段冲突。现有="
                    + (state.motion != null ? state.motion.name : "空") + "，请求=" + clip.name + "；未覆盖现有状态。");
        }

        static bool EnsureState(AnimatorStateMachine machine, string name, AnimationClip clip, int index)
        {
            var state = FindState(machine, name);
            if (state != null && state.motion == clip) return false;
            if (state == null)
            {
                state = machine.AddState(name, new Vector3(240f * (index % 4), 1350f + 90f * (index / 4)));
                state.speed = 1f;
                state.iKOnFeet = false;
                state.writeDefaultValues = true;
            }
            state.motion = clip;
            EditorUtility.SetDirty(state);
            return true;
        }

        static T Required<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("缺少敌人受击接入资源：" + path);
            return asset;
        }

        static void ValidateSelection(SamuraiHitDirection direction, int tier)
        {
            if ((int)direction < 0 || (int)direction >= Directions.Length || tier < 0 || tier >= 3)
                throw new ArgumentOutOfRangeException("普通受击方向或强度索引无效。");
        }

        static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 Play 后接入敌人受击动作。");
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
