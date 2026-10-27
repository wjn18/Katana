using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>中架势原生四向慢/快步，配合相位对齐的四向斜步。</summary>
    public static class SamuraiMidLocomotionSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        // Cardinal order: forward, right, backward, left; same global foot phase as high stance.
        public static readonly string[] WalkCodes = { "02030", "02033", "02031", "02032" };
        public static readonly string[] RunCodes = { "02020", "02028", "02024", "02026" };
        public static readonly float[] WalkPhases = { .0850f, .9925f, .2600f, .1900f };
        public static readonly float[] RunPhases = { .9400f, .9500f, .9300f, .0550f };

        [MenuItem("OC Design/Samurai/Install Native Middle Walk And Run")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出Play模式。");
            var mid = AssetDatabase.LoadAssetAtPath<SamuraiStanceDefinition>(Root + "/Config/Nioh3/Nioh3_Stance_Mid.asset");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "/Animations/SamuraiCombat.controller");
            if (!mid || !controller) throw new InvalidOperationException("缺少现有中架势或角色控制器。");
            var walk = SamuraiHighLocomotionSetup.BuildSet("Mid", "Walk", WalkCodes, WalkPhases, 71f / 60f);
            var run = SamuraiHighLocomotionSetup.BuildSet("Mid", "Run", RunCodes, RunPhases, 37f / 60f);
            var machine = controller.layers[0].stateMachine;
            bool controllerChanged = false;
            foreach (var clip in walk.Concat(run))
            {
                var state = machine.states.FirstOrDefault(s => s.state.name == clip.name).state;
                if (!state) { state = machine.AddState(clip.name, new Vector3(2450, 75 * machine.states.Length)); controllerChanged = true; }
                if (state.motion != clip || state.speed != 1f || state.iKOnFeet)
                { state.motion = clip; state.speed = 1f; state.iKOnFeet = false; EditorUtility.SetDirty(state); controllerChanged = true; }
            }
            if (controllerChanged) { EditorUtility.SetDirty(machine); Save(controller); }
            if (mid.locomotion == null || !Matches(mid.locomotion.walk, walk) || !Matches(mid.locomotion.run, run))
            {
                mid.locomotion = new SamuraiStanceLocomotion { walk = Entries(walk), run = Entries(run) };
                Save(mid);
            }
        }
        static SamuraiLocomotionEntry[] Entries(AnimationClip[] clips)
            => clips.Select(c => new SamuraiLocomotionEntry { clip = c, stateName = c.name }).ToArray();
        static bool Matches(SamuraiLocomotionEntry[] entries, AnimationClip[] clips)
            => entries != null && entries.Length == clips.Length && entries.Select((e, i) => e != null && e.clip == clips[i] && e.stateName == clips[i].name).All(v => v);
        static void Save(UnityEngine.Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
    }
}
