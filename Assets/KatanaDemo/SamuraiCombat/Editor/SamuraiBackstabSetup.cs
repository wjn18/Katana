using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>Creates the approved B2 pair without rebuilding existing combat assets or saving a scene.</summary>
    public static class SamuraiBackstabSetup
    {
        public const string Root = "Assets/KatanaDemo/SamuraiCombat";
        public const string AnimationFolder = Root + "/Animations/Backstab";
        public const string PairPath = Root + "/Config/Backstab/Enemy1_BackstabB2.asset";
        public const string AttackPath = AnimationFolder + "/BackstabB2.anim";
        public const string ReceiverPath = AnimationFolder + "/Enemy1_BackstabB2.anim";
        public const string ReceiverPosePath = AnimationFolder + "/Enemy1_BackstabB2Pose.asset";
        public const string AttackState = "BackstabB2";
        public const string ReceiverState = "BackstabB2Receiver";
        public const string SourceFolder = "Assets/ARPGPack/ARPGWarrior/Additional_Animations/Humanoid";
        public const string SourceAttackPath = SourceFolder + "/ARPG_Warrior_Execute_B2.anim";
        public const string SourceReceiverPath = SourceFolder + "/ARPG_Warrior_Executed_B_2.anim";
        public const int ConfigurationVersion = 1;

        [Serializable] public sealed class InstallReport
        {
            public string pair, playerClip, receiverClip, receiverPose, scene;
            public bool sceneDirtyBefore, sceneDirtyAfter, pairCreated, playerProfileInitialized, enemyProfileInitialized;
            public bool enemyCueAdded;
            public int statesAdded;
            public SamuraiDownPoseAdaptation.BackstabBakeReport bake;
        }

        [MenuItem("Katana/Samurai/Enemies/Install B2 Backstab")]
        public static void Install()
        {
            var report = InstallB2();
            Debug.Log("B2 backstab installed. Existing animation, pair tuning and scene edits are preserved; no scene was saved. " + report.pair);
        }

        public static InstallReport InstallB2()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play before installing B2 backstab assets.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var report = new InstallReport { scene = scene.path, sceneDirtyBefore = scene.isDirty, pair = PairPath, playerClip = AttackPath, receiverClip = ReceiverPath, receiverPose = ReceiverPosePath };
            EnsureFolder(AnimationFolder); EnsureFolder(Root + "/Config/Backstab");
            report.bake = SamuraiDownPoseAdaptation.BuildBackstabB2Assets(false);
            var player = Required<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            var enemy = Required<SamuraiCombatProfile>(Root + "/Config/Enemy1/Enemy1.asset");
            var playerController = Required<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            var enemyController = Required<AnimatorController>(Enemy1CombatSetup.ControllerPath);
            var pair = AssetDatabase.LoadAssetAtPath<SamuraiExecutionPair>(PairPath);
            if (pair == null)
            {
                pair = ScriptableObject.CreateInstance<SamuraiExecutionPair>(); pair.name = "Enemy1_BackstabB2";
                pair.isBackstab = true;
                pair.attackerClip = Required<AnimationClip>(AttackPath); pair.attackerState = AttackState;
                pair.receiverClip = pair.receiverLowHealthClip = Required<AnimationClip>(ReceiverPath);
                pair.receiverState = pair.receiverLowHealthState = ReceiverState;
                pair.receiverPoseHealthy = pair.receiverPoseLowHealth = Required<SamuraiExecutionPoseClip>(ReceiverPosePath);
                pair.attackerRootX = report.bake.attackerRootX; pair.attackerRootZ = report.bake.attackerRootZ;
                pair.receiverRootX = report.bake.receiverRootX; pair.receiverRootZ = report.bake.receiverRootZ;
                pair.receiverOffset = new Vector3(.1365f, 0, 1.525f); pair.receiverYaw = -12.5f;
                pair.receiverDelay = .10f; pair.strikeSeconds = .28f; pair.alignSeconds = .18f;
                pair.receiverBlendSeconds = .10f; pair.attackerStartSeconds = pair.receiverStartSeconds = pair.receiverPreparationSeconds = 0;
                pair.receiverHoldDownUntilStrike = false;
                pair.playbackRate = pair.strikePlaybackRate = 1; pair.strikeLeadSeconds = .10f; pair.strikeTailSeconds = .08f;
                pair.impactHoldSeconds = .12f; pair.cameraPushFov = 6; pair.cameraRecoilFov = 2;
                pair.bloodMaterial = Required<Material>(Root + "/Presentation/Execution/ExecutionBlood.mat");
                pair.bloodIntensity = 1.8f; pair.shakeDegrees = 5; pair.shakeSeconds = .16f;
                pair.getupHealthy = pair.getupLowHealth = null;
                pair.getupPoseHealthy = pair.getupPoseLowHealth = pair.corpseRestPose = null;
                pair.presentationVersion = ConfigurationVersion;
                AssetDatabase.CreateAsset(pair, PairPath); report.pairCreated = true;
            }
            if (AddState(playerController, pair.attackerState, pair.attackerClip)) report.statesAdded++;
            if (AddState(enemyController, pair.receiverState, pair.receiverClip)) report.statesAdded++;
            if (pair.receiverLowHealthState != pair.receiverState && AddState(enemyController, pair.receiverLowHealthState, pair.receiverLowHealthClip)) report.statesAdded++;
            if (player.backstabConfigurationVersion < ConfigurationVersion)
            {
                player.backstabEnabled = true; player.backstabConfigurationVersion = ConfigurationVersion;
                EditorUtility.SetDirty(player); AssetDatabase.SaveAssetIfDirty(player); report.playerProfileInitialized = true;
            }
            if (enemy.backstabConfigurationVersion < ConfigurationVersion)
            {
                enemy.backstabPair = pair; enemy.backstabConfigurationVersion = ConfigurationVersion;
                EditorUtility.SetDirty(enemy); AssetDatabase.SaveAssetIfDirty(enemy); report.enemyProfileInitialized = true;
            }
            report.enemyCueAdded = EnsureBackstabCuePrefab();
            report.sceneDirtyAfter = scene.isDirty;
            WriteReport("asset-install-report.json", report);
            return report;
        }

        [MenuItem("Katana/Samurai/Effects/Install Backstab Heart Cue")]
        public static void InstallHeartCue()
        {
            bool added = EnsureBackstabCuePrefab();
            Debug.Log("Backstab heart cue installed. New component added=" + added + "; existing cue tuning and scene edits preserved.");
        }

        public static bool EnsureBackstabCuePrefab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before installing the backstab cue.");
            var material = SamuraiExecutionPresentationSetup.EnsureMaterial();
            var root = PrefabUtility.LoadPrefabContents(Enemy1CombatSetup.PrefabPath);
            try
            {
                var actor = root.GetComponent<SamuraiCombatant>();
                if (actor == null) throw new InvalidOperationException("Enemy1 prefab has no combatant.");
                if (!SamuraiExecutionPresentationSetup.EnsureCue(actor, material, false)) return false;
                actor.GetComponent<SamuraiExecutionCue>().backstabOnly = true;
                PrefabUtility.SaveAsPrefabAsset(root, Enemy1CombatSetup.PrefabPath);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static bool AddState(AnimatorController controller, string name, AnimationClip clip)
        {
            if (string.IsNullOrEmpty(name) || clip == null) return false;
            var machine = controller.layers[0].stateMachine;
            if (machine.states.Any(s => s.state.name == name)) return false;
            var state = machine.AddState(name); state.motion = clip; state.speed = 1; state.iKOnFeet = false; state.writeDefaultValues = true;
            EditorUtility.SetDirty(state); EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
            return true;
        }
        internal static T Required<T>(string path) where T : UnityEngine.Object
        { var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset == null) throw new InvalidOperationException("Missing backstab dependency: " + path); return asset; }
        internal static void EnsureFolder(string path)
        { if (AssetDatabase.IsValidFolder(path)) return; int i = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, i)); AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1)); }
        internal static void WriteReport(string file, object value)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../斗笠剑士/动画资源/backstab-integration-20261109/analysis", file));
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented));
        }
    }

    public static partial class SamuraiDownPoseAdaptation
    {
        [Serializable] public sealed class BackstabBakeReport
        {
            public bool builtAssets, sourceHashesPreserved, sceneDirtyBefore, sceneDirtyAfter;
            public string sourceAttackSha256, sourceReceiverSha256;
            public float attackerDuration, sourceAttackBoundary, playerIdleBlendSeconds, receiverDuration, playerHumanScale, receiverHumanScale;
            public float attackerPrefixMaximumCurveDifference;
            public float playerMinimumY = float.PositiveInfinity, playerMaximumY = float.NegativeInfinity;
            public float receiverMinimumY = float.PositiveInfinity, receiverMaximumY = float.NegativeInfinity;
            public int playerGroundSamples, receiverSamples, receiverTrackCount;
            public AnimationCurve attackerRootX, attackerRootZ, receiverRootX, receiverRootZ;
        }

        sealed class BackstabRig
        {
            public GameObject gameObject; public Animator animator; public SkinnedMeshRenderer[] skins; public Mesh[] meshes;
            public float Minimum()
            {
                float result = float.PositiveInfinity;
                for (int i = 0; i < skins.Length; i++)
                {
                    skins[i].BakeMesh(meshes[i]);
                    foreach (var v in meshes[i].vertices) result = Mathf.Min(result, skins[i].transform.TransformPoint(v).y);
                }
                return result;
            }
        }

        public static BackstabBakeReport BuildBackstabB2Assets(bool rebuild)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play before baking B2 backstab assets.");
            string[] destinations = { SamuraiBackstabSetup.AttackPath, SamuraiBackstabSetup.ReceiverPath, SamuraiBackstabSetup.ReceiverPosePath };
            var report = new BackstabBakeReport();
            var originalScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene(); report.sceneDirtyBefore = originalScene.isDirty;
            var attackSource = SamuraiBackstabSetup.Required<AnimationClip>(SamuraiBackstabSetup.SourceAttackPath);
            var receiverSource = SamuraiBackstabSetup.Required<AnimationClip>(SamuraiBackstabSetup.SourceReceiverPath);
            report.sourceAttackSha256 = BSHash(SamuraiBackstabSetup.SourceAttackPath); report.sourceReceiverSha256 = BSHash(SamuraiBackstabSetup.SourceReceiverPath);
            report.sourceAttackBoundary = attackSource.length; report.playerIdleBlendSeconds = .22f;
            report.attackerDuration = Mathf.Max(attackSource.length + .22f, receiverSource.length + .10f); report.receiverDuration = receiverSource.length;
            var preview = EditorSceneManager.NewPreviewScene(); var temporary = new List<UnityEngine.Object>();
            try
            {
                var player = BSCreateRig("Assets/KatanaDemo/SamuraiCombat/Prefabs/SamuraiCombatPlayer.prefab", preview, temporary);
                var enemy = BSCreateRig("Assets/KatanaDemo/Characters/Enemy1/Prefabs/Enemy1.prefab", preview, temporary);
                Transform[] receiverBones; var avatar = BuildTemporaryEnemyAvatar(enemy.animator, out receiverBones); temporary.Add(avatar); enemy.animator.avatar = avatar;
                player.animator.enabled = enemy.animator.enabled = true; player.animator.Rebind(); enemy.animator.Rebind();
                if (!player.animator.isHuman || !enemy.animator.isHuman || !avatar.isValid) throw new InvalidOperationException("B2 target avatars are not valid.");
                report.playerHumanScale = player.animator.humanScale * player.animator.transform.lossyScale.y;
                report.receiverHumanScale = enemy.animator.humanScale * enemy.animator.transform.lossyScale.y;
                report.attackerRootX = BSHoldRoot(BSRoot(attackSource, "x", report.playerHumanScale), report.attackerDuration); report.attackerRootZ = BSHoldRoot(BSRoot(attackSource, "z", report.playerHumanScale), report.attackerDuration);
                report.receiverRootX = BSRoot(receiverSource, "x", report.receiverHumanScale); report.receiverRootZ = BSRoot(receiverSource, "z", report.receiverHumanScale);
                bool needAttack = rebuild || AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiBackstabSetup.AttackPath) == null;
                bool needReceiver = rebuild || AssetDatabase.LoadAssetAtPath<AnimationClip>(SamuraiBackstabSetup.ReceiverPath) == null;
                bool needPose = rebuild || AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(SamuraiBackstabSetup.ReceiverPosePath) == null;
                if (needAttack)
                {
                    var clip = PrepareSource(attackSource); temporary.Add(clip); clip.name = "BackstabB2"; clip.frameRate = 120;
                    var prefix = UnityEngine.Object.Instantiate(clip); temporary.Add(prefix);
                    var idle = player.gameObject.GetComponent<SamuraiCombatant>().Profile.idle;
                    if (idle == null) throw new InvalidOperationException("Current player idle is required for the approved B2 return tail.");
                    BSAppendIdleTail(clip, idle, report.sourceAttackBoundary, report.playerIdleBlendSeconds, report.attackerDuration);
                    int steps = Mathf.CeilToInt(clip.length * 120);
                    // Keep the approved Humanoid rotations; calibrate only body height on the actual player rig.
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var sourceY = AnimationUtility.GetEditorCurve(clip, Binding("RootT.y")); var heights = new float[steps + 1];
                        using (var sampling = new Sampling(player.animator, clip, false))
                        for (int f = 0; f <= steps; f++)
                        {
                            float t = clip.length * f / steps; sampling.At(t); heights[f] = sourceY.Evaluate(t) + (.003f - player.Minimum()) / report.playerHumanScale;
                        }
                        AnimationUtility.SetEditorCurve(clip, Binding("RootT.y"), BSLinear(heights, clip.length));
                    }
                    using (var sampling = new Sampling(player.animator, clip, false))
                    for (int f = 0; f <= steps * 2; f++)
                    { sampling.At(clip.length * f / (steps * 2)); float y = player.Minimum(); report.playerMinimumY = Mathf.Min(report.playerMinimumY, y); report.playerMaximumY = Mathf.Max(report.playerMaximumY, y); report.playerGroundSamples++; }
                    report.attackerPrefixMaximumCurveDifference = BSPrefixDifference(prefix, clip, report.sourceAttackBoundary);
                    if (report.attackerPrefixMaximumCurveDifference > .000001f) throw new InvalidOperationException("Approved B2 attack rotation or muscle prefix changed while appending idle.");
                    BSStoreClip(clip, SamuraiBackstabSetup.AttackPath); report.builtAssets = true;
                }
                if (needReceiver || needPose)
                {
                    var source = PrepareSource(receiverSource); temporary.Add(source);
                    int steps = Mathf.CeilToInt(receiverSource.length * 120);
                    var values = Enumerable.Range(0, receiverBones.Length * 10).Select(_ => new float[steps + 1]).ToArray();
                    var previous = new Quaternion[receiverBones.Length]; var hips = enemy.animator.GetBoneTransform(HumanBodyBones.Hips);
                    using (var sampling = new Sampling(enemy.animator, source, false))
                    for (int f = 0; f <= steps; f++)
                    {
                        float t = receiverSource.length * f / steps; sampling.At(t);
                        // The dead receiver's logical anchor stays fixed; all authored travel is inside its visual hips tree.
                        hips.position += new Vector3(report.receiverRootX.Evaluate(t), .003f - enemy.Minimum(), report.receiverRootZ.Evaluate(t));
                        float y = enemy.Minimum(); report.receiverMinimumY = Mathf.Min(report.receiverMinimumY, y); report.receiverMaximumY = Mathf.Max(report.receiverMaximumY, y); report.receiverSamples++;
                        for (int b = 0; b < receiverBones.Length; b++)
                        {
                            var bone = receiverBones[b]; var p = bone.localPosition; var q = bone.localRotation; var s = bone.localScale;
                            if (f > 0 && Quaternion.Dot(previous[b], q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); previous[b] = q;
                            float[] row = { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
                            for (int k = 0; k < 10; k++) values[b * 10 + k][f] = row[k];
                        }
                    }
                    var clip = new AnimationClip { name = "Enemy1_BackstabB2", frameRate = 120, legacy = false, wrapMode = WrapMode.ClampForever }; temporary.Add(clip);
                    var pose = ScriptableObject.CreateInstance<SamuraiExecutionPoseClip>(); temporary.Add(pose); pose.name = "Enemy1_BackstabB2Pose";
                    pose.duration = receiverSource.length; pose.referenceVisualScale = enemy.animator.transform.lossyScale.y; pose.groundContactY = .003f;
                    pose.groundLift = AnimationCurve.Constant(0, receiverSource.length, 0);
                    pose.tracks = new SamuraiExecutionPoseClip.Track[receiverBones.Length];
                    for (int b = 0; b < receiverBones.Length; b++)
                    {
                        string path = AnimationUtility.CalculateTransformPath(receiverBones[b], enemy.animator.transform);
                        var track = new SamuraiExecutionPoseClip.Track { path = path, curves = new AnimationCurve[10] };
                        for (int k = 0; k < 10; k++) { track.curves[k] = BSLinear(values[b * 10 + k], receiverSource.length); AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), TransformProperties[k]), track.curves[k]); }
                        pose.tracks[b] = track;
                    }
                    clip.EnsureQuaternionContinuity(); var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = settings.loopBlend = false; settings.stopTime = receiverSource.length; AnimationUtility.SetAnimationClipSettings(clip, settings);
                    report.receiverTrackCount = pose.tracks.Length;
                    if (needReceiver) BSStoreClip(clip, SamuraiBackstabSetup.ReceiverPath);
                    if (needPose) BSStorePose(pose, SamuraiBackstabSetup.ReceiverPosePath);
                    report.builtAssets = true;
                }
                report.sourceHashesPreserved = report.sourceAttackSha256 == BSHash(SamuraiBackstabSetup.SourceAttackPath) && report.sourceReceiverSha256 == BSHash(SamuraiBackstabSetup.SourceReceiverPath);
                if (!report.sourceHashesPreserved) throw new InvalidOperationException("A protected ARPG source clip changed while baking B2.");
            }
            finally
            {
                foreach (var obj in temporary.Where(o => o != null).AsEnumerable().Reverse()) UnityEngine.Object.DestroyImmediate(obj);
                EditorSceneManager.ClosePreviewScene(preview);
            }
            if (report.playerGroundSamples == 0) report.playerMinimumY = report.playerMaximumY = 0;
            if (report.receiverSamples == 0) report.receiverMinimumY = report.receiverMaximumY = 0;
            report.sceneDirtyAfter = originalScene.isDirty; SamuraiBackstabSetup.WriteReport("asset-bake-report.json", report); return report;
        }

        static BackstabRig BSCreateRig(string path, UnityEngine.SceneManagement.Scene preview, List<UnityEngine.Object> temporary)
        {
            var asset = SamuraiBackstabSetup.Required<GameObject>(path); var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview); temporary.Add(go);
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var script in go.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var animator = go.GetComponentInChildren<Animator>(true); animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.gameObject.activeInHierarchy && s.sharedMesh != null && !s.name.Contains("Comic ink")).ToArray();
            var meshes = skins.Select(_ => new Mesh()).ToArray(); temporary.AddRange(meshes);
            return new BackstabRig { gameObject = go, animator = animator, skins = skins, meshes = meshes };
        }
        static AnimationCurve BSRoot(AnimationClip clip, string axis, float conversion)
        {
            var source = AnimationUtility.GetEditorCurve(clip, Binding("RootT." + axis)); if (source == null) throw new InvalidOperationException("Missing ARPG root " + axis);
            int steps = Mathf.CeilToInt(clip.length * 120); var values = new float[steps + 1]; float first = source.Evaluate(0);
            for (int f = 0; f <= steps; f++) values[f] = (source.Evaluate(clip.length * f / steps) - first) * conversion;
            return BSLinear(values, clip.length);
        }
        static AnimationCurve BSHoldRoot(AnimationCurve source, float duration)
        {
            var keys = source.keys.ToList(); var last = keys[keys.Count - 1]; last.outTangent = 0; keys[keys.Count - 1] = last;
            if (duration > last.time) keys.Add(new Keyframe(duration, last.value, 0, 0));
            return new AnimationCurve(keys.ToArray()) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        }
        static void BSAppendIdleTail(AnimationClip clip, AnimationClip idle, float boundary, float blendSeconds, float duration)
        {
            var attackBindings = AnimationUtility.GetCurveBindings(clip).Where(b => b.path == "" && b.type == typeof(Animator)).ToArray();
            var idleBindings = AnimationUtility.GetCurveBindings(idle).Where(b => b.path == "" && b.type == typeof(Animator)).ToArray();
            var attack = attackBindings.ToDictionary(b => b.propertyName, b => AnimationUtility.GetEditorCurve(clip, b));
            var target = idleBindings.ToDictionary(b => b.propertyName, b => AnimationUtility.GetEditorCurve(idle, b));
            var names = attack.Keys.Concat(target.Keys).Distinct().OrderBy(n => n).ToArray();
            int steps = Mathf.CeilToInt((duration - boundary) * 120);
            Func<Dictionary<string, AnimationCurve>, string, float, float> at = (curves, name, time) => curves.ContainsKey(name) ? curves[name].Evaluate(time) : 0;
            Func<Dictionary<string, AnimationCurve>, string, float, Quaternion> quaternion = (curves, prefix, time) =>
            {
                var q = new Quaternion(at(curves, prefix + "x", time), at(curves, prefix + "y", time), at(curves, prefix + "z", time), at(curves, prefix + "w", time));
                return q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > .000001f ? q.normalized : Quaternion.identity;
            };
            foreach (string name in names)
            {
                bool zeroRoot = name == "RootT.x" || name == "RootT.z";
                var original = attack.ContainsKey(name) ? attack[name] : AnimationCurve.Constant(0, boundary, 0);
                var keys = original.keys.Where(k => k.time <= boundary).ToList();
                if (keys.Count == 0 || Mathf.Abs(keys[keys.Count - 1].time - boundary) > .000001f) keys.Add(new Keyframe(boundary, original.Evaluate(boundary), 0, 0));
                if (zeroRoot)
                {
                    AnimationUtility.SetEditorCurve(clip, Binding(name), AnimationCurve.Constant(0, duration, 0)); continue;
                }
                int dot = name.LastIndexOf('.'); bool isQuaternion = dot > 0 && name[dot - 1] == 'Q' && name.Length == dot + 2 && "xyzw".IndexOf(name[dot + 1]) >= 0;
                string quaternionPrefix = isQuaternion ? name.Substring(0, dot + 1) : null;
                Quaternion previous = isQuaternion ? quaternion(attack, quaternionPrefix, boundary) : Quaternion.identity;
                for (int f = 1; f <= steps; f++)
                {
                    float elapsed = (duration - boundary) * f / steps, time = boundary + elapsed;
                    float idleTime = idle.length > .00001f ? Mathf.Repeat(elapsed, idle.length) : 0;
                    float weight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / blendSeconds)); float value;
                    if (isQuaternion)
                    {
                        var q = Quaternion.Slerp(quaternion(attack, quaternionPrefix, boundary), quaternion(target, quaternionPrefix, idleTime), weight);
                        if (Quaternion.Dot(previous, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); previous = q;
                        value = name[dot + 1] == 'x' ? q.x : name[dot + 1] == 'y' ? q.y : name[dot + 1] == 'z' ? q.z : q.w;
                    }
                    else value = Mathf.Lerp(original.Evaluate(boundary), at(target, name, idleTime), weight);
                    keys.Add(new Keyframe(time, value, 0, 0));
                }
                // Only new outgoing segments use sampled linear tangents; prefix incoming tangents remain intact.
                for (int i = 0; i < keys.Count; i++)
                {
                    var key = keys[i]; if (key.time < boundary - .000001f) continue;
                    if (i > 0 && key.time > boundary + .000001f) key.inTangent = (key.value - keys[i - 1].value) / Mathf.Max(.000001f, key.time - keys[i - 1].time);
                    if (i + 1 < keys.Count) key.outTangent = (keys[i + 1].value - key.value) / Mathf.Max(.000001f, keys[i + 1].time - key.time); else key.outTangent = key.inTangent;
                    keys[i] = key;
                }
                AnimationUtility.SetEditorCurve(clip, Binding(name), new AnimationCurve(keys.ToArray()) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever });
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = settings.loopBlend = false; settings.stopTime = duration; AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
        static float BSPrefixDifference(AnimationClip source, AnimationClip output, float duration)
        {
            float max = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (binding.propertyName == "RootT.y" || binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z") continue;
                var a = AnimationUtility.GetEditorCurve(source, binding); var b = AnimationUtility.GetEditorCurve(output, binding);
                if (b == null) return float.PositiveInfinity;
                int count = Mathf.CeilToInt(duration * 240);
                for (int f = 0; f <= count; f++) max = Mathf.Max(max, Mathf.Abs(a.Evaluate(duration * f / count) - b.Evaluate(duration * f / count)));
            }
            return max;
        }
        static AnimationCurve BSLinear(float[] values, float duration)
        {
            var keys = new Keyframe[values.Length]; float step = duration / Mathf.Max(1, values.Length - 1);
            for (int i = 0; i < keys.Length; i++)
            { float incoming = i == 0 ? (values.Length > 1 ? (values[1] - values[0]) / step : 0) : (values[i] - values[i - 1]) / step; float outgoing = i == keys.Length - 1 ? incoming : (values[i + 1] - values[i]) / step; keys[i] = new Keyframe(step * i, values[i], incoming, outgoing); }
            return new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        }
        static void BSStoreClip(AnimationClip clip, string path)
        {
            var current = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (current == null) { current = UnityEngine.Object.Instantiate(clip); current.hideFlags = HideFlags.None; AssetDatabase.CreateAsset(current, path); }
            else EditorUtility.CopySerialized(clip, current);
            EditorUtility.SetDirty(current); AssetDatabase.SaveAssetIfDirty(current);
        }
        static void BSStorePose(SamuraiExecutionPoseClip pose, string path)
        {
            var current = AssetDatabase.LoadAssetAtPath<SamuraiExecutionPoseClip>(path);
            if (current == null) { current = UnityEngine.Object.Instantiate(pose); current.hideFlags = HideFlags.None; AssetDatabase.CreateAsset(current, path); }
            else EditorUtility.CopySerialized(pose, current);
            EditorUtility.SetDirty(current); AssetDatabase.SaveAssetIfDirty(current);
        }
        static string BSHash(string assetPath)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath))))).Replace("-", "").ToLowerInvariant(); }
    }
}
