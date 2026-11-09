using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Applies a relative lower-landing elevation change to the installed bridge.</summary>
    public static class SamuraiLowerCourtBridgeGrade
    {
        const float UpperX = -13.72f;
        const float LowerX = .70f;
        const string StoneGroup = "05 Stone abutments - 两端石座";
        const string UpperLanding = "Upper stone landing - 上层石座";
        const string LowerLanding = "Lower stone landing - 下层石座";
        const string UndoName = "Grade bridge to lower courtyard";

        /// <returns>The number of replaced render meshes, excluding the rigid landings.</returns>
        public static int Apply(Transform bridge, float delta, string meshFolder)
        {
            if (!bridge) throw new ArgumentNullException(nameof(bridge));
            if (float.IsNaN(delta) || float.IsInfinity(delta))
                throw new ArgumentOutOfRangeException(nameof(delta), "The bridge elevation delta must be finite.");
            if (delta == 0f) return 0;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Grade the bridge in Edit mode.");

            Transform upper = bridge.Find(StoneGroup + "/" + UpperLanding);
            Transform lower = bridge.Find(StoneGroup + "/" + LowerLanding);
            if (!upper || !lower)
                throw new InvalidOperationException("The original upper and lower bridge landings are required.");

            string folder = EnsureFolder(meshFolder);
            var surfaces = new List<(MeshFilter Filter, Mesh Mesh)>();
            var collisions = new List<(MeshCollider Collider, Mesh Mesh)>();
            foreach (MeshFilter filter in bridge.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh || !filter.TryGetComponent(out Renderer renderer) || !renderer.enabled)
                    continue;
                Transform frame = filter.transform;
                if (frame == upper || frame == lower || frame.IsChildOf(upper) || frame.IsChildOf(lower))
                    continue;

                Mesh source = filter.sharedMesh;
                Mesh graded = GradedMesh(source, frame, delta, folder);
                surfaces.Add((filter, graded));
                foreach (MeshCollider collider in filter.GetComponents<MeshCollider>())
                {
                    if (!collider.sharedMesh) continue;
                    // Preserve a hand-authored collision mesh as well as the usual
                    // shared surface/collision asset.
                    Mesh collision = collider.sharedMesh == source
                        ? graded : GradedMesh(collider.sharedMesh, frame, delta, folder);
                    collisions.Add((collider, collision));
                }
            }
            if (surfaces.Count == 0)
                throw new InvalidOperationException("No active bridge meshes were found to grade.");

            // Build all new meshes before changing any scene references.
            foreach (var part in surfaces)
            {
                Undo.RecordObject(part.Filter, UndoName);
                part.Filter.sharedMesh = part.Mesh;
                RecordOverride(part.Filter);
            }
            foreach (var part in collisions)
            {
                Undo.RecordObject(part.Collider, UndoName);
                part.Collider.sharedMesh = part.Mesh;
                RecordOverride(part.Collider);
            }
            Undo.RecordObject(lower, UndoName);
            lower.position += Vector3.up * delta;
            RecordOverride(lower);
            return surfaces.Count;
        }

        static Mesh GradedMesh(Mesh source, Transform frame, float delta, string folder)
        {
            Matrix4x4 toWorld = frame.localToWorldMatrix;
            if (Mathf.Abs(toWorld.determinant) < 1e-10f)
                throw new InvalidOperationException("A bridge mesh has a singular transform: " + frame.name);
            string path = folder + "/BridgeGrade_" + Fingerprint(source, toWorld, delta) + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved) return saved;

            Mesh mesh = ReadableClone(source);
            mesh.name = source.name + " - lower court grade";
            try
            {
                Deform(mesh, toWorld, frame.worldToLocalMatrix, delta);
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            catch
            {
                if (!AssetDatabase.Contains(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }

        static void Deform(Mesh mesh, Matrix4x4 toWorld, Matrix4x4 toLocal, float delta)
        {
            Vector3[] points = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            bool hasNormals = normals.Length == points.Length;
            bool hasTangents = tangents.Length == points.Length;
            Matrix4x4 normalToWorld = toLocal.transpose;
            Matrix4x4 normalToLocal = toWorld.transpose;
            float span = LowerX - UpperX;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 world = toWorld.MultiplyPoint3x4(points[i]);
                float t = Mathf.Clamp01((world.x - UpperX) / span);
                float shift = delta * t * t * (3f - 2f * t);
                if (shift != 0f)
                {
                    world.y += shift;
                    points[i] = toLocal.MultiplyPoint3x4(world);
                }

                float slope = delta * 6f * t * (1f - t) / span;
                if (slope == 0f) continue;
                // Inverse-transpose for the vertical shear. This preserves
                // authored hard normals and the outward normals on ink hulls.
                if (hasNormals)
                {
                    Vector3 normal = normalToWorld.MultiplyVector(normals[i]);
                    normal.x -= slope * normal.y;
                    normals[i] = normalToLocal.MultiplyVector(normal).normalized;
                }
                if (hasTangents)
                {
                    Vector4 original = tangents[i];
                    Vector3 tangent = toWorld.MultiplyVector(new Vector3(original.x, original.y, original.z));
                    tangent.y += slope * tangent.x;
                    tangent = toLocal.MultiplyVector(tangent).normalized;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, original.w);
                }
            }
            mesh.vertices = points;
            if (hasNormals) mesh.normals = normals;
            if (hasTangents) mesh.tangents = tangents;
            mesh.RecalculateBounds();
        }

        static Mesh ReadableClone(Mesh source)
        {
            if (source.isReadable) return UnityEngine.Object.Instantiate(source);

            // The bridge FBX is imported with Read/Write off. Copy the complete
            // editor mesh buffers instead of changing the source importer.
            var clone = new Mesh();
            using (var inputMeshes = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                Mesh.MeshData input = inputMeshes[0];
                var outputMeshes = Mesh.AllocateWritableMeshData(1);
                bool complete = false;
                try
                {
                    Mesh.MeshData output = outputMeshes[0];
                    output.SetVertexBufferParams(input.vertexCount, source.GetVertexAttributes());
                    for (int stream = 0; stream < input.vertexBufferCount; stream++)
                        input.GetVertexData<byte>(stream).CopyTo(output.GetVertexData<byte>(stream));

                    var indexBytes = input.GetIndexData<byte>();
                    int indexSize = source.indexFormat == IndexFormat.UInt16 ? 2 : 4;
                    output.SetIndexBufferParams(indexBytes.Length / indexSize, source.indexFormat);
                    indexBytes.CopyTo(output.GetIndexData<byte>());
                    output.subMeshCount = input.subMeshCount;
                    const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
                    for (int i = 0; i < input.subMeshCount; i++) output.SetSubMesh(i, input.GetSubMesh(i), flags);
                    Mesh.ApplyAndDisposeWritableMeshData(outputMeshes, clone, flags);
                    complete = true;
                }
                finally
                {
                    if (!complete)
                    {
                        outputMeshes.Dispose();
                        UnityEngine.Object.DestroyImmediate(clone);
                    }
                }
            }
            clone.bounds = source.bounds;
            return clone;
        }

        static string Fingerprint(Mesh mesh, Matrix4x4 frame, float delta)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId))
                throw new InvalidOperationException("Save the source bridge mesh before grading it: " + mesh.name);
            var key = new StringBuilder("SamuraiBridgeGrade1|");
            key.Append(guid).Append('|').Append(localId).Append('|');
            key.Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(mesh))).Append('|');
            key.Append(delta.ToString("R", CultureInfo.InvariantCulture));
            for (int i = 0; i < 16; i++) key.Append('|').Append(frame[i].ToString("R", CultureInfo.InvariantCulture));
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key.ToString()))).Replace("-", "").Substring(0, 16);
        }

        static string EnsureFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A bridge mesh asset folder is required.");
            string[] parts = path.Replace('\\', '/').TrimEnd('/').Split('/');
            if (parts[0] != "Assets") throw new ArgumentException("Use a bridge mesh folder under Assets.");
            string parent = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string name = parts[i];
                if (name.Length == 0 || name == "." || name == "..") throw new ArgumentException("Invalid bridge mesh folder.");
                string child = parent + "/" + name;
                if (!AssetDatabase.IsValidFolder(child) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, name)))
                    throw new InvalidOperationException("Could not create bridge mesh folder " + child);
                parent = child;
            }
            return parent;
        }

        static void RecordOverride(UnityEngine.Object component)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }
}
