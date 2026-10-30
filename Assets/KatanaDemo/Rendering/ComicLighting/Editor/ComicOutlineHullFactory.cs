using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.LookDev.Editor
{
    /// <summary>
    /// Creates outline-only copies. Position welding is logical: vertex indices,
    /// UVs, topology, bind poses, complete skin weights and blend shapes are retained.
    /// Only the copy's normal channel is replaced. No source importer is changed.
    /// </summary>
    public static class ComicOutlineHullFactory
    {
        const string CacheFolder = "Assets/KatanaDemo/LookDev/ComicLighting/Meshes/OutlineHulls";
        const string CacheOwner = "ComicOutlineHullFactory";
        const int CacheVersion = 1;
        const double WeightPrecision = 1000000.0;

        [Serializable]
        public sealed class Diagnostics
        {
            public string sourceName;
            public string sourceGuid;
            public long sourceLocalFileId;
            public string sourceFingerprint;
            public string outlineAssetPath;
            public bool reusedCache;
            public int vertexCount;
            public int triangleCount;
            public int positionGroups;
            public int coincidentPositionGroups;
            public int compatibleGroups;
            public int joinedGroups;
            public int joinedVertices;
            public int sourceNormalSplitGroups;
            public int weightSplitPositionGroups;
            public int fallbackNormalGroups;
            public int degenerateTriangles;
            public int skippedNonTriangleSubmeshes;
            public int changedNormalVertices;
            public int blendShapeCount;
            public float positionTolerance;
        }

        [Serializable]
        sealed class CacheStamp
        {
            public string owner;
            public int version;
            public string sourceFingerprint;
            public Diagnostics diagnostics;
        }

        sealed class InputData
        {
            public Vector3[] positions;
            public Vector3[] normals;
            public byte[] bonesPerVertex;
            public BoneWeight1[] weights;
            public int[][] triangles;
            public string fingerprint;
            public int blendShapeCount;
            public int skippedSubmeshes;
        }

        sealed class PositionGroup
        {
            public Vector3 position;
            public readonly List<int> vertices = new List<int>();
        }

        sealed class NormalGroup
        {
            public readonly List<int> vertices = new List<int>();
            public Vector3 weightedNormal;
        }

        readonly struct Cell : IEquatable<Cell>
        {
            readonly long x, y, z;
            public Cell(long x, long y, long z) { this.x = x; this.y = y; this.z = z; }
            public Cell Offset(int dx, int dy, int dz) => new Cell(x + dx, y + dy, z + dz);
            public bool Equals(Cell other) => x == other.x && y == other.y && z == other.z;
            public override bool Equals(object obj) => obj is Cell other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return ((x.GetHashCode() * 397) ^ y.GetHashCode()) * 397 ^ z.GetHashCode(); }
            }
        }

        public static Mesh GetOrCreate(Mesh source)
        {
            return GetOrCreate(source, out _);
        }

        public static Mesh GetOrCreate(Mesh source, out Diagnostics diagnostics)
        {
            return GetOrCreate(source, CacheFolder, out diagnostics);
        }

        public static Mesh GetOrCreate(Mesh source, string cacheFolder, out Diagnostics diagnostics)
        {
            if (string.IsNullOrEmpty(cacheFolder) || !cacheFolder.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Use a project asset folder.", nameof(cacheFolder));
            if (!source) throw new ArgumentNullException(nameof(source));
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Create comic outline assets in Edit mode only.");
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId)
                || string.IsNullOrEmpty(guid))
                throw new ArgumentException("The source mesh must be a saved asset or an imported mesh subasset.", nameof(source));

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (sourcePath.StartsWith(cacheFolder + "/", StringComparison.Ordinal))
                throw new ArgumentException("Pass the original renderer mesh, not a generated outline copy.", nameof(source));

            string id = localId.ToString(CultureInfo.InvariantCulture).Replace("-", "n");
            string path = cacheFolder + "/" + guid + "_" + id + ".asset";
            InputData input = ReadInput(source);
            Mesh cached = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            CacheStamp stamp = ReadStamp(path);
            if (cached && stamp != null && stamp.owner == CacheOwner && stamp.version == CacheVersion
                && stamp.sourceFingerprint == input.fingerprint && stamp.diagnostics != null)
            {
                diagnostics = stamp.diagnostics;
                diagnostics.reusedCache = true;
                return cached;
            }

            Vector3[] normals = BuildNormals(input, out diagnostics);
            diagnostics.sourceName = source.name;
            diagnostics.sourceGuid = guid;
            diagnostics.sourceLocalFileId = localId;
            diagnostics.sourceFingerprint = input.fingerprint;
            diagnostics.outlineAssetPath = path;
            EnsureFolder(cacheFolder);

            if (!cached)
            {
                cached = UnityEngine.Object.Instantiate(source);
                cached.name = source.name + " - comic outline hull";
                cached.normals = normals;
                AssetDatabase.CreateAsset(cached, path);
            }
            else
            {
                // Copy all native mesh data, including modern skin weights and
                // every blend-shape frame. Preserve the existing asset identity.
                EditorUtility.CopySerialized(source, cached);
                cached.name = source.name + " - comic outline hull";
                cached.normals = normals;
                EditorUtility.SetDirty(cached);
            }
            AssetDatabase.SaveAssetIfDirty(cached);
            var importer = AssetImporter.GetAtPath(path);
            if (!importer) throw new InvalidOperationException("Outline asset importer was not created: " + path);
            importer.userData = JsonUtility.ToJson(new CacheStamp
            {
                owner = CacheOwner, version = CacheVersion,
                sourceFingerprint = input.fingerprint, diagnostics = diagnostics
            });
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(path);
            return cached;
        }

        /// <summary>Reports weld groups without creating or changing any asset.</summary>
        public static Diagnostics Analyze(Mesh source)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            InputData input = ReadInput(source);
            BuildNormals(input, out Diagnostics diagnostics);
            diagnostics.sourceName = source.name;
            diagnostics.sourceFingerprint = input.fingerprint;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
            {
                diagnostics.sourceGuid = guid;
                diagnostics.sourceLocalFileId = localId;
            }
            return diagnostics;
        }

        static CacheStamp ReadStamp(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (!importer || string.IsNullOrEmpty(importer.userData)) return null;
            try { return JsonUtility.FromJson<CacheStamp>(importer.userData); }
            catch (ArgumentException) { return null; }
        }

        static InputData ReadInput(Mesh source)
        {
            var input = new InputData { blendShapeCount = source.blendShapeCount };
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                // An actual data fingerprint also catches unsaved source edits.
                // Dependency hashes alone miss those and can change unnecessarily
                // when another subasset of the source model is edited.
                writer.Write(source.name);
                writer.Write(source.vertexCount);
                writer.Write((int)source.indexFormat);
                Write(writer, source.bounds.center);
                Write(writer, source.bounds.size);
                var attributes = source.GetVertexAttributes();
                writer.Write(attributes.Length);
                foreach (var a in attributes)
                {
                    writer.Write((int)a.attribute); writer.Write((int)a.format);
                    writer.Write(a.dimension); writer.Write(a.stream);
                }
                using (var meshes = MeshUtility.AcquireReadOnlyMeshData(source))
                {
                    Mesh.MeshData data = meshes[0];
                    using (var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    using (var normals = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetVertices(vertices);
                        if (data.HasVertexAttribute(VertexAttribute.Normal)) data.GetNormals(normals);
                        input.positions = vertices.ToArray();
                        input.normals = normals.ToArray();
                    }
                    writer.Write(data.vertexBufferCount);
                    for (int i = 0; i < data.vertexBufferCount; i++)
                    {
                        byte[] bytes = data.GetVertexData<byte>(i).ToArray();
                        writer.Write(bytes.Length); writer.Write(bytes);
                    }
                    byte[] indexBytes = data.GetIndexData<byte>().ToArray();
                    writer.Write(indexBytes.Length); writer.Write(indexBytes);
                    writer.Write(data.subMeshCount);
                    input.triangles = new int[data.subMeshCount][];
                    for (int i = 0; i < data.subMeshCount; i++)
                    {
                        SubMeshDescriptor submesh = data.GetSubMesh(i);
                        writer.Write((int)submesh.topology); writer.Write(submesh.baseVertex);
                        writer.Write(submesh.indexStart); writer.Write(submesh.indexCount);
                        writer.Write(submesh.firstVertex); writer.Write(submesh.vertexCount);
                        Write(writer, submesh.bounds.center); Write(writer, submesh.bounds.size);
                        if (submesh.topology != MeshTopology.Triangles)
                        {
                            input.triangles[i] = Array.Empty<int>();
                            input.skippedSubmeshes++;
                            continue;
                        }
                        using (var indices = new NativeArray<int>(submesh.indexCount, Allocator.Temp))
                        {
                            data.GetIndices(indices, i, true);
                            input.triangles[i] = indices.ToArray();
                        }
                    }
                }

                // These arrays borrow the mesh's storage; do not Dispose them.
                var boneCounts = source.GetBonesPerVertex();
                var boneWeights = source.GetAllBoneWeights();
                input.bonesPerVertex = boneCounts.IsCreated ? boneCounts.ToArray() : Array.Empty<byte>();
                input.weights = boneWeights.IsCreated ? boneWeights.ToArray() : Array.Empty<BoneWeight1>();
                if (input.bonesPerVertex.Length == 0)
                    input.bonesPerVertex = new byte[input.positions.Length];
                if (input.bonesPerVertex.Length != input.positions.Length)
                    throw new InvalidOperationException("Unexpected skin-weight count in " + source.name);
                int weightCount = 0;
                foreach (byte count in input.bonesPerVertex) weightCount += count;
                if (weightCount != input.weights.Length)
                    throw new InvalidOperationException("Incomplete skin-weight data in " + source.name);
                writer.Write(input.bonesPerVertex.Length); writer.Write(input.bonesPerVertex);
                writer.Write(input.weights.Length);
                foreach (var weight in input.weights)
                {
                    writer.Write(weight.boneIndex); writer.Write(weight.weight);
                }
                Matrix4x4[] bindposes = source.bindposes;
                writer.Write(bindposes.Length);
                foreach (var matrix in bindposes)
                    for (int i = 0; i < 16; i++) writer.Write(matrix[i]);

                writer.Write(source.blendShapeCount);
                if (source.blendShapeCount > 0)
                {
                    var deltaVertices = new Vector3[source.vertexCount];
                    var deltaNormals = new Vector3[source.vertexCount];
                    var deltaTangents = new Vector3[source.vertexCount];
                    for (int shape = 0; shape < source.blendShapeCount; shape++)
                    {
                        writer.Write(source.GetBlendShapeName(shape));
                        int frames = source.GetBlendShapeFrameCount(shape);
                        writer.Write(frames);
                        for (int frame = 0; frame < frames; frame++)
                        {
                            writer.Write(source.GetBlendShapeFrameWeight(shape, frame));
                            source.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals, deltaTangents);
                            for (int vertex = 0; vertex < source.vertexCount; vertex++)
                            {
                                Write(writer, deltaVertices[vertex]);
                                Write(writer, deltaNormals[vertex]);
                                Write(writer, deltaTangents[vertex]);
                            }
                        }
                    }
                }
                writer.Flush();
                stream.Position = 0;
                using (var sha = SHA256.Create())
                    input.fingerprint = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
            return input;
        }

        static Vector3[] BuildNormals(InputData input, out Diagnostics diagnostics)
        {
            Vector3[] vertices = input.positions;
            diagnostics = new Diagnostics
            {
                vertexCount = vertices.Length, blendShapeCount = input.blendShapeCount,
                skippedNonTriangleSubmeshes = input.skippedSubmeshes
            };
            if (vertices.Length == 0) return Array.Empty<Vector3>();
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (var vertex in vertices) bounds.Encapsulate(vertex);
            float tolerance = Mathf.Max(0.0000001f, bounds.size.magnitude * 0.000001f);
            diagnostics.positionTolerance = tolerance;
            float toleranceSquared = tolerance * tolerance;
            var cells = new Dictionary<Cell, List<int>>();
            var positions = new List<PositionGroup>();
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                var cell = new Cell((long)Math.Floor(vertex.x / tolerance),
                    (long)Math.Floor(vertex.y / tolerance), (long)Math.Floor(vertex.z / tolerance));
                int found = -1;
                for (int dx = -1; dx <= 1 && found < 0; dx++)
                for (int dy = -1; dy <= 1 && found < 0; dy++)
                for (int dz = -1; dz <= 1 && found < 0; dz++)
                {
                    if (!cells.TryGetValue(cell.Offset(dx, dy, dz), out var candidates)) continue;
                    foreach (int candidate in candidates)
                        if ((positions[candidate].position - vertex).sqrMagnitude <= toleranceSquared)
                        { found = candidate; break; }
                }
                if (found < 0)
                {
                    found = positions.Count;
                    positions.Add(new PositionGroup { position = vertex });
                    if (!cells.TryGetValue(cell, out var list)) cells.Add(cell, list = new List<int>());
                    list.Add(found);
                }
                positions[found].vertices.Add(i);
            }
            diagnostics.positionGroups = positions.Count;

            string[] weightKeys = BuildWeightKeys(input);
            int[] groupForVertex = new int[vertices.Length];
            var groups = new List<NormalGroup>();
            foreach (var position in positions)
            {
                if (position.vertices.Count > 1) diagnostics.coincidentPositionGroups++;
                var compatible = new Dictionary<string, int>();
                foreach (int vertex in position.vertices)
                {
                    string key = weightKeys[vertex];
                    if (!compatible.TryGetValue(key, out int groupIndex))
                    {
                        groupIndex = groups.Count;
                        groups.Add(new NormalGroup());
                        compatible.Add(key, groupIndex);
                    }
                    groups[groupIndex].vertices.Add(vertex);
                    groupForVertex[vertex] = groupIndex;
                }
                if (compatible.Count > 1) diagnostics.weightSplitPositionGroups++;
            }
            diagnostics.compatibleGroups = groups.Count;
            foreach (var group in groups)
            {
                if (group.vertices.Count <= 1) continue;
                diagnostics.joinedGroups++;
                diagnostics.joinedVertices += group.vertices.Count;
                Vector3 first = input.normals[group.vertices[0]].normalized;
                foreach (int index in group.vertices)
                    if (Vector3.Dot(first, input.normals[index].normalized) < 0.999f)
                    { diagnostics.sourceNormalSplitGroups++; break; }
            }

            foreach (int[] indices in input.triangles)
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                    throw new InvalidOperationException("Out-of-range source triangle in outline mesh.");
                diagnostics.triangleCount++;
                Vector3 ab = vertices[b] - vertices[a], ac = vertices[c] - vertices[a];
                Vector3 cross = Vector3.Cross(ab, ac);
                float twiceArea = cross.magnitude;
                if (twiceArea <= 0.000000000001f)
                { diagnostics.degenerateTriangles++; continue; }
                Vector3 faceNormal = cross / twiceArea;
                groups[groupForVertex[a]].weightedNormal += faceNormal * CornerAngle(ab, ac);
                groups[groupForVertex[b]].weightedNormal += faceNormal * CornerAngle(-ab, vertices[c] - vertices[b]);
                groups[groupForVertex[c]].weightedNormal += faceNormal * CornerAngle(-ac, vertices[b] - vertices[c]);
            }

            var result = new Vector3[vertices.Length];
            foreach (var group in groups)
            {
                Vector3 normal = group.weightedNormal;
                if (normal.sqrMagnitude < 0.0000000001f)
                {
                    diagnostics.fallbackNormalGroups++;
                    foreach (int vertex in group.vertices) normal += input.normals[vertex];
                    if (normal.sqrMagnitude < 0.0000000001f)
                        normal = input.normals[group.vertices[0]];
                    if (normal.sqrMagnitude < 0.0000000001f) normal = Vector3.up;
                }
                normal.Normalize();
                foreach (int vertex in group.vertices)
                {
                    result[vertex] = normal;
                    if ((normal - input.normals[vertex]).sqrMagnitude > 0.00000001f)
                        diagnostics.changedNormalVertices++;
                }
            }
            return result;
        }

        static string[] BuildWeightKeys(InputData input)
        {
            var result = new string[input.positions.Length];
            int offset = 0;
            for (int vertex = 0; vertex < result.Length; vertex++)
            {
                // Canonicalize ordering and duplicate bone IDs; never truncate to
                // the legacy four-weight format. Quantization is conservative.
                var influences = new SortedDictionary<int, double>();
                int count = input.bonesPerVertex[vertex];
                for (int i = 0; i < count; i++)
                {
                    BoneWeight1 weight = input.weights[offset++];
                    if (weight.weight <= 0) continue;
                    influences.TryGetValue(weight.boneIndex, out double sum);
                    influences[weight.boneIndex] = sum + weight.weight;
                }
                var key = new StringBuilder();
                foreach (var influence in influences)
                {
                    long quantized = (long)Math.Round(influence.Value * WeightPrecision);
                    if (quantized == 0) continue;
                    key.Append(influence.Key.ToString(CultureInfo.InvariantCulture)).Append(':')
                        .Append(quantized.ToString(CultureInfo.InvariantCulture)).Append(';');
                }
                result[vertex] = key.ToString();
            }
            return result;
        }

        static float CornerAngle(Vector3 first, Vector3 second)
        {
            return Mathf.Atan2(Vector3.Cross(first, second).magnitude, Vector3.Dot(first, second));
        }

        static void Write(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x); writer.Write(value.y); writer.Write(value.z);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }
    }
}
