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
    /// <summary>Places the early landmark on the rock shoulder behind the lower key hall.</summary>
    public static class SamuraiHallLandmarkSetup
    {
        public const string RootName = "14 Hall landmark pagoda - 正殿后方引路高塔";
        const string Root = SamuraiComicStyleSetup.Root + "/LandmarkTower";
        static readonly Vector3 Position = new Vector3(15.40f, -1.10f, 27.28f);
        const float TipHeight = 14.28f;
        static readonly Dictionary<string, string> Palette = new Dictionary<string, string>
        {
            { "Roof", "Ink_blue_roof" }, { "Gold", "Quiet_ochre" }, { "Timber", "Old_honey_timber" },
            { "Red", "Vermilion_lacquer" }, { "Plaster", "Ivory_plaster" }, { "Stone", "Warm_sandstone" },
            { "Slate", "Foundation_slate" }, { "Paper", "Warm_lantern_paper" }
        };

        [MenuItem("OC Design/Samurai Combat/Install Hall Landmark Pagoda")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().First(g => g.name == "Courtyard dressing").transform;
            if (dressing.Find(RootName)) return "Hall landmark pagoda already exists. Existing edits retained.";
            if (!dressing.GetComponentsInChildren<Transform>().Any(t => t.name == "Great hall - 可进入大殿"))
                throw new InvalidOperationException("Install the lower key hall first.");
            var crags = dressing.GetComponentsInChildren<MeshCollider>().Where(c => c.name.Contains("Sanctuary_Crag")).ToArray();
            if (crags.Length == 0) throw new InvalidOperationException("The summit rock colliders are missing.");

            Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory(Root + "/Contours");
            AssetDatabase.Refresh();
            Physics.SyncTransforms();

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Place entrance landmark pagoda");
            try
            {
                var root = Group(dressing, RootName);
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Add hall landmark pagoda");
                root.position = Position;
                BuildFooting(root, crags);
                var tower = Group(root, "02 Four-tier tower - 四重檐塔身");
                float upper = 0f;
                SamuraiHallLandmarkTower.Build((name, mesh, key) =>
                {
                    upper = Mathf.Max(upper, mesh.bounds.max.y);
                    var surface = Render(tower, name, mesh, MaterialFor(key));
                    if (key == "Roof") AddOutline(surface);
                });
                if (upper < 16f) throw new InvalidOperationException("The landmark tower mesh is incomplete.");
                tower.localScale = Vector3.one * ((TipHeight - Position.y) / upper);

                if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var outline)) outline.RefreshSources();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(group);
                return "Placed a four-tier landmark pagoda behind the lower key hall.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        static void BuildFooting(Transform root, MeshCollider[] crags)
        {
            var footing = Group(root, "01 Rock shoulder and stone footing - 嵌岩塔基");
            var stone = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Mountains/Materials/Rock_Strata_Near.mat");
            var rock = RockFoot(crags);
            Render(footing, "LandmarkTower_RockShoulder", rock, stone);
            var block = AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root + "/Meshes/Shapes/Block.asset");
            if (!block) throw new InvalidOperationException("Missing stone block mesh.");
            AddBlock(footing, block, "LandmarkTower_Footing_Courses", "Slate",
                new Vector3(0, -1.26f, 0), new Vector3(5.05f, 2.05f, 5.10f));
            AddBlock(footing, block, "LandmarkTower_Stone_Coping", "Stone",
                new Vector3(0, -.135f, 0), new Vector3(5.20f, .27f, 5.25f));
            AddBlock(footing, block, "LandmarkTower_Footing_Toe", "Stone",
                new Vector3(0, -2.20f, 0), new Vector3(5.31f, .27f, 5.38f));
        }

        static void AddBlock(Transform parent, Mesh block, string name, string key, Vector3 center, Vector3 size)
        {
            var mesh = new Mesh();
            mesh.CombineMeshes(new[] { new CombineInstance { mesh = block, transform = Matrix4x4.TRS(center, Quaternion.identity, size) } });
            Render(parent, name, mesh, MaterialFor(key));
        }

        static Mesh RockFoot(MeshCollider[] crags)
        {
            var section = new[]
            {
                new Vector2(-2.57f,-2.10f), new Vector2(-2.65f,2.12f), new Vector2(-2.18f,2.70f),
                new Vector2(2.13f,2.62f), new Vector2(2.70f,2.13f), new Vector2(2.56f,-2.13f),
                new Vector2(2.16f,-2.65f), new Vector2(-2.11f,-2.59f)
            };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var p in section)
                vertices.Add(new Vector3(p.x, -2.08f, p.y));
            for (int i = 0; i < section.Length; i++)
            {
                var p = section[i] * (1.075f + .03f * Mathf.Sin(i * 3.41f));
                var world = Position + new Vector3(p.x, 0f, p.y);
                float ground = float.NegativeInfinity;
                var ray = new Ray(new Vector3(world.x, 55f, world.z), Vector3.down);
                foreach (var crag in crags)
                    if (crag.Raycast(ray, out var hit, 140f)) ground = Mathf.Max(ground, hit.point.y);
                if (float.IsNegativeInfinity(ground)) throw new InvalidOperationException("The landmark footing would miss the summit crag.");
                vertices.Add(new Vector3(p.x, Mathf.Min(ground - Position.y - .42f, -2.55f), p.y));
            }
            int n = section.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                triangles.AddRange(new[] { j, i, i + n, j, i + n, j + n });
            }
            for (int i = 1; i < n - 1; i++)
            {
                triangles.AddRange(new[] { 0, i, i + 1 });
                triangles.AddRange(new[] { n, n + i + 1, n + i });
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (!CourtyardRoadMeshBuilder.Audit(mesh).closedPositiveVolume)
                throw new InvalidOperationException("The landmark rock footing mesh is invalid.");
            return mesh;
        }

        static void AddOutline(Transform surface)
        {
            var ink = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Architecture/Materials/Hall_roof_ink.mat");
            if (!ink) throw new InvalidOperationException("Missing architectural contour material.");
            var shape = surface.GetComponent<MeshFilter>().sharedMesh;
            var hull = ComicOutlineHullFactory.GetOrCreate(shape, Root + "/Contours", out _);
            var line = Group(surface, "Comic ink contour - 漫画描边");
            RenderOn(line, hull, ink, false);
        }

        static Transform Group(Transform parent, string name)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            return node;
        }

        static Material MaterialFor(string key)
        {
            string path = key == "Dark" ? SamuraiLevelFlowSetup.Root + "/Materials/Deep_gate_timber.mat"
                : SamuraiCourtyardDecorationSetup.Root + "/Materials/" + Palette[key] + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) throw new InvalidOperationException("Missing landmark tower material " + path);
            return material;
        }

        static Transform Render(Transform parent, string name, Mesh mesh, Material material)
        {
            string path = Root + "/Meshes/" + name + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = saved;
            }
            else
            {
                mesh.name = name;
                AssetDatabase.CreateAsset(mesh, path);
            }
            var node = Group(parent, name);
            RenderOn(node, mesh, material, true);
            return node;
        }

        static void RenderOn(Transform node, Mesh mesh, Material material, bool casts)
        {
            if (!material) throw new InvalidOperationException("Missing a landmark material.");
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.shadowCastingMode = casts ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = casts;
            GameObjectUtility.SetStaticEditorFlags(node.gameObject, StaticEditorFlags.BatchingStatic);
        }
    }
}
