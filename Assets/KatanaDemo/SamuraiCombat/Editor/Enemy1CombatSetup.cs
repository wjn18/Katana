using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>Enemy1专用动画烘焙与战斗装配。源模型和原Idle只读，定向保存。</summary>
    public static class Enemy1CombatSetup
    {
        public const string PrefabPath = "Assets/KatanaDemo/Characters/Enemy1/Prefabs/Enemy1.prefab";
        public const string ClipFolder = "Assets/KatanaDemo/Characters/Enemy1/Animations/Generic/Combat";
        public const string ControllerPath = ClipFolder + "/Enemy1Combat.controller";
        public const string ConfigFolder = "Assets/KatanaDemo/SamuraiCombat/Config/Enemy1";
        public const string ProfilePath = ConfigFolder + "/Enemy1.asset";
        const string SourceFolder = "Assets/ARPGPack/ARPGWarrior/Animations/Humanoid/ARPG_Warrior_";
        const string ScenePath = "Assets/KatanaDemo/SamuraiCombat/Scenes/SamuraiCombatDemo.unity";
        static readonly string[] HumanNames = { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes" };
        static readonly string[] BoneNames = { "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand", "RightShoulder", "RightArm", "RightForeArm", "RightHand", "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase", "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase" };
        static readonly string[] WalkStates = { "Walk", "WalkForwardRight", "WalkRight", "WalkBackwardRight", "WalkBackward", "WalkBackwardLeft", "WalkLeft", "WalkForwardLeft" };
        static readonly string[] RunStates = { "Run", "RunForwardRight", "RunRight", "RunBackwardRight", "RunBackward", "RunBackwardLeft", "RunLeft", "RunForwardLeft" };
        static readonly string[] Directions = { "Forward", "Forward_Right", "Rightward", "Backward_Right", "Backward", "Backward_Left", "Leftward", "Forward_Left" };
        static readonly string[] Properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w", "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };

        [Serializable] public sealed class ClipReport
        {
            public string state, path, source;
            public float duration, minBodyY, maxBodyY, maximumGroundLift, interpolationGroundLift;
            public int frames, curves;
            public bool loop, adaptedGuard;
        }
        [Serializable] public sealed class InstallReport
        {
            public string date = "2026-10-26";
            public float tPoseError, visualOffset;
            public bool validTPose, avatarValid;
            public int mappedBones, bakedTransforms, sceneEnemies;
            public List<ClipReport> clips = new List<ClipReport>();
        }
        sealed class Spec
        {
            public string state, source;
            public bool loop, guard;
            public float holdAt = -1;
            public Spec(string state, string source, bool loop = false, bool guard = false, float holdAt = -1)
            { this.state = state; this.source = source; this.loop = loop; this.guard = guard; this.holdAt = holdAt; }
        }

        [MenuItem("Katana/Samurai/Enemies/Install Enemy1 Combat In Current Scene")]
        public static void InstallInCurrentScene()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play before installing Enemy1.");
            if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open SamuraiCombatDemo first.");
            EnsureFolder(ClipFolder); EnsureFolder(ConfigFolder);
            var report = new InstallReport();
            var clips = BakeAnimations(report);
            var controller = BuildController(clips);
            var profile = BuildProfile(clips);
            ConfigurePrefab(profile, controller, report.visualOffset);
            EnemyAttackRootMotionSetup.InstallEnemy1();
            Enemy1CloseRangeSetup.InstallAll();
            SamuraiEnemyReadabilitySetup.GroundDeath("Enemy1", true);
            report.sceneEnemies = InstallSceneInstances();
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../锈甲杂兵/动画资源/enemy1-combat/analysis"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "接入构建.json"), JsonUtility.ToJson(report, true));
            Debug.Log("Enemy1 combat installed: " + clips.Count + " clips, " + report.sceneEnemies + " scene enemies.");
        }

        static Dictionary<string, AnimationClip> BakeAnimations(InstallReport report)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject clone = null, genericClone = null; Avatar avatar = null;
            var graph = default(PlayableGraph);
            var result = new Dictionary<string, AnimationClip>();
            try
            {
                clone = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                SceneManager.MoveGameObjectToScene(clone, scene);
                if (PrefabUtility.IsPartOfPrefabInstance(clone)) PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = clone.GetComponentInChildren<Animator>();
                var visual = animator.gameObject;
                visual.transform.localPosition = Vector3.zero;
                var skin = clone.GetComponentInChildren<SkinnedMeshRenderer>();
                animator.runtimeAnimatorController = null; animator.enabled = false;
                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var setup = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool");
                if (setup == null) throw new InvalidOperationException("Unity Avatar calibration tool unavailable.");
                setup.GetMethod("SampleBindPose", flags).Invoke(null, new object[] { visual });
                var mapping = new Dictionary<string, string>();
                for (int i = 0; i < HumanNames.Length; i++) mapping[HumanNames[i]] = BoneNames[i];
                var all = visual.GetComponentsInChildren<Transform>();
                var selected = new HashSet<Transform>(skin.bones);
                selected.Add(visual.transform);
                foreach (var t in all) if (t.name == "Armature") selected.Add(t);
                var actual = selected.ToDictionary(t => t, t => true);
                var method = setup.GetMethods(flags).First(m => m.Name == "GetHumanBones" && m.GetParameters()[0].ParameterType == mapping.GetType());
                var wrappers = method.Invoke(null, new object[] { mapping, actual });
                setup.GetMethod("MakePoseValid", flags).Invoke(null, new[] { wrappers });
                report.tPoseError = (float)setup.GetMethod("GetPoseError", flags).Invoke(null, new[] { wrappers });
                report.validTPose = (bool)setup.GetMethod("IsPoseValid", flags).Invoke(null, new[] { wrappers });
                if (!report.validTPose || report.tPoseError > .01f) throw new InvalidOperationException("Enemy1 reference pose invalid.");
                var human = new HumanBone[HumanNames.Length];
                for (int i = 0; i < human.Length; i++) human[i] = new HumanBone { humanName = HumanNames[i], boneName = BoneNames[i], limit = new HumanLimit { useDefaultValues = true } };
                var skeleton = all.Where(selected.Contains).Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
                avatar = AvatarBuilder.BuildHumanAvatar(visual, new HumanDescription { human = human, skeleton = skeleton, upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f, armStretch = .05f, legStretch = .05f });
                report.avatarValid = avatar.isValid && avatar.isHuman; report.mappedBones = human.Length;
                if (!report.avatarValid) throw new InvalidOperationException("Enemy1 avatar invalid.");
                var bones = all.Where(t => selected.Contains(t) && t != visual.transform).ToArray();
                report.bakedTransforms = bones.Length;
                animator.avatar = avatar; animator.enabled = true; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
                // Read the editor mesh once. CPU skinning avoids same-frame renderer matrix caches.
                var vertices = skin.sharedMesh.vertices; var weights = skin.sharedMesh.boneWeights; var bindposes = skin.sharedMesh.bindposes;
                var footBones = skin.bones.Select(t => t.name.Contains("Foot") || t.name.Contains("Toe")).ToArray();
                // Generic playback must be checked on a separate rig with no Humanoid Avatar.
                genericClone = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                SceneManager.MoveGameObjectToScene(genericClone, scene); genericClone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var genericAnimator = genericClone.GetComponentInChildren<Animator>(); genericAnimator.runtimeAnimatorController = null; genericAnimator.avatar = null; genericAnimator.enabled = false; genericAnimator.transform.localPosition = Vector3.zero;
                var genericSkin = genericClone.GetComponentInChildren<SkinnedMeshRenderer>();
                var specs = new List<Spec> { new Spec("Idle", "Idle1", true) };
                for (int i = 0; i < 8; i++) specs.Add(new Spec(WalkStates[i], "Walk_" + Directions[i] + "_IPC", true));
                for (int i = 0; i < 8; i++) specs.Add(new Spec(RunStates[i], "Run_" + Directions[i] + "_IPC", true));
                specs.AddRange(new[] { new Spec("Light01", "Attack_Combo1"), new Spec("Light02", "Attack_Combo2"), new Spec("Heavy", "Attack_Heavy2"), new Spec("Guard", "Guard", true, true), new Spec("GuardHit", "Guard_Hit1", false, true), new Spec("GuardHit2", "Guard_Hit2", false, true), new Spec("GuardHit3", "Guard_Hit3", false, true), new Spec("GuardBreak", "Guard_Break"), new Spec("Hit", "Hit2"), new Spec("Death", "Death"), new Spec("Deflected", "Collide"), new Spec("Charge", "Attack_Heavy2", true, false, .12f) });
                var paths = bones.Select(t => AnimationUtility.CalculateTransformPath(t, visual.transform)).ToArray();
                foreach (var spec in specs)
                {
                    var sourcePath = SourceFolder + spec.source + ".anim";
                    var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath);
                    if (source == null || !source.isHumanMotion) throw new InvalidOperationException("Missing source " + sourcePath);
                    graph = PlayableGraph.Create("Enemy1 bake " + spec.state); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, source); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                    var output = AnimationPlayableOutput.Create(graph, "Pose", animator); output.SetSourcePlayable(playable); graph.Play();
                    float duration = spec.holdAt >= 0 ? .5f : source.length;
                    int steps = Mathf.Max(1, Mathf.RoundToInt(duration * 60));
                    var samples = new float[bones.Length * 10][];
                    for (int i = 0; i < samples.Length; i++) samples[i] = new float[steps + 1];
                    var previous = new Quaternion[bones.Length];
                    float minBody = float.PositiveInfinity, maxBody = float.NegativeInfinity, maximumLift = 0;
                    for (int frame = 0; frame <= steps; frame++)
                    {
                        playable.SetTime(spec.holdAt >= 0 ? source.length * spec.holdAt : (double)duration * frame / steps); graph.Evaluate(0);
                        if (spec.guard) AdaptEmptyShieldArm(animator);
                        var minimum = SkinMinimum(skin.bones, bindposes, vertices, weights, footBones);
                        float lift = Mathf.Max(0, -(spec.state == "Death" ? minimum.x : minimum.y));
                        animator.GetBoneTransform(HumanBodyBones.Hips).position += Vector3.up * lift;
                        maximumLift = Mathf.Max(maximumLift, lift);
                        for (int i = 0; i < bones.Length; i++)
                        {
                            var p = bones[i].localPosition; var q = bones[i].localRotation; var scale = bones[i].localScale;
                            if (frame > 0 && Quaternion.Dot(previous[i], q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                            previous[i] = q;
                            var values = new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, scale.x, scale.y, scale.z };
                            for (int k = 0; k < 10; k++) samples[i * 10 + k][frame] = values[k];
                        }
                        minBody = Mathf.Min(minBody, minimum.x + lift); maxBody = Mathf.Max(maxBody, minimum.x + lift);
                    }
                    var clip = new AnimationClip { name = "Enemy1_" + spec.state, frameRate = 60, legacy = false };
                    for (int i = 0; i < bones.Length; i++) for (int k = 0; k < 10; k++)
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(paths[i], typeof(Transform), Properties[k]), Curve(samples[i * 10 + k], duration));
                    clip.EnsureQuaternionContinuity();
                    var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.startTime = 0; settings.stopTime = duration; settings.loopTime = spec.loop; settings.loopBlend = false; settings.hasAdditiveReferencePose = false;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    graph.Destroy();
                    float interpolationLift = CorrectGenericGround(clip, genericAnimator.gameObject, genericSkin, bindposes, vertices, weights, footBones, spec.state == "Death");
                    string path = ClipFolder + "/" + clip.name + ".anim";
                    var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (existing == null) { AssetDatabase.CreateAsset(clip, path); existing = clip; }
                    else { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); }
                    EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); result.Add(spec.state, existing);
                    report.clips.Add(new ClipReport { state = spec.state, source = sourcePath, path = path, duration = duration, frames = steps + 1, curves = bones.Length * 10, minBodyY = minBody, maxBodyY = maxBody, maximumGroundLift = maximumLift, interpolationGroundLift = interpolationLift, loop = spec.loop, adaptedGuard = spec.guard });
                    report.visualOffset = 0;
                }
                return result;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                if (genericClone != null) UnityEngine.Object.DestroyImmediate(genericClone);
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static AnimationCurve Curve(float[] values, float duration)
        {
            float min = values.Min(), max = values.Max();
            if (max - min < .000001f) return new AnimationCurve(new Keyframe(0, values[0]), new Keyframe(duration, values[0]));
            var keys = new Keyframe[values.Length]; float dt = duration / (values.Length - 1);
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(dt * i, values[i], i > 0 ? (values[i] - values[i - 1]) / dt : 0, i + 1 < keys.Length ? (values[i + 1] - values[i]) / dt : 0);
            return new AnimationCurve(keys);
        }
        static Vector2 SkinMinimum(Transform[] bones, Matrix4x4[] bindposes, Vector3[] vertices, BoneWeight[] weights, bool[] footBones)
        {
            var matrices = new Matrix4x4[bones.Length];
            for (int i = 0; i < matrices.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            float all = float.PositiveInfinity, feet = float.PositiveInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                var w = weights[i]; var v = vertices[i];
                float y = (matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3).y;
                all = Mathf.Min(all, y);
                if (footBones[w.boneIndex0]) feet = Mathf.Min(feet, y);
            }
            return new Vector2(all, feet);
        }
        static float CorrectGenericGround(AnimationClip clip, GameObject visual, SkinnedMeshRenderer skin, Matrix4x4[] binds, Vector3[] vertices, BoneWeight[] weights, bool[] feet, bool fullBody)
        {
            // Only the hip height receives dense keys. The action timing and all other channels remain at 60 Hz.
            var hip = skin.bones.First(b => b.name == "Hips");
            var binding = EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(hip, visual.transform), typeof(Transform), "m_LocalPosition.y");
            var original = AnimationUtility.GetEditorCurve(clip, binding);
            int steps = Mathf.Max(1, Mathf.RoundToInt(clip.length * 240)); var samples = new float[steps + 1]; float maximum = 0;
            var bones = skin.bones;
            for (int i = 0; i <= steps; i++)
            {
                float time = clip.length * i / steps; clip.SampleAnimation(visual, time);
                var minimum = SkinMinimum(bones, binds, vertices, weights, feet);
                float lift = Mathf.Max(0, .001f - (fullBody ? minimum.x : minimum.y));
                samples[i] = original.Evaluate(time) + lift; maximum = Mathf.Max(maximum, lift);
            }
            AnimationUtility.SetEditorCurve(clip, binding, Curve(samples, clip.length));
            return maximum;
        }
        static void AdaptEmptyShieldArm(Animator animator)
        {
            var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm); var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand); var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            Vector3 target = chest.position + animator.transform.TransformVector(new Vector3(-.15f, .02f, .20f));
            Vector3 start = upper.position, delta = target - start; float a = Vector3.Distance(start, lower.position), b = Vector3.Distance(lower.position, hand.position);
            float length = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f); Vector3 direction = delta.normalized;
            var pole = animator.transform.TransformDirection(new Vector3(-1, -.3f, -.1f)); var side = Vector3.ProjectOnPlane(pole, direction).normalized;
            float along = (a * a + length * length - b * b) / (2 * length); float height = Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            Vector3 elbow = start + direction * along + side * height;
            upper.rotation = Quaternion.FromToRotation(lower.position - start, elbow - start) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, start + direction * length - lower.position) * lower.rotation;
        }
        static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var pair in clips) State(machine, pair.Key, pair.Value);
            State(machine, "Guard_B", clips["Guard"]);
            for (int i = 1; i <= 3; i++) State(machine, "GuardBHit" + i, clips[i == 1 ? "GuardHit" : "GuardHit" + i]);
            State(machine, "Parry", clips["GuardHit"]);
            machine.defaultState = machine.states.First(s => s.state.name == "Idle").state;
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller); return controller;
        }
        static void State(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            var state = machine.states.FirstOrDefault(s => s.state.name == name).state;
            if (state == null) state = machine.AddState(name);
            state.motion = clip; state.speed = 1; state.writeDefaultValues = true;
        }
        static SamuraiCombatProfile BuildProfile(Dictionary<string, AnimationClip> clips)
        {
            var p = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(ProfilePath);
            if (p == null) { p = ScriptableObject.CreateInstance<SamuraiCombatProfile>(); AssetDatabase.CreateAsset(p, ProfilePath); }
            p.idle = clips["Idle"]; p.walk = clips["Walk"]; p.run = clips["Run"]; p.guard = p.guardB = clips["Guard"];
            p.guardHit = clips["GuardHit"]; p.guardHitReactions = new[] { clips["GuardHit"], clips["GuardHit2"], clips["GuardHit3"] }; p.guardBHitReactions = (AnimationClip[])p.guardHitReactions.Clone();
            p.guardBreak = clips["GuardBreak"]; p.hit = clips["Hit"]; p.death = clips["Death"]; p.charge = clips["Charge"]; p.parry = clips["GuardHit"]; p.deflected = clips["Deflected"];
            p.directionalHitReactionsEnabled = false; p.disableCollisionOnDeath = true;
            p.directionalWalk = WalkStates.Select(n => clips[n]).ToArray(); p.directionalRun = RunStates.Select(n => clips[n]).ToArray();
            var first = Action("Light01", clips["Light01"], 18, 23, 12, .29f, .60f, .20f);
            var second = Action("Light02", clips["Light02"], 21, 27, 14, .31f, .63f, .20f);
            p.combo = new[] { first, second, first };
            p.heavy = Action("Heavy", clips["Heavy"], 32, 44, 23, .34f, .68f, .32f); p.heavy.chargeStateName = "Charge"; EditorUtility.SetDirty(p.heavy); AssetDatabase.SaveAssetIfDirty(p.heavy);
            p.charged = p.heavy; p.stances = new SamuraiStanceDefinition[0]; p.dodges = new SamuraiActionDefinition[8];
            p.maxHealth = 180; p.maxStamina = 85; p.staminaRecovery = 25; p.staminaRecoveryDelay = .65f; p.sprintStaminaPerSecond = 7;
            p.walkSpeed = 1.7f; p.runSpeed = 2.9f; p.guardWalkSpeed = 0; p.turnSpeed = 420; p.transitionSeconds = .07f;
            p.chargeSeconds = 1.2f; p.guardHalfAngle = 65; p.parryActiveSeconds = 0; p.guardKnockbackDistance = .12f; p.bladeRadius = .115f;
            p.kiPulseEnabled = p.kiPulseFluxEnabled = p.frostMoonEnabled = p.artsEnabled = p.deflectEnabled = false;
            OCDesign.EditorTools.SamuraiToughnessSetup.InitializeProfile(p); EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p); return p;
        }
        static SamuraiActionDefinition Action(string state, AnimationClip clip, float damage, float stamina, float cost, float start, float end, float step)
        {
            string path = ConfigFolder + "/Enemy1_" + state + ".asset";
            var a = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(path);
            if (a == null) { a = ScriptableObject.CreateInstance<SamuraiActionDefinition>(); AssetDatabase.CreateAsset(a, path); }
            a.clip = clip; a.stateName = state; a.displayName = state == "Heavy" ? "重劈" : "砍击";
            a.damage = damage; a.staminaDamage = stamina; a.staminaCost = cost; a.damageStart = start; a.damageEnd = end;
            a.comboInputStart = .18f; a.comboInputEnd = .82f; a.comboLink = .82f; a.cancelStart = .78f;
            a.stepDistance = step; a.stepStart = .12f; a.stepEnd = .52f;
            if (a.enemyRootMotionConfigurationVersion < 1) a.useRootMotion = false;
            if (a.enemyCloseRangeConfigurationVersion < 1) a.closeRangeReach = 0;
            a.knockbackDistance = state == "Heavy" ? .22f : .10f; a.knockbackSeconds = .18f;
            OCDesign.EditorTools.SamuraiToughnessSetup.InitializeAction(a); SamuraiEnemyReadabilitySetup.InitializeWindup(a); EditorUtility.SetDirty(a); AssetDatabase.SaveAssetIfDirty(a); return a;
        }
        static void ConfigurePrefab(SamuraiCombatProfile profile, AnimatorController controller, float visualOffset)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var animator = root.GetComponentInChildren<Animator>(); animator.avatar = null; animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.transform.localPosition = new Vector3(0, visualOffset, 0);
                var grip = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "RustCleaverGrip");
                var bladeBase = Child(grip, "BladeBase", new Vector3(0, .22f, -.035f));
                var bladeTip = Child(grip, "BladeTip", new Vector3(0, 1.065f, -.11f));
                var motor = root.GetComponent<CharacterController>(); if (motor == null) motor = root.AddComponent<CharacterController>();
                motor.height = 1.5f; motor.center = new Vector3(0, .75f, 0); motor.radius = .30f; motor.skinWidth = .025f; motor.stepOffset = .25f; motor.slopeLimit = 50; motor.minMoveDistance = 0;
                var actor = root.GetComponent<SamuraiCombatant>(); if (actor == null) actor = root.AddComponent<SamuraiCombatant>();
                actor.Configure(animator, motor, profile, bladeBase, bladeTip, 1);
                var brain = root.GetComponent<SamuraiTrainingEnemy>(); if (brain == null) brain = root.AddComponent<SamuraiTrainingEnemy>();
                brain.Configure(actor, null); brain.normalComboStrikeCount = 2; brain.engageDistance = 1.3f; brain.practiceEngageDistance = 1.05f;
                brain.minimumSafeDistance = 1.5f; brain.spacingMinimum = 1.9f; brain.spacingMaximum = 2.3f; brain.telegraphSeconds = .70f; brain.attackInterval = 1.1f; brain.chargeReactionSeconds = .35f; brain.activeTraining = true;
                SamuraiEnemyReadabilitySetup.InitializeBrain(brain);
                var gizmo = root.GetComponent<WeaponTrailGizmos>(); if (gizmo == null) gizmo = root.AddComponent<WeaponTrailGizmos>(); gizmo.Configure(bladeBase, bladeTip, profile.bladeRadius);
                var impact = root.GetComponent<SamuraiImpactFeedback>(); if (impact == null) impact = root.AddComponent<SamuraiImpactFeedback>(); impact.Configure(actor);
                var damage = root.GetComponent<SamuraiDamageVfx>(); if (damage == null) damage = root.AddComponent<SamuraiDamageVfx>();
                damage.Configure(actor, AssetDatabase.LoadAssetAtPath<Shader>("Assets/KatanaDemo/SamuraiCombat/VFX/Shaders/SamuraiDamageBurst.shader"));
                var guardFeedback = root.GetComponent<SamuraiDeflectFeedback>(); if (guardFeedback == null) guardFeedback = root.AddComponent<SamuraiDeflectFeedback>();
                guardFeedback.Configure(actor, AssetDatabase.LoadAssetAtPath<Shader>("Assets/KatanaDemo/SamuraiCombat/VFX/Shaders/SamuraiGuardClash.shader"));
                EnemyAttackRootMotionSetup.ConfigureInstalledDriver(root, "Enemy1");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        static Transform Child(Transform parent, string name, Vector3 position)
        {
            var t = parent.Find(name); if (t == null) { t = new GameObject(name).transform; t.SetParent(parent, false); }
            t.localPosition = position; t.localRotation = Quaternion.identity; t.localScale = Vector3.one; return t;
        }
        static int InstallSceneInstances()
        {
            var scene = SceneManager.GetActiveScene();
            var input = UnityEngine.Object.FindObjectsByType<SamuraiPlayerInput>().First(i => i.gameObject.scene == scene);
            var player = input.GetComponent<SamuraiCombatant>(); var camera = Camera.main; var orbit = camera.GetComponent<SamuraiOrbitCamera>();
            var old = UnityEngine.Object.FindObjectsByType<SamuraiTrainingEnemy>().Where(b => b.gameObject.scene == scene &&
                (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(b.gameObject) == PrefabPath ||
                 PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(b.gameObject) == "Assets/KatanaDemo/SamuraiCombat/Prefabs/SamuraiTrainingOpponent.prefab")).OrderBy(b => b.name).ToArray();
            var enemies = new List<SamuraiTrainingEnemy>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            foreach (var brain in old)
            {
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(brain.gameObject) == PrefabPath) { enemies.Add(brain); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = enemies.Count == 0 ? "Enemy1" : "Enemy1 (" + enemies.Count + ")";
                go.transform.SetPositionAndRotation(brain.transform.position, brain.transform.rotation); go.transform.localScale = brain.transform.localScale;
                go.transform.SetSiblingIndex(brain.transform.GetSiblingIndex()); enemies.Add(go.GetComponent<SamuraiTrainingEnemy>());
                UnityEngine.Object.DestroyImmediate(brain.gameObject);
            }
            if (enemies.Count == 0)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); go.name = "Enemy1"; go.transform.position = player.transform.position + player.transform.forward * 5; go.transform.rotation = Quaternion.LookRotation(-player.transform.forward); enemies.Add(go.GetComponent<SamuraiTrainingEnemy>());
            }
            foreach (var brain in enemies) { brain.Configure(brain.GetComponent<SamuraiCombatant>(), player, input); PrefabUtility.RecordPrefabInstancePropertyModifications(brain); }
            input.Configure(player, camera.transform, orbit, enemies[0], input.GetComponent<SamuraiTargetLock>()); PrefabUtility.RecordPrefabInstancePropertyModifications(input);
            var hud = UnityEngine.Object.FindObjectsByType<SamuraiHUDView>().First(h => h.gameObject.scene == scene); hud.player = player; hud.enemy = enemies[0]; hud.input = input; hud.worldCamera = camera; EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); return enemies.Count;
        }
        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = folder.Substring(0, folder.LastIndexOf('/')); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
