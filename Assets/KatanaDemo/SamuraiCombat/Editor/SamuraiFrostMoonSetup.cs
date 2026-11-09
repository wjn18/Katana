using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>接入用户实机确认的16000/16100；只更新冬月依赖，不重建战斗、Avatar或其他武技。</summary>
    public static class SamuraiFrostMoonSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        const string Anim = Root + "/Animations/Nioh3";
        const string Config = Root + "/Config/Nioh3/";
        public const string HighName = "Nioh3_FrostMoon_High_16000";
        public const string MidName = "Nioh3_FrostMoon_Mid_16100";

        [MenuItem("Katana/Samurai/Player/Install Frost Moon Animations")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出Play模式。");
            var high = Required<SamuraiStanceDefinition>(Config + "Nioh3_Stance_High.asset");
            var mid = Required<SamuraiStanceDefinition>(Config + "Nioh3_Stance_Mid.asset");
            var controller = Required<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            // 独立根运动版保留源RootT曲线；旧手动16000继续使用原地片段，避免改变其他操作槽。
            var highClip = ImportRootClip(HighName, "Nioh3_High_RisingDragon_16000", 101f / 60f);
            var midClip = ImportRootClip(MidName, MidName, 85f / 60f);
            var highAction = PrepareAction(HighName, "冬月·天", highClip, true);
            var midAction = PrepareAction(MidName, "冬月·人", midClip, false);
            var motion = JsonUtility.FromJson<RootMotionLibrary>(Required<TextAsset>(Root + "/Editor/FrostMoonRootMotion.json").text);
            ConfigureRootMotion(highAction, motion.clips.First(c => c.code == "16000"));
            ConfigureRootMotion(midAction, motion.clips.First(c => c.code == "16100"));
            BindState(controller, HighName, highClip);
            BindState(controller, MidName, midClip);
            BindStance(high, highAction); BindStance(mid, midAction);
            SamuraiKiPulseSetup.EnableFrostMoon();

            var contents = PrefabUtility.LoadPrefabContents(SamuraiCombatSetup.PlayerPath);
            try
            {
                var actor = contents.GetComponent<SamuraiCombatant>();
                var animator = actor.Animator;
                var rootDriver = animator.GetComponent<SamuraiRootMotionDriver>();
                bool rootChanged = rootDriver == null || !animator.applyRootMotion;
                if (rootDriver == null) rootDriver = animator.gameObject.AddComponent<SamuraiRootMotionDriver>();
                rootChanged |= new SerializedObject(rootDriver).FindProperty("actor").objectReferenceValue != actor;
                rootDriver.Configure(actor);
                var ribbon = contents.GetComponent<SamuraiBladeRibbon>();
                bool changed = rootChanged;
                if (ribbon)
                {
                    changed |= SamuraiBladeVfxSetup.EnsureAttackProfile(ribbon, highAction, .10f, 1f, 1f);
                    changed |= SamuraiBladeVfxSetup.EnsureAttackProfile(ribbon, midAction, .10f, 1f, 1f);
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(contents, SamuraiCombatSetup.PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            SamuraiFrostMoonEffectsSetup.Install();
        }

        static SamuraiActionDefinition PrepareAction(string name, string label, AnimationClip clip, bool high)
        {
            string path = Config + name + ".asset";
            var action = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(path);
            if (!action)
            {
                action = ScriptableObject.CreateInstance<SamuraiActionDefinition>();
                action.name = name; action.clip = clip; action.stateName = name; action.displayName = label;
                action.isMartialArt = true; action.staminaCost = 0f;
                // 项目初始战斗参数；不宣称来自原作事件表或精确数值测量。
                action.damage = high ? 36f : 28f;
                action.staminaDamage = high ? 36f : 30f;
                float frames = high ? 101f : 85f;
                action.damageStart = (high ? 39f : 23f) / frames;
                action.damageEnd = (high ? 49f : 37f) / frames;
                action.damageWindows = new[] { new Vector2(action.damageStart, action.damageEnd) };
                action.cancelStart = (high ? 62f : 48f) / frames;
                action.comboLink = action.cancelStart;
                action.comboInputStart = action.damageStart; action.comboInputEnd = .9f;
                action.comboBlendSeconds = .08f;
                action.stepDistance = high ? 1.84f : 1.17f;
                action.stepStart = (high ? 5f : 14f) / frames;
                action.stepEnd = (high ? 48f : 49f) / frames;
                action.approachEnabled = true;
                action.frostMoonSuperArmorWindow = high ? new Vector2(0f, 1f) : Vector2.zero;
                action.frostMoonGuardWindow = high ? Vector2.zero : new Vector2(0f, 22f / frames);
                action.invulnerableStart = action.invulnerableEnd = 0f;
                SamuraiAttackKnockbackSetup.InitializeNew(action);
                SamuraiToughnessSetup.InitializeAction(action);
                AssetDatabase.CreateAsset(action, path);
            }
            else if (action.clip != clip || action.stateName != name)
            { action.clip = clip; action.stateName = name; Save(action); }
            return action;
        }

        [Serializable] sealed class RootMotionLibrary { public RootMotionEntry[] clips; }
        [Serializable] sealed class RootMotionEntry { public string code; public float duration; public float[] t, x, z; }

        static void ConfigureRootMotion(SamuraiActionDefinition action, RootMotionEntry entry)
        {
            if (entry.t.Length < 2 || entry.t.Length != entry.x.Length || entry.t.Length != entry.z.Length
                || Mathf.Abs(entry.duration-action.Duration) > .002f)
                throw new InvalidOperationException("冬月原始根轨迹无效：" + entry.code);
            var x = RootCurve(entry.t, entry.x); var z = RootCurve(entry.t, entry.z);
            float distanceMultiplier = entry.code == "16100" ? 3.2f : 1f;
            if (action.useRootMotion && CurvesEqual(action.rootMotionX, x) && CurvesEqual(action.rootMotionZ, z)
                && action.stepDistance == 0f && !action.approachEnabled && !action.pushEnemies
                && action.rootMotionDistanceMultiplier == distanceMultiplier) return;
            action.useRootMotion = true; action.rootMotionX = x; action.rootMotionZ = z;
            action.rootMotionDistanceMultiplier = distanceMultiplier;
            action.stepDistance = 0f; action.approachEnabled = action.pushEnemies = false;
            Save(action);
        }
        static AnimationCurve RootCurve(float[] times, float[] values)
        {
            var keys = new Keyframe[times.Length];
            for (int i=0;i<keys.Length;i++) keys[i] = new Keyframe(times[i],values[i]);
            var curve = new AnimationCurve(keys);
            for(int i=0;i<keys.Length;i++)
            { AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear); }
            return curve;
        }
        static bool CurvesEqual(AnimationCurve a, AnimationCurve b) => a != null && a.length == b.length && a.keys.SequenceEqual(b.keys);

        static AnimationClip ImportRootClip(string name, string sourceName, float duration)
        {
            string destination = Anim + "/Runtime/" + name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(destination);
            if (existing && !AnimationUtility.GetAnimationClipSettings(existing).loopBlendPositionXZ)
            { Validate(existing, duration); return existing; }
            string path = Anim + "/SourceFBX/" + sourceName + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.animationType != ModelImporterAnimationType.Human || !importer.sourceAvatar)
                throw new InvalidOperationException("缺少已验证的Humanoid源FBX：" + sourceName);
            var source = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            Validate(source, duration);
            var result = UnityEngine.Object.Instantiate(source);
            try
            {
                result.name = name; result.frameRate = 60f;
                var settings = AnimationUtility.GetAnimationClipSettings(result);
                settings.loopTime = settings.loopBlend = false; settings.stopTime = duration;
                settings.loopBlendPositionXZ = false;
                settings.loopBlendOrientation = settings.loopBlendPositionY = true;
                settings.keepOriginalPositionXZ = settings.keepOriginalPositionY = settings.keepOriginalOrientation = true;
                AnimationUtility.SetAnimationClipSettings(result, settings);
                Validate(result, duration);
                if (existing)
                {
                    EditorUtility.CopySerialized(result, existing); Save(existing);
                    UnityEngine.Object.DestroyImmediate(result); return existing;
                }
                AssetDatabase.CreateAsset(result, destination);
                return result;
            }
            catch { if (!AssetDatabase.Contains(result)) UnityEngine.Object.DestroyImmediate(result); throw; }
        }

        static void BindState(AnimatorController controller, string name, AnimationClip clip)
        {
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.FirstOrDefault(s => s.state.name == name).state;
            bool added = !state;
            if (added) state = machine.AddState(name, new Vector3(2050, name == HighName ? 100 : 180));
            if (!added && state.motion == clip && state.speed == 1f && !state.iKOnFeet) return;
            state.motion = clip; state.speed = 1f; state.iKOnFeet = false;
            EditorUtility.SetDirty(state); EditorUtility.SetDirty(machine); Save(controller);
        }
        static void BindStance(SamuraiStanceDefinition stance, SamuraiActionDefinition action)
        { if (stance.frostMoon != action) { stance.frostMoon = action; Save(stance); } }
        static void Validate(AnimationClip clip, float duration)
        {
            if (!clip || !clip.humanMotion || clip.isLooping || Mathf.Abs(clip.length-duration) > .002f)
                throw new InvalidOperationException("冬月片段无效或时长改变。");
        }
        static T Required<T>(string path) where T : UnityEngine.Object
        { var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (!asset) throw new InvalidOperationException("缺少资源："+path); return asset; }
        static void Save(UnityEngine.Object asset) { if (asset is SamuraiActionDefinition action) SamuraiToughnessSetup.InitializeAction(action); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
    }
}
