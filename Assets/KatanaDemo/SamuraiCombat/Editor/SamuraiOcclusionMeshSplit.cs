using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Partition a Blender render batch without changing any source triangle or existing slot index.</summary>
    internal static class SamuraiOcclusionMeshSplit
    {
        internal sealed class Slice
        {
            public int sourceSlot;
            public HashSet<int> triangleStarts;
        }

        internal static int[] Apply(MeshRenderer renderer, IReadOnlyList<Slice> slices, string assetPath, bool dryRun)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh source = filter ? filter.sharedMesh : null;
            Material[] materials = renderer.sharedMaterials;
            if (!source || !source.isReadable || source.subMeshCount != materials.Length)
                throw new InvalidOperationException($"Cannot split {renderer.name} with its current mesh and materials.");

            var sourceIndices = new Dictionary<int, int[]>();
            var claimed = new Dictionary<int, HashSet<int>>();
            var appended = new List<int[]>(slices.Count);
            var outputSlots = new int[slices.Count];
            for (int i = 0; i < slices.Count; i++)
            {
                Slice slice = slices[i];
                if (slice.sourceSlot < 0 || slice.sourceSlot >= source.subMeshCount || slice.triangleStarts == null ||
                    slice.triangleStarts.Count == 0 || source.GetTopology(slice.sourceSlot) != MeshTopology.Triangles)
                    throw new InvalidOperationException($"Invalid triangle slice {i} on {renderer.name}.");
                if (!sourceIndices.TryGetValue(slice.sourceSlot, out int[] indices))
                {
                    sourceIndices.Add(slice.sourceSlot, indices = source.GetTriangles(slice.sourceSlot));
                    claimed.Add(slice.sourceSlot, new HashSet<int>());
                }
                var triangles = new List<int>(slice.triangleStarts.Count * 3);
                foreach (int start in slice.triangleStarts.OrderBy(value => value))
                {
                    if (start < 0 || start % 3 != 0 || start > indices.Length - 3 || !claimed[slice.sourceSlot].Add(start))
                        throw new InvalidOperationException($"Invalid or overlapping triangle {start} in {renderer.name}[{slice.sourceSlot}].");
                    triangles.Add(indices[start]);
                    triangles.Add(indices[start + 1]);
                    triangles.Add(indices[start + 2]);
                }
                appended.Add(triangles.ToArray());
                outputSlots[i] = source.subMeshCount + i;
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath))
                throw new InvalidOperationException($"Preserve and inspect the existing mesh at {assetPath} before rebinding.");
            if (dryRun || slices.Count == 0) return outputSlots;

            Mesh split = Object.Instantiate(source);
            try
            {
                split.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                split.subMeshCount += slices.Count;
                foreach (var pair in sourceIndices)
                {
                    var removed = claimed[pair.Key];
                    int[] indices = pair.Value;
                    var retained = new List<int>(indices.Length - removed.Count * 3);
                    for (int start = 0; start < indices.Length; start += 3)
                    {
                        if (removed.Contains(start)) continue;
                        retained.Add(indices[start]);
                        retained.Add(indices[start + 1]);
                        retained.Add(indices[start + 2]);
                    }
                    split.SetTriangles(retained, pair.Key, false);
                }
                for (int i = 0; i < appended.Count; i++)
                    split.SetTriangles(appended[i], outputSlots[i], false);
                split.bounds = source.bounds;

                long originalCount = Enumerable.Range(0, source.subMeshCount).Sum(index => (long)source.GetIndexCount(index));
                long splitCount = Enumerable.Range(0, split.subMeshCount).Sum(index => (long)split.GetIndexCount(index));
                if (originalCount != splitCount || source.vertexCount != split.vertexCount)
                    throw new InvalidOperationException($"Geometry changed while partitioning {renderer.name}.");

                Material[] assigned = materials.Concat(slices.Select(slice => materials[slice.sourceSlot])).ToArray();
                AssetDatabase.CreateAsset(split, assetPath);
                Undo.RecordObjects(new Object[] { filter, renderer }, "Split camera occlusion materials");
                filter.sharedMesh = split;
                renderer.sharedMaterials = assigned;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                return outputSlots;
            }
            finally
            {
                if (split && !EditorUtility.IsPersistent(split)) Object.DestroyImmediate(split);
            }
        }
    }
}
