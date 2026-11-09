using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    internal static class SamuraiBlenderOcclusionFoliage
    {
        const string OutlineShader = "OC Design/Comic Lighting/Outline";
        const string ForegroundPass = "SamuraiOcclusionForeground";

        static readonly HashSet<string> FoliageMaterials = new HashSet<string>(StringComparer.Ordinal)
        {
            "Pine_dark_teal", "Pine_sage",
            "Maple_peach", "Maple_copper", "Maple_brick",
            "01 Moss and fern", "02 Meadow sage", "03 Dry grass tips"
        };

        public static string Install(Scene scene, bool dryRun = false)
        {
            if (scene.path != SamuraiBlenderAssemblyImport.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open BlenderAssembly in Edit mode.");

            var dressing = scene.GetRootGameObjects().Single(root => root.name == "Courtyard dressing");
            var pending = new List<(MeshRenderer owner, Mesh mesh, SamuraiOcclusionGroup.Surface[] surfaces)>();
            int existing = 0;
            foreach (var renderer in dressing.GetComponentsInChildren<MeshRenderer>())
            {
                if (!TryGetMesh(renderer, out var mesh)) continue;
                var materials = renderer.sharedMaterials;
                var surfaces = new List<SamuraiOcclusionGroup.Surface>();
                for (int slot = 0; slot < Math.Min(mesh.subMeshCount, materials.Length); slot++)
                {
                    var material = materials[slot];
                    if (!material || !FoliageMaterials.Contains(material.name) || mesh.GetIndexCount(slot) == 0)
                        continue;
                    if (material.FindPass(ForegroundPass) < 0 || mesh.GetTopology(slot) != MeshTopology.Triangles)
                        throw new InvalidOperationException($"Invalid foliage surface {renderer.name}, slot {slot}.");
                    surfaces.Add(new SamuraiOcclusionGroup.Surface(renderer, slot));
                }
                if (surfaces.Count == 0) continue;

                // Baked pine contour hulls have the same per-slot triangles as the body.
                // Preserve the bark outline by claiming only the slots for the needles.
                var bodySlots = surfaces.Select(surface => surface.materialIndex).ToArray();
                foreach (Transform child in renderer.transform)
                {
                    if (!child.TryGetComponent(out MeshRenderer ink) || !TryGetMesh(ink, out var inkMesh)) continue;
                    var inkMaterials = ink.sharedMaterials;
                    if (inkMaterials.Length == 0 || !inkMaterials.All(material => material && material.shader.name == OutlineShader))
                        continue;
                    if (inkMesh.subMeshCount != mesh.subMeshCount || inkMaterials.Length != mesh.subMeshCount)
                        throw new InvalidOperationException($"Foliage contour slots do not match {renderer.name}.");
                    foreach (int slot in bodySlots)
                    {
                        if (inkMesh.GetIndexCount(slot) != mesh.GetIndexCount(slot) || inkMaterials[slot].FindPass(ForegroundPass) < 0)
                            throw new InvalidOperationException($"Foliage contour triangles do not match {renderer.name}, slot {slot}.");
                        surfaces.Add(new SamuraiOcclusionGroup.Surface(ink, slot));
                    }
                }

                var installed = renderer.GetComponents<SamuraiOcclusionGroup>().Where(group => group.hideWhenOccluding).ToArray();
                if (installed.Any(group => group.useVisualProbes && group.surfaces.Length == surfaces.Count &&
                    group.surfaces.All(surface => surfaces.Any(expected => expected.renderer == surface.renderer &&
                        expected.materialIndex == surface.materialIndex))))
                {
                    existing++;
                    continue;
                }
                pending.Add((renderer, mesh, surfaces.ToArray()));
            }

            if (!dryRun && pending.Count > 0)
            {
                var probes = new Dictionary<Mesh, Mesh>();
                foreach (var entry in pending)
                    if (!entry.mesh.isReadable && !probes.ContainsKey(entry.mesh))
                        probes.Add(entry.mesh, SamuraiOcclusionProbeMesh.Readable(entry.mesh));

                foreach (var entry in pending)
                {
                    if (probes.TryGetValue(entry.mesh, out var probe))
                    {
                        for (int i = 0; i < entry.surfaces.Length; i++)
                            if (entry.surfaces[i].renderer == entry.owner)
                                entry.surfaces[i] = new SamuraiOcclusionGroup.Surface(entry.owner, entry.surfaces[i].materialIndex, probe);
                    }
                    var group = Undo.AddComponent<SamuraiOcclusionGroup>(entry.owner.gameObject);
                    group.useVisualProbes = true;
                    group.hideWhenOccluding = true;
                    group.surfaces = entry.surfaces;
                    EditorUtility.SetDirty(group);
                }
                EditorSceneManager.MarkSceneDirty(scene);
            }

            return $"{(dryRun ? "Validated" : "Installed")} {pending.Count} foliage groups with " +
                   $"{pending.Sum(entry => entry.surfaces.Length)} material slots. {existing} existing groups retained.";
        }

        static bool TryGetMesh(MeshRenderer renderer, out Mesh mesh)
        {
            mesh = null;
            if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                !renderer.TryGetComponent(out MeshFilter filter))
                return false;
            mesh = filter.sharedMesh;
            return mesh;
        }
    }
}
