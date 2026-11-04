using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>Only adds missing R7/R8 references and states; preserves existing tuning and scenes.</summary>
    public static class SamuraiToughnessGetupSetup
    {
        const string Root = "Assets/KatanaDemo/SamuraiCombat";

        [MenuItem("Tools/OC Design/Samurai Combat/Install Toughness Getup Animations")]
        public static void Install()
        {
            Debug.Log("Toughness getups: " + InstallAll() + " missing references/states added.");
        }

        public static int InstallAll()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play before installing getup animations.");
            var actors = new[] { "Enemy1", "Elite1" };
            var profiles = actors.Select(a => Required<SamuraiCombatProfile>(Root + "/Config/" + a + "/" + a + ".asset")).ToArray();
            var controllers = actors.Select(a => Required<AnimatorController>("Assets/KatanaDemo/Characters/" + a + "/Animations/" + (a == "Enemy1" ? "Generic" : "Humanoid") + "/Combat/" + a + "Combat.controller")).ToArray();
            var clips = actors.SelectMany(a => new[] { "GetupHealthy", "GetupLowHealth" }.Select(spec =>
                Required<AnimationClip>(Root + "/Animations/ToughnessBreak/" + (a == "Enemy1" ? "Enemy1/Enemy1_" : "Humanoid/Nioh3_") + spec + ".anim"))).ToArray();
            if (clips.Any(c => c.isLooping)) throw new InvalidOperationException("Getup clips must not loop.");
            if (controllers.Any(c => c.layers.Length == 0 || c.layers[0].stateMachine == null))
                throw new InvalidOperationException("Missing base state machine.");
            int changed = 0;
            for (int i = 0; i < actors.Length; i++)
            {
                var profile = profiles[i]; var controller = controllers[i]; var machine = controller.layers[0].stateMachine;
                bool profileChanged = false;
                if (profile.toughnessGetupHealthy == null) { profile.toughnessGetupHealthy = clips[i * 2]; profileChanged = true; changed++; }
                if (profile.toughnessGetupLowHealth == null) { profile.toughnessGetupLowHealth = clips[i * 2 + 1]; profileChanged = true; changed++; }
                int added = AddState(machine, SamuraiActionController.ToughnessGetupHealthyState, profile.toughnessGetupHealthy, 800);
                added += AddState(machine, SamuraiActionController.ToughnessGetupLowHealthState, profile.toughnessGetupLowHealth, 870);
                changed += added;
                if (profileChanged) { EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile); }
                if (added > 0) { EditorUtility.SetDirty(machine); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller); }
            }
            return changed;
        }

        static T Required<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("Missing getup resource: " + path);
            return asset;
        }

        static int AddState(AnimatorStateMachine machine, string name, AnimationClip clip, float y)
        {
            if (machine.states.Any(s => s.state.name == name)) return 0;
            var state = machine.AddState(name, new Vector3(1140, y, 0));
            state.motion = clip; state.speed = 1; state.iKOnFeet = false; state.writeDefaultValues = true;
            state.transitions = Array.Empty<AnimatorStateTransition>();
            EditorUtility.SetDirty(state);
            return 1;
        }
    }
}
