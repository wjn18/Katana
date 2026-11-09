using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OCDesign.LookDev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiBlenderAssemblyImport
    {
        public const string ScenePath = "Assets/KatanaDemo/SamuraiCombat/Scenes/SamuraiCombatDemo_BlenderAssembly.unity";
        const string SourceScenePath = SamuraiComicStyleSetup.ScenePath;
        const string AssetFolder = SamuraiComicStyleSetup.Root + "/BlenderAssembly";
        const string MeshAssetPath = AssetFolder + "/CourtyardAssembly.asset";
        const string RootName = "Blender architecture - Blender 建筑装配";
        const string SaveSuffix = ".BlenderAssembly";

        sealed class Group
        {
            public string Key, Name, Anchor, Body;
            public string[] Members;
            public int Layer;
            public ShadowCastingMode Shadow;
            public bool Receive;
            public LightProbeUsage? LightProbe;
            public ReflectionProbeUsage? ReflectionProbe;
            public uint? RenderingLayer;
            public Mesh Mesh;
            public Material[] Materials;
            public MeshRenderer[] OriginalRenderers;
            public Transform Node;
        }

        sealed class SceneNode
        {
            public string Name, Components;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public int Layer;
            public bool Active;
        }

        [MenuItem("Katana/Level/Import Blender Courtyard As New Scene")]
        static void ImportFromMenu()
        {
            string geometry = EditorUtility.OpenFilePanel("Choose the assembled Blender geometry", "", "bin");
            if (!string.IsNullOrEmpty(geometry)) Debug.Log(Build(geometry));
        }

        public static string Build(string geometryPath)
        {
            Scene source = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || source.path != SourceScenePath ||
                source.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Open only the saved SamuraiCombatDemo scene in Edit mode.");
            if (AssetDatabase.LoadMainAssetAtPath(ScenePath) || File.Exists(ScenePath) ||
                AssetDatabase.LoadMainAssetAtPath(MeshAssetPath) || File.Exists(MeshAssetPath))
                throw new InvalidOperationException("The Blender assembly destination already exists. Preserve it before creating another copy.");

            string manifestPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(geometryPath)), "groups.tsv");
            List<Group> groups = ReadGroups(manifestPath);
            Dictionary<string, SceneNode> baseline = Snapshot(source);
            int colliderCount = source.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Collider>(true).Length);
            int originalRendererCount = source.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Renderer>(true).Length);
            string sourceHash = FileHash(SourceScenePath);
            Scene target = default;
            bool madeScene = false, madeAsset = false;
            try
            {
                ResolveSources(source, groups);
                if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
                    throw new IOException("Unity could not copy the original scene.");
                madeScene = true;
                target = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                if (!target.IsValid() || !target.isLoaded || target.path != ScenePath)
                    throw new IOException("Unity could not open the copied scene.");
                ValidateSnapshot(target, baseline, allowNew: false);
                ResolveSources(target, groups);
                ReadGeometry(geometryPath, groups);
                ReparentMovingMeshes(target, groups);

                if (!AssetDatabase.IsValidFolder(AssetFolder))
                    AssetDatabase.CreateFolder(SamuraiComicStyleSetup.Root, "BlenderAssembly");
                AssetDatabase.StartAssetEditing();
                try
                {
                    AssetDatabase.CreateAsset(groups[0].Mesh, MeshAssetPath);
                    madeAsset = true;
                    foreach (Group group in groups.Skip(1)) AssetDatabase.AddObjectToAsset(group.Mesh, MeshAssetPath);
                }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.SaveAssets();

                Install(target, groups);
                IsolateProgress(target);
                ValidateSnapshot(target, baseline, allowNew: true);
                ValidateRendering(target, groups, originalRendererCount, colliderCount);
                foreach (var outline in target.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ComicScreenOutline>(true)))
                    outline.RefreshSources();
                if (FileHash(SourceScenePath) != sourceHash) throw new IOException("The original scene changed during the assembly.");
                EditorSceneManager.MarkSceneDirty(target);
                if (!EditorSceneManager.SaveScene(target)) throw new IOException("Unity did not save the assembled scene.");

                int retired = groups.Sum(group => group.OriginalRenderers.Length);
                int triangles = 0;
                foreach (Group group in groups)
                    for (int subMesh = 0; subMesh < group.Mesh.subMeshCount; subMesh++)
                        triangles += checked((int)group.Mesh.GetIndexCount(subMesh) / 3);
                return $"Saved {ScenePath}\n{groups.Count} Blender mesh groups, {triangles} triangles, {retired} replaced renderers, " +
                    $"{baseline.Count} original transforms and {colliderCount} colliders verified.\nOriginal scene SHA-256 {sourceHash}";
            }
            catch
            {
                if (target.IsValid() && target.isLoaded)
                {
                    Scene reopened = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
                    SceneManager.SetActiveScene(reopened);
                    EditorSceneManager.CloseScene(target, true);
                }
                if (madeScene) AssetDatabase.DeleteAsset(ScenePath);
                if (madeAsset) AssetDatabase.DeleteAsset(MeshAssetPath);
                throw;
            }
            finally
            {
                foreach (Group group in groups)
                    if (group.Mesh && !EditorUtility.IsPersistent(group.Mesh)) Object.DestroyImmediate(group.Mesh);
            }
        }

        static List<Group> ReadGroups(string path)
        {
            string[] rows = File.ReadAllLines(path, Encoding.UTF8);
            const string columns = "key\tassembly\tlayer\tshadow\treceive\tmembers";
            if (rows.Length < 2 || !rows[0].StartsWith(columns, StringComparison.Ordinal))
                throw new InvalidDataException("Invalid Blender assembly manifest.");

            var result = new List<Group>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string row in rows.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                string[] fields = row.Split('\t');
                if (fields.Length < 6 || string.IsNullOrWhiteSpace(fields[0]) ||
                    string.IsNullOrWhiteSpace(fields[1]) || !keys.Add(fields[0]))
                    throw new InvalidDataException("Invalid Blender mesh group.");
                int layer = int.Parse(fields[2], CultureInfo.InvariantCulture);
                int shadow = int.Parse(fields[3], CultureInfo.InvariantCulture);
                if (layer < 0 || layer > 31 || !Enum.IsDefined(typeof(ShadowCastingMode), shadow) ||
                    (fields[4] != "0" && fields[4] != "1"))
                    throw new InvalidDataException($"Invalid render settings for {fields[0]}.");
                string Field(int index) => index < fields.Length ? fields[index] : "";
                var group = new Group
                {
                    Key = fields[0], Name = fields[1], Layer = layer, Shadow = (ShadowCastingMode)shadow,
                    Receive = fields[4] == "1", Members = fields[5].Split('|'), Anchor = Field(6), Body = Field(7),
                    LightProbe = EnumField<LightProbeUsage>(Field(8)), ReflectionProbe = EnumField<ReflectionProbeUsage>(Field(9)),
                    RenderingLayer = string.IsNullOrEmpty(Field(10)) ? (uint?)null : uint.Parse(Field(10), CultureInfo.InvariantCulture)
                };
                result.Add(group);
            }
            if (result.Count == 0) throw new InvalidDataException("No Blender mesh groups were supplied.");
            var byKey = result.ToDictionary(group => group.Key, StringComparer.Ordinal);
            foreach (Group group in result.Where(group => !string.IsNullOrEmpty(group.Body)))
                if (!byKey.TryGetValue(group.Body, out Group body) || !string.IsNullOrEmpty(body.Body) || body.Anchor != group.Anchor)
                    throw new InvalidDataException($"Invalid outline parent for {group.Key}.");
            return result;
        }

        static T? EnumField<T>(string value) where T : struct, Enum
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (!Enum.TryParse(value, out T parsed) || !Enum.IsDefined(typeof(T), parsed))
                throw new InvalidDataException($"Invalid {typeof(T).Name} value {value}.");
            return parsed;
        }

        static Dictionary<string, Transform> IndexTransforms(Scene scene)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            void Add(Transform node, string key)
            {
                result.Add(key, node);
                for (int i = 0; i < node.childCount; i++) Add(node.GetChild(i), key + "." + i);
            }
            foreach (GameObject root in scene.GetRootGameObjects()) Add(root.transform, root.transform.GetSiblingIndex().ToString(CultureInfo.InvariantCulture));
            return result;
        }

        static void ResolveSources(Scene scene, List<Group> groups)
        {
            Dictionary<string, Transform> nodes = IndexTransforms(scene);
            var used = new HashSet<MeshRenderer>();
            foreach (Group group in groups)
            {
                if (!string.IsNullOrEmpty(group.Anchor) && !nodes.ContainsKey(group.Anchor))
                    throw new InvalidDataException($"Missing motion anchor {group.Anchor} for {group.Key}.");
                group.OriginalRenderers = group.Members.Select(member =>
                {
                    if (!nodes.TryGetValue(member, out Transform node) || !node.TryGetComponent(out MeshRenderer renderer) ||
                        !node.TryGetComponent(out MeshFilter filter) || !filter.sharedMesh || !renderer.enabled || renderer.forceRenderingOff ||
                        !used.Add(renderer))
                        throw new InvalidDataException($"Missing, disabled or repeated source renderer {member} for {group.Key}.");
                    if (node.gameObject.layer != group.Layer || renderer.shadowCastingMode != group.Shadow ||
                        renderer.receiveShadows != group.Receive ||
                        (group.LightProbe.HasValue && renderer.lightProbeUsage != group.LightProbe.Value) ||
                        (group.ReflectionProbe.HasValue && renderer.reflectionProbeUsage != group.ReflectionProbe.Value) ||
                        (group.RenderingLayer.HasValue && renderer.renderingLayerMask != group.RenderingLayer.Value))
                        throw new InvalidDataException($"Source render settings changed for {node.name} in {group.Key}.");
                    if (renderer.HasPropertyBlock() || (renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 65534))
                        throw new InvalidDataException($"Cannot merge per-object material overrides or baked lightmaps for {node.name}.");
                    if (!string.IsNullOrEmpty(group.Anchor) && node != nodes[group.Anchor] && !node.IsChildOf(nodes[group.Anchor]))
                        throw new InvalidDataException($"The mesh {node.name} is outside its motion anchor.");
                    return renderer;
                }).ToArray();
            }
        }

        static void ReadGeometry(string path, List<Group> groups)
        {
            var byKey = groups.ToDictionary(group => group.Key, StringComparer.Ordinal);
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
            int groupCount = reader.ReadInt32();
            if (groupCount != groups.Count) throw new InvalidDataException("The Blender mesh and manifest group counts differ.");
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                string key = ReadString(reader);
                if (!byKey.TryGetValue(key, out Group group) || group.Mesh)
                    throw new InvalidDataException($"Unexpected or repeated Blender group {key}.");
                int partCount = reader.ReadInt32();
                if (partCount <= 0 || partCount > 1024) throw new InvalidDataException($"Invalid material count in {key}.");
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uv = new List<Vector2>();
                var indices = new List<int[]>(partCount);
                group.Materials = new Material[partCount];
                var sourceMaterials = new HashSet<Material>(group.OriginalRenderers.SelectMany(renderer => renderer.sharedMaterials));
                for (int part = 0; part < partCount; part++)
                {
                    string materialKey = ReadString(reader);
                    if (!materials.TryGetValue(materialKey, out Material material))
                        materials.Add(materialKey, material = LoadMaterial(materialKey));
                    if (!sourceMaterials.Contains(material))
                        throw new InvalidDataException($"Material {material.name} is outside its source group {key}.");
                    group.Materials[part] = material;
                    int vertexCount = reader.ReadInt32();
                    if (vertexCount <= 0 || vertexCount > (reader.BaseStream.Length - reader.BaseStream.Position) / 32)
                        throw new InvalidDataException($"Invalid vertex count in {key}.");
                    int offset = vertices.Count;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        vertices.Add(new Vector3(ReadFloat(reader), ReadFloat(reader), ReadFloat(reader)));
                        normals.Add(new Vector3(ReadFloat(reader), ReadFloat(reader), ReadFloat(reader)));
                        uv.Add(new Vector2(ReadFloat(reader), ReadFloat(reader)));
                    }
                    int indexCount = reader.ReadInt32();
                    if (indexCount <= 0 || indexCount % 3 != 0 || indexCount > (reader.BaseStream.Length - reader.BaseStream.Position) / 4)
                        throw new InvalidDataException($"Invalid triangle count in {key}.");
                    var partIndices = new int[indexCount];
                    for (int i = 0; i < indexCount; i++)
                    {
                        int index = reader.ReadInt32();
                        if (index < 0 || index >= vertexCount) throw new InvalidDataException($"Invalid triangle index in {key}.");
                        partIndices[i] = offset + index;
                    }
                    indices.Add(partIndices);
                }

                group.Mesh = new Mesh { name = group.Name + " | " + key };
                if (vertices.Count > 65535) group.Mesh.indexFormat = IndexFormat.UInt32;
                group.Mesh.SetVertices(vertices);
                group.Mesh.SetNormals(normals);
                group.Mesh.SetUVs(0, uv);
                group.Mesh.subMeshCount = partCount;
                for (int part = 0; part < partCount; part++) group.Mesh.SetTriangles(indices[part], part, false);
                group.Mesh.RecalculateBounds();
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
                throw new InvalidDataException("The Blender geometry file contains unexpected trailing data.");
        }

        static Material LoadMaterial(string key)
        {
            int split = key.LastIndexOf('_');
            if (split < 0 || !long.TryParse(key.Substring(split + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                throw new InvalidDataException($"Invalid material identifier {key}.");
            string guid = key.Substring(0, split);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
                foreach (Material material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string materialGuid, out long materialId) &&
                        materialGuid == guid && materialId == id) return material;
            throw new InvalidDataException($"Unity material {key} could not be resolved.");
        }

        static string ReadString(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count <= 0 || count > 8192) throw new InvalidDataException("Invalid string in Blender geometry.");
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        static float ReadFloat(BinaryReader reader)
        {
            float value = reader.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("A Blender vertex is not finite.");
            return value;
        }

        static void ReparentMovingMeshes(Scene scene, List<Group> groups)
        {
            Dictionary<string, Transform> nodes = IndexTransforms(scene);
            foreach (Group group in groups.Where(group => !string.IsNullOrEmpty(group.Anchor)))
            {
                Transform anchor = nodes[group.Anchor];
                Matrix4x4 toLocal = anchor.worldToLocalMatrix;
                Matrix4x4 normalToLocal = anchor.localToWorldMatrix.transpose;
                Vector3[] vertices = group.Mesh.vertices;
                Vector3[] normals = group.Mesh.normals;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = toLocal.MultiplyPoint3x4(vertices[i]);
                    normals[i] = normalToLocal.MultiplyVector(normals[i]).normalized;
                }
                group.Mesh.vertices = vertices;
                group.Mesh.normals = normals;
                if (anchor.localToWorldMatrix.determinant < 0f)
                {
                    for (int sub = 0; sub < group.Mesh.subMeshCount; sub++)
                    {
                        int[] triangles = group.Mesh.GetTriangles(sub);
                        for (int i = 0; i < triangles.Length; i += 3)
                            (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                        group.Mesh.SetTriangles(triangles, sub, false);
                    }
                }
                group.Mesh.RecalculateBounds();
            }
        }

        static void Install(Scene scene, List<Group> groups)
        {
            Dictionary<string, Transform> nodes = IndexTransforms(scene);
            var byKey = groups.ToDictionary(group => group.Key, StringComparer.Ordinal);
            var assemblies = new Dictionary<string, Transform>(StringComparer.Ordinal);
            var root = new GameObject(RootName);
            if (root.scene != scene) SceneManager.MoveGameObjectToScene(root, scene);
            foreach (Group group in groups.OrderBy(group => string.IsNullOrEmpty(group.Body) ? 0 : 1))
            {
                Transform parent;
                if (!string.IsNullOrEmpty(group.Body)) parent = byKey[group.Body].Node;
                else if (!string.IsNullOrEmpty(group.Anchor)) parent = nodes[group.Anchor];
                else if (!assemblies.TryGetValue(group.Name, out parent))
                {
                    parent = new GameObject(group.Name).transform;
                    parent.SetParent(root.transform, false);
                    assemblies.Add(group.Name, parent);
                }

                var node = new GameObject(group.Key, typeof(MeshFilter), typeof(MeshRenderer));
                node.transform.SetParent(parent, false);
                node.layer = group.Layer;
                group.Node = node.transform;
                node.GetComponent<MeshFilter>().sharedMesh = group.Mesh;
                var renderer = node.GetComponent<MeshRenderer>();
                MeshRenderer source = group.OriginalRenderers[0];
                renderer.sharedMaterials = group.Materials;
                renderer.shadowCastingMode = group.Shadow;
                renderer.receiveShadows = group.Receive;
                renderer.lightProbeUsage = group.LightProbe ?? source.lightProbeUsage;
                renderer.reflectionProbeUsage = group.ReflectionProbe ?? source.reflectionProbeUsage;
                renderer.renderingLayerMask = group.RenderingLayer ?? source.renderingLayerMask;
                renderer.motionVectorGenerationMode = source.motionVectorGenerationMode;
                renderer.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
                renderer.sortingLayerID = source.sortingLayerID;
                renderer.sortingOrder = source.sortingOrder;
                renderer.probeAnchor = source.probeAnchor;
                foreach (MeshRenderer original in group.OriginalRenderers)
                {
                    original.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(original);
                }
            }
        }

        static void IsolateProgress(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            SamuraiLevelRoute route = roots.SelectMany(root => root.GetComponentsInChildren<SamuraiLevelRoute>(true)).Single();
            SamuraiCourtyardCheckpoint checkpoint = roots.SelectMany(root => root.GetComponentsInChildren<SamuraiCourtyardCheckpoint>(true)).Single();
            var routeObjects = new Component[] { route.player, route.input, route.keyAnchor, route.keyRoot,
                route.sideGateRoot, route.bridgeGateRoot, route.sideGateBlocker, route.bridgeGateBlocker }
                .Concat(route.sideGateHinges).Concat(route.bridgeGateHinges);
            var visuals = new[] { route.keyVisual, route.sideLockVisual, route.bridgeLatchVisual };
            if (routeObjects.Any(component => !component || component.gameObject.scene != scene) ||
                visuals.Any(item => !item || item.scene != scene) ||
                !checkpoint.player || checkpoint.player.gameObject.scene != scene ||
                !checkpoint.respawnAnchor || checkpoint.respawnAnchor.gameObject.scene != scene)
                throw new InvalidDataException("A copied route or checkpoint reference does not point into the new scene.");
            route.persistenceKey += SaveSuffix;
            checkpoint.persistenceKey += SaveSuffix;
            PrefabUtility.RecordPrefabInstancePropertyModifications(route);
            PrefabUtility.RecordPrefabInstancePropertyModifications(checkpoint);
            EditorUtility.SetDirty(route);
            EditorUtility.SetDirty(checkpoint);
        }

        static Dictionary<string, SceneNode> Snapshot(Scene scene)
        {
            return IndexTransforms(scene).ToDictionary(entry => entry.Key, entry =>
            {
                Transform node = entry.Value;
                return new SceneNode
                {
                    Name = node.name, Position = node.localPosition, Scale = node.localScale,
                    Rotation = node.localRotation, Layer = node.gameObject.layer, Active = node.gameObject.activeSelf,
                    Components = ComponentSignature(node)
                };
            }, StringComparer.Ordinal);
        }

        static string ComponentSignature(Transform node)
        {
            return string.Join("|", node.GetComponents<Component>().Select(component => component ? component.GetType().FullName : "<missing>"));
        }

        static void ValidateSnapshot(Scene scene, Dictionary<string, SceneNode> baseline, bool allowNew)
        {
            Dictionary<string, Transform> current = IndexTransforms(scene);
            if ((!allowNew && current.Count != baseline.Count) || current.Count < baseline.Count)
                throw new InvalidDataException("The copied scene transform count differs from the source.");
            foreach (var entry in baseline)
            {
                SceneNode original = entry.Value;
                if (!current.TryGetValue(entry.Key, out Transform node) || node.name != original.Name ||
                    !node.localPosition.Equals(original.Position) || !node.localRotation.Equals(original.Rotation) ||
                    !node.localScale.Equals(original.Scale) || node.gameObject.layer != original.Layer ||
                    node.gameObject.activeSelf != original.Active || ComponentSignature(node) != original.Components)
                    throw new InvalidDataException($"An original scene transform or component layout changed at {entry.Key} ({original.Name}).");
            }
        }

        static void ValidateRendering(Scene scene, List<Group> groups, int originalRendererCount, int colliderCount)
        {
            var roots = scene.GetRootGameObjects();
            if (roots.Sum(root => root.GetComponentsInChildren<Collider>(true).Length) != colliderCount ||
                roots.Sum(root => root.GetComponentsInChildren<Renderer>(true).Length) != originalRendererCount + groups.Count)
                throw new InvalidDataException("The new scene has an unexpected collider or renderer count.");
            foreach (Group group in groups)
            {
                MeshRenderer renderer = group.Node.GetComponent<MeshRenderer>();
                if (group.OriginalRenderers.Any(original => original.enabled) || !renderer.enabled ||
                    !renderer.sharedMaterials.SequenceEqual(group.Materials) || group.Mesh.subMeshCount != group.Materials.Length ||
                    group.Node.GetComponent<MeshFilter>().sharedMesh != group.Mesh || !EditorUtility.IsPersistent(group.Mesh))
                    throw new InvalidDataException($"Blender geometry or visibility did not bind for {group.Key}.");
            }
        }

        static string FileHash(string path)
        {
            using var sha = SHA256.Create();
            using var file = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
    }
}
