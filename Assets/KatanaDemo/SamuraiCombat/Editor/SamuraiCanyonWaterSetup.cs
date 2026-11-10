using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiCanyonWaterSetup
    {
        const string Destination = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/Mountains/CanyonWatercourse.asset";
        const string RootName = "Canyon river and waterfalls - 山瀑峡川";

        [Serializable] sealed class WaterData { public WaterPart[] groups; }
        [Serializable] sealed class WaterPart
        {
            public string name, material;
            public float[] points, uv;
            public int[] triangles;
        }
        [Serializable] sealed class CollisionData { public SamuraiTerrainRedesign.Landform[] meshes; }

        public static string Install(string folder)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.name != "SamuraiCombatDemo_BlenderAssembly")
                throw new InvalidOperationException("Open BlenderAssembly in Edit mode.");
            if (AssetDatabase.LoadMainAssetAtPath(Destination))
                throw new InvalidOperationException("The canyon watercourse is already installed.");
            var nodes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            if (nodes.Any(node => node.name == RootName))
                throw new InvalidOperationException("A canyon watercourse is already present.");
            var shader = Shader.Find("OC Design/Comic Lighting/Mountain Flow Water");
            if (!shader || ShaderUtil.GetShaderMessages(shader).Any(message =>
                message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error))
                throw new InvalidOperationException("The flowing water shader has not compiled.");
            var water = JsonUtility.FromJson<WaterData>(File.ReadAllText(Path.Combine(folder, "water.json")));
            string collisionText = File.ReadAllText(Path.Combine(folder, "collision-carved.json"));
            if (collisionText.TrimStart().StartsWith("[", StringComparison.Ordinal))
                collisionText = "{\"meshes\":" + collisionText + "}";
            var collisions = JsonUtility.FromJson<CollisionData>(collisionText);
            if (collisions.meshes.Length != 2) throw new InvalidDataException("Expected the two guarded summit collision meshes.");
            var lookup = nodes.GroupBy(FullPath).ToDictionary(group => group.Key, group => group.First());
            var terrain = nodes.Single(node => node.name == "06 Terrain and setting - 山体与布景");
            var river = nodes.Single(node => node.name == "Surface - Ravine_River").GetComponent<MeshRenderer>();
            foreach (var piece in water.groups)
                if (piece.points.Length % 3 != 0 || piece.uv.Length != piece.points.Length / 3 * 2 ||
                    piece.triangles.Length % 3 != 0 || piece.triangles.Any(index => index < 0 || index >= piece.points.Length / 3))
                    throw new InvalidDataException("Invalid water piece " + piece.name);
            foreach (var shape in collisions.meshes)
                if (!lookup.ContainsKey(shape.path) || !lookup[shape.path].GetComponent<MeshCollider>())
                    throw new InvalidDataException("Missing canyon collision target " + shape.path);
            if (!EditorSceneManager.SaveScene(scene, Path.Combine(folder, "apply-before.unity"), true))
                throw new IOException("The live scene could not be preserved.");
            var profiles = water.groups.Select(piece => piece.material).Append("Channel").Distinct()
                .ToDictionary(name => name, name => WaterMaterial(shader, name));
            var asset = profiles["Channel"];
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install the continuous mountain watercourse");
            bool created = false;
            try
            {
                AssetDatabase.CreateAsset(asset, Destination);
                created = true;
                foreach (var material in profiles.Values.Where(material => material != asset))
                    AssetDatabase.AddObjectToAsset(material, asset);
                // The fused visual body keeps its original three fade sections.
                string fused = SamuraiTerrainRedesign.FuseVisibleSurfaces(Path.Combine(folder, "carved.json"));
                var root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Create the local river composition");
                root.transform.SetParent(terrain, false);
                int faces = 0;
                foreach (var piece in water.groups)
                {
                    var child = new GameObject(piece.name) { layer = river.gameObject.layer };
                    child.transform.SetParent(root.transform, false);
                    var mesh = MeshFrom(piece.name, piece.points, new[] { piece.triangles }, root.transform);
                    var uv = new List<Vector2>();
                    for (int index = 0; index < piece.uv.Length; index += 2)
                        uv.Add(new Vector2(piece.uv[index], piece.uv[index + 1]));
                    mesh.SetUVs(0, uv);
                    AssetDatabase.AddObjectToAsset(mesh, asset);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = profiles[piece.material];
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    if (piece.material == "Foam" || piece.material == "Mist") renderer.receiveShadows = false;
                    faces += piece.triangles.Length / 3;
                }
                foreach (var shape in collisions.meshes)
                {
                    var node = lookup[shape.path];
                    var collider = node.GetComponent<MeshCollider>();
                    var mesh = MeshFrom("River-carved collision - " + shape.name, shape.points,
                        shape.submeshes.Select(layer => layer.triangles).ToArray(), node);
                    AssetDatabase.AddObjectToAsset(mesh, asset);
                    Undo.RecordObject(collider, "Cut the river below the existing courtyards");
                    collider.sharedMesh = mesh;
                }
                RejoinLowerRiver(river, profiles["Channel"], asset, nodes);
                AssetDatabase.SaveAssets();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return $"{water.groups.Length} flowing-water pieces, {faces} triangles, two guarded collision cuts\n" + fused;
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                if (created) AssetDatabase.DeleteAsset(Destination);
                AssetDatabase.SaveAssets();
                throw;
            }
        }

        static Mesh MeshFrom(string name, float[] points, int[][] submeshes, Transform space)
        {
            var vertices = new List<Vector3>();
            for (int index = 0; index < points.Length; index += 3)
                vertices.Add(space.InverseTransformPoint(new Vector3(points[index], points[index + 1], points[index + 2])));
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.subMeshCount = submeshes.Length;
            for (int slot = 0; slot < submeshes.Length; slot++) mesh.SetTriangles(submeshes[slot], slot, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material WaterMaterial(Shader shader, string role)
        {
            if (role == "Stone")
            {
                var stone = new Material(AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/Mountains/TerrainRedesign.asset")) { name = "Wet river stone" };
                stone.SetFloat("_MossAmount", .20f);
                return stone;
            }
            var material = new Material(shader) { name = role + " mountain water" };
            material.SetColor("_BaseColor", new Color(.30f, .48f, .46f, 1));
            material.SetColor("_ShadowColor", new Color(.17f, .32f, .38f, 1));
            material.SetColor("_FoamColor", new Color(.90f, .94f, .86f, 1));
            material.SetFloat("_FlowSpeed", role == "Falls" ? 5.4f : role == "Pool" ? .24f : 1.15f);
            material.SetFloat("_FlowType", role == "Falls" ? 1 : role == "Foam" || role == "Mist" ? 2 : 0);
            material.SetFloat("_FoamStrength", role == "Falls" ? .83f : role == "Pool" ? .12f : .38f);
            material.SetFloat("_StreakDensity", role == "Falls" ? 10 : 6);
            material.SetFloat("_DetailFar", 180);
            if (role == "Foam" || role == "Mist")
            {
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.SetFloat("_Opacity", role == "Mist" ? .22f : .63f);
                material.SetFloat("_SoftIntersection", role == "Mist" ? .35f : 0);
                if (role == "Mist") material.SetFloat("_FlowLength", 1);
                material.renderQueue = 3000;
                material.SetOverrideTag("RenderType", "Transparent");
            }
            return material;
        }

        public static string Refresh(string folder)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.name != "SamuraiCombatDemo_BlenderAssembly")
                throw new InvalidOperationException("Open BlenderAssembly in Edit mode.");
            var nodes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var root = nodes.Single(node => node.name == RootName);
            var river = nodes.Single(node => node.name == "Surface - Ravine_River").GetComponent<MeshRenderer>();
            var asset = AssetDatabase.LoadMainAssetAtPath(Destination);
            var water = JsonUtility.FromJson<WaterData>(File.ReadAllText(Path.Combine(folder, "water.json")));
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refine the canyon watercourse");
            foreach (var piece in water.groups)
            {
                var node = root.Find(piece.name);
                var replacement = MeshFrom(piece.name, piece.points, new[] { piece.triangles }, root);
                replacement.uv = Enumerable.Range(0, piece.uv.Length / 2)
                    .Select(index => new Vector2(piece.uv[index * 2], piece.uv[index * 2 + 1])).ToArray();
                ReplaceMesh(node.GetComponent<MeshFilter>().sharedMesh, replacement);
            }
            RejoinLowerRiver(river, (Material)asset, asset, nodes);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undo);
            return "Refreshed the 13 water meshes and the exact lower-river confluence.";
        }

        public static string ExportInstalled(string folder)
        {
            var root = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<Transform>(true)).Single(node => node.name == RootName);
            Func<MeshFilter, WaterPart> export = filter =>
            {
                var mesh = filter.sharedMesh;
                return new WaterPart { name = filter.name, points = mesh.vertices.SelectMany(point =>
                {
                    var world = filter.transform.TransformPoint(point);
                    return new[] { world.x, world.y, world.z };
                }).ToArray(), triangles = mesh.triangles, uv = mesh.uv.SelectMany(uv => new[] { uv.x, uv.y }).ToArray() };
            };
            var survey = new WaterData { groups = root.GetComponentsInChildren<MeshFilter>().Select(export).ToArray() };
            File.WriteAllText(Path.Combine(folder, "installed-water-parts.json"), JsonUtility.ToJson(survey));
            var lower = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<MeshFilter>(true))
                .Single(filter => filter.name == "Surface - Ravine_River");
            File.WriteAllText(Path.Combine(folder, "installed-lower-river.json"),
                JsonUtility.ToJson(new WaterData { groups = new[] { export(lower) } }));
            return "Exported installed water geometry.";
        }

        public static string UpdateCollisions(string folder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            var nodes = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).GroupBy(FullPath)
                .ToDictionary(group => group.Key, group => group.First());
            string text = File.ReadAllText(Path.Combine(folder, "collision-carved.json"));
            var data = JsonUtility.FromJson<CollisionData>("{\"meshes\":" + text + "}");
            foreach (var shape in data.meshes)
            {
                var node = nodes[shape.path];
                var collider = node.GetComponent<MeshCollider>();
                var persistent = collider.sharedMesh;
                var replacement = MeshFrom(persistent.name, shape.points,
                    shape.submeshes.Select(layer => layer.triangles).ToArray(), node);
                Undo.RecordObject(collider, "Align the deep rock collision with the river banks");
                collider.sharedMesh = null;
                try { ReplaceMesh(persistent, replacement); }
                finally { collider.sharedMesh = persistent; }
            }
            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return "Updated the two existing deep-rock colliders, preserving their original walking caps.";
        }

        static void ReplaceMesh(Mesh destination, Mesh source)
        {
            Undo.RecordObject(destination, "Refine the water mesh");
            destination.Clear();
            destination.indexFormat = source.indexFormat;
            destination.vertices = source.vertices;
            destination.normals = source.normals;
            destination.uv = source.uv;
            destination.subMeshCount = source.subMeshCount;
            for (int slot = 0; slot < source.subMeshCount; slot++) destination.SetTriangles(source.GetTriangles(slot), slot, false);
            destination.RecalculateBounds();
            destination.UploadMeshData(false);
            EditorUtility.SetDirty(destination);
            Object.DestroyImmediate(source);
        }

        static void RejoinLowerRiver(MeshRenderer river, Material profile, Object asset, Transform[] nodes)
        {
            var filter = river.GetComponent<MeshFilter>();
            var before = AssetDatabase.LoadAllAssetsAtPath(
                "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/Mountains/TerrainRedesign.asset")
                .OfType<Mesh>().Single(mesh => mesh.name == "Refined Ravine_River");
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.name = "Continuous animated lower river";
            var world = before.vertices.Select(river.transform.TransformPoint).ToArray();
            var indices = before.triangles;
            var vertices = new List<Vector3>();
            var retained = new List<int>();
            for (int index = 0; index < indices.Length; index += 3)
            {
                var triangle = new[] { world[indices[index]], world[indices[index + 1]], world[indices[index + 2]] };
                foreach (bool downstream in new[] { false, true })
                {
                    var clipped = ClipRiver(triangle, downstream ? -100 : -88, downstream);
                    int first = vertices.Count;
                    vertices.AddRange(clipped);
                    for (int corner = 1; corner + 1 < clipped.Count; corner++)
                    {
                        if (Vector3.Cross(clipped[corner] - clipped[0], clipped[corner + 1] - clipped[0]).sqrMagnitude < 1e-12f) continue;
                        retained.Add(first); retained.Add(first + corner); retained.Add(first + corner + 1);
                    }
                }
            }
            var terminal = nodes.Single(node => node.name == "Channel reach 10").GetComponent<MeshFilter>().sharedMesh;
            float finalV = terminal.uv.Max(coordinate => coordinate.y);
            var rings = world.GroupBy(point => Mathf.RoundToInt(point.z * 1000))
                .Select(group => group.OrderBy(point => point.x).ToArray()).OrderBy(group => group[0].z).ToArray();
            var uv = new List<Vector2>();
            foreach (var point in vertices)
            {
                int upper = Array.FindIndex(rings, ring => ring[0].z >= point.z);
                upper = Mathf.Clamp(upper, 1, rings.Length - 1);
                var lower = rings[upper - 1]; var higher = rings[upper];
                float t = Mathf.InverseLerp(lower[0].z, higher[0].z, point.z);
                float left = Mathf.Lerp(lower.First().x, higher.First().x, t);
                float right = Mathf.Lerp(lower.Last().x, higher.Last().x, t);
                uv.Add(new Vector2(1 - Mathf.InverseLerp(left, right, point.x), finalV - 100 - point.z));
            }
            mesh.SetVertices(vertices.Select(river.transform.InverseTransformPoint).ToList());
            mesh.SetTriangles(retained, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var persistent = AssetDatabase.LoadAllAssetsAtPath(Destination).OfType<Mesh>()
                .FirstOrDefault(item => item.name == mesh.name);
            if (persistent) { ReplaceMesh(persistent, mesh); mesh = persistent; }
            else AssetDatabase.AddObjectToAsset(mesh, asset);
            Undo.RecordObjects(new Object[] { filter, river }, "Join the new stream to the original lower river");
            filter.sharedMesh = mesh;
            river.sharedMaterial = profile;
            foreach (var group in nodes.Select(node => node.GetComponent<SamuraiOcclusionGroup>()).Where(group =>
                group && group.surfaces.Any(surface => surface.renderer == river)))
            {
                Undo.RecordObject(group, "Retain the lower river mesh reference");
                for (int index = 0; index < group.surfaces.Length; index++)
                    if (group.surfaces[index].renderer == river) group.surfaces[index].probeMesh = mesh;
                EditorUtility.SetDirty(group);
            }
        }

        static List<Vector3> ClipRiver(Vector3[] polygon, float z, bool below)
        {
            var result = new List<Vector3>();
            for (int index = 0; index < polygon.Length; index++)
            {
                var a = polygon[index]; var b = polygon[(index + 1) % polygon.Length];
                bool insideA = below ? a.z <= z : a.z >= z;
                bool insideB = below ? b.z <= z : b.z >= z;
                if (insideA) result.Add(a);
                if (insideA != insideB) result.Add(Vector3.Lerp(a, b, (z - a.z) / (b.z - a.z)));
            }
            for (int index = result.Count - 1; index > 0; index--)
                if ((result[index] - result[index - 1]).sqrMagnitude < 1e-12f) result.RemoveAt(index);
            if (result.Count > 1 && (result[0] - result[result.Count - 1]).sqrMagnitude < 1e-12f) result.RemoveAt(result.Count - 1);
            return result;
        }

        static string FullPath(Transform node) => node.parent ? FullPath(node.parent) + "/" + node.name : node.name;
    }
}
