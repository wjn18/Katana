using System;
using System.Collections.Generic;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>增量接入两套轻中重格挡受击；原包、已有派生片段和用户配置保持。</summary>
    public static class SamuraiGuardReactionsSetup
    {
        const string Root = "Assets/KatanaDemo/SamuraiCombat";
        const string SourceRoot = "Assets/ARPGPack/ARPGSamurai/Animations/Humanoid";
        const float ReactionLeadSeconds = .12f;
        const float BakeRate = 120f;
        const float BakeErrorTolerance = .0001f;

        struct CurveSample
        {
            public float Time;
            public int SourceKey;
            public CurveSample(float time, int sourceKey = -1) { Time = time; SourceKey = sourceKey; }
        }

        [MenuItem("OC Design/Samurai/Install Guard Reactions")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 Play 后安装格挡受击动作。");
            var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            if (profile == null || controller == null || controller.layers.Length == 0
                || !AssetDatabase.IsValidFolder(Root + "/Animations"))
                throw new InvalidOperationException("缺少现有玩家配置、战斗控制器或动画目录。");
            var legacy = profile.guardHit != null ? profile.guardHit
                : AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Animations/Samurai_GuardHit.anim");
            if (legacy == null)
                throw new InvalidOperationException("缺少现有 Samurai_GuardHit 格挡片段。");

            // 先核对全部源，避免缺包时只接入半套；这里不会修改第三方动画。
            var sourceNames = new[] { "Guard_Hit2", "Guard_Hit3", "Guard_B_Hit1", "Guard_B_Hit2", "Guard_B_Hit3", "Guard_B" };
            foreach (string name in sourceNames)
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(SourcePath(name)) == null)
                    throw new InvalidOperationException("缺少武士格挡源动作：" + name);

            var defaultsA = new[]
            {
                legacy,
                EnsureClip("GuardHit2", "Guard_Hit2", false),
                EnsureClip("GuardHit3", "Guard_Hit3", false)
            };
            var defaultsB = new[]
            {
                EnsureClip("GuardBHit1", "Guard_B_Hit1", false),
                EnsureClip("GuardBHit2", "Guard_B_Hit2", false),
                EnsureClip("GuardBHit3", "Guard_B_Hit3", false)
            };
            var guardB = EnsureClip("GuardB", "Guard_B", true);
            bool profileChanged = false;
            profile.guardHitReactions = FillMissing(profile.guardHitReactions, defaultsA, ref profileChanged);
            profile.guardBHitReactions = FillMissing(profile.guardBHitReactions, defaultsB, ref profileChanged);
            if (profile.guardB == null) { profile.guardB = guardB; profileChanged = true; }
            // 强度阈值由 Profile 的字段初值初始化；重复安装不重设用户阈值或数组内容。
            if (profileChanged)
            {
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }

            var machine = controller.layers[0].stateMachine;
            bool controllerChanged = false;
            controllerChanged |= EnsureState(machine, "GuardHit", profile.guardHitReactions[0], 0);
            controllerChanged |= EnsureState(machine, "GuardHit2", profile.guardHitReactions[1], 1);
            controllerChanged |= EnsureState(machine, "GuardHit3", profile.guardHitReactions[2], 2);
            controllerChanged |= EnsureState(machine, "GuardBHit1", profile.guardBHitReactions[0], 3);
            controllerChanged |= EnsureState(machine, "GuardBHit2", profile.guardBHitReactions[1], 4);
            controllerChanged |= EnsureState(machine, "GuardBHit3", profile.guardBHitReactions[2], 5);
            controllerChanged |= EnsureState(machine, "GuardB", profile.guardB, 6);
            if (controllerChanged)
            {
                EditorUtility.SetDirty(machine);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
            }
        }

        static string SourcePath(string name) => SourceRoot + "/ARPG_Samurai_" + name + ".anim";

        static AnimationClip[] FillMissing(AnimationClip[] current, AnimationClip[] defaults, ref bool changed)
        {
            if (current == null || current.Length < defaults.Length)
            {
                var expanded = new AnimationClip[defaults.Length];
                if (current != null) Array.Copy(current, expanded, current.Length);
                current = expanded;
                changed = true;
            }
            for (int i = 0; i < defaults.Length; i++)
                if (current[i] == null) { current[i] = defaults[i]; changed = true; }
            return current;
        }

        static bool EnsureState(AnimatorStateMachine machine, string name, AnimationClip clip, int index)
        {
            // 已有状态保留 motion、播放速度和其他人工调参。
            if (machine.states.Any(item => item.state.name == name)) return false;
            var state = machine.AddState(name, new Vector3(240f * (index % 4), 1080f + 90f * (index / 4)));
            state.motion = clip;
            state.speed = 1f;
            state.iKOnFeet = false;
            state.writeDefaultValues = true;
            EditorUtility.SetDirty(state);
            return true;
        }

        static AnimationClip EnsureClip(string label, string sourceName, bool loop)
        {
            string path = Root + "/Animations/Samurai_" + label + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("格挡动画目标路径已被其他资产占用：" + path);
            var clip = CreatePreparedClip(label, sourceName, loop);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        /// <summary>显式重烘焙本工具新增的五个反应，保留资产 GUID；不修改旧 GuardHit、Parry 或配置。</summary>
        public static void RebuildPreparedReactions()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 Play 后重烘焙格挡受击动作。");
            string[] labels = { "GuardHit2", "GuardHit3", "GuardBHit1", "GuardBHit2", "GuardBHit3" };
            string[] sources = { "Guard_Hit2", "Guard_Hit3", "Guard_B_Hit1", "Guard_B_Hit2", "Guard_B_Hit3" };
            var targets = new AnimationClip[labels.Length];
            var prepared = new AnimationClip[labels.Length];
            try
            {
                // 全部在内存中准备成功后才更新现有资产；正常 Install 仍保留现存片段。
                for (int i = 0; i < labels.Length; i++)
                {
                    targets[i] = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Animations/Samurai_" + labels[i] + ".anim");
                    if (targets[i] == null) throw new InvalidOperationException("请先安装格挡反应：" + labels[i]);
                    prepared[i] = CreatePreparedClip(labels[i], sources[i], false);
                }
                for (int i = 0; i < labels.Length; i++)
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

        static AnimationClip CreatePreparedClip(string label, string sourceName, bool loop)
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(SourcePath(sourceName));
            if (source == null) throw new InvalidOperationException("缺少武士格挡源动作：" + sourceName);
            float duration = source.length;
            if (duration <= 0f) throw new InvalidOperationException("格挡源动作时长无效：" + sourceName);
            var clip = UnityEngine.Object.Instantiate(source);
            clip.name = "Samurai_" + label;
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                bool planarRoot = binding.type == typeof(Animator)
                    && (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z");
                if (planarRoot) curve = AnimationCurve.Linear(0f, 0f, duration, 0f);
                else if (!loop) curve = BakeReactionCurve(curve, duration);
                // B 待机的其余 authored 曲线直接复制，不做重采样或重定时。
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = duration;
            settings.loopTime = loop;
            settings.loopBlend = loop;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        static AnimationCurve BakeReactionCurve(AnimationCurve source, float duration)
        {
            // 原始拐点必须按单调时间映射反解后保留，不能用均匀采样跨过拐点。
            var sourceKeys = source.keys;
            var samples = new List<CurveSample>();
            for (int i = 0; i < sourceKeys.Length; i++)
                if (sourceKeys[i].time >= 0f && sourceKeys[i].time <= duration)
                    samples.Add(new CurveSample(ReactionOutputTime(sourceKeys[i].time), i));
            int uniformSamples = Mathf.FloorToInt(duration * BakeRate + .0001f);
            for (int i = 0; i <= uniformSamples; i++)
            {
                float time = i / BakeRate;
                if (time < duration - .00001f) samples.Add(new CurveSample(time));
            }
            if (ReactionLeadSeconds < duration) samples.Add(new CurveSample(ReactionLeadSeconds));
            samples.Add(new CurveSample(duration));
            samples.Sort((a, b) => a.Time.CompareTo(b.Time));
            var unique = new List<CurveSample>();
            foreach (var sample in samples)
            {
                if (unique.Count > 0 && Mathf.Abs(unique[unique.Count - 1].Time - sample.Time) < .0000005f)
                {
                    if (sample.SourceKey >= 0) unique[unique.Count - 1] = sample;
                    continue;
                }
                unique.Add(sample);
            }
            var keys = new List<Keyframe> { ReactionKey(source, sourceKeys, unique[0]) };
            for (int i = 1; i < unique.Count; i++)
                AppendRefined(source, sourceKeys, keys[keys.Count - 1], ReactionKey(source, sourceKeys, unique[i]), keys, 0);
            return new AnimationCurve(keys.ToArray()) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
        }

        static Keyframe ReactionKey(AnimationCurve source, Keyframe[] sourceKeys, CurveSample sample)
        {
            float rate = ReactionSourceRate(sample.Time);
            if (sample.SourceKey >= 0)
            {
                Keyframe original = sourceKeys[sample.SourceKey];
                // 左右切线分别保留；中心差分会抹平原始的非平滑关节转折。
                return new Keyframe(sample.Time, original.value, original.inTangent * rate, original.outTangent * rate);
            }
            float mapped = ReactionSourceTime(sample.Time);
            float slope = SourceSlope(source, sourceKeys, mapped) * rate;
            return new Keyframe(sample.Time, source.Evaluate(mapped), slope, slope);
        }

        static float SourceSlope(AnimationCurve source, Keyframe[] keys, float time)
        {
            if (keys.Length < 2) return 0f;
            int segment = 0;
            while (segment + 1 < keys.Length - 1 && keys[segment + 1].time < time) segment++;
            Keyframe left = keys[segment], right = keys[segment + 1];
            float span = right.time - left.time;
            if (span <= 0f || float.IsInfinity(left.outTangent) || float.IsInfinity(right.inTangent)) return 0f;
            if ((left.weightedMode & WeightedMode.Out) != 0 || (right.weightedMode & WeightedMode.In) != 0)
            {
                // 当前源为非加权曲线；对将来的加权源用段内差分并由自适应误差检查补点。
                float delta = Mathf.Min(.00001f, span * .001f);
                float before = Mathf.Max(left.time, time - delta), after = Mathf.Min(right.time, time + delta);
                return after > before ? (source.Evaluate(after) - source.Evaluate(before)) / (after - before) : 0f;
            }
            float u = Mathf.Clamp01((time - left.time) / span), u2 = u * u;
            return ((6f * u2 - 6f * u) * left.value + (3f * u2 - 4f * u + 1f) * span * left.outTangent
                + (-6f * u2 + 6f * u) * right.value + (3f * u2 - 2f * u) * span * right.inTangent) / span;
        }

        static void AppendRefined(AnimationCurve source, Keyframe[] sourceKeys, Keyframe left, Keyframe right,
            List<Keyframe> output, int depth)
        {
            float error = 0f;
            // 同时查看四分点、八分点及中点，避免只查中点漏掉两侧误差峰值。
            for (int i = 1; i < 8; i++)
            {
                float time = Mathf.Lerp(left.time, right.time, i / 8f);
                error = Mathf.Max(error, Mathf.Abs(HermiteValue(left, right, time) - source.Evaluate(ReactionSourceTime(time))));
            }
            if (error > BakeErrorTolerance)
            {
                if (depth >= 12 || right.time - left.time < .000001f)
                    throw new InvalidOperationException("格挡反应曲线无法满足重定时误差上限，误差=" + error);
                var middle = ReactionKey(source, sourceKeys, new CurveSample((left.time + right.time) * .5f));
                AppendRefined(source, sourceKeys, left, middle, output, depth + 1);
                AppendRefined(source, sourceKeys, middle, right, output, depth + 1);
            }
            else output.Add(right);
        }

        static float HermiteValue(Keyframe left, Keyframe right, float time)
        {
            if (float.IsInfinity(left.outTangent) || float.IsInfinity(right.inTangent)) return left.value;
            float span = right.time - left.time, u = (time - left.time) / span, u2 = u * u, u3 = u2 * u;
            return (2f * u3 - 3f * u2 + 1f) * left.value + (u3 - 2f * u2 + u) * span * left.outTangent
                + (-2f * u3 + 3f * u2) * right.value + (u3 - u2) * span * right.inTangent;
        }

        static float ReactionOutputTime(float sourceTime)
        {
            if (sourceTime >= ReactionLeadSeconds || sourceTime <= 0f) return sourceTime;
            double low = 0d, high = ReactionLeadSeconds;
            for (int i = 0; i < 40; i++)
            {
                double middle = (low + high) * .5d, remaining = 1d - middle / ReactionLeadSeconds;
                double mapped = middle + 1.8d * middle * remaining * remaining;
                if (mapped < sourceTime) low = middle; else high = middle;
            }
            return (float)((low + high) * .5d);
        }

        static float ReactionSourceRate(float time)
        {
            if (time >= ReactionLeadSeconds) return 1f;
            float u = time / ReactionLeadSeconds;
            return 1f + 1.8f * (1f - u) * (1f - 3f * u);
        }

        static float ReactionSourceTime(float time)
        {
            if (time >= ReactionLeadSeconds) return time;
            float remaining = 1f - time / ReactionLeadSeconds;
            return time + 1.8f * time * remaining * remaining;
        }
    }
}
