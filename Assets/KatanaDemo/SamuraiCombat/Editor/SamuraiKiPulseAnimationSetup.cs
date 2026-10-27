using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>只接入用户选定的三条残心；高/中绑定现有架势，低位留作可用片段。</summary>
    public static class SamuraiKiPulseAnimationSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        const string Anim = Root + "/Animations/Nioh3";
        public static readonly string[] Names = {
            "Nioh3_Mid_KiPulse_02008", "Nioh3_High_KiPulse_03008", "Nioh3_Low_KiPulse_04008"
        };

        [MenuItem("OC Design/Samurai/Install Ki Pulse Animations")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出Play模式。");
            var clips = Names.Select(ImportClip).ToArray();
            var controller = Required<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            var machine = controller.layers[0].stateMachine;
            bool changed = false;
            for (int i = 0; i < Names.Length; i++)
            {
                var state = machine.states.FirstOrDefault(s => s.state.name == Names[i]).state;
                if (state == null) { state = machine.AddState(Names[i], new Vector3(1400, 1000 + i * 80)); changed = true; }
                if (state.motion != clips[i] || state.speed != 1f || state.iKOnFeet)
                {
                    state.motion = clips[i]; state.speed = 1f; state.iKOnFeet = false;
                    EditorUtility.SetDirty(state); changed = true;
                }
            }
            if (changed)
            {
                EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
            }
            BindStance("Mid", clips[0]);
            BindStance("High", clips[1]);
            var player = Required<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            if (player.kiPulseAnimation != clips[0] || player.kiPulseStateName != Names[0])
            {
                player.kiPulseAnimation = clips[0]; player.kiPulseStateName = Names[0];
                EditorUtility.SetDirty(player); AssetDatabase.SaveAssetIfDirty(player);
            }
        }

        static void BindStance(string name, AnimationClip clip)
        {
            var stance = Required<SamuraiStanceDefinition>(Root + "/Config/Nioh3/Nioh3_Stance_" + name + ".asset");
            if (stance.kiPulse == clip && stance.kiPulseStateName == clip.name) return;
            stance.kiPulse = clip; stance.kiPulseStateName = clip.name;
            EditorUtility.SetDirty(stance); AssetDatabase.SaveAssetIfDirty(stance);
        }

        static AnimationClip ImportClip(string name)
        {
            string runtimePath = Anim + "/Runtime/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(runtimePath);
            if (existing != null) { ValidateClip(existing); return existing; }
            string path = Anim + "/SourceFBX/" + name + ".fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("缺少残心源FBX：" + path);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Anim + "/SourceFBX/Nioh3_Canonical_TPose.fbx")
                .OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException("缺少有效共享T Pose Avatar。");
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.humanDescription = avatar.humanDescription;
            var serialized = new SerializedObject(importer);
            serialized.FindProperty("m_AutoGenerateAvatarMappingIfUnspecified").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importCameras = importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var takes = importer.clipAnimations.Length == 0 ? importer.defaultClipAnimations : importer.clipAnimations;
            foreach (var take in takes)
            {
                take.lockRootRotation = take.lockRootHeightY = take.lockRootPositionXZ = true;
                take.keepOriginalOrientation = take.keepOriginalPositionY = take.keepOriginalPositionXZ = true;
                take.heightFromFeet = false; take.heightOffset = take.rotationOffset = 0f;
                take.loopTime = false;
            }
            importer.clipAnimations = takes;
            importer.SaveAndReimport();
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (source == null || !source.humanMotion)
                throw new InvalidOperationException("残心Humanoid导入失败：" + name);
            var clip = UnityEngine.Object.Instantiate(source);
            try
            {
                clip.name = name; clip.frameRate = 60f;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.type == typeof(Animator) && (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z"))
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        var keys = curve.keys;
                        for (int k = 0; k < keys.Length; k++)
                        { keys[k].value = 0f; keys[k].inTangent = keys[k].outTangent = 0f; }
                        curve.keys = keys; AnimationUtility.SetEditorCurve(clip, binding, curve);
                    }
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = false; AnimationUtility.SetAnimationClipSettings(clip, settings);
                ValidateClip(clip);
                AssetDatabase.CreateAsset(clip, runtimePath);
                return clip;
            }
            catch
            {
                if (!AssetDatabase.Contains(clip)) UnityEngine.Object.DestroyImmediate(clip);
                throw;
            }
        }

        static void ValidateClip(AnimationClip clip)
        {
            if (!clip.humanMotion || Mathf.Abs(clip.length - .35f) > .002f || clip.isLooping)
                throw new InvalidOperationException("残心片段必须为0.35秒非循环Humanoid：" + clip.name);
        }

        static T Required<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("缺少资源：" + path);
            return asset;
        }
    }
}
