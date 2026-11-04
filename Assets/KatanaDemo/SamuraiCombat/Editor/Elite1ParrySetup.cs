using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>仅补足精英独立弹反；迁移旧的 GuardHit 共用绑定，保留后续手调。</summary>
    public static class Elite1ParrySetup
    {
        public const string SourcePath = "Assets/ARPGPack/ARPGHalberd/Animations/Humanoid/ARPG_Halberd_Parry.anim";
        public const string ClipPath = Elite1CombatSetup.ClipFolder + "/Elite1_Parry.anim";
        public const float DefaultRecoverySeconds = .6f;

        [MenuItem("Tools/OC Design/Samurai Combat/Install Elite1 Independent Parry")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play before installing Elite1 parry.");
            var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Elite1CombatSetup.ProfilePath);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Elite1CombatSetup.ControllerPath);
            if (profile == null || controller == null) throw new InvalidOperationException("Existing Elite1 combat assets are required.");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(SourcePath);
                if (source == null || !source.humanMotion) throw new InvalidOperationException("Two-handed Humanoid parry source is missing.");
                clip = UnityEngine.Object.Instantiate(source);
                try
                {
                    clip.name = "Elite1_Parry"; clip.legacy = false;
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.startTime = 0f; settings.stopTime = source.length;
                    settings.loopTime = false; settings.loopBlend = false;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        if (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z")
                            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, source.length, 0f));
                    GroundFeet(clip);
                    AssetDatabase.CreateAsset(clip, ClipPath);
                    AssetDatabase.SaveAssetIfDirty(clip);
                }
                catch { if (!AssetDatabase.Contains(clip)) UnityEngine.Object.DestroyImmediate(clip); throw; }
            }
            // 仅识别旧默认共用绑定；明确的 0 和非默认恢复秒数均保留。
            if (profile.parry == null || profile.parry == profile.guardHit)
            {
                profile.parry = clip;
                if (Mathf.Approximately(profile.parrySuccessRecoverySeconds, .28f))
                    profile.parrySuccessRecoverySeconds = DefaultRecoverySeconds;
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            }
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.FirstOrDefault(s => s.state.name == "Parry").state;
            bool created = state == null;
            if (created) state = machine.AddState("Parry");
            if (created || state.motion == null || state.motion == profile.guardHit)
            {
                state.motion = profile.parry; state.speed = 1f; state.speedParameterActive = false;
                state.writeDefaultValues = true; state.iKOnFeet = true;
                EditorUtility.SetDirty(state); EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
            }

            Debug.Log("Elite1 independent parry installed; existing custom clips, recovery values and scene edits retained.");
        }

        static void GroundFeet(AnimationClip clip)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject target = null; var graph = default(PlayableGraph);
            try
            {
                target = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(Elite1CombatSetup.PrefabPath), scene);
                foreach (var b in target.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
                target.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = target.GetComponentInChildren<Animator>();
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.Rebind();
                var skin = target.GetComponentInChildren<SkinnedMeshRenderer>();
                var bones = skin.bones; var bind = skin.sharedMesh.bindposes;
                var vertices = skin.sharedMesh.vertices; var weights = skin.sharedMesh.boneWeights;
                var feet = Enumerable.Range(0, vertices.Length).Where(i =>
                    bones[weights[i].boneIndex0].name.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    bones[weights[i].boneIndex0].name.IndexOf("Toe", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                if (feet.Length == 0) throw new InvalidOperationException("Elite1 foot skin vertices are required.");
                var binding = AnimationUtility.GetCurveBindings(clip).First(b => b.propertyName == "RootT.y");
                float height = animator.humanScale * animator.transform.lossyScale.y;
                int steps = Mathf.CeilToInt(clip.length * 240f);
                // 只平移新片段的整身高度，不改肌肉、骨骼比例或原动作资产。
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    var keys = new Keyframe[steps + 1];
                    graph = PlayableGraph.Create("Elite1 parry ground calibration");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(true);
                    AnimationPlayableOutput.Create(graph, "pose", animator).SetSourcePlayable(playable); graph.Play();
                    for (int f = 0; f <= steps; f++)
                    {
                        float time = clip.length * f / steps; playable.SetTime(time); graph.Evaluate(.000001f);
                        var matrices = bones.Select((b, i) => b.localToWorldMatrix * bind[i]).ToArray();
                        float minimum = float.PositiveInfinity;
                        foreach (int i in feet)
                        {
                            var w = weights[i]; var v = vertices[i];
                            var p = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 +
                                matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                                matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 +
                                matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                            minimum = Mathf.Min(minimum, p.y);
                        }
                        keys[f] = new Keyframe(time, curve.Evaluate(time) + (.0015f - minimum) / height);
                    }
                    graph.Destroy();
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i].inTangent = i > 0 ? (keys[i].value - keys[i-1].value) / (keys[i].time - keys[i-1].time) : 0f;
                        keys[i].outTangent = i + 1 < keys.Length ? (keys[i+1].value - keys[i].value) / (keys[i+1].time - keys[i].time) : 0f;
                    }
                    AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
                }
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
