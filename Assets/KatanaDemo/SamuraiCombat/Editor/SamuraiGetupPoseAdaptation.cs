using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>Offline target recovery bake; uses the down baker's isolated rigs and skin/IK helpers.</summary>
    public static partial class SamuraiDownPoseAdaptation
    {
        static readonly string[] GetupSpecs = { "GetupHealthy", "GetupLowHealth" };
        static readonly string[] GetupCodes = { "00442", "30222" };
        const float GetupJoinSeconds = .30f;
        public static string GetupFailureReportPath => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
            "../../斗笠剑士/动画资源/execution-audit-20261030/analysis/getup-integration-20261030/getup-adaptation-failure.json"));

        [Serializable] public sealed class GetupClipReport
        {
            public ClipReport contact;
            public string downReference, idleReference;
            public float downReferenceTime, currentBreakEndDownPhase;
            public float firstPoseMaximumJointDifference, currentBreakEndMaximumJointDifference;
            public float firstPoseVsDownZeroMaximumJointDifference;
            public string firstPoseWorstJoint;
            public string[] initialJoinAdditionalChannels;
            public float firstHipsY, lastHipsY, idleHipsY, endToIdleMaximumJointDifference;
            public float sourceFirstHipsY, sourceLastHipsY, kneelCorrectionZeroPhase;
            public float firstPoseJoinSeconds = GetupJoinSeconds;
            public int leftFootConstraintReducedSamples;
            public float maximumReleasedFootCorrection;
            public float maximumFinalRootHeightCorrection;
            public float maximumFinalRootHeightCorrectionPhase;
            public int finalRootCalibrationPasses;
            public int finalRootCalibrationHz;
            // Zero means not applicable for Generic clips; reports remain strict JSON.
            public float minimumRootQuaternionNorm, maximumRootQuaternionNorm;
            public float maximumAdjacentRootQuaternionDegrees;
            public int rootQuaternionNegativeAdjacentDots;
            public bool leftLegQuaternionPrefix;
            public int leftLegTwistWrapAdjustments;
            public string[] leftLegTwistLimits;
            public float leftLegPrefixReplayMaximumJointError;
            public float firstHalfSecondMaximumJointStep, firstHalfSecondMaximumJointStepTime;
            public string firstHalfSecondMaximumJointStepBone;
            public float firstHalfSecondMaximumLeftLegStep;
            public float firstHalfSecondMaximumStepVectorChange, firstHalfSecondMaximumStepVectorChangeTime;
            public string firstHalfSecondMaximumStepVectorChangeBone;
            public float maximumLeftLegTwistKeyDegrees;
            public float previousSavedFirstHalfSecondMaximumJointStep, previousSavedFirstHalfSecondMaximumStepVectorChange;
            public bool previousSavedMotionMeasured;
            public bool nonLooping, originalDurationPreserved, protectedInputsUnchanged;
        }
        [Serializable] public sealed class GetupAdaptationReport
        {
            public bool saved, scenePreserved, protectedInputsUnchanged;
            public string failure;
            public string method = "Source FootIK baked offline; leg-skin root grounding; left-leg kneeling correction fades with source standing height; arms only raised if skin crosses floor. First pose matched to saved Down at current Profile break-window end phase, with 0.30s local pose join. Runtime FootIK/PlayableIK off during validation. Source/Down clips unchanged; later user edits to break duration rely on runtime crossfade until rebuilt.";
            public List<GetupClipReport> clips = new List<GetupClipReport>();
        }

        public static GetupAdaptationReport RebuildSelectedGetups(bool saveAssets)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play before baking getups.");
            var originalScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool originalDirty = originalScene.isDirty;
            int originalCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            foreach (string code in GetupCodes) GetSource(code);
            var protectedPaths = GetupCodes.Select(SourcePath).Concat(new[] { "Enemy1", "Elite1" }.SelectMany(actor => Specs.Select(spec => Destination(actor, spec)))).ToArray();
            var hashes = protectedPaths.ToDictionary(p => p, GetupHash);
            var scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            var staged = new Dictionary<string, AnimationClip>();
            var report = new GetupAdaptationReport();
            try
            {
                foreach (string actor in new[] { "Enemy1", "Elite1" })
                {
                    bool generic = actor == "Enemy1";
                    var rig = CreateGetupRig(actor, scene, temporary);
                    Transform[] bakeBones;
                    if (generic)
                    {
                        var avatar = BuildTemporaryEnemyAvatar(rig.animator, out bakeBones);
                        temporary.Add(avatar); rig.animator.avatar = avatar;
                    }
                    else bakeBones = Array.Empty<Transform>();
                    rig.animator.enabled = true; rig.animator.Rebind();
                    var skin = new SkinSampler(rig.gameObject, rig.animator);
                    var profile = AssetDatabase.LoadAssetAtPath<OCDesign.Samurai.SamuraiCombatProfile>("Assets/KatanaDemo/SamuraiCombat/Config/" + actor + "/" + actor + ".asset");
                    if (profile == null || profile.idle == null) throw new InvalidOperationException("Missing target profile/Idle: " + actor);
                    using (var handler = new HumanPoseHandler(rig.animator.avatar, rig.animator.transform))
                    {
                        for (int index = 0; index < 2; index++)
                        {
                            var down = AssetDatabase.LoadAssetAtPath<AnimationClip>(Destination(actor, Specs[index]));
                            if (down == null) throw new InvalidOperationException("Missing saved Down reference: " + actor);
                            var source = PrepareSource(GetSource(GetupCodes[index]));
                            temporary.Add(source);
                            var row = new GetupClipReport
                            {
                                contact = new ClipReport { actor = actor, spec = GetupSpecs[index], source = SourcePath(GetupCodes[index]), destination = Destination(actor, GetupSpecs[index]), duration = source.length, sourceFootIKBaked = true },
                                downReference = AssetDatabase.GetAssetPath(down), idleReference = AssetDatabase.GetAssetPath(profile.idle),
                                downReferenceTime = profile.toughnessBreakSeconds % down.length, currentBreakEndDownPhase = (profile.toughnessBreakSeconds % down.length) / down.length
                            };
                            report.clips.Add(row);
                            int steps = Mathf.Max(1, Mathf.RoundToInt(source.length * (generic ? 60 : 240)));
                            var kneel = GetupKneelWeights(rig.animator, source, steps, row);
                            AnimationClip output;
                            if (generic)
                            {
                                var referenceRig = CreateGetupRig(actor, scene, temporary);
                                output = BakeGenericGetup(rig.animator, source, skin, bakeBones, referenceRig.animator, down, kneel, row);
                            }
                            else output = BakeHumanGetup(rig.animator, source, skin, handler, down, kneel, row);
                            output.name = (generic ? "Enemy1_" : "Nioh3_") + GetupSpecs[index];
                            output.hideFlags = HideFlags.None;
                            temporary.Add(output); staged.Add(row.contact.destination, output);
                            var playback = CreateGetupRig(actor, scene, temporary);
                            ValidateGetup(playback.animator, output, down, profile.idle, new SkinSampler(playback.gameObject, playback.animator), generic, row);
                            row.contact.curves = AnimationUtility.GetCurveBindings(output).Length;
                            row.protectedInputsUnchanged = hashes.All(p => p.Value == GetupHash(p.Key));
                            if (!row.protectedInputsUnchanged) throw new InvalidOperationException("Protected getup input changed.");
                        }
                    }
                }
                report.protectedInputsUnchanged = hashes.All(p => p.Value == GetupHash(p.Key));
                if (!report.protectedInputsUnchanged) throw new InvalidOperationException("Protected source/Down clips changed.");
                if (saveAssets)
                {
                    var existing = staged.Keys.ToDictionary(p => p, p => AssetDatabase.LoadAssetAtPath<AnimationClip>(p));
                    var rollback = existing.Where(p => p.Value != null).ToDictionary(p => p.Key, p => UnityEngine.Object.Instantiate(p.Value));
                    var created = new List<string>();
                    try
                    {
                        foreach (var pair in staged)
                        {
                            if (existing[pair.Key] == null)
                            {
                                var asset = UnityEngine.Object.Instantiate(pair.Value); asset.name = pair.Value.name;
                                AssetDatabase.CreateAsset(asset, pair.Key); created.Add(pair.Key);
                                AssetDatabase.SaveAssetIfDirty(asset);
                            }
                            else
                            {
                                EditorUtility.CopySerialized(pair.Value, existing[pair.Key]);
                                EditorUtility.SetDirty(existing[pair.Key]); AssetDatabase.SaveAssetIfDirty(existing[pair.Key]);
                            }
                        }
                        report.saved = true;
                    }
                    catch
                    {
                        foreach (var pair in rollback)
                        {
                            EditorUtility.CopySerialized(pair.Value, existing[pair.Key]); EditorUtility.SetDirty(existing[pair.Key]); AssetDatabase.SaveAssetIfDirty(existing[pair.Key]);
                        }
                        // Only paths created above from the four fixed Getup destination names can enter this list.
                        foreach (string path in created) AssetDatabase.DeleteAsset(path);
                        throw;
                    }
                    finally { foreach (var clip in rollback.Values) UnityEngine.Object.DestroyImmediate(clip); }
                }
            }
            catch (Exception exception) { report.failure = exception.Message; throw; }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var item in temporary) if (item != null) UnityEngine.Object.DestroyImmediate(item);
                report.scenePreserved = originalScene == UnityEngine.SceneManagement.SceneManager.GetActiveScene() && originalScene.isDirty == originalDirty && UnityEngine.SceneManagement.SceneManager.sceneCount == originalCount;
                if (!string.IsNullOrEmpty(report.failure))
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(GetupFailureReportPath));
                    System.IO.File.WriteAllText(GetupFailureReportPath, JsonUtility.ToJson(report, true));
                }
            }
            if (!report.scenePreserved) throw new InvalidOperationException("Original scene changed during getup bake.");
            return report;
        }

        static string GetupHash(string assetPath)
        {
            string path = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, assetPath);
            using (var algorithm = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-", "");
        }
        static Rig CreateGetupRig(string actor, UnityEngine.SceneManagement.Scene preview, List<UnityEngine.Object> temporary)
        {
            var rig = CreateRig(actor, preview, temporary);
            if (EditorUtility.IsPersistent(rig.gameObject) || rig.gameObject.scene != preview)
                throw new InvalidOperationException("Getup rig is not an isolated preview instance.");
            // AvatarSetupTool.SampleBindPose temporarily reparents bones. A connected nested FBX
            // instance rejects those operations even inside a PreviewScene. Unpack only this clone;
            // the source Prefab, model, Avatar and the user's scene are never applied or saved.
            if (PrefabUtility.IsPartOfPrefabInstance(rig.gameObject))
                PrefabUtility.UnpackPrefabInstance(rig.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (rig.gameObject.GetComponentsInChildren<Transform>(true).Any(t => PrefabUtility.IsPartOfPrefabInstance(t.gameObject)))
                throw new InvalidOperationException("Getup preview still contains a connected Prefab instance.");
            return rig;
        }
        static float[] GetupKneelWeights(Animator animator, AnimationClip source, int steps, GetupClipReport report)
        {
            var values = new float[steps + 1];
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            using (var sampling = new Sampling(animator, source, true))
                for (int i = 0; i <= steps; i++) { sampling.At(source.length * i / steps); values[i] = hips.position.y; }
            report.sourceFirstHipsY = values[0]; report.sourceLastHipsY = values[steps];
            float rise = values[steps] - values[0];
            if (rise <= .05f) throw new InvalidOperationException("Source does not rise sufficiently: " + report.contact.spec);
            float progress = 0;
            var weights = new float[steps + 1];
            report.kneelCorrectionZeroPhase = 1;
            for (int i = 0; i <= steps; i++)
            {
                // Monotonic standing progress prevents a small late idle bob from re-enabling kneeling IK.
                progress = Mathf.Max(progress, Mathf.Clamp01((values[i] - values[0]) / rise));
                weights[i] = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / .95f));
                if (weights[i] < .0001f) report.kneelCorrectionZeroPhase = Mathf.Min(report.kneelCorrectionZeroPhase, (float)i / steps);
            }
            weights[steps] = 0;
            return weights;
        }
        static void AdaptGetupPose(Animator animator, SkinSampler skin, float kneel, GetupClipReport getup)
        {
            var report = getup.contact;
            float shift = Ground - skin.Minimum(0);
            animator.GetBoneTransform(HumanBodyBones.Hips).position += Vector3.up * shift;
            report.maximumBodyShift = Mathf.Max(report.maximumBodyShift, Mathf.Abs(shift));
            if (kneel > .0001f)
            {
                var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                float requestedDrop = Mathf.Max(0, skin.Minimum(1) - Ground) * kneel;
                float reach = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position) - .00002f;
                Vector3 horizontal = Vector3.ProjectOnPlane(foot.position - upper.position, Vector3.up);
                float verticalReach = Mathf.Sqrt(Mathf.Max(0, reach * reach - horizontal.sqrMagnitude));
                Vector3 target = foot.position - Vector3.up * requestedDrop;
                float reachableY = Mathf.Max(target.y, upper.position.y - verticalReach);
                float released = Mathf.Max(0, reachableY - target.y);
                if (released > .0001f) { getup.leftFootConstraintReducedSamples++; getup.maximumReleasedFootCorrection = Mathf.Max(getup.maximumReleasedFootCorrection, released); }
                target.y = Mathf.Max(reachableY, target.y);
                // Do not pull a nearly straight rising leg horizontally just to satisfy a weak ground constraint.
                if (requestedDrop > .0001f && target.y < foot.position.y - .00001f)
                {
                    float residual = SolveTwoBone(upper, lower, foot, target, lower.position - upper.position, foot.rotation);
                    report.maximumUnreachableDistance = Mathf.Max(report.maximumUnreachableDistance, residual);
                    report.maximumLeftFootCorrection = Mathf.Max(report.maximumLeftFootCorrection, requestedDrop - released);
                }
            }
            // A rising hand is not held down. This only removes actual below-plane arm skin.
            RaiseHand(animator, skin, true, report); RaiseHand(animator, skin, false, report);
        }
        static void SetGetupOnce(AnimationClip clip, float duration)
        {
            clip.wrapMode = WrapMode.ClampForever;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0; settings.stopTime = duration; settings.loopTime = false; settings.loopBlend = false; settings.hasAdditiveReferencePose = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
        static float GetupJoinWeight(float time) => 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(time / GetupJoinSeconds));
        static AnimationCurve GetupJoinedPrefix(AnimationCurve source, Func<float, float> value)
        {
            int count = Mathf.RoundToInt(GetupJoinSeconds * 240);
            var keys = new List<Keyframe>();
            const float epsilon = .00001f;
            for (int i = 0; i <= count; i++)
            {
                float t = GetupJoinSeconds * i / count;
                float before = Mathf.Max(0, t - epsilon), after = t + epsilon;
                float tangent = (value(after) - value(before)) / (after - before);
                keys.Add(new Keyframe(t, value(t), tangent, tangent));
            }
            keys.AddRange(source.keys.Where(k => k.time > GetupJoinSeconds + .000001f));
            return new AnimationCurve(keys.ToArray()) { preWrapMode = source.preWrapMode, postWrapMode = source.postWrapMode };
        }
        static void ApplyGetupInitialBodyJoin(AnimationClip output, AnimationClip source, AnimationClip down, GetupClipReport report)
        {
            var changed = new List<string>();
            // Limb channels are already encoded below. Match the torso and fingers only during the initial join;
            // preserve the original source keys/tangents after the join instead of resampling the entire body.
            foreach (string name in HumanTrait.MuscleName.Where(n => !IsLimb(n)))
            {
                var curve = AnimationUtility.GetEditorCurve(source, Binding(name));
                var reference = AnimationUtility.GetEditorCurve(down, Binding(name));
                if (curve == null || reference == null) continue;
                float delta = reference.Evaluate(report.downReferenceTime) - curve.Evaluate(0);
                AnimationUtility.SetEditorCurve(output, Binding(name), GetupJoinedPrefix(curve, t => curve.Evaluate(t) + delta * GetupJoinWeight(t)));
                changed.Add(name);
            }
            string[] axes = { "x", "y", "z", "w" };
            var src = axes.Select(a => AnimationUtility.GetEditorCurve(source, Binding("RootQ." + a))).ToArray();
            var dst = axes.Select(a => AnimationUtility.GetEditorCurve(down, Binding("RootQ." + a))).ToArray();
            if (src.Any(c => c == null) || dst.Any(c => c == null)) throw new InvalidOperationException("Missing body quaternion for getup join.");
            Func<AnimationCurve[], float, Quaternion> read = (curves, time) => new Quaternion(curves[0].Evaluate(time), curves[1].Evaluate(time), curves[2].Evaluate(time), curves[3].Evaluate(time)).normalized;
            var deltaQ = read(dst, report.downReferenceTime) * Quaternion.Inverse(read(src, 0));
            for (int axis = 0; axis < 4; axis++)
            {
                int component = axis;
                var curve = GetupJoinedPrefix(src[axis], t =>
                {
                    var original = read(src, t);
                    var q = (Quaternion.Slerp(Quaternion.identity, deltaQ, GetupJoinWeight(t)) * original).normalized;
                    if (Quaternion.Dot(q, original) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    return component == 0 ? q.x : component == 1 ? q.y : component == 2 ? q.z : q.w;
                });
                AnimationUtility.SetEditorCurve(output, Binding("RootQ." + axes[axis]), curve);
                changed.Add("RootQ." + axes[axis]);
            }
            report.initialJoinAdditionalChannels = changed.ToArray();
        }

        static AnimationClip BakeHumanGetup(Animator animator, AnimationClip source, SkinSampler skin, HumanPoseHandler handler, AnimationClip down, float[] kneel, GetupClipReport report)
        {
            int steps = kneel.Length - 1;
            var output = UnityEngine.Object.Instantiate(source); output.frameRate = 240;
            string[] names = HumanTrait.MuscleName.Where(IsLimb).ToArray();
            int[] indices = names.Select(n => Array.IndexOf(HumanTrait.MuscleName, n)).ToArray();
            report.contact.changedMuscles = names; report.contact.frames = steps + 1;
            var downValues = names.Select(n => AnimationUtility.GetEditorCurve(down, Binding(n)).Evaluate(report.downReferenceTime)).ToArray();
            float downY = AnimationUtility.GetEditorCurve(down, Binding("RootT.y")).Evaluate(report.downReferenceTime);
            float conversion = animator.humanScale * animator.transform.lossyScale.y;
            if (conversion <= .00001f) throw new InvalidOperationException("Invalid getup human scale.");
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            for (int pass = 0; pass < 5; pass++)
            {
                var input = pass == 0 ? source : output;
                var rootY = AnimationUtility.GetEditorCurve(input, Binding("RootT.y"));
                var inputCurves = names.Select(n => AnimationUtility.GetEditorCurve(input, Binding(n))).ToArray();
                var values = names.Select(_ => new float[steps + 1]).ToArray(); var heights = new float[steps + 1];
                using (var sampling = new Sampling(animator, input, false))
                {
                    for (int frame = 0; frame <= steps; frame++)
                    {
                        float time = source.length * frame / steps;
                        sampling.playable.SetApplyFootIK(false); sampling.At(time); float hipBefore = hips.position.y;
                        if (pass == 0) { sampling.playable.SetApplyFootIK(true); sampling.At(time); }
                        AdaptGetupPose(animator, skin, kneel[frame], report);
                        var pose = new HumanPose(); handler.GetHumanPose(ref pose);
                        for (int m = 0; m < names.Length; m++) values[m][frame] = pass == 0 || IsArm(names[m]) || IsLeftLeg(names[m]) ? pose.muscles[indices[m]] : inputCurves[m].Evaluate(time);
                        heights[frame] = rootY.Evaluate(time) + (hips.position.y - hipBefore) / conversion;
                    }
                }
                // Match the initial Down pose briefly; do not carry a static offset through the rising motion.
                for (int m = 0; m < names.Length; m++)
                {
                    float delta = downValues[m] - values[m][0];
                    for (int i = 0; i <= steps; i++) values[m][i] += delta * GetupJoinWeight(source.length * i / steps);
                    AnimationUtility.SetEditorCurve(output, Binding(names[m]), Curve(values[m], source.length));
                }
                float dy = downY - heights[0];
                for (int i = 0; i <= steps; i++) heights[i] += dy * GetupJoinWeight(source.length * i / steps);
                AnimationUtility.SetEditorCurve(output, Binding("RootT.y"), Curve(heights, source.length));
                ApplyGetupInitialBodyJoin(output, source, down, report);
                report.contact.roundtripPasses = pass + 1;
            }
            SetGetupOnce(output, source.length);
            if (report.contact.actor == "Elite1" && report.contact.spec == "GetupLowHealth")
                RepairGetupLeftLegPrefix(animator, output, down, handler, report);
            FinalizeGetupHumanGround(animator, output, skin, report, conversion);
            return output;
        }

        static void RepairGetupLeftLegPrefix(Animator animator, AnimationClip clip, AnimationClip down, HumanPoseHandler handler, GetupClipReport report)
        {
            // Inverse muscle decomposition may wrap a leg twist by 360 degrees near a straight knee.
            // Repeated muscle-space offsets then interpolate through that entire turn. Build this short
            // join once in local quaternion space, and choose equivalent continuous twist branches.
            var ids = new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes };
            var bones = ids.Select(animator.GetBoneTransform).ToArray();
            if (bones.Any(b => b == null)) throw new InvalidOperationException("Missing left-leg prefix bone.");
            var human = animator.avatar.humanDescription.human;
            foreach (var id in ids.Take(3))
            {
                var definition = human.FirstOrDefault(b => b.humanName == id.ToString());
                if (!definition.limit.useDefaultValues) throw new InvalidOperationException("Getup twist continuity needs explicit custom Avatar limits: " + id);
            }
            var names = HumanTrait.MuscleName.Where(IsLeftLeg).ToArray();
            var indices = names.Select(n => Array.IndexOf(HumanTrait.MuscleName, n)).ToArray();
            var original = names.Select(n => AnimationUtility.GetEditorCurve(clip, Binding(n))).ToArray();
            var downCurves = names.Select(n => AnimationUtility.GetEditorCurve(down, Binding(n))).ToArray();
            var min = indices.Select(HumanTrait.GetMuscleDefaultMin).ToArray();
            var max = indices.Select(HumanTrait.GetMuscleDefaultMax).ToArray();
            report.leftLegTwistLimits = names.Select((n, m) => new { n, m }).Where(p => p.n.Contains("Twist"))
                .Select(p => p.n + ": " + min[p.m] + ".." + max[p.m] + " degrees; angular period=360").ToArray();
            Quaternion[] start, finish;
            using (var sampling = new Sampling(animator, down, false))
            { sampling.At(report.downReferenceTime); start = bones.Select(b => b.localRotation).ToArray(); }
            using (var sampling = new Sampling(animator, clip, false))
            { sampling.At(GetupJoinSeconds); finish = bones.Select(b => b.localRotation).ToArray(); }
            int steps = Mathf.RoundToInt(clip.length * 240);
            var values = names.Select(_ => new float[steps + 1]).ToArray();
            var expected = new Dictionary<int, Vector3[]>();
            using (var sampling = new Sampling(animator, clip, false))
            {
                for (int i = 0; i <= steps; i++)
                {
                    float time = clip.length * i / steps;
                    if (time <= GetupJoinSeconds + .000001f)
                    {
                        sampling.At(time);
                        float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(time / GetupJoinSeconds));
                        for (int b = 0; b < bones.Length; b++) bones[b].localRotation = Quaternion.Slerp(start[b], finish[b], u);
                        expected.Add(i, bones.Select(b => b.position).ToArray());
                        var pose = new HumanPose(); handler.GetHumanPose(ref pose);
                        for (int m = 0; m < names.Length; m++)
                            values[m][i] = i == 0 ? downCurves[m].Evaluate(report.downReferenceTime) : pose.muscles[indices[m]];
                    }
                    else for (int m = 0; m < names.Length; m++) values[m][i] = original[m].Evaluate(time);
                    for (int m = 0; m < names.Length; m++)
                    {
                        if (i == 0 || !names[m].Contains("Twist")) continue;
                        float angle = values[m][i] * (values[m][i] >= 0 ? max[m] : -min[m]);
                        float previous = values[m][i - 1] * (values[m][i - 1] >= 0 ? max[m] : -min[m]);
                        int turns = Mathf.RoundToInt((previous - angle) / 360f);
                        if (turns != 0)
                        {
                            angle += 360f * turns;
                            values[m][i] = angle / (angle >= 0 ? max[m] : -min[m]);
                            report.leftLegTwistWrapAdjustments++;
                        }
                    }
                }
            }
            for (int m = 0; m < names.Length; m++) AnimationUtility.SetEditorCurve(clip, Binding(names[m]), Curve(values[m], clip.length));
            for (int m = 0; m < names.Length; m++) if (names[m].Contains("Twist"))
                for (int i = 1; i <= steps; i++)
                {
                    float currentAngle = values[m][i] * (values[m][i] >= 0 ? max[m] : -min[m]);
                    float previousAngle = values[m][i - 1] * (values[m][i - 1] >= 0 ? max[m] : -min[m]);
                    report.maximumLeftLegTwistKeyDegrees = Mathf.Max(report.maximumLeftLegTwistKeyDegrees, Mathf.Abs(currentAngle - previousAngle));
                }
            if (report.maximumLeftLegTwistKeyDegrees >= 180f)
                throw new InvalidOperationException("Unresolved left-leg twist branch in getup: " + report.maximumLeftLegTwistKeyDegrees + " degrees per key.");
            using (var sampling = new Sampling(animator, clip, false))
            {
                foreach (var pair in expected)
                {
                    sampling.At(clip.length * pair.Key / steps);
                    for (int b = 0; b < bones.Length; b++) report.leftLegPrefixReplayMaximumJointError = Mathf.Max(report.leftLegPrefixReplayMaximumJointError, Vector3.Distance(bones[b].position, pair.Value[b]));
                }
            }
            report.leftLegQuaternionPrefix = true;
            if (report.leftLegPrefixReplayMaximumJointError > .03f)
                throw new InvalidOperationException("Quaternion getup prefix changed target pose during muscle encoding: " + report.leftLegPrefixReplayMaximumJointError);
        }

        static void FinalizeGetupHumanGround(Animator animator, AnimationClip clip, SkinSampler skin, GetupClipReport report, float conversion)
        {
            // Muscles and the first-pose body join are now fixed. RootT.y changes are a linear world-Y
            // shift, so two dense passes remove the remaining encoding/interpolation height residual.
            const int rate = 1920;
            int steps = Mathf.Max(1, Mathf.RoundToInt(clip.length * rate));
            report.finalRootCalibrationHz = rate;
            report.minimumRootQuaternionNorm = float.PositiveInfinity;
            var quaternionCurves = new[] { "x", "y", "z", "w" }.Select(axis => AnimationUtility.GetEditorCurve(clip, Binding("RootQ." + axis))).ToArray();
            Quaternion previous = Quaternion.identity;
            for (int i = 0; i <= steps; i++)
            {
                float time = clip.length * i / steps;
                var q = new Quaternion(quaternionCurves[0].Evaluate(time), quaternionCurves[1].Evaluate(time), quaternionCurves[2].Evaluate(time), quaternionCurves[3].Evaluate(time));
                float norm = Mathf.Sqrt(Quaternion.Dot(q, q));
                report.minimumRootQuaternionNorm = Mathf.Min(report.minimumRootQuaternionNorm, norm);
                report.maximumRootQuaternionNorm = Mathf.Max(report.maximumRootQuaternionNorm, norm);
                q = q.normalized;
                if (i > 0)
                {
                    if (Quaternion.Dot(previous, q) < 0) report.rootQuaternionNegativeAdjacentDots++;
                    report.maximumAdjacentRootQuaternionDegrees = Mathf.Max(report.maximumAdjacentRootQuaternionDegrees, Quaternion.Angle(previous, q));
                }
                previous = q;
            }
            for (int pass = 0; pass < 2; pass++)
            {
                var sourceY = AnimationUtility.GetEditorCurve(clip, Binding("RootT.y"));
                var heights = new float[steps + 1];
                using (var sampling = new Sampling(animator, clip, false))
                {
                    for (int frame = 0; frame <= steps; frame++)
                    {
                        float time = clip.length * frame / steps;
                        sampling.At(time);
                        float correction = Ground - skin.Minimum(0);
                        heights[frame] = sourceY.Evaluate(time) + correction / conversion;
                        if (Mathf.Abs(correction) > report.maximumFinalRootHeightCorrection)
                        {
                            report.maximumFinalRootHeightCorrection = Mathf.Abs(correction);
                            report.maximumFinalRootHeightCorrectionPhase = (float)frame / steps;
                        }
                    }
                }
                AnimationUtility.SetEditorCurve(clip, Binding("RootT.y"), Curve(heights, clip.length));
                report.finalRootCalibrationPasses = pass + 1;
            }
        }

        static AnimationClip BakeGenericGetup(Animator animator, AnimationClip source, SkinSampler skin, Transform[] bones, Animator downAnimator, AnimationClip down, float[] kneel, GetupClipReport report)
        {
            int steps = kneel.Length - 1;
            report.contact.frames = steps + 1; report.contact.changedMuscles = Array.Empty<string>();
            string[] paths = bones.Select(t => AnimationUtility.CalculateTransformPath(t, animator.transform)).ToArray();
            downAnimator.enabled = false; down.SampleAnimation(downAnimator.gameObject, report.downReferenceTime);
            var targets = paths.Select(p => downAnimator.transform.Find(p)).ToArray();
            if (targets.Any(t => t == null)) throw new InvalidOperationException("Generic Down reference path mismatch.");
            var downP = targets.Select(t => t.localPosition).ToArray();
            var downQ = targets.Select(t => t.localRotation).ToArray();
            var downS = targets.Select(t => t.localScale).ToArray();
            var values = Enumerable.Range(0, bones.Length * 10).Select(_ => new float[steps + 1]).ToArray();
            var previous = new Quaternion[bones.Length];
            var deltaP = new Vector3[bones.Length]; var deltaQ = new Quaternion[bones.Length]; var deltaS = new Vector3[bones.Length];
            using (var sampling = new Sampling(animator, source, true))
            {
                for (int frame = 0; frame <= steps; frame++)
                {
                    float time = source.length * frame / steps;
                    sampling.At(time);
                    AdaptGetupPose(animator, skin, kneel[frame], report);
                    for (int b = 0; b < bones.Length; b++)
                    {
                        var p = bones[b].localPosition; var q = bones[b].localRotation; var s = bones[b].localScale;
                        if (frame == 0) { deltaP[b] = downP[b] - p; deltaQ[b] = downQ[b] * Quaternion.Inverse(q); deltaS[b] = downS[b] - s; }
                        float join = GetupJoinWeight(time);
                        p += deltaP[b] * join; q = Quaternion.Slerp(Quaternion.identity, deltaQ[b], join) * q; s += deltaS[b] * join;
                        bones[b].localPosition = p; bones[b].localRotation = q; bones[b].localScale = s;
                    }
                    // Contact correction must see the actual joined pose, not only the pre-join source pose.
                    float shift = Ground - skin.Minimum(0);
                    animator.GetBoneTransform(HumanBodyBones.Hips).position += Vector3.up * shift;
                    report.contact.maximumBodyShift = Mathf.Max(report.contact.maximumBodyShift, Mathf.Abs(shift));
                    RaiseHand(animator, skin, true, report.contact); RaiseHand(animator, skin, false, report.contact);
                    for (int b = 0; b < bones.Length; b++)
                    {
                        var p = bones[b].localPosition; var q = bones[b].localRotation; var s = bones[b].localScale;
                        if (frame > 0 && Quaternion.Dot(previous[b], q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        previous[b] = q;
                        var row = new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
                        for (int k = 0; k < 10; k++) values[b * 10 + k][frame] = row[k];
                    }
                }
            }
            var output = new AnimationClip { frameRate = 60, legacy = false };
            for (int b = 0; b < bones.Length; b++) for (int k = 0; k < 10; k++) AnimationUtility.SetEditorCurve(output, EditorCurveBinding.FloatCurve(paths[b], typeof(Transform), TransformProperties[k]), Curve(values[b * 10 + k], source.length));
            output.EnsureQuaternionContinuity(); SetGetupOnce(output, source.length); return output;
        }

        static Dictionary<string, Vector3> GetupJoints(Animator animator, bool generic)
        {
            var all = animator.GetComponentsInChildren<Transform>(true);
            var result = new Dictionary<string, Vector3>();
            for (int i = 0; i < HumanNames.Length; i++)
            {
                var t = generic ? all.FirstOrDefault(x => x.name == GenericNames[i]) : animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), HumanNames[i]));
                if (t != null) result.Add(HumanNames[i], t.position);
            }
            return result;
        }
        static Dictionary<string, Vector3> SampleGetupReference(Animator animator, AnimationClip clip, float time, bool generic)
        {
            if (generic) { animator.enabled = false; clip.SampleAnimation(animator.gameObject, time); return GetupJoints(animator, true); }
            using (var sampling = new Sampling(animator, clip, false)) { sampling.At(time); return GetupJoints(animator, false); }
        }
        static float GetupDifference(Dictionary<string, Vector3> a, Dictionary<string, Vector3> b) => a.Where(p => b.ContainsKey(p.Key)).Max(p => Vector3.Distance(p.Value, b[p.Key]));

        static void ValidateGetup(Animator animator, AnimationClip clip, AnimationClip down, AnimationClip idle, SkinSampler skin, bool generic, GetupClipReport report)
        {
            var downZero = SampleGetupReference(animator, down, 0, generic);
            var downStart = SampleGetupReference(animator, down, report.downReferenceTime, generic);
            var downBreakEnd = SampleGetupReference(animator, down, report.currentBreakEndDownPhase * down.length, generic);
            var idlePose = SampleGetupReference(animator, idle, 0, generic);
            report.idleHipsY = idlePose["Hips"].y;
            var previousSaved = AssetDatabase.LoadAssetAtPath<AnimationClip>(report.contact.destination);
            if (report.leftLegQuaternionPrefix && previousSaved != null)
            {
                Dictionary<string, Vector3> previous = null, previousStep = null;
                using (var baseline = new Sampling(animator, previousSaved, false))
                {
                    for (int i = 0; i <= 60; i++)
                    {
                        baseline.At(i / 120f); var pose = GetupJoints(animator, false);
                        if (previous != null)
                        {
                            var step = pose.ToDictionary(p => p.Key, p => p.Value - previous[p.Key]);
                            report.previousSavedFirstHalfSecondMaximumJointStep = Mathf.Max(report.previousSavedFirstHalfSecondMaximumJointStep, step.Values.Max(v => v.magnitude));
                            if (previousStep != null) report.previousSavedFirstHalfSecondMaximumStepVectorChange = Mathf.Max(report.previousSavedFirstHalfSecondMaximumStepVectorChange, step.Max(p => (p.Value - previousStep[p.Key]).magnitude));
                            previousStep = step;
                        }
                        previous = pose;
                    }
                }
                report.previousSavedMotionMeasured = true;
            }
            Sampling sampling = null;
            var row = report.contact;
            try
            {
                if (generic) animator.enabled = false; else sampling = new Sampling(animator, clip, false);
                int steps = Mathf.Max(1, Mathf.CeilToInt(clip.length * (generic ? 120 : 480)));
                for (int i = 0; i <= steps; i++)
                {
                    float time = clip.length * i / steps;
                    if (generic) clip.SampleAnimation(animator.gameObject, time); else sampling.At(time);
                    var y = skin.MeasureAll();
                    if (y[5] < row.minimumBodyY) { row.minimumBodyY = y[5]; row.minimumBodyPhase = (float)i / steps; row.minimumBodyVertex = skin.LastBodyMinimum; }
                    if (y[0] < row.minimumSupportY) { row.minimumSupportY = y[0]; row.minimumSupportPhase = (float)i / steps; }
                    if (y[0] > row.maximumSupportY) { row.maximumSupportY = y[0]; row.maximumSupportPhase = (float)i / steps; }
                    row.minimumLeftSupportY = Mathf.Min(row.minimumLeftSupportY, y[1]); row.maximumLeftSupportY = Mathf.Max(row.maximumLeftSupportY, y[1]);
                    row.minimumRightSupportY = Mathf.Min(row.minimumRightSupportY, y[2]); row.maximumRightSupportY = Mathf.Max(row.maximumRightSupportY, y[2]);
                    row.minimumLeftHandY = Mathf.Min(row.minimumLeftHandY, y[3]); row.minimumRightHandY = Mathf.Min(row.minimumRightHandY, y[4]);
                    if (i == 0)
                    {
                        var pose = GetupJoints(animator, generic);
                        report.firstHipsY = pose["Hips"].y;
                        report.firstPoseMaximumJointDifference = GetupDifference(pose, downStart);
                        report.firstPoseVsDownZeroMaximumJointDifference = GetupDifference(pose, downZero);
                        report.firstPoseWorstJoint = pose.Keys.OrderByDescending(k => Vector3.Distance(pose[k], downStart[k])).First();
                        report.currentBreakEndMaximumJointDifference = GetupDifference(pose, downBreakEnd);
                    }
                    if (i == steps)
                    {
                        var pose = GetupJoints(animator, generic); report.lastHipsY = pose["Hips"].y;
                        report.endToIdleMaximumJointDifference = GetupDifference(pose, idlePose);
                    }
                }
                row.validationSamples = steps + 1;
                report.nonLooping = !AnimationUtility.GetAnimationClipSettings(clip).loopTime;
                report.originalDurationPreserved = Mathf.Abs(clip.length - row.duration) < .00001f;
                Dictionary<string, Vector3> previousPose = null, previousDelta = null;
                for (int i = 0; i <= 60; i++)
                {
                    float time = i / 120f;
                    if (generic) clip.SampleAnimation(animator.gameObject, time); else sampling.At(time);
                    var pose = GetupJoints(animator, generic);
                    if (previousPose != null)
                    {
                        var delta = pose.ToDictionary(p => p.Key, p => p.Value - previousPose[p.Key]);
                        foreach (var joint in delta)
                        {
                            float distance = joint.Value.magnitude;
                            if (joint.Key == "LeftUpperLeg" || joint.Key == "LeftLowerLeg" || joint.Key == "LeftFoot" || joint.Key == "LeftToes")
                                report.firstHalfSecondMaximumLeftLegStep = Mathf.Max(report.firstHalfSecondMaximumLeftLegStep, distance);
                            if (distance > report.firstHalfSecondMaximumJointStep)
                            {
                                report.firstHalfSecondMaximumJointStep = distance;
                                report.firstHalfSecondMaximumJointStepTime = time;
                                report.firstHalfSecondMaximumJointStepBone = joint.Key;
                            }
                            if (previousDelta != null)
                            {
                                float change = (joint.Value - previousDelta[joint.Key]).magnitude;
                                if (change > report.firstHalfSecondMaximumStepVectorChange)
                                {
                                    report.firstHalfSecondMaximumStepVectorChange = change;
                                    report.firstHalfSecondMaximumStepVectorChangeTime = time;
                                    report.firstHalfSecondMaximumStepVectorChangeBone = joint.Key;
                                }
                            }
                        }
                        previousDelta = delta;
                    }
                    previousPose = pose;
                }
                // Fast continuous arm motion is possible: the saved Down elbow is 1.47 m from the
                // .30 s recovery elbow. Reject the original leg jump and sudden changes of velocity,
                // not that necessary average arm speed. 25 mm step-vector change at 120 Hz is below
                // the old 95-97 mm branch glitches and above all three unchanged clips (max 16 mm).
                if (report.leftLegQuaternionPrefix && (report.firstHalfSecondMaximumLeftLegStep > .04f || report.firstHalfSecondMaximumStepVectorChange > .025f))
                    throw new InvalidOperationException("Getup prefix discontinuity at 120 Hz: leftLegStep=" + report.firstHalfSecondMaximumLeftLegStep
                        + " stepVectorChange=" + report.firstHalfSecondMaximumStepVectorChange + " at " + report.firstHalfSecondMaximumStepVectorChangeTime + " " + report.firstHalfSecondMaximumStepVectorChangeBone);
                if (!report.nonLooping || !report.originalDurationPreserved || report.firstPoseMaximumJointDifference > .03f
                    || row.minimumSupportY < -.015f || row.maximumSupportY > .035f || row.minimumBodyY < -.02f
                    || row.minimumLeftHandY < -.015f || row.minimumRightHandY < -.015f || report.lastHipsY < report.idleHipsY * .8f)
                    throw new InvalidOperationException("Getup replay needs adjustment " + row.actor + " " + row.spec
                        + ": firstGap=" + report.firstPoseMaximumJointDifference + "(" + report.firstPoseWorstJoint + ") support=" + row.minimumSupportY + "@" + row.minimumSupportPhase + ".." + row.maximumSupportY + "@" + row.maximumSupportPhase
                        + " body=" + row.minimumBodyY + " bone=" + row.minimumBodyVertex?.dominantBone + " hands=" + row.minimumLeftHandY + "," + row.minimumRightHandY + " hipsEnd/Idle=" + report.lastHipsY + "/" + report.idleHipsY);
            }
            finally { if (sampling != null) sampling.Dispose(); }
        }
    }
}
