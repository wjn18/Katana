using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>定向接入弹反、攻击者硬直与双方碰撞表现，保留用户调参。</summary>
    public static class SamuraiDeflectSetup
    {
        const string Root = "Assets/KatanaDemo/SamuraiCombat";

        [MenuItem("OC Design/Samurai/Install Deflect")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出Play后安装弹反。");
            var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/VFX/Shaders/SamuraiGuardClash.shader");
            if (profile == null || shader == null) throw new InvalidOperationException("缺少现有玩家配置或反馈Shader。");
            if (!profile.deflectEnabled)
            {
                profile.deflectEnabled = true;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
            var deflected = EnsureDeflectedClip();
            InstallReaction(profile, deflected);
            InstallReaction(AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Root + "/Config/TrainingOpponent.asset"), deflected);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            if (controller == null) throw new InvalidOperationException("缺少现有战斗控制器。");
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.FirstOrDefault(s => s.state.name == "Deflected").state;
            if (state == null)
            {
                state = machine.AddState("Deflected", new Vector3(960f, 810f));
                state.motion = deflected; state.speed = 1f; state.iKOnFeet = false;
                state.writeDefaultValues = true;
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
            }
            InstallFeedback(Root + "/Prefabs/SamuraiCombatPlayer.prefab", shader);
            InstallFeedback(Root + "/Prefabs/SamuraiTrainingOpponent.prefab", shader);
        }

        static AnimationClip EnsureDeflectedClip()
        {
            string path = Root + "/Animations/Samurai_Deflected.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/ARPGPack/ARPGSamurai/Animations/Humanoid/ARPG_Samurai_Collide.anim");
            if (source == null) throw new InvalidOperationException("缺少武士 Collide 碰撞反应源动作。");
            const float duration = .7f;
            float factor = duration / source.length;
            var clip = UnityEngine.Object.Instantiate(source);
            clip.name = "Samurai_Deflected";
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (binding.type == typeof(Animator) && (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z"))
                    curve = AnimationCurve.Linear(0f, 0f, duration, 0f);
                else
                {
                    var keys = curve.keys;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i].time *= factor;
                        keys[i].inTangent /= factor; keys[i].outTangent /= factor;
                    }
                    curve.keys = keys;
                }
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            var events = AnimationUtility.GetAnimationEvents(source);
            foreach (var item in events) item.time *= factor;
            AnimationUtility.SetAnimationEvents(clip, events);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f; settings.stopTime = duration;
            settings.loopTime = false; settings.loopBlend = false;
            settings.loopBlendPositionXZ = true; settings.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        static void InstallReaction(SamuraiCombatProfile profile, AnimationClip clip)
        {
            if (profile == null) throw new InvalidOperationException("缺少现有战斗角色配置。");
            if (profile.deflected != null) return;
            profile.deflected = clip;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        static void InstallFeedback(string path, Shader shader)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var feedback = contents.GetComponent<SamuraiDeflectFeedback>();
                bool added = feedback == null;
                if (added) feedback = contents.AddComponent<SamuraiDeflectFeedback>();
                var serialized = new SerializedObject(feedback);
                var shaderProperty = serialized.FindProperty("sparkShader");
                var actorProperty = serialized.FindProperty("actor");
                var actor = contents.GetComponent<SamuraiCombatant>();
                if (!added && shaderProperty.objectReferenceValue == shader
                    && actorProperty.objectReferenceValue == actor) return;
                // 只更新接线与专用 Shader，不用 Configure 重设任何已存的外观调参。
                shaderProperty.objectReferenceValue = shader;
                actorProperty.objectReferenceValue = actor;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
    }
}
