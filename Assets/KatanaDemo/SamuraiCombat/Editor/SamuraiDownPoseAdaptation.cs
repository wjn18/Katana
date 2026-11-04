using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>Target-specific offline repair of the four new down loops only. Never changes source FBX, Avatars or scenes.</summary>
    public static partial class SamuraiDownPoseAdaptation
    {
        const string Folder = "Assets/KatanaDemo/SamuraiCombat/Animations/ToughnessBreak";
        const float Ground = .0015f;
        public static string FailureReportPath => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../斗笠剑士/动画资源/execution-audit-20261030/analysis/down-state-integration/adaptation-failure.json"));
        static readonly string[] Specs = { "DownHealthy", "DownLowHealth" };
        static readonly string[] Codes = { "00441", "30221" };
        static readonly string[] HumanNames = { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes" };
        static readonly string[] GenericNames = { "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "RightShoulder", "RightArm", "RightForeArm", "RightHand", "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase", "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase" };
        static readonly string[] TransformProperties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w", "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };

        [Serializable] public sealed class ClipReport
        {
            public string actor, spec, source, destination;
            public float duration, maximumBodyShift, maximumHandLift, maximumUnreachableDistance;
            public float maximumLeftFootCorrection;
            public float maximumLeftFootHorizontalAdjustment;
            public int mirroredArmSolutions;
            public int frames, curves, roundtripPasses;
            public string[] changedMuscles;
            public bool sourceFootIKBaked;
            public float minimumBodyY = float.PositiveInfinity;
            public float minimumBodyPhase;
            public ContactVertex minimumBodyVertex;
            public float minimumSupportY = float.PositiveInfinity, maximumSupportY = float.NegativeInfinity;
            public float minimumSupportPhase, maximumSupportPhase, maximumLeftSupportPhase, minimumLeftHandPhase, minimumRightHandPhase;
            public float minimumLeftSupportY = float.PositiveInfinity, maximumLeftSupportY = float.NegativeInfinity;
            public float minimumRightSupportY = float.PositiveInfinity, maximumRightSupportY = float.NegativeInfinity;
            public float minimumLeftHandY = float.PositiveInfinity, minimumRightHandY = float.PositiveInfinity;
            public float nearSeamMaximumJointDistance;
            public int validationSamples;
        }
        [Serializable] public sealed class ContactVertex
        {
            public string renderer, dominantBone, influences;
            public int vertex;
            public float y, dominantWeight;
            public Vector3 position;
        }
        [Serializable] public sealed class AdaptationReport
        {
            public bool saved, scenePreserved;
            public string failure;
            public string supportRule = "At least 50% combined UpperLeg/LowerLeg/Foot/Toes skin weight; the right kneeling thigh/knee armor supports the body, and independent left-leg IK grounds the left support. Arm contact includes Forearm/Hand/fingers. Union minimum alone is not a guarantee of three-point contact.";
            public List<ClipReport> clips = new List<ClipReport>();
        }

        // Explicit save flag: root can first inspect a fully baked/replayed dry run without touching assets.
        public static AdaptationReport RebuildSelected(bool saveAssets)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play before adapting down loops.");
            var originalScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bool dirtyBefore = originalScene.isDirty;
            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            var targets = new Dictionary<string, AnimationClip>();
            foreach (string actor in new[] { "Enemy1", "Elite1" }) foreach (string spec in Specs)
            {
                string path = Destination(actor, spec);
                var target = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (target == null) throw new InvalidOperationException("Expected existing new clip: " + path);
                targets.Add(path, target);
            }
            foreach (string code in Codes) GetSource(code); // Validate both takes before creating or changing anything.
            var scene = EditorSceneManager.NewPreviewScene();
            var staged = new Dictionary<string, AnimationClip>();
            var temporary = new List<UnityEngine.Object>();
            var report = new AdaptationReport();
            try
            {
                foreach (string actor in new[] { "Enemy1", "Elite1" })
                {
                    var rig = CreateRig(actor, scene, temporary);
                    bool generic = actor == "Enemy1";
                    Transform[] bakeBones;
                    if (generic)
                    {
                        var avatar = BuildTemporaryEnemyAvatar(rig.animator, out bakeBones);
                        temporary.Add(avatar);
                        rig.animator.avatar = avatar;
                    }
                    else bakeBones = Array.Empty<Transform>();
                    if (rig.animator.avatar == null || !rig.animator.avatar.isValid || !rig.animator.avatar.isHuman)
                        throw new InvalidOperationException("Invalid temporary target Avatar: " + actor);
                    rig.animator.enabled = true;
                    rig.animator.Rebind();
                    var skin = new SkinSampler(rig.gameObject, rig.animator);
                    using (var handler = new HumanPoseHandler(rig.animator.avatar, rig.animator.transform))
                    {
                        for (int index = 0; index < Specs.Length; index++)
                        {
                            var source = PrepareSource(GetSource(Codes[index]));
                            temporary.Add(source);
                            var row = new ClipReport { actor = actor, spec = Specs[index], source = SourcePath(Codes[index]), destination = Destination(actor, Specs[index]), duration = source.length, sourceFootIKBaked = true };
                            report.clips.Add(row);
                            var output = generic ? BakeGeneric(rig.animator, source, skin, bakeBones, row) : BakeHuman(rig.animator, source, skin, handler, row);
                            output.name = targets[row.destination].name;
                            output.hideFlags = HideFlags.None;
                            staged.Add(row.destination, output);
                            temporary.Add(output);
                            if (generic)
                            {
                                var playback = CreateRig(actor, scene, temporary);
                                playback.animator.enabled = false;
                                Validate(playback.animator, output, new SkinSampler(playback.gameObject, playback.animator), true, row);
                            }
                            else Validate(rig.animator, output, skin, false, row);
                            row.curves = AnimationUtility.GetCurveBindings(output).Length;
                        }
                    }
                }
                if (saveAssets)
                {
                    // All four clips are built and replayed first. CopySerialized preserves existing GUID/fileID references.
                    var rollback = targets.ToDictionary(p => p.Key, p => UnityEngine.Object.Instantiate(p.Value));
                    try
                    {
                        foreach (var pair in staged)
                        {
                            EditorUtility.CopySerialized(pair.Value, targets[pair.Key]);
                            EditorUtility.SetDirty(targets[pair.Key]);
                            AssetDatabase.SaveAssetIfDirty(targets[pair.Key]);
                        }
                        report.saved = true;
                    }
                    catch
                    {
                        foreach (var pair in rollback)
                        {
                            EditorUtility.CopySerialized(pair.Value, targets[pair.Key]);
                            EditorUtility.SetDirty(targets[pair.Key]);
                            AssetDatabase.SaveAssetIfDirty(targets[pair.Key]);
                        }
                        throw;
                    }
                    finally { foreach (var clip in rollback.Values) UnityEngine.Object.DestroyImmediate(clip); }
                }
            }
            catch (Exception exception)
            {
                report.failure = exception.Message;
                throw;
            }
            finally
            {
                // Close the isolated scene before disposing the temporary Avatars used by its Animators.
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var item in temporary) if (item != null) UnityEngine.Object.DestroyImmediate(item);
                report.scenePreserved = originalScene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                    && originalScene.isDirty == dirtyBefore && sceneCount == UnityEngine.SceneManagement.SceneManager.sceneCount;
                if (!string.IsNullOrEmpty(report.failure))
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FailureReportPath));
                    System.IO.File.WriteAllText(FailureReportPath, JsonUtility.ToJson(report, true));
                }
            }
            report.scenePreserved = originalScene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                && originalScene.isDirty == dirtyBefore && sceneCount == UnityEngine.SceneManagement.SceneManager.sceneCount;
            if (!report.scenePreserved) throw new InvalidOperationException("Original scene state changed unexpectedly.");
            return report;
        }

        static string SourcePath(string code) => Folder + "/SourceFBX/CP_HUMAN_COMMON_" + code + ".fbx";
        static string Destination(string actor, string spec) => Folder + (actor == "Enemy1" ? "/Enemy1/Enemy1_" : "/Humanoid/Nioh3_") + spec + ".anim";
        static AnimationClip GetSource(string code)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(SourcePath(code)).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview", StringComparison.Ordinal)).ToArray();
            if (clips.Length != 1 || !clips[0].isHumanMotion) throw new InvalidOperationException("Expected one Humanoid take: " + SourcePath(code));
            return clips[0];
        }
        static AnimationClip PrepareSource(AnimationClip imported)
        {
            var clip = UnityEngine.Object.Instantiate(imported);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clip.wrapMode = WrapMode.ClampForever;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false; settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            foreach (string axis in new[] { "x", "z" })
                AnimationUtility.SetEditorCurve(clip, Binding("RootT." + axis), new AnimationCurve(new Keyframe(0, 0), new Keyframe(clip.length, 0)));
            return clip;
        }
        static EditorCurveBinding Binding(string property) => EditorCurveBinding.FloatCurve("", typeof(Animator), property);
        sealed class Rig { public GameObject gameObject; public Animator animator; }
        static Rig CreateRig(string actor, UnityEngine.SceneManagement.Scene scene, List<UnityEngine.Object> temporary)
        {
            string path = "Assets/KatanaDemo/Characters/" + actor + "/Prefabs/" + actor + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Missing " + path);
            var clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            temporary.Add(clone);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var b in clone.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            var animator = clone.GetComponentInChildren<Animator>(true);
            if (animator == null) throw new InvalidOperationException("No Animator: " + actor);
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = false;
            return new Rig { gameObject = clone, animator = animator };
        }

        static Avatar BuildTemporaryEnemyAvatar(Animator animator, out Transform[] bakeBones)
        {
            var visual = animator.gameObject;
            var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var setup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool");
            if (setup == null) throw new InvalidOperationException("Avatar calibration tool unavailable.");
            setup.GetMethod("SampleBindPose", flags).Invoke(null, new object[] { visual });
            var mapping = HumanNames.Select((name, i) => new { name, bone = GenericNames[i] }).ToDictionary(p => p.name, p => p.bone);
            var all = visual.GetComponentsInChildren<Transform>(true);
            var selected = new HashSet<Transform>(skins.SelectMany(s => s.bones).Where(t => t != null));
            selected.Add(visual.transform);
            foreach (var t in all) if (t.name == "Armature") selected.Add(t);
            var method = setup.GetMethods(flags).First(m => m.Name == "GetHumanBones" && m.GetParameters()[0].ParameterType == mapping.GetType());
            var wrappers = method.Invoke(null, new object[] { mapping, selected.ToDictionary(t => t, t => true) });
            setup.GetMethod("MakePoseValid", flags).Invoke(null, new[] { wrappers });
            bool valid = (bool)setup.GetMethod("IsPoseValid", flags).Invoke(null, new[] { wrappers });
            float error = (float)setup.GetMethod("GetPoseError", flags).Invoke(null, new[] { wrappers });
            if (!valid || error > .01f) throw new InvalidOperationException("Enemy reference pose invalid: " + error);
            var human = HumanNames.Select((name, i) => new HumanBone { humanName = name, boneName = GenericNames[i], limit = new HumanLimit { useDefaultValues = true } }).ToArray();
            var skeleton = all.Where(selected.Contains).Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            var avatar = AvatarBuilder.BuildHumanAvatar(visual, new HumanDescription { human = human, skeleton = skeleton, upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f, armStretch = .05f, legStretch = .05f });
            bakeBones = all.Where(t => selected.Contains(t) && t != visual.transform).ToArray();
            return avatar;
        }

        sealed class Sampling : IDisposable
        {
            readonly PlayableGraph graph;
            public readonly AnimationClipPlayable playable;
            public Sampling(Animator animator, AnimationClip clip, bool footIK)
            {
                animator.enabled = true; animator.Rebind();
                graph = PlayableGraph.Create("Down pose adaptation");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(footIK); playable.SetApplyPlayableIK(false);
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                output.SetSourcePlayable(playable); graph.Play();
            }
            public void At(float time) { playable.SetTime(time); playable.SetDone(false); graph.Evaluate(0); }
            public void Dispose() { if (graph.IsValid()) graph.Destroy(); }
        }
        static bool IsArm(string name) => (name.StartsWith("Left ", StringComparison.Ordinal) || name.StartsWith("Right ", StringComparison.Ordinal)) && (name.Contains("Arm") || name.Contains("Forearm") || name.Contains("Hand"));
        static bool IsLimb(string name) => IsArm(name) || ((name.StartsWith("Left ", StringComparison.Ordinal) || name.StartsWith("Right ", StringComparison.Ordinal)) && (name.Contains("Leg") || name.Contains("Foot") || name.Contains("Toes")));
        static bool IsLeftLeg(string name) => name.StartsWith("Left ", StringComparison.Ordinal) && (name.Contains("Leg") || name.Contains("Foot") || name.Contains("Toes"));

        static AnimationClip BakeHuman(Animator animator, AnimationClip source, SkinSampler skin, HumanPoseHandler handler, ClipReport report)
        {
            var output = UnityEngine.Object.Instantiate(source);
            // Keep the imported 240 Hz body motion and corrected limb samples on the same grid.
            // 60 Hz limb keys missed a short contact-height peak between frames in N8.
            output.frameRate = 240;
            int steps = Mathf.Max(1, Mathf.RoundToInt(source.length * 240));
            report.frames = steps + 1;
            string[] names = HumanTrait.MuscleName.Where(IsLimb).ToArray();
            int[] indices = names.Select(n => Array.IndexOf(HumanTrait.MuscleName, n)).ToArray();
            report.changedMuscles = names;
            float conversion = animator.humanScale * animator.transform.lossyScale.y;
            if (conversion <= .00001f) throw new InvalidOperationException("Invalid world human scale.");
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            // First pass bakes source Foot IK. Four replay passes correct the actual muscle-encoded result,
            // because target retargeting can shift body center when arm muscle coordinates change.
            for (int pass = 0; pass < 5; pass++)
            {
                var input = pass == 0 ? source : output;
                var rootY = AnimationUtility.GetEditorCurve(input, Binding("RootT.y"));
                if (rootY == null) throw new InvalidOperationException("No source RootT.y curve.");
                var values = names.Select(n => new float[steps + 1]).ToArray();
                var heights = new float[steps + 1];
                using (var sampling = new Sampling(animator, input, false))
                {
                    for (int frame = 0; frame <= steps; frame++)
                    {
                        float time = source.length * frame / steps;
                        sampling.playable.SetApplyFootIK(false); sampling.At(time);
                        float beforeHipY = hips.position.y;
                        if (pass == 0) { sampling.playable.SetApplyFootIK(true); sampling.At(time); }
                        AdaptPose(animator, skin, report);
                        var pose = new HumanPose(); handler.GetHumanPose(ref pose);
                        for (int m = 0; m < names.Length; m++)
                        {
                            // The right kneeling leg is baked once. Re-encode the corrected left support leg and arms.
                            values[m][frame] = pass == 0 || IsArm(names[m]) || IsLeftLeg(names[m]) ? pose.muscles[indices[m]]
                                : AnimationUtility.GetEditorCurve(input, Binding(names[m])).Evaluate(time);
                            if (float.IsNaN(values[m][frame]) || float.IsInfinity(values[m][frame])) throw new InvalidOperationException("Invalid muscle " + names[m]);
                        }
                        heights[frame] = rootY.Evaluate(time) + (hips.position.y - beforeHipY) / conversion;
                    }
                }
                // Curves are never mutated while their playable is being sampled.
                for (int m = 0; m < names.Length; m++) AnimationUtility.SetEditorCurve(output, Binding(names[m]), Curve(values[m], source.length));
                AnimationUtility.SetEditorCurve(output, Binding("RootT.y"), Curve(heights, source.length));
                report.roundtripPasses = pass + 1;
            }
            SetLoop(output, source.length);
            return output;
        }

        static AnimationClip BakeGeneric(Animator animator, AnimationClip source, SkinSampler skin, Transform[] bones, ClipReport report)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(source.length * 60));
            report.frames = steps + 1; report.changedMuscles = Array.Empty<string>();
            var values = Enumerable.Range(0, bones.Length * 10).Select(_ => new float[steps + 1]).ToArray();
            var previous = new Quaternion[bones.Length];
            using (var sampling = new Sampling(animator, source, true))
            {
                for (int frame = 0; frame <= steps; frame++)
                {
                    sampling.At(source.length * frame / steps);
                    AdaptPose(animator, skin, report);
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
            for (int b = 0; b < bones.Length; b++) for (int k = 0; k < 10; k++)
                AnimationUtility.SetEditorCurve(output, EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[b], animator.transform), typeof(Transform), TransformProperties[k]), Curve(values[b * 10 + k], source.length));
            output.EnsureQuaternionContinuity(); SetLoop(output, source.length);
            return output;
        }

        static void AdaptPose(Animator animator, SkinSampler skin, ClipReport report)
        {
            // This is deliberately a LEG support minimum, never the whole-body minimum.
            float shift = Ground - skin.Minimum(0);
            animator.GetBoneTransform(HumanBodyBones.Hips).position += Vector3.up * shift;
            report.maximumBodyShift = Mathf.Max(report.maximumBodyShift, Mathf.Abs(shift));
            LowerLeftFoot(animator, skin, report);
            RaiseHand(animator, skin, true, report);
            RaiseHand(animator, skin, false, report);
        }
        static void LowerLeftFoot(Animator animator, SkinSampler skin, ClipReport report)
        {
            var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var footRotation = foot.rotation;
            Vector3 pole = lower.position - upper.position;
            Vector3 footBefore = foot.position;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                float gap = skin.Minimum(1) - Ground;
                if (Mathf.Abs(gap) < .0001f) break;
                report.maximumLeftFootCorrection = Mathf.Max(report.maximumLeftFootCorrection, Mathf.Abs(gap));
                Vector3 target = foot.position - Vector3.up * gap;
                float reach = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position) - .00002f;
                float vertical = target.y - upper.position.y;
                if (Mathf.Abs(vertical) >= reach)
                    throw new InvalidOperationException("Left foot ground target is vertically unreachable: " + report.actor + " " + report.spec + " vertical=" + vertical + " reach=" + reach);
                Vector3 horizontal = Vector3.ProjectOnPlane(target - upper.position, Vector3.up);
                float horizontalReach = Mathf.Sqrt(Mathf.Max(0, reach * reach - vertical * vertical));
                if (horizontal.magnitude > horizontalReach)
                {
                    Vector3 shortened = horizontal.normalized * horizontalReach;
                    target.x = upper.position.x + shortened.x;
                    target.z = upper.position.z + shortened.z;
                }
                float unreachable = SolveTwoBone(upper, lower, foot, target, pole, footRotation);
                report.maximumUnreachableDistance = Mathf.Max(report.maximumUnreachableDistance, unreachable);
                report.maximumLeftFootHorizontalAdjustment = Mathf.Max(report.maximumLeftFootHorizontalAdjustment, Vector3.ProjectOnPlane(foot.position - footBefore, Vector3.up).magnitude);
            }
        }
        static void RaiseHand(Animator animator, SkinSampler skin, bool left, ClipReport report)
        {
            var upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var handRotation = hand.rotation;
            Vector3 pole = lower.position - upper.position;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                float lift = Ground - skin.Minimum(left ? 3 : 4);
                if (lift < .0001f) break;
                report.maximumHandLift = Mathf.Max(report.maximumHandLift, lift);
                Vector3 target = hand.position + Vector3.up * lift;
                var beforeUpper = upper.rotation; var beforeLower = lower.rotation;
                float unreachable = SolveTwoBone(upper, lower, hand, target, pole, handRotation);
                float originalPoleMinimum = skin.Minimum(left ? 3 : 4);
                // Keep the source elbow side unless it buries actual forearm/hand skin. Compare the other
                // analytic elbow branch at exactly the same wrist target, without moving the body.
                if (originalPoleMinimum < -.001f)
                {
                    var preferredUpper = upper.rotation; var preferredLower = lower.rotation;
                    upper.rotation = beforeUpper; lower.rotation = beforeLower; hand.rotation = handRotation;
                    SolveTwoBone(upper, lower, hand, target, pole, handRotation, true);
                    float mirrorMinimum = skin.Minimum(left ? 3 : 4);
                    if (mirrorMinimum > originalPoleMinimum + .001f) report.mirroredArmSolutions++;
                    else { upper.rotation = preferredUpper; lower.rotation = preferredLower; hand.rotation = handRotation; }
                }
                report.maximumUnreachableDistance = Mathf.Max(report.maximumUnreachableDistance, unreachable);
            }
        }
        static float SolveTwoBone(Transform upper, Transform lower, Transform hand, Vector3 requested, Vector3 pole, Quaternion handRotation, bool mirror = false)
        {
            Vector3 s = upper.position, e = lower.position, w = hand.position;
            float a = Vector3.Distance(s, e), b = Vector3.Distance(e, w);
            Vector3 offset = requested - s;
            float requestedLength = offset.magnitude;
            if (a < .00001f || b < .00001f || requestedLength < .00001f) throw new InvalidOperationException("Degenerate limb chain.");
            float distance = Mathf.Clamp(requestedLength, Mathf.Abs(a - b) + .00001f, a + b - .00001f);
            Vector3 axis = offset / requestedLength;
            Vector3 bend = pole - axis * Vector3.Dot(pole, axis);
            if (bend.sqrMagnitude < .00000001f) bend = Vector3.Cross(axis, Vector3.up);
            if (bend.sqrMagnitude < .00000001f) bend = Vector3.Cross(axis, Vector3.right);
            if (mirror) bend = -bend;
            float along = (a * a - b * b + distance * distance) / (2 * distance);
            Vector3 elbow = s + axis * along + bend.normalized * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            Vector3 target = s + axis * distance;
            var upperRotation = Quaternion.FromToRotation(e - s, elbow - s) * upper.rotation;
            var lowerRotation = Quaternion.FromToRotation(w - e, target - elbow) * lower.rotation;
            upper.rotation = upperRotation; lower.rotation = lowerRotation; hand.rotation = handRotation;
            return Mathf.Abs(distance - requestedLength);
        }

        static void SetLoop(AnimationClip clip, float duration)
        {
            clip.wrapMode = WrapMode.Loop;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0; settings.stopTime = duration; settings.loopTime = true; settings.loopBlend = false; settings.hasAdditiveReferencePose = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
        static AnimationCurve Curve(float[] values, float duration)
        {
            float dt = duration / (values.Length - 1);
            var keys = new Keyframe[values.Length];
            for (int i = 0; i < values.Length; i++) keys[i] = new Keyframe(dt * i, values[i], i == 0 ? 0 : (values[i] - values[i - 1]) / dt, i == values.Length - 1 ? 0 : (values[i + 1] - values[i]) / dt);
            return new AnimationCurve(keys);
        }

        static void Validate(Animator animator, AnimationClip clip, SkinSampler skin, bool generic, ClipReport report)
        {
            // Test the exact final clip with runtime Foot IK OFF, including between authored keys.
            var test = UnityEngine.Object.Instantiate(clip);
            var settings = AnimationUtility.GetAnimationClipSettings(test); settings.loopTime = false; settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(test, settings); test.wrapMode = WrapMode.ClampForever;
            Sampling sampling = null;
            try
            {
                if (generic) animator.enabled = false;
                else sampling = new Sampling(animator, test, false);
                int steps = Mathf.Max(1, Mathf.CeilToInt(clip.length * (generic ? 120 : 480)));
                Vector3[] first = null;
                var observed = animator.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i <= steps; i++)
                {
                    float time = clip.length * i / steps;
                    if (generic) test.SampleAnimation(animator.gameObject, time); else sampling.At(time);
                    var y = skin.MeasureAll();
                    if (y[5] < report.minimumBodyY) { report.minimumBodyY = y[5]; report.minimumBodyPhase = (float)i / steps; report.minimumBodyVertex = skin.LastBodyMinimum; }
                    if (y[0] < report.minimumSupportY) { report.minimumSupportY = y[0]; report.minimumSupportPhase = (float)i / steps; }
                    if (y[0] > report.maximumSupportY) { report.maximumSupportY = y[0]; report.maximumSupportPhase = (float)i / steps; }
                    report.minimumLeftSupportY = Mathf.Min(report.minimumLeftSupportY, y[1]);
                    if (y[1] > report.maximumLeftSupportY) { report.maximumLeftSupportY = y[1]; report.maximumLeftSupportPhase = (float)i / steps; }
                    report.minimumRightSupportY = Mathf.Min(report.minimumRightSupportY, y[2]); report.maximumRightSupportY = Mathf.Max(report.maximumRightSupportY, y[2]);
                    if (y[3] < report.minimumLeftHandY) { report.minimumLeftHandY = y[3]; report.minimumLeftHandPhase = (float)i / steps; }
                    if (y[4] < report.minimumRightHandY) { report.minimumRightHandY = y[4]; report.minimumRightHandPhase = (float)i / steps; }
                    if (i == 0) first = observed.Select(t => t.position).ToArray();
                    if (i == steps) report.nearSeamMaximumJointDistance = observed.Select((t, index) => Vector3.Distance(t.position, first[index])).Max();
                }
                report.validationSamples = steps + 1;
                // Clothing may cross the plane, but encoded limbs must still support the body and hands must not be buried.
                if (report.minimumSupportY < -.015f || report.maximumSupportY > .03f || report.maximumLeftSupportY > .03f || report.minimumLeftHandY < -.015f || report.minimumRightHandY < -.015f)
                    throw new InvalidOperationException("Target replay needs further adjustment: " + report.actor + " " + report.spec + " support=" + report.minimumSupportY + "@" + report.minimumSupportPhase + ".." + report.maximumSupportY + "@" + report.maximumSupportPhase + " leftSupport=" + report.minimumLeftSupportY + ".." + report.maximumLeftSupportY + "@" + report.maximumLeftSupportPhase + " arms=" + report.minimumLeftHandY + "@" + report.minimumLeftHandPhase + "," + report.minimumRightHandY + "@" + report.minimumRightHandPhase + " bodyBone=" + report.minimumBodyVertex?.dominantBone + "; report=" + FailureReportPath);
            }
            finally { if (sampling != null) sampling.Dispose(); UnityEngine.Object.DestroyImmediate(test); }
        }

        sealed class SkinSampler
        {
            sealed class Skin
            {
                public Transform[] bones; public Matrix4x4[] binds, matrices; public Vector3[] vertices; public BoneWeight[] weights;
                public int[][] groups;
                public string rendererName;
            }
            readonly List<Skin> skins = new List<Skin>();
            public ContactVertex LastBodyMinimum { get; private set; }
            public SkinSampler(GameObject actor, Animator animator)
            {
                var all = animator.GetComponentsInChildren<Transform>(true);
                Func<HumanBodyBones, Transform> find = human =>
                {
                    if (animator.avatar != null && animator.avatar.isHuman) return animator.GetBoneTransform(human);
                    int index = Array.IndexOf(HumanNames, human.ToString());
                    return index >= 0 ? all.FirstOrDefault(t => t.name == GenericNames[index]) : null;
                };
                var leftSupport = new HashSet<Transform>(new[] { find(HumanBodyBones.LeftUpperLeg), find(HumanBodyBones.LeftLowerLeg), find(HumanBodyBones.LeftFoot), find(HumanBodyBones.LeftToes) });
                var rightSupport = new HashSet<Transform>(new[] { find(HumanBodyBones.RightUpperLeg), find(HumanBodyBones.RightLowerLeg), find(HumanBodyBones.RightFoot), find(HumanBodyBones.RightToes) });
                // The independent left contact must be its foot, not a low thigh armor vertex.
                var leftFootSkin = new HashSet<Transform>(new[] { find(HumanBodyBones.LeftFoot), find(HumanBodyBones.LeftToes) });
                var leftHand = find(HumanBodyBones.LeftHand); var rightHand = find(HumanBodyBones.RightHand);
                var leftForearm = find(HumanBodyBones.LeftLowerArm); var rightForearm = find(HumanBodyBones.RightLowerArm);
                if (leftSupport.Contains(null) || rightSupport.Contains(null) || leftHand == null || rightHand == null || leftForearm == null || rightForearm == null) throw new InvalidOperationException("Missing support/hand bone.");
                foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMesh == null) continue;
                    var mesh = renderer.sharedMesh;
                    for (int i = 0; i < mesh.blendShapeCount; i++) if (Mathf.Abs(renderer.GetBlendShapeWeight(i)) > .0001f) throw new InvalidOperationException("CPU skinning requires zero blendshape weights: " + renderer.name);
                    var skin = new Skin { bones = renderer.bones, binds = mesh.bindposes, vertices = mesh.vertices, weights = mesh.boneWeights, rendererName = renderer.name };
                    if (skin.vertices.Length != skin.weights.Length) throw new InvalidOperationException("Unsupported skin weights: " + renderer.name);
                    skin.matrices = new Matrix4x4[skin.bones.Length];
                    var masks = skin.bones.Select(t => new[] { leftSupport.Contains(t) || rightSupport.Contains(t), leftFootSkin.Contains(t), rightSupport.Contains(t), t == leftForearm || t == leftHand || t.IsChildOf(leftHand), t == rightForearm || t == rightHand || t.IsChildOf(rightHand) }).ToArray();
                    var groups = Enumerable.Range(0, 6).Select(_ => new List<int>()).ToArray();
                    for (int v = 0; v < skin.vertices.Length; v++)
                    {
                        var w = skin.weights[v];
                        if (w.weight0 + w.weight1 + w.weight2 + w.weight3 < .5f) continue;
                        groups[5].Add(v);
                        for (int g = 0; g < 5; g++)
                        {
                            float weight = (masks[w.boneIndex0][g] ? w.weight0 : 0) + (masks[w.boneIndex1][g] ? w.weight1 : 0) + (masks[w.boneIndex2][g] ? w.weight2 : 0) + (masks[w.boneIndex3][g] ? w.weight3 : 0);
                            if (weight >= .5f) groups[g].Add(v);
                        }
                    }
                    skin.groups = groups.Select(g => g.ToArray()).ToArray(); skins.Add(skin);
                }
                for (int g = 0; g < 6; g++) if (skins.Sum(s => s.groups[g].Length) == 0) throw new InvalidOperationException("No skin vertices for contact group " + g);
            }
            void Matrices(Skin skin)
            {
                for (int i = 0; i < skin.bones.Length; i++) skin.matrices[i] = skin.bones[i].localToWorldMatrix * skin.binds[i];
            }
            static float VertexY(Skin skin, int index)
            {
                var w = skin.weights[index]; var p = skin.vertices[index];
                return (skin.matrices[w.boneIndex0].MultiplyPoint3x4(p) * w.weight0 + skin.matrices[w.boneIndex1].MultiplyPoint3x4(p) * w.weight1 + skin.matrices[w.boneIndex2].MultiplyPoint3x4(p) * w.weight2 + skin.matrices[w.boneIndex3].MultiplyPoint3x4(p) * w.weight3).y;
            }
            public float Minimum(int group)
            {
                float result = float.PositiveInfinity;
                foreach (var skin in skins) { Matrices(skin); foreach (int v in skin.groups[group]) result = Mathf.Min(result, VertexY(skin, v)); }
                return result;
            }
            public float[] MeasureAll()
            {
                var result = Enumerable.Repeat(float.PositiveInfinity, 6).ToArray();
                LastBodyMinimum = null;
                foreach (var skin in skins)
                {
                    Matrices(skin);
                    for (int g = 0; g < 6; g++) foreach (int v in skin.groups[g])
                    {
                        float y = VertexY(skin, v);
                        if (y >= result[g]) continue;
                        result[g] = y;
                        if (g == 5)
                        {
                            var w = skin.weights[v]; var p = skin.vertices[v];
                            var boneIndices = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                            var boneWeights = new[] { w.weight0, w.weight1, w.weight2, w.weight3 };
                            int dominant = Array.IndexOf(boneWeights, boneWeights.Max());
                            Vector3 world = Vector3.zero;
                            for (int b = 0; b < 4; b++) world += skin.matrices[boneIndices[b]].MultiplyPoint3x4(p) * boneWeights[b];
                            LastBodyMinimum = new ContactVertex { renderer = skin.rendererName, vertex = v, y = y, position = world, dominantBone = skin.bones[boneIndices[dominant]].name, dominantWeight = boneWeights[dominant], influences = string.Join(",", Enumerable.Range(0, 4).Where(b => boneWeights[b] > 0).Select(b => skin.bones[boneIndices[b]].name + ":" + boneWeights[b].ToString("F4", System.Globalization.CultureInfo.InvariantCulture))) };
                        }
                    }
                }
                return result;
            }
        }
    }
}
