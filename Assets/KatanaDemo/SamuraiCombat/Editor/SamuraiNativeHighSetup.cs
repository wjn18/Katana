using System;
using System.Collections.Generic;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>定向接入用户指定高位四连、重击与05701切架，不重建场景或Avatar。</summary>
    public static class SamuraiNativeHighSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        const string Anim = Root + "/Animations/Nioh3";
        const string Config = Root + "/Config/Nioh3/";
        const float BridgeSeconds = 8f / 60f;
        public static readonly string[] Codes = { "03100", "03110", "03120", "03210" };
        public static readonly string[] Names = {
            "Nioh3_High_Attack1_03100", "Nioh3_High_Attack2_03110",
            "Nioh3_High_Attack3_03120", "Nioh3_High_Attack4_03210"
        };
        public const string StrongName = "Nioh3_High_Strong_03300";
        public const string HighIdleName = "Nioh3_High_Idle_03000";
        public const string MidIdleName = "Nioh3_Mid_Idle_02000";
        public const string HighToMidName = "Nioh3_HighToMid_05701";
        public const string MidToHighName = "Nioh3_MidToHigh_05701";

        [MenuItem("Katana/Samurai/Player/Install Native High Four And Stance Transitions")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出Play模式。");
            var high = Required<SamuraiStanceDefinition>(Config + "Nioh3_Stance_High.asset");
            var mid = Required<SamuraiStanceDefinition>(Config + "Nioh3_Stance_Mid.asset");
            var player = Required<SamuraiCombatProfile>(Root + "/Config/SamuraiPlayer.asset");
            var controller = Required<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            var allNames = Names.Concat(new[] { StrongName, HighIdleName, MidIdleName, HighToMidName, MidToHighName }).ToArray();
            foreach (var name in allNames)
                if (!System.IO.File.Exists(Anim + "/SourceFBX/" + name + ".fbx"))
                    throw new InvalidOperationException("缺少本轮源FBX：" + name);

            var clips = new Dictionary<string, AnimationClip>();
            clips[HighIdleName] = ImportClip(HighIdleName, 81f / 60f, true);
            clips[MidIdleName] = ImportClip(MidIdleName, 81f / 60f, true);
            float[] durations = { 91f / 60f, 101f / 60f, 117f / 60f, 109f / 60f };
            float[] hitStart = { 26f / 60f, 30f / 60f, 43f / 60f, 31f / 60f };
            float[] hitEnd = { 33f / 60f, 40f / 60f, 49f / 60f, 40f / 60f };
            float[] link = { 40f / 60f, 43f / 60f, 76f / 60f, 1.45f };
            float[] blends = { .10f, .08f, .08f, .18f };
            float[] stepStart = { 10f / 60f, 11f / 60f, 26f / 60f, 12f / 60f };
            float[] stepEnd = { 31f / 60f, 38f / 60f, 49f / 60f, 39f / 60f };
            var attacks = new SamuraiActionDefinition[4];
            for (int i = 0; i < 4; i++)
            {
                clips[Names[i]] = ImportClip(Names[i], durations[i]);
                // Preserve the current four attacks' damage, cost and knockback when creating replacements.
                var template = Required<SamuraiActionDefinition>(Root + "/Config/Combo" + (i + 1) + ".asset");
                attacks[i] = CreateAction(Names[i], "高架势-普通攻击" + (i + 1) + "（" + Codes[i] + "）", clips[Names[i]], template,
                    hitStart[i], hitEnd[i], link[i], blends[i], stepStart[i], stepEnd[i]);
            }
            clips[StrongName] = ImportClip(StrongName, 111f / 60f);
            var strongTemplate = Required<SamuraiActionDefinition>(Config + "Nioh3_High_RisingSlash_05180.asset");
            var strong = CreateAction(StrongName, "高架势-重击（03300）", clips[StrongName], strongTemplate,
                37f / 60f, 44f / 60f, 1.35f, .10f, 12f / 60f, 42f / 60f);
            clips[HighToMidName] = ImportClip(HighToMidName, 16f / 60f, false, clips[MidIdleName], false);
            clips[MidToHighName] = ImportClip(MidToHighName, 105f / 60f, false, clips[MidIdleName], true);

            var machine = controller.layers[0].stateMachine;
            bool changed = false;
            foreach (var item in clips)
            {
                var state = machine.states.FirstOrDefault(s => s.state.name == item.Key).state;
                if (!state) { state = machine.AddState(item.Key, new Vector3(1750, 80 * machine.states.Length)); changed = true; }
                if (state.motion != item.Value || state.speed != 1f || state.iKOnFeet)
                { state.motion = item.Value; state.speed = 1f; state.iKOnFeet = false; EditorUtility.SetDirty(state); changed = true; }
            }
            if (changed) { EditorUtility.SetDirty(machine); Save(controller); }
            BindIdle(high, clips[HighIdleName]); BindIdle(mid, clips[MidIdleName]);
            BindTransition(high, mid, clips[MidToHighName]);
            BindTransition(mid, high, clips[HighToMidName]);
            ApplyPreparedAttacks(player, high);

            var contents = PrefabUtility.LoadPrefabContents(SamuraiCombatSetup.PlayerPath);
            try
            {
                var ribbon = contents.GetComponent<SamuraiBladeRibbon>(); bool ribbonChanged = false;
                if (ribbon)
                    foreach (var attack in attacks.Concat(new[] { strong }))
                        ribbonChanged |= SamuraiBladeVfxSetup.EnsureAttackProfile(ribbon, attack, .10f, 1f, 1f);
                if (ribbonChanged) PrefabUtility.SaveAsPrefabAsset(contents, SamuraiCombatSetup.PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        // Existing maintenance menus call this so they cannot silently put ARPG Combo1–4 back.
        public static bool ApplyPreparedAttacks(SamuraiCombatProfile player, SamuraiStanceDefinition high)
        {
            var attacks = Names.Select(n => AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(Config + n + ".asset")).ToArray();
            var strong = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(Config + StrongName + ".asset");
            if (!player || !high || attacks.Any(a => !a || !a.IsUsable) || !strong || !strong.IsUsable) return false;
            bool highChanged = high.attackChains == null || high.attackChains.Length == 0 || high.attackChains[0] == null
                || high.attackChains[0].attacks == null || !high.attackChains[0].attacks.SequenceEqual(attacks);
            if (highChanged)
            {
                var chains = high.attackChains != null && high.attackChains.Length > 0
                    ? high.attackChains.ToArray() : new SamuraiAttackChain[1];
                chains[0] = new SamuraiAttackChain { displayName = "03100 → 03110 → 03120 → 03210", attacks = attacks };
                high.attackChains = chains;
            }
            if (high.strongAttack != strong) { high.strongAttack = strong; highChanged = true; }
            if (highChanged) Save(high);
            bool playerChanged = player.combo == null || !player.combo.SequenceEqual(attacks);
            if (playerChanged) player.combo = attacks.ToArray();
            // The former return clip embeds ARPG Combo1, so movement follow-ups now enter native 03100.
            if (player.dodgeAttackToCombo1 != attacks[0]) { player.dodgeAttackToCombo1 = attacks[0]; playerChanged = true; }
            if (player.heavy != strong) { player.heavy = strong; playerChanged = true; }
            if (playerChanged) Save(player);
            return true;
        }

        public static SamuraiActionDefinition PreparedFirstAttack()
            => AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(Config + Names[0] + ".asset");

        static SamuraiActionDefinition CreateAction(string name, string label, AnimationClip clip, SamuraiActionDefinition template,
            float hitStart, float hitEnd, float link, float blend, float stepStart, float stepEnd)
        {
            string path = Config + name + ".asset";
            var action = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(path);
            if (!action)
            {
                action = ScriptableObject.CreateInstance<SamuraiActionDefinition>();
                EditorUtility.CopySerialized(template, action);
                action.toughnessConfigurationVersion = 0;
                action.name = name; action.displayName = label; action.isMartialArt = false;
                action.clip = clip; action.stateName = name; action.chargeStateName = "";
                action.damageStart = hitStart / clip.length; action.damageEnd = hitEnd / clip.length;
                action.damageWindows = new[] { new Vector2(action.damageStart, action.damageEnd) };
                action.comboInputStart = .03f; action.comboInputEnd = .9f;
                action.comboLink = link / clip.length; action.comboBlendSeconds = blend;
                action.cancelStart = Mathf.Max(action.damageEnd, (hitEnd + .12f) / clip.length);
                action.overrideKiPulseTiming = false;
                action.stepStart = stepStart / clip.length; action.stepEnd = stepEnd / clip.length;
                action.approachEnabled = action.pushEnemies = false;
                action.closeRangeReach = .42f; action.closeRangeHalfAngle = 55f;
                action.closeRangeStart = action.damageStart; action.closeRangeEnd = action.damageEnd;
                action.invulnerableStart = action.invulnerableEnd = 0f;
                SamuraiToughnessSetup.InitializeAction(action);
                AssetDatabase.CreateAsset(action, path);
            }
            else if (action.clip != clip || action.stateName != name)
            { action.clip = clip; action.stateName = name; Save(action); }
            return action;
        }

        static void BindIdle(SamuraiStanceDefinition stance, AnimationClip clip)
        {
            if (stance.idle == clip && stance.idleStateName == clip.name) return;
            stance.idle = clip; stance.idleStateName = clip.name; Save(stance);
        }
        static void BindTransition(SamuraiStanceDefinition destination, SamuraiStanceDefinition source, AnimationClip clip)
        {
            var entries = destination.entryTransitions ?? new SamuraiStanceEntryTransition[0];
            var entry = entries.FirstOrDefault(e => e != null && e.fromStance == source);
            if (entry != null && entry.clip == clip && entry.stateName == clip.name) return;
            if (entry == null)
            {
                entry = new SamuraiStanceEntryTransition { fromStance = source, blendSeconds = .08f };
                destination.entryTransitions = entries.Concat(new[] { entry }).ToArray();
            }
            entry.clip = clip; entry.stateName = clip.name; Save(destination);
        }

        static AnimationClip ImportClip(string name, float rawDuration, bool loop = false, AnimationClip bridgeIdle = null, bool prependBridge = false)
        {
            string destination = Anim + "/Runtime/" + name + ".anim";
            float expected = rawDuration + (bridgeIdle ? BridgeSeconds : 0f);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
            if (existing) { Validate(existing, expected, loop); return existing; }
            string path = Anim + "/SourceFBX/" + name + ".fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("源FBX未导入：" + path);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Anim + "/SourceFBX/Nioh3_Canonical_TPose.fbx").OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("现有Canonical Avatar无效。");
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
                take.heightFromFeet = false; take.heightOffset = take.rotationOffset = 0f; take.loopTime = loop;
            }
            importer.clipAnimations = takes; importer.SaveAndReimport();
            var sourceClip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (!sourceClip || !sourceClip.humanMotion || Mathf.Abs(sourceClip.length - rawDuration) > .002f)
                throw new InvalidOperationException("源片段时长或Humanoid失败：" + name);
            var result = UnityEngine.Object.Instantiate(sourceClip);
            try
            {
                result.name = name; result.frameRate = 60f;
                foreach (var binding in AnimationUtility.GetCurveBindings(result))
                {
                    var curve = AnimationUtility.GetEditorCurve(result, binding);
                    bool horizontal = binding.type == typeof(Animator) && (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z");
                    if (bridgeIdle)
                    {
                        var idleCurve = AnimationUtility.GetEditorCurve(bridgeIdle, binding);
                        float target = idleCurve != null ? idleCurve.Evaluate(0) : curve.Evaluate(prependBridge ? 0 : rawDuration);
                        int count = Mathf.RoundToInt(expected * 120f); var keys = new Keyframe[count + 1];
                        for (int i = 0; i <= count; i++)
                        {
                            float time = expected * i / count;
                            float value;
                            if (prependBridge && time < BridgeSeconds)
                                value = Mathf.Lerp(target, curve.Evaluate(0), Mathf.SmoothStep(0, 1, time / BridgeSeconds));
                            else if (!prependBridge && time > rawDuration)
                                value = Mathf.Lerp(curve.Evaluate(rawDuration), target, Mathf.SmoothStep(0, 1, (time - rawDuration) / BridgeSeconds));
                            else value = curve.Evaluate(prependBridge ? time - BridgeSeconds : time);
                            keys[i] = new Keyframe(time, horizontal ? 0 : value);
                        }
                        curve = new AnimationCurve(keys);
                        for (int i = 0; i < keys.Length; i++)
                        { AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear); }
                    }
                    else if (horizontal)
                    {
                        var keys = curve.keys;
                        for (int i = 0; i < keys.Length; i++) { keys[i].value = keys[i].inTangent = keys[i].outTangent = 0; }
                        curve.keys = keys;
                    }
                    else continue;
                    AnimationUtility.SetEditorCurve(result, binding, curve);
                }
                var settings = AnimationUtility.GetAnimationClipSettings(result);
                settings.loopTime = loop; settings.loopBlend = loop; settings.stopTime = expected;
                AnimationUtility.SetAnimationClipSettings(result, settings);
                Validate(result, expected, loop);
                AssetDatabase.CreateAsset(result, destination);
                return result;
            }
            catch { if (!AssetDatabase.Contains(result)) UnityEngine.Object.DestroyImmediate(result); throw; }
        }
        static void Validate(AnimationClip clip, float duration, bool loop)
        {
            if (!clip.humanMotion || Mathf.Abs(clip.length - duration) > .002f || clip.isLooping != loop)
                throw new InvalidOperationException("片段验证失败：" + clip.name + " length=" + clip.length);
        }
        static T Required<T>(string path) where T : UnityEngine.Object
        { var result = AssetDatabase.LoadAssetAtPath<T>(path); if (!result) throw new InvalidOperationException("缺少资源：" + path); return result; }
        static void Save(UnityEngine.Object asset) { if (asset is SamuraiActionDefinition action) SamuraiToughnessSetup.InitializeAction(action); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
    }
}
