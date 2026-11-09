using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Keep the two courtyard slabs independently selectable by the camera.</summary>
    public static class SamuraiBlenderOcclusionFoundations
    {
        const string Action = "Install Blender courtyard foundation camera fading";
        const string SplitAsset = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/BlenderAssembly/Occlusion/CourtyardFoundations_B001_Surface.asset";
        const string UpperName = "Camera fade - upper courtyard foundation";
        const string LowerName = "Camera fade - lower courtyard foundation";
        const int PavementSlot = 0;
        const int PracticeGridSlot = 2;
        const float Tolerance = .01f;

        public static string Install(Scene scene, bool dryRun = false)
        {
            if (!scene.isLoaded || scene.path != SamuraiBlenderAssemblyImport.ScenePath ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open BlenderAssembly in Edit mode.");

            var roots = scene.GetRootGameObjects();
            var renderer = roots.SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                .Single(item => item.name == "B001_Surface" && item.gameObject.activeInHierarchy &&
                                item.enabled && item.transform.parent.name == "Courtyard Ground and Retaining Walls");
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var upper = renderer.transform.Find(UpperName);
            var lower = renderer.transform.Find(LowerName);
            if (upper || lower)
            {
                if (mesh.subMeshCount != 6 || AssetDatabase.GetAssetPath(mesh) != SplitAsset ||
                    mesh.GetIndexCount(4) != 36 || mesh.GetIndexCount(5) != 36 ||
                    !IsInstalled(upper, renderer, 4, PracticeGridSlot) || !IsInstalled(lower, renderer, 5))
                    throw new InvalidOperationException("Inspect the existing courtyard foundation fade groups.");
                return "Two courtyard foundation groups already installed.";
            }

            if (!mesh || !mesh.isReadable || mesh.subMeshCount != 4 || mesh.GetIndexCount(PavementSlot) != 72 ||
                mesh.GetIndexCount(PracticeGridSlot) != 2532)
                throw new InvalidOperationException("The courtyard pavement or practice grid differs from the audited mesh.");
            if (roots.SelectMany(root => root.GetComponentsInChildren<SamuraiOcclusionGroup>(true))
                .SelectMany(group => group.surfaces)
                .Any(slot => slot.renderer == renderer &&
                             (slot.materialIndex < 0 || slot.materialIndex == PavementSlot || slot.materialIndex == PracticeGridSlot)))
                throw new InvalidOperationException("A different group already controls the courtyard pavement or practice grid.");

            var upperTriangles = new HashSet<int>();
            var lowerTriangles = new HashSet<int>();
            int[] indices = mesh.GetTriangles(PavementSlot);
            Vector3[] vertices = mesh.vertices;
            for (int start = 0; start < indices.Length; start += 3)
            {
                var a = vertices[indices[start]].y;
                var b = vertices[indices[start + 1]].y;
                var c = vertices[indices[start + 2]].y;
                if (InBand(a, b, c, 1.15f, 1.45f)) upperTriangles.Add(start);
                else if (InBand(a, b, c, -2.75f, -2.45f)) lowerTriangles.Add(start);
                else throw new InvalidOperationException("A pavement triangle crosses the audited courtyard slabs.");
            }
            if (upperTriangles.Count != 12 || lowerTriangles.Count != 12)
                throw new InvalidOperationException("Expected two closed 12-triangle courtyard slabs.");
            var slices = new[]
            {
                new SamuraiOcclusionMeshSplit.Slice { sourceSlot = PavementSlot, triangleStarts = upperTriangles },
                new SamuraiOcclusionMeshSplit.Slice { sourceSlot = PavementSlot, triangleStarts = lowerTriangles }
            };
            SamuraiOcclusionMeshSplit.Apply(renderer, slices, SplitAsset, true);
            const string summary = "Two independent courtyard foundations; the upper foundation includes its practice grid.";
            if (dryRun) return summary;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            try
            {
                int[] slots = SamuraiOcclusionMeshSplit.Apply(renderer, slices, SplitAsset, false);
                AddGroup(renderer, UpperName, slots[0], PracticeGridSlot);
                AddGroup(renderer, LowerName, slots[1]);
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                if (AssetDatabase.LoadMainAssetAtPath(SplitAsset))
                    AssetDatabase.DeleteAsset(SplitAsset);
                throw;
            }
            return summary;
        }

        static bool InBand(float a, float b, float c, float min, float max)
        {
            return Mathf.Min(a, Mathf.Min(b, c)) >= min - Tolerance &&
                   Mathf.Max(a, Mathf.Max(b, c)) <= max + Tolerance;
        }

        static bool IsInstalled(Transform anchor, MeshRenderer renderer, params int[] slots)
        {
            var group = anchor ? anchor.GetComponent<SamuraiOcclusionGroup>() : null;
            return group && group.useVisualProbes && !group.hideWhenOccluding && group.blockers.Length == 0 &&
                   group.surfaces.All(slot => slot.renderer == renderer) &&
                   group.surfaces.Select(slot => slot.materialIndex).SequenceEqual(slots);
        }

        static void AddGroup(MeshRenderer renderer, string name, params int[] slots)
        {
            var anchor = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(anchor, Action);
            Undo.SetTransformParent(anchor.transform, renderer.transform, Action);
            anchor.transform.localPosition = Vector3.zero;
            anchor.transform.localRotation = Quaternion.identity;
            anchor.transform.localScale = Vector3.one;
            var group = Undo.AddComponent<SamuraiOcclusionGroup>(anchor);
            group.useVisualProbes = true;
            group.surfaces = slots.Select(slot => new SamuraiOcclusionGroup.Surface(renderer, slot)).ToArray();
            EditorUtility.SetDirty(group);
        }
    }
}
