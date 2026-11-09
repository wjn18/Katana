using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Installs one fade group per existing door leaf in the Blender assembly.</summary>
    public static class SamuraiBlenderOcclusionDoors
    {
        const string Folder = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/BlenderAssembly/Occlusion";
        const string Action = "Install Blender door leaf camera fading";
        const float VertexTolerance = .00032f;

        sealed class Leaf
        {
            public Transform anchor;
            public bool installed;
            public readonly List<SamuraiOcclusionGroup.Surface> surfaces = new();
            public readonly HashSet<(Vector3Int, Vector3Int)> edges = new();
        }

        sealed class Geometry
        {
            public readonly MeshRenderer renderer;
            public readonly Vector3[] points;
            public readonly Vector3[] normals;
            public readonly int[][] indices;
            public readonly Material[] materials;

            public Geometry(MeshRenderer renderer)
            {
                this.renderer = renderer;
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh)
                    throw new InvalidOperationException($"A door source mesh is required on {renderer.name}.");
                var mesh = filter.sharedMesh;
                materials = renderer.sharedMaterials;
                if (mesh.subMeshCount != materials.Length)
                    throw new InvalidOperationException($"Door material count differs from mesh slots on {renderer.name}.");
                // The original architectural FBX meshes are imported with Read/Write off.
                using (var meshes = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var data = meshes[0];
                    using (var values = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetVertices(values);
                        points = values.ToArray();
                        if (data.HasVertexAttribute(VertexAttribute.Normal))
                        {
                            data.GetNormals(values);
                            normals = values.ToArray();
                        }
                        else normals = Array.Empty<Vector3>();
                    }
                    indices = new int[data.subMeshCount][];
                    for (int slot = 0; slot < indices.Length; slot++)
                    {
                        var submesh = data.GetSubMesh(slot);
                        if (submesh.topology != MeshTopology.Triangles)
                            throw new InvalidOperationException($"Non-triangle door submesh on {renderer.name}.");
                        using (var values = new NativeArray<int>(submesh.indexCount, Allocator.Temp))
                        {
                            data.GetIndices(values, slot, true);
                            indices[slot] = values.ToArray();
                        }
                    }
                }
                var placement = renderer.localToWorldMatrix;
                var normalPlacement = placement.inverse.transpose;
                for (int i = 0; i < points.Length; i++)
                    points[i] = placement.MultiplyPoint3x4(points[i]);
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = normalPlacement.MultiplyVector(normals[i]).normalized;
            }
        }

        sealed class Slice
        {
            public Leaf leaf;
            public MeshRenderer renderer;
            public int slot;
            public Material substitute;
            public readonly HashSet<int> starts = new();
        }

        public static string Install(Scene scene, bool dryRun = false)
        {
            if (scene.path != SamuraiBlenderAssemblyImport.ScenePath || !scene.isLoaded ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open BlenderAssembly in Edit mode.");

            var roots = scene.GetRootGameObjects();
            var route = roots.SelectMany(root => root.GetComponentsInChildren<SamuraiLevelRoute>(true)).Single();
            var renderers = roots.SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                .Where(item => item.gameObject.activeInHierarchy).ToLookup(item => item.name, StringComparer.Ordinal);
            var leaves = new List<Leaf>();
            var slices = new List<Slice>();

            AddHinges(route.bridgeGateHinges, new[] { "B110_Surface", "B112_Surface" },
                new[] { "B111_Surface", "B113_Surface" }, new[] { "B179_Ink", "B180_Ink" }, leaves);
            AddHinges(route.sideGateHinges, new[] { "B115_Surface", "B117_Surface" },
                new[] { "B116_Surface", "B118_Surface" }, new[] { "B181_Ink", "B182_Ink" }, leaves);
            AddSlidingLeaves(renderers, "Mountain Storehouse", "B004_Surface", "B005_Surface", "B124_Ink",
                leaf => new[] { $"Storehouse_11_DoorLeaf{leaf}_Timber", $"Storehouse_12_DoorStraps{leaf}_Slate", $"Storehouse_13_Handle{leaf}_Gold" },
                leaves, slices);
            AddSlidingLeaves(renderers, "East Residence", "B012_Surface", "B013_Surface", "B129_Ink",
                leaf => new[] { $"Residence_15_SlidingScreen{leaf}_Backing_Paper", $"Residence_15_SlidingScreen{leaf}_Lattice_Timber", $"Residence_16_DoorKick{leaf}_Timber" },
                leaves, slices);

            foreach (var batch in slices.GroupBy(slice => slice.renderer))
                ApplySlices(batch.Key, batch.ToArray(), true);

            var pending = leaves.Where(leaf => !leaf.installed).ToArray();
            int triangleCount = slices.Sum(slice => slice.starts.Count);
            if (dryRun || pending.Length == 0)
                return Summary(pending.Length, leaves.Count, slices.Count, triangleCount, dryRun);

            if (!AssetDatabase.IsValidFolder(Folder))
                throw new InvalidOperationException("The Blender occlusion asset folder is missing.");

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            try
            {
                foreach (var batch in slices.GroupBy(slice => slice.renderer))
                    ApplySlices(batch.Key, batch.ToArray(), false);
                foreach (Leaf leaf in pending)
                {
                    if (leaf.surfaces.Count == 0)
                        throw new InvalidOperationException($"No fade surfaces for {leaf.anchor.name}.");
                    var group = Undo.AddComponent<SamuraiOcclusionGroup>(leaf.anchor.gameObject);
                    group.useVisualProbes = true;
                    group.surfaces = leaf.surfaces.ToArray();
                    EditorUtility.SetDirty(group);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
            return Summary(pending.Length, leaves.Count, slices.Count, triangleCount, false);
        }

        static string Summary(int pending, int total, int slices, int triangles, bool dryRun) =>
            $"Door leaves: {total} (4 moving, 4 fixed), {(dryRun ? "to install" : "installed")} {pending}, {slices} isolated slots, {triangles} existing triangles.";

        static Leaf PlanLeaf(Transform anchor)
        {
            var current = anchor.GetComponent<SamuraiOcclusionGroup>();
            if (!current)
                return new Leaf { anchor = anchor };
            if (!current.enabled || !current.useVisualProbes || current.blockers.Length != 0 ||
                current.surfaces.Length == 0 || current.surfaces.Any(surface => !surface.renderer ||
                    !surface.renderer.enabled || surface.renderer.gameObject.scene != anchor.gameObject.scene ||
                    surface.materialIndex < -1 || surface.materialIndex >= surface.renderer.sharedMaterials.Length))
                throw new InvalidOperationException($"Inspect existing door fade group on {anchor.name}.");
            return new Leaf { anchor = anchor, installed = true };
        }

        static void AddHinges(Transform[] hinges, string[] bodies, string[] fittings, string[] ink,
            List<Leaf> leaves)
        {
            if (hinges == null || hinges.Length != 2 || hinges.Any(hinge => !hinge || !hinge.gameObject.activeInHierarchy))
                throw new InvalidOperationException("Expected two active hinges on each existing keyed or bridge gate.");
            for (int i = 0; i < hinges.Length; i++)
            {
                Transform hinge = hinges[i];
                var body = VisibleRenderer(hinge.Find(bodies[i]));
                var trim = VisibleRenderer(hinge.Find(fittings[i]));
                var contour = VisibleRenderer(body.transform.Find(ink[i]));
                var selection = new[] { body, trim, contour };
                foreach (var renderer in selection)
                    foreach (var material in renderer.sharedMaterials)
                        RequireFadeMaterial(material);
                var leaf = PlanLeaf(hinge);
                if (leaf.installed)
                {
                    var group = hinge.GetComponent<SamuraiOcclusionGroup>();
                    if (group.surfaces.Length != selection.Length || selection.Any(renderer =>
                            !group.surfaces.Any(surface => surface.renderer == renderer && surface.materialIndex == -1)))
                        throw new InvalidOperationException($"Existing moving door group on {hinge.name} has different surfaces.");
                }
                else
                    leaf.surfaces.AddRange(selection.Select(renderer => new SamuraiOcclusionGroup.Surface(renderer)));
                leaves.Add(leaf);
            }
        }

        static MeshRenderer VisibleRenderer(Transform node)
        {
            if (!node || !node.gameObject.activeInHierarchy ||
                !node.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled || renderer.forceRenderingOff)
                throw new InvalidOperationException("Expected a visible Blender door renderer on its original anchor.");
            return renderer;
        }

        static void AddSlidingLeaves(ILookup<string, MeshRenderer> renderers, string building, string firstSurface,
            string secondSurface, string hull, Func<int, string[]> partNames, List<Leaf> leaves, List<Slice> slices)
        {
            var primary = renderers[firstSurface].Single();
            var trim = renderers[secondSurface].Single();
            var outline = renderers[hull].Single();
            if (primary.transform.parent.name != building || trim.transform.parent != primary.transform.parent ||
                outline.transform.parent != primary.transform)
                throw new InvalidOperationException($"Blender door mesh hierarchy changed in {building}.");

            var parts = Enumerable.Range(1, 2).Select(index => partNames(index)
                .Select(name => renderers["Surface - " + name].Single()).ToArray()).ToArray();
            var planned = parts.Select(part => PlanLeaf(part[0].transform.parent)).ToArray();
            leaves.AddRange(planned);
            if (planned.All(leaf => leaf.installed)) return;
            if (planned.Any(leaf => leaf.installed))
                throw new InvalidOperationException($"Incomplete door fade installation in {building}.");

            var visible = new[] { new Geometry(VisibleRenderer(primary.transform)),
                new Geometry(VisibleRenderer(trim.transform)), new Geometry(VisibleRenderer(outline.transform)) };
            for (int index = 0; index < planned.Length; index++)
            {
                Leaf leaf = planned[index];
                foreach (MeshRenderer part in parts[index])
                    foreach (MeshRenderer source in part.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (source.enabled && !source.forceRenderingOff)
                        {
                            foreach (var material in source.sharedMaterials) RequireFadeMaterial(material);
                            leaf.surfaces.Add(new SamuraiOcclusionGroup.Surface(source));
                            continue;
                        }
                        var old = new Geometry(source);
                        var candidates = source == part ? visible.Take(2) : visible.Skip(2);
                        MatchPart(leaf, old, candidates, slices, source == part);
                    }
            }
            AddCreaseInk(primary.transform.parent, planned, slices);
        }

        static void MatchPart(Leaf leaf, Geometry source, IEnumerable<Geometry> candidates,
            List<Slice> slices, bool recordEdges)
        {
            float sourceArea = 0f;
            foreach (int[] indices in source.indices)
                for (int i = 0; i < indices.Length; i += 3)
                    sourceArea += TriangleArea(source.points, indices, i);
            float matchedArea = 0f;
            var bounds = new Bounds(source.points[0], Vector3.zero);
            foreach (var point in source.points) bounds.Encapsulate(point);
            bounds.Expand(VertexTolerance * 2f);
            bool useNormals = source.normals.Length == source.points.Length;
            foreach (Geometry target in candidates)
            {
                var matches = new bool[target.points.Length];
                for (int point = 0; point < target.points.Length; point++)
                {
                    if (!bounds.Contains(target.points[point])) continue;
                    for (int original = 0; original < source.points.Length; original++)
                    {
                        if ((source.points[original] - target.points[point]).sqrMagnitude >
                            VertexTolerance * VertexTolerance ||
                            (useNormals && target.normals.Length == target.points.Length &&
                                Vector3.Dot(source.normals[original], target.normals[point]) < .999f))
                            continue;
                        matches[point] = true;
                        break;
                    }
                }
                for (int slot = 0; slot < target.indices.Length; slot++)
                {
                    var indices = target.indices[slot];
                    Slice slice = null;
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        if (!matches[indices[i]] || !matches[indices[i + 1]] || !matches[indices[i + 2]]) continue;
                        slice ??= FindSlice(slices, leaf, target.renderer, slot);
                        if (!slice.starts.Add(i))
                            throw new InvalidOperationException($"Overlapping original door pieces on {target.renderer.name}, slot {slot}.");
                        matchedArea += TriangleArea(target.points, indices, i);
                        if (recordEdges)
                            for (int j = 0; j < 3; j++)
                                AddEdge(leaf, target.points[indices[i + j]], target.points[indices[i + (j + 1) % 3]]);
                    }
                    if (slice != null) RequireFadeMaterial(target.materials[slot]);
                }
            }
            if (sourceArea <= 0f || Mathf.Abs(matchedArea - sourceArea) > Mathf.Max(.00002f, sourceArea * .015f))
                throw new InvalidOperationException($"{source.renderer.name} matches {matchedArea:F5} of {sourceArea:F5} square metres. Inspect the Blender door geometry.");
        }

        static float TriangleArea(Vector3[] points, int[] indices, int i) =>
            .5f * Vector3.Cross(points[indices[i + 1]] - points[indices[i]],
                points[indices[i + 2]] - points[indices[i]]).magnitude;

        static Slice FindSlice(List<Slice> slices, Leaf leaf, MeshRenderer renderer, int slot)
        {
            var current = slices.FirstOrDefault(slice => slice.leaf == leaf && slice.renderer == renderer && slice.slot == slot);
            if (current != null) return current;
            var result = new Slice { leaf = leaf, renderer = renderer, slot = slot };
            slices.Add(result);
            return result;
        }

        static void AddEdge(Leaf leaf, Vector3 a, Vector3 b)
        {
            Vector3Int first = Vector3Int.RoundToInt(a * 1000f);
            Vector3Int second = Vector3Int.RoundToInt(b * 1000f);
            leaf.edges.Add((first, second));
            leaf.edges.Add((second, first));
        }

        static void AddCreaseInk(Transform building, Leaf[] leaves, List<Slice> slices)
        {
            var ink = building.Cast<Transform>().SingleOrDefault(node =>
                node.name.StartsWith("Architectural crease ink -", StringComparison.Ordinal));
            if (!ink) return;
            var geometry = new Geometry(VisibleRenderer(ink));
            if (geometry.points.Length % 8 != 0)
                throw new InvalidOperationException($"Unexpected architectural ink geometry in {building.name}.");

            var owners = new Leaf[geometry.points.Length / 8];
            for (int stroke = 0; stroke < owners.Length; stroke++)
            {
                Vector3 start = Vector3.zero, end = Vector3.zero;
                for (int i = 0; i < 8; i += 2)
                {
                    start += geometry.points[stroke * 8 + i];
                    end += geometry.points[stroke * 8 + i + 1];
                }
                var edge = (Vector3Int.RoundToInt(start * 250f), Vector3Int.RoundToInt(end * 250f));
                owners[stroke] = leaves.SingleOrDefault(leaf => leaf.edges.Contains(edge));
            }
            if (owners.All(leaf => leaf == null)) return;

            var fadingInk = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/RidgeLodge_FadingInk.mat");
            RequireFadeMaterial(fadingInk);
            for (int slot = 0; slot < geometry.indices.Length; slot++)
            {
                int[] indices = geometry.indices[slot];
                var material = geometry.materials[slot];
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int stroke = indices[i] / 8;
                    if (indices[i + 1] / 8 != stroke || indices[i + 2] / 8 != stroke)
                        throw new InvalidOperationException($"A stroke crosses authored ink segments in {building.name}.");
                    if (owners[stroke] == null) continue;
                    if (!material || !material.HasProperty("_BaseColor") ||
                        fadingInk.GetColor("_BaseColor") != material.GetColor("_BaseColor"))
                        throw new InvalidOperationException($"Door ink color differs from the established fade ink in {building.name}.");
                    var slice = FindSlice(slices, owners[stroke], geometry.renderer, slot);
                    slice.substitute = fadingInk;
                    slice.starts.Add(i);
                }
            }
        }

        static void ApplySlices(MeshRenderer renderer, Slice[] slices, bool dryRun)
        {
            var claims = new HashSet<(int, int)>();
            foreach (Slice slice in slices)
                foreach (int start in slice.starts)
                    if (!claims.Add((slice.slot, start)))
                        throw new InvalidOperationException($"Two door leaves claim the same triangle on {renderer.name}.");
            string assetName = renderer.name.StartsWith("Architectural crease ink -", StringComparison.Ordinal)
                ? renderer.transform.parent.name.Replace(" ", "") + "Crease"
                : renderer.name;
            string assetPath = $"{Folder}/DoorLeaves_{assetName}.asset";
            var partitions = slices.Select(slice => new SamuraiOcclusionMeshSplit.Slice
            {
                sourceSlot = slice.slot, triangleStarts = slice.starts
            }).ToArray();
            int[] slots = SamuraiOcclusionMeshSplit.Apply(renderer, partitions, assetPath, dryRun);
            if (dryRun) return;

            var materials = renderer.sharedMaterials;
            bool alteredInk = false;
            for (int i = 0; i < slices.Length; i++)
            {
                slices[i].leaf.surfaces.Add(new SamuraiOcclusionGroup.Surface(renderer, slots[i]));
                if (!slices[i].substitute) continue;
                materials[slots[i]] = slices[i].substitute;
                alteredInk = true;
            }
            if (!alteredInk) return;
            Undo.RecordObject(renderer, Action);
            renderer.sharedMaterials = materials;
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            if (mesh.normals.Length == 0)
            {
                mesh.normals = Enumerable.Repeat(Vector3.up, mesh.vertexCount).ToArray();
                EditorUtility.SetDirty(mesh);
                AssetDatabase.SaveAssetIfDirty(mesh);
            }
        }

        static void RequireFadeMaterial(Material material)
        {
            if (!material || material.FindPass("SamuraiOcclusionForeground") < 0)
                throw new InvalidOperationException($"Door material {material?.name} lacks the camera fade pass.");
        }
    }
}
