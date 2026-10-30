using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.LookDev.Editor
{
    /// <summary>Repairs preview-only contour copies; source meshes and prefabs stay untouched.</summary>
    public static class ComicOutlineRepairSetup
    {
        [MenuItem("OC Design/LookDev/Repair Sharp Contours")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ComicLightingSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the comic lighting sample in Edit mode first.");
            int changed = 0;
            var renderers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
            foreach (var shell in renderers)
            {
                var ink = shell.sharedMaterial;
                if (!ink || !ink.shader || ink.shader.name != "OC Design/Comic Lighting/Outline"
                    || ink.GetFloat("_BoxOutline") > .5f || !shell.transform.parent) continue;
                var source = shell.transform.parent.GetComponent<Renderer>();
                if (!source || source.sharedMaterial == ink) continue;
                var sourceSkin = source as SkinnedMeshRenderer;
                var sourceFilter = source.GetComponent<MeshFilter>();
                var sourceMesh = sourceSkin ? sourceSkin.sharedMesh : sourceFilter ? sourceFilter.sharedMesh : null;
                if (!sourceMesh) continue;
                var hull = ComicOutlineHullFactory.GetOrCreate(sourceMesh);
                if (shell is SkinnedMeshRenderer skin)
                {
                    if (skin.sharedMesh == hull) continue;
                    Undo.RecordObject(skin, "Repair preview contour normals");
                    skin.sharedMesh = hull;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
                }
                else
                {
                    var filter = shell.GetComponent<MeshFilter>();
                    if (!filter || filter.sharedMesh == hull) continue;
                    Undo.RecordObject(filter, "Repair preview contour normals");
                    filter.sharedMesh = hull;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                }
                changed++;
            }
            var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true))
                .FirstOrDefault(c => c.name == "04  Preview Camera");
            if (!camera) throw new InvalidOperationException("Sample preview camera is missing.");
            var repair = camera.GetComponent<ComicScreenOutline>();
            if (!repair)
            {
                repair = Undo.AddComponent<ComicScreenOutline>(camera.gameObject);
                repair.viewCamera = camera;
                var ink = AssetDatabase.LoadAssetAtPath<Material>(ComicLightingSceneBuilder.Root + "/Materials/Ink.mat");
                if (ink)
                {
                    repair.minimumPixels = ink.GetFloat("_MinOutlinePixels");
                    repair.outlineColor = ink.GetColor("_OutlineColor");
                }
            }
            if (changed > 0 || EditorUtility.IsDirty(repair)) EditorSceneManager.MarkSceneDirty(scene);
            return "Sharp contour repair: " + changed + " hull references updated; pixel floor " + repair.minimumPixels + ".";
        }
    }
}
