using System;
using System.Collections.Generic;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>只为高架势安装原生四向慢/快步及相邻方向混合的四向斜步。</summary>
    public static class SamuraiHighLocomotionSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        const string Anim = Root + "/Animations/Nioh3";
        public static readonly string[] Directions = { "Forward", "ForwardRight", "Right", "BackwardRight", "Backward", "BackwardLeft", "Left", "ForwardLeft" };
        // Cardinal order is forward, right, backward, left. Diagonals are project-derived clips.
        public static readonly string[] WalkCodes = { "03030", "03033", "03031", "03032" };
        public static readonly string[] RunCodes = { "03020", "03028", "03024", "03026" };
        // Source cyclic phase alignment is recorded in this batch's source analysis.
        public static readonly float[] WalkPhases = { 0f, .9775f, .2600f, .1900f };
        public static readonly float[] RunPhases = { .8425f, .8600f, .1225f, .8550f };

        [MenuItem("Katana/Samurai/Player/Install Native High Walk And Run")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出Play模式。");
            var high = Required<SamuraiStanceDefinition>(Root + "/Config/Nioh3/Nioh3_Stance_High.asset");
            var controller = Required<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            var walk = BuildSet("High", "Walk", WalkCodes, WalkPhases, 71f / 60f);
            var run = BuildSet("High", "Run", RunCodes, RunPhases, 37f / 60f);
            var machine = controller.layers[0].stateMachine;
            bool controllerChanged = false;
            foreach (var clip in walk.Concat(run))
            {
                var state = machine.states.FirstOrDefault(s => s.state.name == clip.name).state;
                if (!state) { state = machine.AddState(clip.name, new Vector3(2100, 75 * machine.states.Length)); controllerChanged = true; }
                if (state.motion != clip || state.speed != 1f || state.iKOnFeet)
                { state.motion = clip; state.speed = 1f; state.iKOnFeet = false; EditorUtility.SetDirty(state); controllerChanged = true; }
            }
            if (controllerChanged) { EditorUtility.SetDirty(machine); Save(controller); }
            if (high.locomotion == null || !Matches(high.locomotion.walk, walk) || !Matches(high.locomotion.run, run))
            {
                high.locomotion = new SamuraiStanceLocomotion { walk = Entries(walk), run = Entries(run) };
                Save(high);
            }
        }

        static SamuraiLocomotionEntry[] Entries(AnimationClip[] clips)
            => clips.Select(c => new SamuraiLocomotionEntry { clip = c, stateName = c.name }).ToArray();
        static bool Matches(SamuraiLocomotionEntry[] entries, AnimationClip[] clips)
            => entries != null && entries.Length == clips.Length && entries.Select((e, i) => e != null && e.clip == clips[i] && e.stateName == clips[i].name).All(v => v);

        // Shared by the middle-stance installer; existing high assets keep their original names.
        internal static AnimationClip[] BuildSet(string stanceName, string gait, string[] codes, float[] phases, float duration)
        {
            var result = new AnimationClip[8];
            for (int i = 0; i < 4; i++)
            {
                string name = "Nioh3_" + stanceName + "_" + gait + Directions[i * 2] + "_" + codes[i];
                result[i * 2] = ImportNative(name, duration, phases[i]);
            }
            for (int i = 0; i < 4; i++)
            {
                int diagonal = i * 2 + 1;
                string name = "Nioh3_" + stanceName + "_" + gait + Directions[diagonal] + "_Blend";
                string path = Anim + "/Runtime/" + name + ".anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing) { Validate(existing, duration); result[diagonal] = existing; continue; }
                result[diagonal] = BakeLoop(name, result[i * 2], result[(i * 2 + 2) % 8], duration, 0f);
                AssetDatabase.CreateAsset(result[diagonal], path);
            }
            return result;
        }

        static AnimationClip ImportNative(string name, float duration, float phase)
        {
            string destination = Anim + "/Runtime/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
            if (existing) { Validate(existing, duration); return existing; }
            string sourcePath = Anim + "/SourceFBX/" + name + ".fbx";
            if (!System.IO.File.Exists(sourcePath)) throw new InvalidOperationException("缺少本轮源FBX：" + sourcePath);
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Anim + "/SourceFBX/Nioh3_Canonical_TPose.fbx").OfType<Avatar>().FirstOrDefault();
            if (!importer || !avatar || !avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("源模型或现有Canonical Avatar无效。");
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar = avatar;
            importer.humanDescription = avatar.humanDescription;
            var serialized = new SerializedObject(importer);
            serialized.FindProperty("m_AutoGenerateAvatarMappingIfUnspecified").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            importer.importAnimation = true; importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importCameras = importer.importLights = false; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var takes = importer.clipAnimations.Length == 0 ? importer.defaultClipAnimations : importer.clipAnimations;
            foreach (var take in takes)
            {
                take.lockRootRotation = take.lockRootHeightY = take.lockRootPositionXZ = true;
                take.keepOriginalOrientation = take.keepOriginalPositionY = take.keepOriginalPositionXZ = true;
                take.heightFromFeet = false; take.heightOffset = take.rotationOffset = 0f; take.loopTime = true;
            }
            importer.clipAnimations = takes; importer.SaveAndReimport();
            var source = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (!source || !source.humanMotion || Mathf.Abs(source.length - duration) > .002f) throw new InvalidOperationException("源动作导入失败：" + name);
            var result = BakeLoop(name, source, null, duration, phase);
            AssetDatabase.CreateAsset(result, destination);
            return result;
        }

        // Blend only after source gait phases match. Root orientation uses a normalized slerp.
        static AnimationClip BakeLoop(string name, AnimationClip first, AnimationClip second, float duration, float phase)
        {
            var result = UnityEngine.Object.Instantiate(first); result.name = name; result.frameRate = 60f;
            var bindings = AnimationUtility.GetCurveBindings(first);
            var a = bindings.ToDictionary(b => b.propertyName, b => AnimationUtility.GetEditorCurve(first, b));
            var bCurves = second ? AnimationUtility.GetCurveBindings(second).ToDictionary(b => b.propertyName, b => AnimationUtility.GetEditorCurve(second, b)) : null;
            if (bCurves != null && (a.Count != bCurves.Count || a.Keys.Any(k => !bCurves.ContainsKey(k))))
                throw new InvalidOperationException("方向动作曲线不匹配：" + name);
            int samples = Mathf.RoundToInt(duration * 120f);
            foreach (var binding in bindings)
            {
                var keys = new Keyframe[samples + 1];
                for (int i = 0; i <= samples; i++)
                {
                    float time = duration * i / samples;
                    float sourceTime = Mathf.Repeat(time + phase * duration, duration);
                    if (i == samples) sourceTime = Mathf.Repeat(phase * duration, duration);
                    float value = ClosedValue(a[binding.propertyName], sourceTime, duration);
                    if (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z") value = 0f;
                    else if (binding.propertyName.StartsWith("RootQ."))
                    {
                        Quaternion q = ClosedRotation(a, sourceTime, duration);
                        if (bCurves != null) q = Quaternion.Slerp(q, ClosedRotation(bCurves, sourceTime, duration), .5f);
                        int component = "xyzw".IndexOf(binding.propertyName[binding.propertyName.Length - 1]);
                        value = q[component];
                    }
                    else if (bCurves != null) value = (value + ClosedValue(bCurves[binding.propertyName], sourceTime, duration)) * .5f;
                    if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("非有限动画曲线：" + name);
                    keys[i] = new Keyframe(time, value);
                }
                var curve = new AnimationCurve(keys);
                for (int i = 0; i < keys.Length; i++)
                { AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear); }
                AnimationUtility.SetEditorCurve(result, binding, curve);
            }
            AnimationUtility.SetAnimationEvents(result, new AnimationEvent[0]);
            var settings = AnimationUtility.GetAnimationClipSettings(result);
            settings.loopTime = settings.loopBlend = true; settings.startTime = 0f; settings.stopTime = duration;
            AnimationUtility.SetAnimationClipSettings(result, settings);
            Validate(result, duration); return result;
        }

        // Remove the small original end/start mismatch before moving the cyclic seam.
        static float ClosedValue(AnimationCurve curve, float time, float duration)
            => curve.Evaluate(time) - (curve.Evaluate(duration) - curve.Evaluate(0f)) * (time / duration);
        static Quaternion ClosedRotation(Dictionary<string, AnimationCurve> curves, float time, float duration)
        {
            Quaternion correction = Quaternion.Inverse(RootRotation(curves, duration)) * RootRotation(curves, 0f);
            return (RootRotation(curves, time) * Quaternion.Slerp(Quaternion.identity, correction, time / duration)).normalized;
        }
        static Quaternion RootRotation(Dictionary<string, AnimationCurve> curves, float time)
        {
            var q = new Quaternion(curves["RootQ.x"].Evaluate(time), curves["RootQ.y"].Evaluate(time), curves["RootQ.z"].Evaluate(time), curves["RootQ.w"].Evaluate(time));
            return q.normalized;
        }
        static void Validate(AnimationClip clip, float duration)
        {
            if (!clip.humanMotion || !clip.isLooping || Mathf.Abs(clip.length - duration) > .002f)
                throw new InvalidOperationException("运行步态验证失败：" + clip.name);
        }
        static T Required<T>(string path) where T : UnityEngine.Object
        { var value = AssetDatabase.LoadAssetAtPath<T>(path); if (!value) throw new InvalidOperationException("缺少资源：" + path); return value; }
        static void Save(UnityEngine.Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
    }
}
