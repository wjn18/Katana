using System;
using System.Globalization;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Make a readable, position-only view probe without modifying the render import or collider.</summary>
    internal static class SamuraiOcclusionProbeMesh
    {
        const string Parent = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/BlenderAssembly/Occlusion";
        const string Folder = Parent + "/VisualProbes";

        internal static Mesh Readable(Mesh source)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            if (source.isReadable) return source;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
                throw new InvalidOperationException($"Cannot identify the source of {source.name}.");

            string sourcePath = AssetDatabase.GetAssetPath(source);
            string revision = AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
            string path = $"{Folder}/{guid}_{localId.ToString(CultureInfo.InvariantCulture)}_{revision}.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) return existing;

            var probe = new Mesh
            {
                name = source.name + " camera probe",
                indexFormat = source.vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : source.indexFormat
            };
            try
            {
                using (var meshes = MeshUtility.AcquireReadOnlyMeshData(source))
                {
                    var data = meshes[0];
                    using (var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetVertices(positions);
                        probe.SetVertices(positions);
                    }
                    probe.subMeshCount = data.subMeshCount;
                    for (int i = 0; i < data.subMeshCount; i++)
                    {
                        var slot = data.GetSubMesh(i);
                        using (var indices = new NativeArray<int>(slot.indexCount, Allocator.Temp))
                        {
                            data.GetIndices(indices, i, true);
                            probe.SetIndices(indices.ToArray(), slot.topology, i, false);
                        }
                    }
                }
                probe.bounds = source.bounds;
                if (!AssetDatabase.IsValidFolder(Folder))
                    AssetDatabase.CreateFolder(Parent, "VisualProbes");
                AssetDatabase.CreateAsset(probe, path);
                return probe;
            }
            finally
            {
                if (probe && !EditorUtility.IsPersistent(probe)) Object.DestroyImmediate(probe);
            }
        }
    }
}
