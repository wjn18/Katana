using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiBlenderArchitectureInk
    {
        const string InkObject = "Architectural crease ink - 建筑结构墨线";
        const string InkAsset = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/BlenderAssembly/ArchitecturalCreaseInk.asset";
        const float WeldStep = .0005f;
        const float MinimumEdgeLength = .63f;
        const float MinimumFaceHeight = .077f;
        const float InkRadius = .0115f;
        const int RingSides = 4;

        static readonly HashSet<string> Buildings = new HashSet<string>(StringComparer.Ordinal)
        {
            "Mountain Storehouse", "Tea Pavilion", "Main Entrance Gate", "East Residence",
            "Ridge Viewing Lodge", "Elite1 Grand Hall", "Elite1 Hall Passage", "Landmark Pagoda",
            "Lower Grand Hall", "Curved Mountain Gallery",
            "Upper bridge portal", "Lower bridge portal", "Tea garden side gate",
            "West prayer room", "West store room", "East guard room", "East study room",
            "West covered walk -6", "West covered walk 5", "East covered walk 0", "East covered walk 7"
        };

        readonly struct Edge : IEquatable<Edge>
        {
            readonly Vector3Int first;
            readonly Vector3Int second;

            public Edge(Vector3 a, Vector3 b)
            {
                var x = Vector3Int.RoundToInt(a / WeldStep);
                var y = Vector3Int.RoundToInt(b / WeldStep);
                if (x.x < y.x || (x.x == y.x && (x.y < y.y || (x.y == y.y && x.z <= y.z))))
                {
                    first = x;
                    second = y;
                }
                else
                {
                    first = y;
                    second = x;
                }
            }

            public bool Equals(Edge other) => first == other.first && second == other.second;
            public override bool Equals(object obj) => obj is Edge other && Equals(other);
            public override int GetHashCode() => unchecked(first.GetHashCode() * 397 ^ second.GetHashCode());
        }

        struct EdgeFaces
        {
            public Vector3 start;
            public Vector3 end;
            public Vector3 firstNormal;
            public Vector3 secondNormal;
            public Vector3 firstCorner;
            public Vector3 secondCorner;
            public int count;
        }

        [MenuItem("Katana/Level/Add Blender Architecture Crease Ink")]
        static void InstallFromMenu() => Debug.Log(Install());

        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != SamuraiBlenderAssemblyImport.ScenePath || !scene.isLoaded ||
                EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the Blender assembly scene in Edit mode.");

            var assemblies = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>())
                .Where(node => Buildings.Contains(node.name) &&
                    node.Cast<Transform>().Any(child => child.name.EndsWith("_Surface", StringComparison.Ordinal))).ToArray();
            if (assemblies.Length != Buildings.Count)
                throw new InvalidOperationException("Expected the complete Blender architecture assembly.");

            var existing = assemblies.Count(node => node.Find(InkObject));
            var savedInk = AssetDatabase.LoadMainAssetAtPath(InkAsset);
            if (savedInk && existing > 0 && AssetDatabase.LoadAllAssetsAtPath(InkAsset).Count(asset => asset is Mesh) == existing)
                return $"Architecture ink already exists on {existing} assemblies.";
            if (existing != 0 || savedInk)
                throw new InvalidOperationException("Found an incomplete architecture ink install.");

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader || !shader.isSupported)
                throw new InvalidOperationException("The URP Unlit shader is unavailable.");

            var meshes = new Dictionary<Transform, Mesh>();
            var material = new Material(shader) { name = "Architecture warm black" };
            material.SetColor("_BaseColor", new Color(.041f, .035f, .030f, 1f));
            material.SetFloat("_Cull", (float)CullMode.Off);
            bool wroteAssets = false;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Blender architecture crease ink");
            try
            {
                foreach (var assembly in assemblies)
                {
                    var mesh = BuildMesh(assembly);
                    if (mesh) meshes.Add(assembly, mesh);
                }
                if (meshes.Count == 0)
                    throw new InvalidOperationException("The assemblies have no eligible structural creases.");

                AssetDatabase.CreateAsset(meshes.First().Value, InkAsset);
                wroteAssets = true;
                AssetDatabase.AddObjectToAsset(material, InkAsset);
                foreach (var mesh in meshes.Values.Skip(1))
                    AssetDatabase.AddObjectToAsset(mesh, InkAsset);
                AssetDatabase.SaveAssets();

                foreach (var entry in meshes)
                {
                    var stroke = new GameObject(InkObject, typeof(MeshFilter), typeof(MeshRenderer));
                    Undo.RegisterCreatedObjectUndo(stroke, "Add architectural crease ink");
                    stroke.transform.SetParent(entry.Key, false);
                    stroke.layer = entry.Key.GetChild(0).gameObject.layer;
                    stroke.GetComponent<MeshFilter>().sharedMesh = entry.Value;
                    var renderer = stroke.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }

                Undo.CollapseUndoOperations(undoGroup);
                EditorSceneManager.MarkSceneDirty(scene);
                var edges = meshes.Values.Sum(mesh => mesh.vertexCount / (2 * RingSides));
                return $"Added {edges} depth-tested architectural strokes across {meshes.Count} assemblies.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                if (wroteAssets) AssetDatabase.DeleteAsset(InkAsset);
                foreach (var mesh in meshes.Values)
                    if (mesh && !EditorUtility.IsPersistent(mesh)) Object.DestroyImmediate(mesh);
                if (material && !EditorUtility.IsPersistent(material)) Object.DestroyImmediate(material);
                throw;
            }
        }

        static Mesh BuildMesh(Transform assembly)
        {
            var edges = new Dictionary<Edge, EdgeFaces>();
            foreach (var renderer in assembly.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || !renderer.name.EndsWith("_Surface", StringComparison.Ordinal)) continue;
                var source = renderer.GetComponent<MeshFilter>().sharedMesh;
                var points = source.vertices;
                var normals = source.normals;
                var placement = assembly.worldToLocalMatrix * renderer.localToWorldMatrix;
                var normalPlacement = placement.inverse.transpose;
                for (int i = 0; i < points.Length; i++) points[i] = placement.MultiplyPoint3x4(points[i]);

                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    if (source.GetTopology(sub) != MeshTopology.Triangles) continue;
                    var triangles = source.GetIndices(sub);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int ai = triangles[i], bi = triangles[i + 1], ci = triangles[i + 2];
                        var a = points[ai];
                        var b = points[bi];
                        var c = points[ci];
                        var cross = Vector3.Cross(b - a, c - a);
                        if (cross.sqrMagnitude < .00000001f) continue;
                        var normal = cross.normalized;
                        if (normals.Length == points.Length && Vector3.Dot(normal,
                                normalPlacement.MultiplyVector(normals[ai] + normals[bi] + normals[ci])) < 0f)
                            normal = -normal;
                        AddEdge(edges, a, b, c, normal);
                        AddEdge(edges, b, c, a, normal);
                        AddEdge(edges, c, a, b, normal);
                    }
                }
            }

            var vertices = new List<Vector3>();
            var indices = new List<int>();
            foreach (var edge in edges.Values)
            {
                if (edge.count != 2) continue;
                float normalAgreement = Vector3.Dot(edge.firstNormal, edge.secondNormal);
                // Triangulation diagonals and back-to-back surfaces do not make creases.
                if (normalAgreement > .84f || normalAgreement < -.98f) continue;
                if (Vector3.Dot(edge.firstNormal, edge.secondCorner - edge.start) > -.008f ||
                    Vector3.Dot(edge.secondNormal, edge.firstCorner - edge.start) > -.008f) continue;
                AddStroke(vertices, indices, edge.start, edge.end);
            }

            if (vertices.Count == 0) return null;
            var mesh = new Mesh { name = $"{assembly.name} | architecture ink" };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            return mesh;
        }

        static void AddEdge(Dictionary<Edge, EdgeFaces> edges, Vector3 a, Vector3 b, Vector3 opposite, Vector3 normal)
        {
            var segment = b - a;
            if (segment.sqrMagnitude < MinimumEdgeLength * MinimumEdgeLength) return;
            if (Vector3.Cross(segment, opposite - a).sqrMagnitude <
                segment.sqrMagnitude * MinimumFaceHeight * MinimumFaceHeight) return;
            var key = new Edge(a, b);
            edges.TryGetValue(key, out var faces);
            if (faces.count == 0)
            {
                faces.start = a;
                faces.end = b;
                faces.firstNormal = normal;
                faces.firstCorner = opposite;
            }
            else if (faces.count == 1)
            {
                faces.secondNormal = normal;
                faces.secondCorner = opposite;
            }
            faces.count++;
            edges[key] = faces;
        }

        static void AddStroke(List<Vector3> vertices, List<int> indices, Vector3 start, Vector3 end)
        {
            var axis = (end - start).normalized;
            var basis = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized;
            var other = Vector3.Cross(axis, basis);
            int offset = vertices.Count;
            for (int side = 0; side < RingSides; side++)
            {
                float angle = side * Mathf.PI * 2f / RingSides;
                var radial = (basis * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * InkRadius;
                vertices.Add(start + radial);
                vertices.Add(end + radial);
            }

            for (int side = 0; side < RingSides; side++)
            {
                int a = offset + side * 2;
                int b = offset + (side + 1) % RingSides * 2;
                indices.Add(a);
                indices.Add(a + 1);
                indices.Add(b);
                indices.Add(a + 1);
                indices.Add(b + 1);
                indices.Add(b);
            }
        }
    }
}
