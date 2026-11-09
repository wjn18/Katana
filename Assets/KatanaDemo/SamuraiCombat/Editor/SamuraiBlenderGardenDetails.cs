using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Painted beds and low, collision-free planting for the assembled courtyard.</summary>
    public static class SamuraiBlenderGardenDetails
    {
        public const string RootName = "Garden detail - 草簇与蕨类";
        const string AssetFolder = SamuraiComicStyleSetup.Root + "/BlenderAssembly/GardenDetails";
        const int Seed = 68091;

        // Small groups of planting on the flanks, away from the fighting courts.
        static readonly (string Name, float X, float Z, float RadiusX, float RadiusZ, int Count)[] Terraces =
        {
            ("Upper west lawn", -41.1f, 20.6f, 2.4f, 4.3f, 66),
            ("Upper hall foundation", -32.5f, 13.9f, 2.4f, 1.1f, 37),
            ("West garden foot", -42.0f, -6.4f, 2.0f, 4.2f, 60),
            ("Tea garden slope", -29.9f, -17.6f, 2.4f, 3.4f, 69),
            ("Upper south shoulder", -34.8f, -20.1f, 3.1f, 2.6f, 64),
            ("Lower west approach", 16.9f, -21.7f, 2.8f, 2.9f, 67),
            ("Lower east approach", 27.1f, -21.3f, 2.2f, 2.7f, 67),
            ("East garden shoulder", 28.2f, -12.8f, 1.8f, 4.1f, 73),
            ("East residence garden", 30.3f, 1.2f, 2.3f, 4.7f, 75),
            ("Tower garden foot", 30.6f, 12.1f, 2.4f, 3.9f, 57),
        };

        static readonly (Vector2 A, Vector2 B, float Clear)[] GardenRoutes =
        {
            (new Vector2(-25.4f, -11.8f), new Vector2(-25.4f, -16.7f), 2.15f),
            (new Vector2(-25.4f, -16.7f), new Vector2(-22.5f, -16.7f), 1.45f),
            (new Vector2(-27.5f, -13f), new Vector2(-39.9f, -13f), 1.30f),
            (new Vector2(-39.9f, -13f), new Vector2(-39.9f, -11.5f), 1.30f),
            (new Vector2(-25.4f, 11.8f), new Vector2(-25.4f, 14.8f), 2.50f),
            (new Vector2(22.386f, -26.7f), new Vector2(22.386f, -12f), 2.60f),
            (new Vector2(24.1f, -18.7f), new Vector2(25.2f, -18.3f), 1.05f),
            (new Vector2(25.2f, -18.3f), new Vector2(25.2f, -11.5f), 1.05f),
            (new Vector2(25.2f, -11.5f), new Vector2(26.4f, -9.8f), 1.05f),
            (new Vector2(26.4f, -9.8f), new Vector2(26.4f, -7.2f), 1.05f),
            (new Vector2(26.4f, -7.2f), new Vector2(25.2f, -5.5f), 1.05f),
            (new Vector2(25.2f, -5.5f), new Vector2(25.2f, 3.5f), 1.05f),
            (new Vector2(25.2f, 3.5f), new Vector2(28.2f, 5f), 1.05f),
            (new Vector2(28.2f, 5f), new Vector2(29.2f, 5f), 1.05f),
            (new Vector2(20.6f, -18.3f), new Vector2(13f, -18.4f), 1.35f),
            (new Vector2(13f, -18.4f), new Vector2(11f, -23f), 1.35f),
        };

        sealed class PlantMesh
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<int>[] Triangles = { new List<int>(), new List<int>(), new List<int>() };
            public int Tufts, Ferns;

            public void Strip(int palette, Vector3 root, Vector3 knee, Vector3 tip,
                Vector3 across, float baseWidth, float kneeWidth)
            {
                var offset = Vertices.Count;
                Vertices.Add(root - across * baseWidth);
                Vertices.Add(root + across * baseWidth);
                Vertices.Add(knee - across * kneeWidth);
                Vertices.Add(knee + across * kneeWidth);
                Vertices.Add(tip);
                // A soft upwards normal keeps the turned blades within the established comic palette.
                var normal = (Vector3.up + Vector3.Cross(across, Vector3.up) * .34f).normalized;
                for (int i = 0; i < 5; i++) Normals.Add(normal);
                Triangles[palette].AddRange(new[]
                {
                    offset, offset + 2, offset + 1, offset + 1, offset + 2, offset + 3,
                    offset + 2, offset + 4, offset + 3
                });
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = Triangles.Length };
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                for (int i = 0; i < Triangles.Length; i++) mesh.SetTriangles(Triangles[i], i, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        [MenuItem("Katana/Level/Dress Blender Courtyard Gardens")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiBlenderAssemblyImport.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo_BlenderAssembly in Edit mode first.");

            var roots = scene.GetRootGameObjects();
            if (roots.Any(root => root.name == RootName) ||
                roots.SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true))
                    .Any(filter => filter.name.StartsWith("Raised garden bed ", StringComparison.Ordinal)))
                return "Garden detail already installed.";
            var crags = roots.SelectMany(root => root.GetComponentsInChildren<MeshCollider>())
                .Where(collider => collider.enabled && collider.sharedMesh &&
                    (collider.sharedMesh.name == "Upper_Sanctuary_Crag" || collider.sharedMesh.name == "Lower_Sanctuary_Crag"))
                .ToArray();
            if (crags.Length != 2) throw new InvalidOperationException("The two summit ground colliders are required.");
            var shader = Shader.Find("OC Design/Comic Lighting/Surface");
            if (!shader) throw new InvalidOperationException("The courtyard comic surface shader is required.");

            Physics.SyncTransforms();
            var physics = scene.GetPhysicsScene();
            var random = new Random(Seed);
            var patches = new List<(string Name, PlantMesh Mesh)>();
            foreach (var area in Terraces)
            {
                var patch = new PlantMesh();
                var origins = new List<Vector2>();
                for (int i = 0; i < area.Count * 4 && patch.Tufts < area.Count; i++)
                {
                    var offset = InDisc(random);
                    var point = new Vector2(area.X + offset.x * area.RadiusX, area.Z + offset.y * area.RadiusZ);
                    if (!CanPlant(point, origins) || !TryGround(physics, crags, point, out var ground)) continue;
                    var scale = Range(random, .65f, 1.12f);
                    AddTuft(patch, ground, scale, random);
                    if (patch.Tufts % 12 == 4) AddFern(patch, ground, scale, random);
                    origins.Add(point);
                }
                if (patch.Tufts > 0) patches.Add((area.Name, patch));
            }

            // Existing raised garden beds have no physics surface. Sample the authored mesh itself.
            foreach (var renderer in roots.SelectMany(root => root.GetComponentsInChildren<MeshRenderer>())
                .Where(r => r.enabled && r.name == "Surface - Garden_dry_sage").OrderBy(r => r.bounds.center.x))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                var patch = PlantBed(renderer, filter, random);
                if (patch.Tufts > 0) patches.Add(("Raised garden bed " + patches.Count.ToString("00"), patch));
            }
            if (patches.Count == 0) throw new InvalidOperationException("No approved planting surfaces were found.");

            if (!AssetDatabase.IsValidFolder(AssetFolder))
                AssetDatabase.CreateFolder(SamuraiComicStyleSetup.Root + "/BlenderAssembly", "GardenDetails");
            var materials = new[]
            {
                PlantMaterial(shader, "01 Moss and fern", "#687C52", "#374E49"),
                PlantMaterial(shader, "02 Meadow sage", "#8E9A60", "#4D6454"),
                PlantMaterial(shader, "03 Dry grass tips", "#A6945C", "#665F4D"),
            };

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Dress Blender courtyard gardens");
            var group = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(group, scene);
            Undo.RegisterCreatedObjectUndo(group, "Create garden detail group");
            foreach (var patch in patches)
            {
                var child = new GameObject(patch.Name);
                Undo.RegisterCreatedObjectUndo(child, "Add planted garden");
                child.transform.SetParent(group.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = SaveMesh(patch.Mesh.Build(patch.Name));
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undo);
            int blades = patches.Sum(patch => patch.Mesh.Tufts);
            int ferns = patches.Sum(patch => patch.Mesh.Ferns);
            int faces = patches.Sum(patch => patch.Mesh.Triangles.Sum(indices => indices.Count / 3));
            return $"Installed {patches.Count} planted areas, {blades} grass tufts and {ferns} ferns ({faces} triangles).";
        }

        static PlantMesh PlantBed(MeshRenderer renderer, MeshFilter filter, Random random)
        {
            var patch = new PlantMesh();
            var bounds = renderer.bounds;
            var vertices = filter.sharedMesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = filter.transform.TransformPoint(vertices[i]);
            var triangles = filter.sharedMesh.triangles;
            var origins = new List<Vector2>();
            var bed = renderer.transform.parent;
            var garden = bed ? bed.parent : null;
            var stones = garden
                ? garden.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && r.transform.parent &&
                    r.transform.parent.name.StartsWith("Stone ", StringComparison.Ordinal))
                    .Select(r => r.bounds).ToArray()
                : Array.Empty<Bounds>();
            for (int i = 0; i < 210 && patch.Tufts < 43; i++)
            {
                var offset = InDisc(random);
                // Leave the trunk base and the inside of its canopy clean.
                if (offset.sqrMagnitude < .12f) continue;
                var point = new Vector2(bounds.center.x + offset.x * bounds.extents.x * .92f,
                    bounds.center.z + offset.y * bounds.extents.z * .92f);
                if (!CanPlant(point, origins) || stones.Any(stone =>
                    point.x > stone.min.x - .14f && point.x < stone.max.x + .14f &&
                    point.y > stone.min.z - .14f && point.y < stone.max.z + .14f)) continue;
                if (!SampleBed(vertices, triangles, point, out float y)) continue;
                var origin = new Vector3(point.x, y, point.y);
                var scale = Range(random, .60f, .90f);
                AddTuft(patch, origin, scale, random);
                if (patch.Tufts % 11 == 6) AddFern(patch, origin, scale, random);
                origins.Add(point);
            }
            return patch;
        }

        static bool SampleBed(Vector3[] vertices, int[] triangles, Vector2 point, out float y)
        {
            y = float.NegativeInfinity;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var ab = vertices[triangles[i + 1]] - a;
                var ac = vertices[triangles[i + 2]] - a;
                var normal = Vector3.Cross(ab, ac);
                if (Mathf.Abs(normal.y) < .61f * normal.magnitude || Mathf.Abs(normal.y) < .00001f) continue;
                float x = point.x - a.x;
                float z = point.y - a.z;
                float determinant = ab.x * ac.z - ab.z * ac.x;
                float u = (x * ac.z - z * ac.x) / determinant;
                float v = (z * ab.x - x * ab.z) / determinant;
                if (u >= 0 && v >= 0 && u + v <= 1)
                    y = Mathf.Max(y, a.y + ab.y * u + ac.y * v);
            }
            return !float.IsNegativeInfinity(y);
        }

        static bool TryGround(PhysicsScene physics, MeshCollider[] crags, Vector2 point, out Vector3 ground)
        {
            ground = default;
            var ray = new Ray(new Vector3(point.x, 40f, point.y), Vector3.down);
            RaycastHit surface = default;
            float top = float.NegativeInfinity;
            foreach (var crag in crags)
            {
                if (!crag.Raycast(ray, out var hit, 60f) || hit.point.y <= top) continue;
                surface = hit;
                top = hit.point.y;
            }
            if (top < -10f || surface.normal.y < .68f) return false;
            var above = surface.point + Vector3.up * 12f;
            if (!physics.Raycast(above, Vector3.down, out var cover, 12.05f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) || cover.collider != surface.collider) return false;
            ground = surface.point;
            return true;
        }

        static bool CanPlant(Vector2 point, List<Vector2> origins)
        {
            if ((point - new Vector2(-37.90f, 18.61f)).sqrMagnitude < 2.30f * 2.30f) return false;
            foreach (var route in GardenRoutes)
            {
                var segment = route.B - route.A;
                float t = Mathf.Clamp01(Vector2.Dot(point - route.A, segment) / segment.sqrMagnitude);
                if ((point - (route.A + segment * t)).sqrMagnitude < route.Clear * route.Clear) return false;
            }
            return origins.All(previous => (point - previous).sqrMagnitude > .24f * .24f);
        }

        static void AddTuft(PlantMesh mesh, Vector3 origin, float scale, Random random)
        {
            mesh.Tufts++;
            float start = Range(random, 0, 2 * Mathf.PI);
            int count = random.Next(4, 8);
            bool dry = random.NextDouble() < .23;
            for (int i = 0; i < count; i++)
            {
                float a = start + i * 2.399963f + Range(random, -.21f, .21f);
                var outward = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var across = Vector3.Cross(Vector3.up, outward);
                var root = origin + outward * Range(random, .015f, .068f) * scale - Vector3.up * .017f;
                float height = Range(random, .17f, .39f) * scale;
                float spread = Range(random, .06f, .22f) * scale;
                float width = Range(random, .022f, .044f) * scale;
                var knee = root + Vector3.up * (height * .65f) + outward * (spread * .38f);
                var tip = root + Vector3.up * height + outward * spread;
                mesh.Strip(dry && i % 3 == 0 ? 2 : i % 3 == 0 ? 1 : 0,
                    root, knee, tip, across, width, width * .62f);
            }
        }

        static void AddFern(PlantMesh mesh, Vector3 origin, float scale, Random random)
        {
            mesh.Ferns++;
            float start = Range(random, 0, 2 * Mathf.PI);
            for (int f = 0; f < 4; f++)
            {
                float a = start + f * Mathf.PI * .5f + Range(random, -.20f, .20f);
                var outward = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var sideways = Vector3.Cross(Vector3.up, outward);
                float length = Range(random, .29f, .50f) * scale;
                float height = Range(random, .12f, .25f) * scale;
                var root = origin - Vector3.up * .015f;
                var knee = origin + outward * (length * .5f) + Vector3.up * (height * .80f);
                var tip = origin + outward * length + Vector3.up * (height * .66f);
                mesh.Strip(0, root, knee, tip, sideways, .016f * scale, .012f * scale);
                for (int i = 1; i <= 4; i++)
                {
                    float t = i / 5f;
                    float sideLength = length * (.33f - .17f * t);
                    var position = Vector3.Lerp(Vector3.Lerp(root, knee, t), Vector3.Lerp(knee, tip, t), t);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        var leaf = sideways * sign * sideLength + outward * (length * .12f);
                        mesh.Strip(i % 3 == 0 ? 1 : 0, position,
                            position + leaf * .45f + Vector3.up * .018f * scale,
                            position + leaf + Vector3.up * .005f * scale,
                            outward, .020f * scale, .014f * scale);
                    }
                }
            }
        }

        static Vector2 InDisc(Random random)
        {
            float angle = Range(random, 0, Mathf.PI * 2);
            float radius = Mathf.Sqrt((float)random.NextDouble());
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        static float Range(Random random, float min, float max)
        {
            return min + (max - min) * (float)random.NextDouble();
        }

        static Material PlantMaterial(Shader shader, string name, string lit, string shade)
        {
            string path = AssetFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(shader) { name = name, enableInstancing = true };
            ColorUtility.TryParseHtmlString(lit, out var litColor);
            ColorUtility.TryParseHtmlString(shade, out var shadeColor);
            material.SetColor("_BaseColor", litColor);
            material.SetColor("_ShadowColor", shadeColor);
            material.SetFloat("_Threshold", .22f);
            material.SetFloat("_Feather", .07f);
            material.SetFloat("_PaperStrength", .022f);
            material.SetFloat("_PaperScale", 1.3f);
            material.SetFloat("_ShadowStrength", .88f);
            material.SetFloat("_Cull", 0);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Mesh SaveMesh(Mesh mesh)
        {
            string path = AssetFolder + "/" + mesh.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
    }
}
