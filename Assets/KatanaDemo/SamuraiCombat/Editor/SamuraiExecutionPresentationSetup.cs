using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>Opt-in cue installation only. Existing component values and explicit disable are preserved.</summary>
    public static class SamuraiExecutionPresentationSetup
    {
        public const string MaterialPath = "Assets/KatanaDemo/SamuraiCombat/Presentation/Execution/ExecutionCue.mat";
        public const string ScenePath = "Assets/KatanaDemo/SamuraiCombat/Scenes/SamuraiCombatDemo.unity";

        [MenuItem("Katana/Samurai/Effects/Install Elite1 Execution Cue")]
        public static void InstallElite1()
        {
            RequireEditMode();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("请先打开正式 SamuraiCombatDemo 场景。");
            Material material = EnsureMaterial();
            bool prefabChanged = EnsureElite1Prefab(material);
            int sceneChanges = EnsureInScene(scene, material);
            Debug.Log($"Execution cue: prefab added={prefabChanged}, scene components added={sceneChanges}. Existing settings retained; scene was not saved.");
        }

        public static Material EnsureMaterial()
        {
            RequireEditMode();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            var shader = Shader.Find("OC Design/Execution Cue");
            if (shader == null) throw new InvalidOperationException("Execution Cue Shader 尚未导入。");
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            material = new Material(shader) { name = "ExecutionCue" };
            material.SetColor("_Tint", Color.white);
            material.SetFloat("_Shape", 0f);
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        public static bool EnsureElite1Prefab(Material material)
        {
            RequireEditMode();
            var root = PrefabUtility.LoadPrefabContents(Elite1CombatSetup.PrefabPath);
            try
            {
                var actor = root.GetComponent<SamuraiCombatant>();
                if (actor == null) throw new InvalidOperationException("Elite1完整Prefab缺少SamuraiCombatant。");
                bool changed = EnsureCue(actor, material, false);
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, Elite1CombatSetup.PrefabPath);
                return changed;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static int EnsureInScene(Scene scene, Material material)
        {
            RequireEditMode();
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("需要已加载的场景。");
            var profile = AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Elite1CombatSetup.ProfilePath);
            if (profile == null) throw new InvalidOperationException("Elite1 Profile缺失。");
            var actors = UnityEngine.Object.FindObjectsByType<SamuraiCombatant>(FindObjectsInactive.Include);
            int count = 0;
            for (int i = 0; i < actors.Length; i++)
                if (actors[i].gameObject.scene == scene && actors[i].Profile == profile && EnsureCue(actors[i], material, true)) count++;
            if (count > 0) EditorSceneManager.MarkSceneDirty(scene);
            return count;
        }

        /// <summary>Future bosses may explicitly opt in through this method or the component.</summary>
        public static bool EnsureCue(SamuraiCombatant actor, Material material, bool withUndo = true)
        {
            RequireEditMode();
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (actor.GetComponent<SamuraiExecutionCue>() != null) return false;
            if (material == null) throw new InvalidOperationException("提示材质必须存在。");
            var cue = withUndo ? Undo.AddComponent<SamuraiExecutionCue>(actor.gameObject) : actor.gameObject.AddComponent<SamuraiExecutionCue>();
            cue.Configure(actor);
            cue.ConfigurePresentation(material);
            EditorUtility.SetDirty(cue);
            if (PrefabUtility.IsPartOfPrefabInstance(cue)) PrefabUtility.RecordPrefabInstancePropertyModifications(cue);
            return true;
        }

        static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
        }
    }
}
