using System;
using System.Collections.Generic;
using System.Linq;
using OCDesign.LookDev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiCourtyardTreeDetailSetup
    {
        const string RootName = "21 Tree details - 树根与幼松";
        const string AssetParent = SamuraiComicStyleSetup.Root + "/BlenderAssembly";
        const string Assets = AssetParent + "/TreeDetails";

        static readonly Vector2[] PineSites =
        {
            new Vector2(-42.6f, 20.8f), new Vector2(-43.1f, 7.6f),
            new Vector2(-38.7f, 24.9f), new Vector2(-23.1f, 26.0f),
            new Vector2(-31.8f, -19.4f), new Vector2(13.1f, -18.9f),
            new Vector2(30.7f, -18.6f), new Vector2(33.1f, 11.8f),
            new Vector2(5.0f, -18.5f)
        };

        static readonly Vector2[] PineSearch =
        {
            Vector2.zero, new Vector2(.7f, .2f), new Vector2(-.6f, .55f),
            new Vector2(.45f, -.75f), new Vector2(-.8f, -.4f)
        };

        [MenuItem("Katana/Level/Detail Trees In Blender Courtyard")]
        public static string Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiBlenderAssemblyImport.ScenePath)
                throw new InvalidOperationException("Open the Blender courtyard scene in Edit mode.");

            Transform dressing = scene.GetRootGameObjects()
                .Single(g => g.name == SamuraiCourtyardDecorationSetup.RootName).transform;
            if (dressing.GetComponentsInChildren<MeshFilter>(true)
                .Any(filter => filter.name.EndsWith(" - 根盘与落叶", StringComparison.Ordinal)))
                return "Tree details already present.";

            Transform[] gardens = dressing.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("Copper maple garden ", StringComparison.Ordinal)
                         || t.name.StartsWith("Ink pine garden ", StringComparison.Ordinal))
                .Where(t => t.Find("Editable components - 可调部件"))
                .OrderBy(t => t.name, StringComparer.Ordinal).ToArray();
            Transform[] existingPines = dressing.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("Mountain pine ", StringComparison.Ordinal)).ToArray();
            var ground = dressing.GetComponentsInChildren<MeshCollider>()
                .Where(c => c.enabled && c.sharedMesh && c.sharedMesh.name.EndsWith("_Sanctuary_Crag", StringComparison.Ordinal))
                .ToArray();
            if (gardens.Length == 0 || ground.Length != 2)
                throw new InvalidOperationException("The existing tree gardens and the two summit colliders are required.");

            Material[] gardenMaterials =
            {
                Palette("Old_honey_timber"), Palette("Maple_copper"), Palette("Maple_peach"), Palette("Maple_brick")
            };
            Material[] pineMaterials = { gardenMaterials[0], Palette("Pine_dark_teal"), Palette("Pine_sage") };
            Mesh[] pines = AssetDatabase.LoadAllAssetsAtPath(SamuraiMountainCourtyardSetup.Model).OfType<Mesh>()
                .Where(m => m.name == "Pine_A" || m.name == "Pine_B" || m.name == "Pine_C")
                .OrderBy(m => m.name, StringComparer.Ordinal).ToArray();
            if (pines.Length != 3 || pines.Any(m => m.subMeshCount != pineMaterials.Length))
                throw new InvalidOperationException("The three existing pine meshes are required.");

            if (!AssetDatabase.IsValidFolder(Assets))
                AssetDatabase.CreateFolder(AssetParent, "TreeDetails");
            Physics.SyncTransforms();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Detail courtyard trees");
            Transform root = Create(RootName, dressing);
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Add tree details");
            try
            {
                int rootCount = 0, leafCount = 0;
                foreach (Transform garden in gardens)
                {
                    Transform trunk = garden.GetComponentsInChildren<Transform>()
                        .FirstOrDefault(t => t.name.StartsWith("03 Trunk", StringComparison.Ordinal));
                    Transform cover = garden.GetComponentsInChildren<Transform>()
                        .FirstOrDefault(t => t.name.StartsWith("02 Ground cover", StringComparison.Ordinal));
                    if (!trunk || !cover) continue;
                    MeshFilter filter = cover.GetComponentInChildren<MeshFilter>();
                    if (!filter || !filter.sharedMesh) continue;

                    Vector3 origin = garden.position;
                    var coverHeight = new GroundCover(filter);
                    var detail = new DetailMesh(origin);
                    var random = new System.Random(1469 + rootCount * 103);
                    Vector3 foot = Vector3.Lerp(garden.position, trunk.position, .27f);
                    float startAngle = Range(random, 0f, 6.283185f);
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 outward = Direction(startAngle + i * 1.5708f + Range(random, -.32f, .32f));
                        Vector3 start = foot + outward * .13f;
                        Vector3 end = foot + outward * Range(random, .65f, 1.16f);
                        if (!coverHeight.TryHeight(start, out float startY)
                            || !coverHeight.TryHeight(end, out float endY)
                            || Mathf.Abs(startY - endY) > .48f) continue;
                        start.y = startY;
                        end.y = endY;
                        detail.Root(start, end, Range(random, .11f, .18f), Range(random, .15f, .25f));
                    }

                    if (garden.name.StartsWith("Copper maple", StringComparison.Ordinal))
                    {
                        for (int i = 0; i < 38; i++)
                        {
                            float angle = startAngle + (i % 3) * 2.0944f + Range(random, -.58f, .58f);
                            Vector3 center = foot + Direction(angle) * Range(random, .52f, 2.15f);
                            if (!coverHeight.TryHeight(center, out float y)) continue;
                            center.y = y + Range(random, .019f, .044f);
                            detail.Leaf(center, Direction(Range(random, 0f, 6.283185f)),
                                Range(random, .038f, .088f), 1 + random.Next(3));
                            leafCount++;
                        }
                    }
                    if (detail.VertexCount == 0) continue;
                    var mesh = detail.Save(garden.name);
                    Transform node = Create(garden.name + " - 根盘与落叶", root);
                    node.position = origin;
                    Surface(node, mesh, gardenMaterials, ShadowCastingMode.Off);
                    rootCount++;
                }

                var plants = Create("Understory pines - 石边幼松", root);
                var occupied = gardens.Concat(existingPines).Select(t => new Vector2(t.position.x, t.position.z)).ToList();
                int pineCount = 0;
                for (int i = 0; i < PineSites.Length; i++)
                {
                    var mesh = pines[i % pines.Length];
                    float scale = .29f + i % 3 * .047f;
                    foreach (Vector2 shift in PineSearch)
                    {
                        Vector2 point = PineSites[i] + shift;
                        if ((point - new Vector2(-37.90f, 18.61f)).sqrMagnitude < 12.25f
                            || occupied.Any(t => (t - point).sqrMagnitude < 5.76f)
                            || !TryGround(ground, point, out RaycastHit hit)
                            || hit.normal.y < .86f) continue;

                        float height = mesh.bounds.size.y * scale;
                        var boundsCenter = hit.point + Vector3.up * (height * .5f + .13f);
                        Vector3 extents = new Vector3(1.03f, height * .5f - .10f, 1.03f);
                        if (Physics.OverlapBox(boundsCenter, extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                            .Any(c => !ground.Contains(c))) continue;
                        Transform pine = Create("Understory pine " + (++pineCount).ToString("00"), plants);
                        pine.position = hit.point - Vector3.up * (.045f + mesh.bounds.min.y * scale);
                        pine.rotation = Quaternion.Euler(0f, i * 137.5f, 0f);
                        pine.localScale = Vector3.one * scale;
                        Surface(pine, mesh, pineMaterials, ShadowCastingMode.On);
                        occupied.Add(point);
                        break;
                    }
                }
                if (pineCount == 0) throw new InvalidOperationException("No clear understory planting sites were found.");
                foreach (var outline in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ComicScreenOutline>()))
                    outline.RefreshSources();
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return $"Detailed {rootCount} tree gardens, {leafCount} fallen maple leaves and {pineCount} understory pines.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
        }

        static Material Palette(string name)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                SamuraiCourtyardDecorationSetup.Root + "/Materials/" + name + ".mat");
            if (!material) throw new InvalidOperationException("Missing courtyard palette " + name);
            return material;
        }

        static Transform Create(string name, Transform parent)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            return node;
        }

        static void Surface(Transform node, Mesh mesh, Material[] materials, ShadowCastingMode shadow)
        {
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = shadow;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(node.gameObject, StaticEditorFlags.BatchingStatic);
        }

        static bool TryGround(MeshCollider[] ground, Vector2 point, out RaycastHit highest)
        {
            highest = default;
            bool found = false;
            var ray = new Ray(new Vector3(point.x, 60f, point.y), Vector3.down);
            foreach (var collider in ground)
                if (collider.Raycast(ray, out RaycastHit hit, 95f) && (!found || hit.point.y > highest.point.y))
                {
                    highest = hit;
                    found = true;
                }
            return found;
        }

        static Vector3 Direction(float angle) => new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

        static float Range(System.Random random, float min, float max) =>
            Mathf.Lerp(min, max, (float)random.NextDouble());

        sealed class GroundCover
        {
            readonly Vector3[] vertices;
            readonly int[] triangles;

            public GroundCover(MeshFilter filter)
            {
                vertices = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                triangles = filter.sharedMesh.triangles;
            }

            public bool TryHeight(Vector3 at, out float highest)
            {
                highest = float.NegativeInfinity;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Vector3 ab = b - a, ac = c - a, q = at - a;
                    float determinant = ab.x * ac.z - ab.z * ac.x;
                    if (Mathf.Abs(determinant) < .0001f) continue;
                    float u = (q.x * ac.z - q.z * ac.x) / determinant;
                    float v = (ab.x * q.z - ab.z * q.x) / determinant;
                    if (u >= -.0001f && v >= -.0001f && u + v <= 1.0001f)
                        highest = Mathf.Max(highest, a.y + u * ab.y + v * ac.y);
                }
                return !float.IsNegativeInfinity(highest);
            }
        }

        sealed class DetailMesh
        {
            readonly Vector3 origin;
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int>[] submeshes = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

            public DetailMesh(Vector3 origin) => this.origin = origin;
            public int VertexCount => vertices.Count;

            public void Root(Vector3 start, Vector3 end, float width, float height)
            {
                Vector3 side = Vector3.Cross(Vector3.up, end - start).normalized;
                Vector3 middle = Vector3.Lerp(start, end, .49f) + side * width * .35f;
                Vector3 nearLeft = start - side * width - Vector3.up * .02f;
                Vector3 nearTop = start + Vector3.up * height;
                Vector3 nearRight = start + side * width - Vector3.up * .03f;
                Vector3 midLeft = middle - side * width * .53f - Vector3.up * .026f;
                Vector3 midTop = middle + Vector3.up * height * .48f;
                Vector3 midRight = middle + side * width * .64f - Vector3.up * .018f;
                Vector3 tip = end - Vector3.up * .035f;
                Triangle(nearLeft, nearTop, midTop, 0);
                Triangle(nearLeft, midTop, midLeft, 0);
                Triangle(nearTop, nearRight, midRight, 0);
                Triangle(nearTop, midRight, midTop, 0);
                Triangle(midLeft, midTop, tip, 0);
                Triangle(midTop, midRight, tip, 0);
            }

            public void Leaf(Vector3 center, Vector3 forward, float size, int material)
            {
                Vector3 side = Vector3.Cross(Vector3.up, forward);
                Vector3 tip = center + forward * size;
                Vector3 basePoint = center - forward * size * .82f;
                Vector3 leftNotch = center + forward * size * .23f - side * size * .32f;
                Vector3 leftLobe = center + forward * size * .35f - side * size * .80f;
                Vector3 rightNotch = center + forward * size * .23f + side * size * .32f;
                Vector3 rightLobe = center + forward * size * .29f + side * size * .74f;
                center.y += size * .13f;
                Triangle(center, tip, leftNotch, material);
                Triangle(center, leftNotch, leftLobe, material);
                Triangle(center, leftLobe, basePoint, material);
                Triangle(center, basePoint, rightLobe, material);
                Triangle(center, rightLobe, rightNotch, material);
                Triangle(center, rightNotch, tip, material);
            }

            void Triangle(Vector3 a, Vector3 b, Vector3 c, int material)
            {
                var indices = submeshes[material];
                int index = vertices.Count;
                vertices.Add(a - origin);
                vertices.Add(b - origin);
                vertices.Add(c - origin);
                if (Vector3.Cross(b - a, c - a).y >= 0f)
                {
                    indices.Add(index);
                    indices.Add(index + 1);
                    indices.Add(index + 2);
                }
                else
                {
                    indices.Add(index);
                    indices.Add(index + 2);
                    indices.Add(index + 1);
                }
            }

            public Mesh Save(string name)
            {
                string path = Assets + "/" + name.Replace(' ', '_') + ".asset";
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing) return existing;
                var mesh = new Mesh { name = name + " roots and leaves" };
                mesh.SetVertices(vertices);
                mesh.subMeshCount = submeshes.Length;
                for (int i = 0; i < submeshes.Length; i++) mesh.SetTriangles(submeshes[i], i);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
        }
    }
}
