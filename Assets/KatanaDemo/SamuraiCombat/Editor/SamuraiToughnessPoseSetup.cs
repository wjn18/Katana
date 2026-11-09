using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>只补齐破韧保持循环；保留已有人工作业，不更改失衡计时、恢复动作或处决开关。</summary>
    public static class SamuraiToughnessPoseSetup
    {
        const string Root = "Assets/KatanaDemo/SamuraiCombat";
        const string Clips = Root + "/Animations/ToughnessBreak";

        [MenuItem("Katana/Samurai/Enemies/Install Toughness Break Pose Loops")]
        public static void Install()
        {
            int changed = InstallAll();
            Debug.Log("Toughness break pose loops: " + changed + " missing references/states added. Existing settings and scene preserved.");
        }

        /// <summary>返回本次补齐的Profile引用与Animator状态数量；重复调用为0，不保存场景。</summary>
        public static int InstallAll()
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先退出Play后安装倒伏保持动画。");

            // 全部依赖先验证，避免缺失任一派生片段时只装好一半角色。
            var enemyProfile = Required<SamuraiCombatProfile>(Root + "/Config/Enemy1/Enemy1.asset");
            var eliteProfile = Required<SamuraiCombatProfile>(Root + "/Config/Elite1/Elite1.asset");
            var enemyController = Required<AnimatorController>("Assets/KatanaDemo/Characters/Enemy1/Animations/Generic/Combat/Enemy1Combat.controller");
            var eliteController = Required<AnimatorController>("Assets/KatanaDemo/Characters/Elite1/Animations/Humanoid/Combat/Elite1Combat.controller");
            var enemyHealthy = Required<AnimationClip>(Clips + "/Enemy1/Enemy1_DownHealthy.anim");
            var enemyLow = Required<AnimationClip>(Clips + "/Enemy1/Enemy1_DownLowHealth.anim");
            var eliteHealthy = Required<AnimationClip>(Clips + "/Humanoid/Nioh3_DownHealthy.anim");
            var eliteLow = Required<AnimationClip>(Clips + "/Humanoid/Nioh3_DownLowHealth.anim");
            ValidateController(enemyController);
            ValidateController(eliteController);

            return Ensure(enemyProfile, enemyController, enemyHealthy, enemyLow)
                + Ensure(eliteProfile, eliteController, eliteHealthy, eliteLow);
        }

        static T Required<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("缺少倒伏保持资源：" + path);
            return asset;
        }

        static void ValidateController(AnimatorController controller)
        {
            if (controller.layers.Length == 0 || controller.layers[0].stateMachine == null)
                throw new InvalidOperationException("控制器缺少第0层状态机：" + AssetDatabase.GetAssetPath(controller));
        }

        static int Ensure(SamuraiCombatProfile profile, AnimatorController controller, AnimationClip healthy, AnimationClip low)
        {
            int changed = 0;
            bool profileChanged = false;
            if (profile.toughnessBrokenHealthyLoop == null)
            {
                profile.toughnessBrokenHealthyLoop = healthy;
                profileChanged = true;
                changed++;
            }
            if (profile.toughnessBrokenLowHealthLoop == null)
            {
                profile.toughnessBrokenLowHealthLoop = low;
                profileChanged = true;
                changed++;
            }

            var machine = controller.layers[0].stateMachine;
            int addedStates = AddMissingState(machine, SamuraiActionController.ToughnessBrokenHealthyState,
                profile.toughnessBrokenHealthyLoop, new Vector3(1140f, 660f, 0f));
            addedStates += AddMissingState(machine, SamuraiActionController.ToughnessBrokenLowHealthState,
                profile.toughnessBrokenLowHealthLoop, new Vector3(1140f, 730f, 0f));
            changed += addedStates;
            if (profileChanged)
            {
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
            if (addedStates > 0)
            {
                EditorUtility.SetDirty(machine);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
            }
            return changed;
        }

        static int AddMissingState(AnimatorStateMachine machine, string name, AnimationClip clip, Vector3 position)
        {
            // 已有状态的motion/speed/IK/transition均可能为手调，本安装器不覆盖。
            if (machine.states.Any(entry => entry.state.name == name)) return 0;
            var state = machine.AddState(name, position);
            state.motion = clip;
            state.speed = 1f;
            state.iKOnFeet = false;
            state.writeDefaultValues = true;
            state.transitions = Array.Empty<AnimatorStateTransition>();
            EditorUtility.SetDirty(state);
            return 1;
        }
    }
}
