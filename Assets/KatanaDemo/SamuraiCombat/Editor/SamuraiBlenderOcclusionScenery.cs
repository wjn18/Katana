using System;
using System.Collections.Generic;
using System.Linq;
using OCDesign.LookDev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Registers the remaining visible courtyard scenery after the piece-specific installers.</summary>
    internal static class SamuraiBlenderOcclusionScenery
    {
        const string OutlineShader = "OC Design/Comic Lighting/Outline";
        const string AssemblyFolder = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/BlenderAssembly/";
        const string CreaseSource = AssemblyFolder + "ArchitecturalCreaseInk.asset";
        const string FadingInk = AssemblyFolder + "Occlusion/RidgeLodge_FadingInk.mat";

        sealed class RendererSlots
        {
            public readonly MeshRenderer renderer;
            public readonly Mesh mesh;
            public readonly Material[] materials;
            public readonly List<int> surfaces = new List<int>();
            public readonly List<int> outlines = new List<int>();
            public readonly List<int> creases = new List<int>();
            public int eligibleSlots;

            public RendererSlots(MeshRenderer renderer, Mesh mesh)
            {
                this.renderer = renderer;
                this.mesh = mesh;
                materials = renderer.sharedMaterials;
            }
        }

        public static string Install(Scene scene, bool dryRun = false)
        {
            if (!scene.IsValid() || !scene.isLoaded || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the courtyard scene in Edit mode.");

            GameObject[] roots = scene.GetRootGameObjects();
            var dressing = roots.SingleOrDefault(root => root.name == "Courtyard dressing" && root.activeSelf);
            if (!dressing) throw new InvalidOperationException("Active Courtyard dressing root not found.");

            var claims = new HashSet<(Renderer, int)>();
            foreach (var root in roots)
                foreach (var group in root.GetComponentsInChildren<SamuraiOcclusionGroup>(true))
                    foreach (var surface in group.surfaces)
                        if (surface.renderer) claims.Add((surface.renderer, surface.materialIndex));

            var renderers = new List<RendererSlots>();
            var unsupported = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int covered = 0;
            foreach (var renderer in dressing.GetComponentsInChildren<MeshRenderer>())
            {
                if (!IsScenery(renderer, dressing.transform) || !renderer.TryGetComponent(out MeshFilter filter) ||
                    !filter.sharedMesh) continue;

                var slots = new RendererSlots(renderer, filter.sharedMesh);
                int count = Mathf.Min(slots.mesh.subMeshCount, slots.materials.Length);
                for (int index = 0; index < count; index++)
                {
                    Material material = slots.materials[index];
                    if (!material || material.renderQueue > (int)RenderQueue.GeometryLast ||
                        slots.mesh.GetTopology(index) != MeshTopology.Triangles ||
                        slots.mesh.GetIndexCount(index) == 0) continue;

                    slots.eligibleSlots++;
                    if (claims.Contains((renderer, -1)) || claims.Contains((renderer, index)))
                    {
                        covered++;
                        continue;
                    }

                    if (material.FindPass("SamuraiOcclusionForeground") >= 0)
                    {
                        if (material.shader.name == OutlineShader) slots.outlines.Add(index);
                        else slots.surfaces.Add(index);
                    }
                    else if (AssetDatabase.GetAssetPath(material) == CreaseSource) slots.creases.Add(index);
                    else
                    {
                        string label = material.shader.name + " / " + material.name;
                        unsupported.TryGetValue(label, out int previous);
                        unsupported[label] = previous + 1;
                    }
                }
                if (slots.surfaces.Count + slots.outlines.Count + slots.creases.Count > 0) renderers.Add(slots);
            }

            Material fadingInk = null;
            if (renderers.Any(slots => slots.creases.Count > 0))
            {
                fadingInk = AssetDatabase.LoadAssetAtPath<Material>(FadingInk);
                if (!fadingInk || fadingInk.FindPass("SamuraiOcclusionForeground") < 0)
                    throw new InvalidOperationException("The flat architectural ink fade material is unavailable.");
            }

            var contours = new Dictionary<MeshRenderer, List<RendererSlots>>();
            foreach (var slots in renderers.Where(slots => slots.outlines.Count > 0))
            {
                MeshRenderer source = ContourSource(slots.renderer);
                if (!source) continue;
                if (!contours.TryGetValue(source, out var children))
                    contours.Add(source, children = new List<RendererSlots>());
                children.Add(slots);
            }

            var paired = new HashSet<RendererSlots>();
            var probeMeshes = new Dictionary<Mesh, Mesh>();
            int groups = 0, surfaceCount = 0, outlineCount = 0, creaseCount = 0, pairedCount = 0;

            void AddGroup(MeshRenderer owner, bool hide, List<(RendererSlots slots, int index)> selected)
            {
                groups++;
                if (dryRun) return;
                var surfaces = new SamuraiOcclusionGroup.Surface[selected.Count];
                for (int i = 0; i < selected.Count; i++)
                {
                    var (slots, index) = selected[i];
                    if (!probeMeshes.TryGetValue(slots.mesh, out Mesh probe))
                    {
                        probe = SamuraiOcclusionProbeMesh.Readable(slots.mesh);
                        probeMeshes.Add(slots.mesh, probe);
                    }
                    surfaces[i] = new SamuraiOcclusionGroup.Surface(slots.renderer, index, probe);
                }
                var group = Undo.AddComponent<SamuraiOcclusionGroup>(owner.gameObject);
                Undo.RecordObject(group, "Set scenery camera occlusion surfaces");
                group.surfaces = surfaces;
                group.useVisualProbes = true;
                group.hideWhenOccluding = hide;
                EditorUtility.SetDirty(group);
            }

            foreach (var slots in renderers)
            {
                surfaceCount += slots.surfaces.Count;
                if (slots.surfaces.Count == 0) continue;
                // A merged assembly renderer can contain several unrelated pieces. Keep those
                // slots independent so a probe against one does not fade the others.
                if (slots.surfaces.Count != slots.eligibleSlots || slots.renderer.name.EndsWith("_Surface", StringComparison.Ordinal))
                {
                    foreach (int index in slots.surfaces)
                        AddGroup(slots.renderer, false, new List<(RendererSlots, int)> { (slots, index) });
                    continue;
                }

                var selected = slots.surfaces.Select(index => (slots, index)).ToList();
                if (contours.TryGetValue(slots.renderer, out var children))
                    foreach (var child in children)
                    {
                        selected.AddRange(child.outlines.Select(index => (child, index)));
                        paired.Add(child);
                        pairedCount += child.outlines.Count;
                    }
                AddGroup(slots.renderer, false, selected);
            }

            foreach (var slots in renderers)
            {
                outlineCount += slots.outlines.Count;
                if (!paired.Contains(slots))
                    foreach (int index in slots.outlines)
                        AddGroup(slots.renderer, false, new List<(RendererSlots, int)> { (slots, index) });

                creaseCount += slots.creases.Count;
                if (slots.creases.Count == 0) continue;
                if (!dryRun)
                {
                    Undo.RecordObject(slots.renderer, "Use fade-compatible architectural ink");
                    foreach (int index in slots.creases) slots.materials[index] = fadingInk;
                    slots.renderer.sharedMaterials = slots.materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(slots.renderer);
                }
                foreach (int index in slots.creases)
                    AddGroup(slots.renderer, true, new List<(RendererSlots, int)> { (slots, index) });
            }

            int preservedBatchProbes = PreserveBatchProbes(roots, dryRun);
            if (!dryRun && (groups > 0 || preservedBatchProbes > 0)) EditorSceneManager.MarkSceneDirty(scene);
            string report = $"{(dryRun ? "Planned" : "Installed")} {groups} scenery groups: {surfaceCount} surface slots, " +
                $"{outlineCount} outline slots ({pairedCount} paired), {creaseCount} crease slots; {covered} existing slots retained. " +
                $"{preservedBatchProbes} existing static batch probes bound to authored meshes.";
            if (unsupported.Count > 0)
                report += " Unsupported opaque slots: " + string.Join(", ", unsupported.Select(entry => $"{entry.Key} ×{entry.Value}"));
            return report;
        }

        static int PreserveBatchProbes(GameObject[] roots, bool dryRun)
        {
            int count = 0;
            foreach (var root in roots)
                foreach (var group in root.GetComponentsInChildren<SamuraiOcclusionGroup>())
                {
                    if (!group.useVisualProbes) continue;
                    bool recorded = false;
                    for (int i = 0; i < group.surfaces.Length; i++)
                    {
                        var surface = group.surfaces[i];
                        var renderer = surface.renderer;
                        if (surface.probeMesh || !renderer ||
                            !GameObjectUtility.AreStaticEditorFlagsSet(renderer.gameObject, StaticEditorFlags.BatchingStatic) ||
                            !renderer.TryGetComponent(out MeshFilter filter) || !filter.sharedMesh) continue;
                        count++;
                        if (dryRun) continue;
                        if (!recorded) Undo.RecordObject(group, "Preserve authored camera probe for static batch");
                        recorded = true;
                        surface.probeMesh = SamuraiOcclusionProbeMesh.Readable(filter.sharedMesh);
                        group.surfaces[i] = surface;
                    }
                    if (recorded)
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(group);
                        EditorUtility.SetDirty(group);
                    }
                }
            return count;
        }

        static bool IsScenery(MeshRenderer renderer, Transform dressing)
        {
            if (!renderer.enabled || renderer.forceRenderingOff || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                return false;
            if (renderer.GetComponentInParent<SamuraiCombatant>()) return false;
            for (Transform node = renderer.transform; node && node != dressing; node = node.parent)
                if (node.name.StartsWith("99 Build references", StringComparison.Ordinal) ||
                    node.name.StartsWith("Cloud", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        static MeshRenderer ContourSource(MeshRenderer renderer)
        {
            if (renderer.TryGetComponent(out ComicContourFollower follower))
                return follower.source as MeshRenderer;
            Transform parent = renderer.transform.parent;
            return parent && renderer.name.StartsWith("Comic ink contour", StringComparison.Ordinal)
                ? parent.GetComponent<MeshRenderer>() : null;
        }
    }
}
