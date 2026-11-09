using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OCDesign.LookDev;
using OCDesign.LookDev.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiRidgeHallSetup
    {
        public const string RootName = "11 Ridge trail lodge - 山道观峡大屋";
        const string Root = SamuraiComicStyleSetup.Root + "/RidgeHall";
        const string PrefabPath = Root + "/Prefabs/RidgeTrailLodge.prefab";
        const float HalfLength = 7.21f;
        const float HalfWidth = 2.45f;
        const float West = -13.5f;
        const float East = -2.6f;
        const float LengthScale = (East - West) / (2f * HalfLength);
        static readonly Vector3 Center = new Vector3((West + East) * .5f, 4.9f, 25.5f);
        static readonly float[] Posts = { -7.06f, -4.7f, -2.35f, 0f, 2.35f, 4.7f, 7.06f };
        static readonly Dictionary<string, string> DecorationMaterials = new Dictionary<string, string>
        {
            { "Roof", "Ink_blue_roof" }, { "Gold", "Quiet_ochre" },
            { "Timber", "Old_honey_timber" }, { "Red", "Vermilion_lacquer" },
            { "Plaster", "Ivory_plaster" }, { "Stone", "Warm_sandstone" },
            { "Slate", "Foundation_slate" }, { "Paper", "Warm_lantern_paper" }
        };

        [MenuItem("Katana/Level/Install Ridge Trail Lodge")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().First(g => g.name == "Courtyard dressing").transform;
            if (dressing.Find(RootName)) return "Ridge trail lodge already installed. Existing adjustments retained.";

            var path = dressing.Find(SamuraiLevelFlowSetup.RootName + "/03 High maintenance path - 高处单向山路");
            if (!path || !path.gameObject.activeInHierarchy) throw new InvalidOperationException("Install the active mountain route first.");
            var oldDeck = path.Find("Continuous walking deck - 连续高路行走面").GetComponent<MeshCollider>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab)
            {
                Physics.SyncTransforms();
                prefab = CreatePrefab(SampleRidgeDeck(oldDeck));
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build ridge trail lodge");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Place ridge trail lodge");
            instance.name = RootName;
            Undo.SetTransformParent(instance.transform, dressing, "Organize ridge trail lodge");
            instance.transform.SetPositionAndRotation(Center, Quaternion.identity);
            SamuraiRidgeHallRailTrim.Trim(path, instance.transform, Root + "/Meshes", West, East);

            if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var outline)) outline.RefreshSources();
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            return "Ridge trail lodge installed on the keyed high route. Main bridge and original progression remain active.";
        }

        static float[] SampleRidgeDeck(MeshCollider deck)
        {
            const int intervals = 64;
            var heights = new float[intervals + 1];
            for (int i = 0; i <= intervals; i++)
            {
                float x = Mathf.Lerp(-HalfLength, HalfLength, i / (float)intervals) * LengthScale;
                var ray = new Ray(Center + new Vector3(x, 4f, 0f), Vector3.down);
                if (!deck.Raycast(ray, out var hit, 5f))
                    throw new InvalidOperationException("Cannot sample original ridge deck at x=" + (Center.x + x));
                heights[i] = hit.point.y - Center.y + .006f;
            }
            return heights;
        }

        static GameObject CreatePrefab(float[] heights)
        {
            foreach (string folder in new[] { "Meshes", "Contours", "Prefabs" }) Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            var root = new GameObject("Ridge Trail Lodge");
            try
            {
                float Floor(float x)
                {
                    float index = Mathf.InverseLerp(-HalfLength, HalfLength, x) * (heights.Length - 1);
                    int before = Mathf.Min(Mathf.FloorToInt(index), heights.Length - 2);
                    return Mathf.Lerp(heights[before], heights[before + 1], index - before);
                }

                var platform = Group(root.transform, "01 Continuous crossing floor - 一体缓坡通道");
                var board = new Solid();
                var xs = Enumerable.Range(0, heights.Length).Select(i => Mathf.Lerp(-HalfLength, HalfLength, i / (float)(heights.Length - 1))).ToArray();
                board.Profile(xs, heights, heights.Select(y => y - .29f).ToArray(), -HalfWidth, HalfWidth);
                var deck = Render(platform, "RidgeHall_Continuous_Deck", board.Mesh(), "Deck", true);
                deck.gameObject.AddComponent<MeshCollider>().sharedMesh = deck.GetComponent<MeshFilter>().sharedMesh;
                var joints = new PaletteGroup(platform, "Floor inlay and joints - 地板拼缝与行走引导");
                for (float x = -7f; x < 7f; x += .39f)
                    joints.Box("Dark", new Vector3(x, Floor(x) + .002f, 0), new Vector3(.013f, .004f, 4.77f));
                foreach (int side in new[] { -1, 1 })
                {
                    var stripe = new Solid();
                    stripe.Profile(xs, heights.Select(y => y + .004f).ToArray(), heights.Select(y => y + .001f).ToArray(), side * .61f - .026f, side * .61f + .026f);
                    Render(platform, "RidgeHall_Floor_Inlay_" + side, stripe.Mesh(), "Gold");
                    var stringer = new Solid();
                    stringer.Profile(xs, heights.Select(y => y - .20f).ToArray(), heights.Select(y => y - .71f).ToArray(), side * 2.22f - .13f, side * 2.22f + .13f);
                    Render(platform, "RidgeHall_Outer_Stringer_" + side, stringer.Mesh(), "Dark");
                }
                for (int i = 0; i < Posts.Length; i++)
                {
                    float x = Posts[i], y = Floor(x);
                    joints.Box("Dark", new Vector3(x, y - .48f, 0), new Vector3(.33f, .40f, 5.14f));
                    foreach (int side in new[] { -1, 1 })
                        joints.Beam("Timber", new Vector3(x, y - 1.12f, side * 1.30f), new Vector3(x, y - .45f, side * 2.38f), .22f, .23f);
                }
                joints.Flush("Floor_Detail");

                BuildWalls(root.transform, Floor);
                BuildPortals(root.transform, Floor);
                BuildInterior(root.transform, Floor);
                var roof = Group(root.transform, "05 Roof and ridge - 青瓦大屋顶");
                bool first = true;
                SamuraiRidgeHallRoof.Build((name, mesh, material) =>
                {
                    var surface = Render(roof, name, mesh, material, first);
                    if (first)
                        surface.gameObject.AddComponent<MeshCollider>().sharedMesh = surface.GetComponent<MeshFilter>().sharedMesh;
                    first = false;
                });

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void BuildWalls(Transform parent, Func<float, float> floor)
        {
            var walls = new PaletteGroup(parent, "02 Window bays and structure - 柱廊与观景窗");
            foreach (int side in new[] { -1, 1 })
            {
                float z = 2.34f * side;
                foreach (float x in Posts)
                {
                    float bottom = floor(x) - .09f;
                    walls.Box("Stone", new Vector3(x, bottom + .19f, z), new Vector3(.43f, .38f, .43f));
                    walls.Box("Red", new Vector3(x, (bottom + 5.12f) * .5f, z), new Vector3(.28f, 5.12f - bottom, .31f));
                    walls.Box("Dark", new Vector3(x, 4.78f, z), new Vector3(.42f, .18f, .42f));
                    walls.Box("Gold", new Vector3(x, 4.69f, z), new Vector3(.40f, .05f, .39f));
                }
                walls.Box("Dark", new Vector3(0, 4.94f, z), new Vector3(14.44f, .29f, .34f));
                walls.Box("Red", new Vector3(0, 4.35f, z), new Vector3(14.37f, .19f, .25f));
                for (int i = 0; i < Posts.Length - 1; i++)
                {
                    float start = Posts[i] + .11f, end = Posts[i + 1] - .11f;
                    float x = (start + end) * .5f, span = end - start;
                    float sill = floor(x) + 1.04f;
                    walls.Profile("Plaster", new[] { start, end }, new[] { sill, sill }, new[] { floor(start) - .13f, floor(end) - .13f }, z - .11f, z + .11f);
                    walls.Profile("Dark", new[] { start, end }, new[] { floor(start) + .27f, floor(end) + .27f }, new[] { floor(start) - .09f, floor(end) - .09f }, z - .13f, z + .13f);
                    walls.Box("Plaster", new Vector3(x, 4.57f, z), new Vector3(span, .37f, .19f));
                    walls.Box("Timber", new Vector3(x, sill, z), new Vector3(span, .115f, .28f));
                    walls.Box("Dark", new Vector3(x, 3.83f, z), new Vector3(span, .12f, .21f));
                    walls.Box("Plaster", new Vector3(x, 4.065f, z), new Vector3(span, .35f, .16f));
                    float middle = (sill + 3.83f) * .5f;
                    for (int v = -2; v <= 2; v++)
                        walls.Box(v == 0 ? "Timber" : "Dark", new Vector3(x + span * .17f * v, middle, z), new Vector3(v == 0 ? .10f : .048f, 3.83f - sill, .105f));
                    walls.Box("Dark", new Vector3(x, middle, z), new Vector3(span, .058f, .105f));
                    walls.Box("Gold", new Vector3(x, sill + .058f, z), new Vector3(span * .72f, .026f, .24f));
                }
                Collision(walls.Root, "Continuous window wall collision", new Vector3(0f, 2.32f, z), new Vector3(14.42f, 5.2f, .235f));
            }
            walls.Flush("Window_Bay");
        }

        static void BuildPortals(Transform parent, Func<float, float> floor)
        {
            var portals = new PaletteGroup(parent, "03 Open ridge entrances - 两端山道门廊");
            foreach (int end in new[] { -1, 1 })
            {
                float x = end * 7.02f, y = floor(end * HalfLength);
                foreach (int side in new[] { -1, 1 })
                {
                    float z = side * 1.15f;
                    portals.Box("Stone", new Vector3(x, y + .15f, z), new Vector3(.60f, .40f, .50f));
                    portals.Box("Red", new Vector3(x, (y + 5.13f) * .5f, z), new Vector3(.43f, 5.13f - y, .42f));
                    portals.Box("Dark", new Vector3(x, 4.70f, z), new Vector3(.61f, .22f, .59f));
                    float window = side * 1.83f;
                    portals.Box("Plaster", new Vector3(x, y + .60f, window), new Vector3(.25f, 1.26f, 1.24f));
                    portals.Box("Plaster", new Vector3(x, 4.15f, window), new Vector3(.25f, 1.42f, 1.24f));
                    portals.Box("Timber", new Vector3(x, y + 1.24f, window), new Vector3(.33f, .095f, 1.24f));
                    for (int i = -2; i <= 2; i++)
                        portals.Box("Dark", new Vector3(x, (y + 1.24f + 3.45f) * .5f, window + i * .21f), new Vector3(.17f, 3.45f - y - 1.24f, .055f));
                    Collision(portals.Root, "Ridge doorway flanking wall", new Vector3(x, (y + 4.76f) * .5f, side * 1.69f), new Vector3(.54f, 4.80f - y, 1.58f));
                }
                portals.Box("Dark", new Vector3(x, 4.96f, 0), new Vector3(.45f, .28f, 5.0f));
                portals.Box("Red", new Vector3(x, 4.55f, 0), new Vector3(.32f, .29f, 4.5f));
                portals.Box("Gold", new Vector3(x + end * .184f, 4.54f, 0), new Vector3(.08f, .04f, 4.27f));
                float face = x + end * .30f;
                portals.Box("Dark", new Vector3(face, 4.30f, 0), new Vector3(.11f, .48f, 1.42f));
                portals.Box("Gold", new Vector3(face + end * .06f, 4.51f, 0), new Vector3(.05f, .037f, 1.36f));
                portals.Box("Gold", new Vector3(face + end * .06f, 4.09f, 0), new Vector3(.05f, .037f, 1.36f));
                for (int crest = -1; crest <= 1; crest++)
                {
                    float z = crest * .32f, faceX = face + end * .075f;
                    var peak = new Vector3(faceX, 4.41f - Mathf.Abs(crest) * .05f, z);
                    portals.Beam("Gold", new Vector3(faceX, 4.23f, z - .135f), peak, .042f, .040f);
                    portals.Beam("Gold", peak, new Vector3(faceX, 4.23f, z + .135f), .042f, .040f);
                }
            }
            portals.Flush("Open_Entrance");
        }

        static void BuildInterior(Transform parent, Func<float, float> floor)
        {
            var trusses = new PaletteGroup(parent, "04 Interior trusses and lanterns - 梁架与暖灯");
            foreach (float x in Posts)
            {
                trusses.Box("Timber", new Vector3(x, 4.80f, 0), new Vector3(.21f, .24f, 4.66f));
                trusses.Box("Dark", new Vector3(x, 5.45f, 0), new Vector3(.16f, 1.31f, .19f));
                foreach (int side in new[] { -1, 1 })
                {
                    trusses.Beam("Timber", new Vector3(x, 5.03f, side * 2.30f), new Vector3(x, 6.15f, 0), .18f, .19f);
                    trusses.Beam("Red", new Vector3(x, 4.11f, side * 2.24f), new Vector3(x, 4.72f, side * 1.63f), .14f, .14f);
                }
            }
            foreach (float x in new[] { -5.9f, -3.5f, -1.17f, 1.17f, 3.5f, 5.9f })
                foreach (int side in new[] { -1, 1 })
                {
                    float z = side * 1.82f;
                    trusses.Box("Dark", new Vector3(x, 4.48f, z), new Vector3(.044f, .65f, .044f));
                    trusses.Box("Paper", new Vector3(x, 4.00f, z), new Vector3(.30f, .47f, .30f));
                    foreach (float y in new[] { 3.75f, 4.25f })
                        trusses.Box("Dark", new Vector3(x, y, z), new Vector3(.36f, .065f, .36f));
                    foreach (float dx in new[] { -.15f, .15f }) foreach (float dz in new[] { -.15f, .15f })
                        trusses.Box("Red", new Vector3(x + dx, 4.00f, z + dz), new Vector3(.032f, .48f, .032f));
                }
            trusses.Flush("Interior_Joinery");
            var seating = new PaletteGroup(parent, "06 Viewing alcoves - 室内观峡座席");
            foreach (int side in new[] { -1, 1 })
            {
                float y = floor(0), z = side * 1.93f;
                seating.Box("Timber", new Vector3(0, y + .39f, z), new Vector3(3.56f, .14f, .49f));
                foreach (float x in new[] { -1.47f, 0f, 1.47f })
                    seating.Box("Dark", new Vector3(x, y + .18f, z), new Vector3(.19f, .42f, .43f));
                seating.Box("Gold", new Vector3(0, y + .47f, z), new Vector3(2.5f, .025f, .33f));
                Collision(seating.Root, "Window seat collision", new Vector3(0, y + .23f, z), new Vector3(3.56f, .46f, .49f));
            }
            seating.Flush("Window_Seats");
        }

        static Transform Group(Transform parent, string name)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            return node;
        }

        static void Collision(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var node = Group(parent, name);
            var collider = node.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(center.x * LengthScale, center.y, center.z);
            collider.size = new Vector3(size.x * LengthScale, size.y, size.z);
        }

        static Material MaterialFor(string key)
        {
            string path;
            if (key == "Deck") path = SamuraiLevelFlowSetup.Root + "/Materials/Bridge_landmark_timber.mat";
            else if (key == "Dark") path = SamuraiLevelFlowSetup.Root + "/Materials/Deep_gate_timber.mat";
            else path = SamuraiCourtyardDecorationSetup.Root + "/Materials/" + DecorationMaterials[key] + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) throw new InvalidOperationException("Missing ridge lodge material: " + path);
            return material;
        }

        static Transform Render(Transform parent, string name, Mesh mesh, string key, bool contour = false)
        {
            string path = Root + "/Meshes/" + name + ".asset";
            var previous = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (previous)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = previous;
            }
            else
            {
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i].x *= LengthScale;
                mesh.vertices = vertices;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.name = name;
                AssetDatabase.CreateAsset(mesh, path);
            }
            var surface = Group(parent, name);
            surface.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            ConfigureRenderer(surface.gameObject.AddComponent<MeshRenderer>(), MaterialFor(key));
            if (contour)
            {
                var shell = Group(surface, "Comic ink contour - 漫画描边");
                shell.gameObject.AddComponent<MeshFilter>().sharedMesh = ComicOutlineHullFactory.GetOrCreate(mesh, Root + "/Contours", out _);
                var renderer = shell.gameObject.AddComponent<MeshRenderer>();
                var ink = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Architecture/Materials/Hall_roof_ink.mat");
                ConfigureRenderer(renderer, ink);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return surface;
        }

        static void ConfigureRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
        }

        sealed class PaletteGroup
        {
            public readonly Transform Root;
            readonly Dictionary<string, Solid> batches = new Dictionary<string, Solid>();

            public PaletteGroup(Transform parent, string name) { Root = Group(parent, name); }

            Solid Batch(string key)
            {
                if (!batches.TryGetValue(key, out var solid)) batches.Add(key, solid = new Solid());
                return solid;
            }

            public void Box(string key, Vector3 center, Vector3 size) => Batch(key).Box(center, size, Quaternion.identity);

            public void Beam(string key, Vector3 from, Vector3 to, float width, float depth)
            {
                Vector3 direction = to - from;
                Batch(key).Box((from + to) * .5f, new Vector3(width, direction.magnitude, depth), Quaternion.FromToRotation(Vector3.up, direction));
            }

            public void Profile(string key, float[] xs, float[] top, float[] bottom, float left, float right)
                => Batch(key).Profile(xs, top, bottom, left, right);

            public void Flush(string prefix)
            {
                foreach (var batch in batches) Render(Root, "RidgeHall_" + prefix + "_" + batch.Key, batch.Value.Mesh(), batch.Key);
            }
        }

        sealed class Solid
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();

            public void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                int offset = vertices.Count;
                Profile(new[] { -.5f, .5f }, new[] { .5f, .5f }, new[] { -.5f, -.5f }, -.5f, .5f);
                for (int i = offset; i < vertices.Count; i++) vertices[i] = center + rotation * Vector3.Scale(vertices[i], size);
            }

            public void Profile(float[] xs, float[] top, float[] bottom, float left, float right)
            {
                int offset = vertices.Count;
                for (int i = 0; i < xs.Length; i++)
                {
                    vertices.Add(new Vector3(xs[i], top[i], left));
                    vertices.Add(new Vector3(xs[i], top[i], right));
                    vertices.Add(new Vector3(xs[i], bottom[i], right));
                    vertices.Add(new Vector3(xs[i], bottom[i], left));
                }
                for (int ring = 0; ring < xs.Length - 1; ring++)
                {
                    int a = offset + ring * 4, b = a + 4;
                    for (int i = 0; i < 4; i++)
                    {
                        int next = (i + 1) % 4;
                        Quad(a + i, a + next, b + next, b + i);
                    }
                }
                Quad(offset, offset + 3, offset + 2, offset + 1);
                int end = offset + (xs.Length - 1) * 4;
                Quad(end, end + 1, end + 2, end + 3);
            }

            void Quad(int a, int b, int c, int d)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }

            public Mesh Mesh()
            {
                var mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
