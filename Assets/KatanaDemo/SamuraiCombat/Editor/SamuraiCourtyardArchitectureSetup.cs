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
    /// <summary>Reusable static architecture. Existing placements and user edits are retained on repeat runs.</summary>
    public static class SamuraiCourtyardArchitectureSetup
    {
        public const string Root = SamuraiComicStyleSetup.Root + "/Architecture";
        public const string ModelPath = Root + "/Models/Courtyard_Architecture.fbx";
        public const string StairName = "08 Entrance stone stairs - 门前石阶";
        static readonly Dictionary<string, string> Palette = new Dictionary<string, string> {
            {"Stone","Warm_sandstone"},{"Slate","Foundation_slate"},{"Plaster","Ivory_plaster"},
            {"Timber","Old_honey_timber"},{"Red","Vermilion_lacquer"},{"Roof","Ink_blue_roof"},
            {"Paper","Warm_lantern_paper"},{"Gold","Quiet_ochre"}
        };
        static Material Mat(string suffix)
        {
            var path = SamuraiCourtyardDecorationSetup.Root + "/Materials/" + Palette[suffix] + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) throw new InvalidOperationException("Missing courtyard material: " + path);
            return material;
        }
        static Material Ink(bool fine)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(fine ? SamuraiCourtyardDecorationSetup.Root + "/Materials/Ink_Detail.mat" : SamuraiComicStyleSetup.Root + "/Materials/Ink.mat");
        }

        [MenuItem("Katana/Level/Install Courtyard Architecture And Stairs")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Courtyard dressing");
            if (!dressing) throw new InvalidOperationException("Install the courtyard first.");
            if (!dressing.transform.Find("05 Mountain environment - 山岳环境/01 Summit crags - 庭院承托山体"))
                throw new InvalidOperationException("Install the mountain environment before grounding the courtyard buildings.");
            var meshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Mesh>().OrderBy(m => m.name).ToArray();
            foreach (var prefix in new[] { "Residence_", "Storehouse_", "Pavilion_", "Stairs_" })
                if (!meshes.Any(m => m.name.StartsWith(prefix))) throw new InvalidOperationException("Incomplete architecture FBX: " + prefix);
            foreach (var pair in Palette) Mat(pair.Key);
            foreach (var folder in new[] { "Prefabs", "Meshes/Contours", "Meshes/Details" }) Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            var residence = ModelPrefab("Residence", "Courtyard_SideResidence", meshes);
            var store = ModelPrefab("Storehouse", "Courtyard_Storehouse", meshes);
            var pavilion = ModelPrefab("Pavilion", "Courtyard_TeaPavilion", meshes);
            var stairs = ModelPrefab("Stairs", "Courtyard_EntranceStairs", meshes);
            var upper = dressing.transform.Find("01 Upper courtyard - 上层庭院/02 Architecture - 建筑与门架");
            var lower = dressing.transform.Find("02 Lower courtyard - 下层庭院/02 Architecture - 建筑与门架");
            if (!upper || !lower) throw new InvalidOperationException("Missing courtyard architecture groups.");
            int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Add courtyard architecture and stairs");
            int changed = 0;
            changed += RefineHall(upper.Find("04 Main practice hall"), 16, 6, "Courtyard_GrandPracticeHall");
            changed += RefineHall(lower.Find("05 Eastern pavilion"), 10, 5.2f, "Courtyard_EasternHall");
            changed += SamuraiCourtyardHallRefinementSetup.EnsurePrefabs();
            var sites = Folder(dressing.transform, "06 Architecture sites - 建筑落地石基");
            changed += Place(store, upper, "09 Mountain storehouse - 山侧小仓房", new Vector3(-43, 0, -11.5f), -90, sites, new Vector2(5.05f, 5.96f));
            changed += Place(pavilion, upper, "10 Tea pavilion - 山前茶亭", new Vector3(-22.5f, 0, -20), 180, sites, new Vector2(5.5f, 5.72f));
            changed += Place(residence, lower, "09 Side residence - 东侧廊屋", new Vector3(33, 0, 5), 90, sites, new Vector2(8.95f, 7.1f));
            changed += HallSite(upper.Find("04 Main practice hall"), sites, new Vector2(17.5f, 7.9f));
            changed += HallSite(lower.Find("05 Eastern pavilion"), sites, new Vector2(11.5f, 7.1f));
            if (!lower.Find(StairName))
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(stairs, lower);
                Undo.RegisterCreatedObjectUndo(instance, "Add entrance stairs");
                instance.name = StairName; instance.transform.position = new Vector3(22.38641f, -3.6f, -25.7f);
                instance.transform.rotation = Quaternion.identity;
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
                var floor = scene.GetRootGameObjects().First(g => g.name == "Floor");
                var slab = floor.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "South Wall (1)");
                if (slab) { Undo.RecordObject(slab.gameObject, "Retain superseded entrance slab"); slab.gameObject.SetActive(false); }
                changed++;
            }
            changed += ClearApproaches(dressing.transform);
            // Picking follows each editable surface; thin hull children do not intercept clicks.
            foreach (var r in dressing.GetComponentsInChildren<Renderer>(true))
                if (r.transform.parent && r.transform.parent.GetComponent<Renderer>()) SceneVisibilityManager.instance.DisablePicking(r.gameObject, false);
            if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var screen)) screen.RefreshSources();
            if (changed > 0) { Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(scene); }
            Undo.CollapseUndoOperations(undo);
            return "Architecture: " + changed + " scene additions/refinements; five reusable house Prefabs and one stair Prefab. Repeat runs retain edited objects and assets.";
        }

        static Transform Folder(Transform parent, string name)
        {
            var existing = parent.Find(name); if (existing) return existing;
            var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Add architecture group"); go.transform.SetParent(parent, false); return go.transform;
        }
        static string Category(string name)
        {
            if (name.Contains("Foundation") || name.Contains("Platform") || name.Contains("Step") || name.Contains("Threshold") || name.Contains("Deck") || name.Contains("Cheek") || name.Contains("Facings")) return "01 Base and access - 石基与台阶";
            if (name.Contains("Roof") || name.Contains("Ridge") || name.Contains("Eave")) return "05 Roof and eaves - 屋顶与檐口";
            if (name.Contains("Window") || name.Contains("Lattice") || name.Contains("Paper") || name.Contains("Door") || name.Contains("Handle")) return "04 Doors and windows - 门窗";
            if (name.Contains("Wall") || name.Contains("Skirt") || name.Contains("Band") || name.Contains("Panel")) return "02 Walls - 墙面";
            if (name.Contains("Lantern") || name.Contains("TeaTable") || name.Contains("Bench")) return "06 Furnishings - 灯具与家具";
            return "03 Timber frame - 梁柱与托架";
        }
        static GameObject ModelPrefab(string prefix, string file, Mesh[] meshes)
        {
            string path = Root + "/Prefabs/" + file + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing) return existing;
            var preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(file); SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                foreach (var mesh in meshes.Where(m => m.name.StartsWith(prefix + "_")))
                {
                    string suffix = mesh.name.Substring(mesh.name.LastIndexOf('_') + 1);
                    var category = root.transform.Find(Category(mesh.name));
                    if (!category) { var g = new GameObject(Category(mesh.name)); g.transform.SetParent(root.transform, false); category = g.transform; }
                    var part = new GameObject(mesh.name.Substring(prefix.Length + 1)); part.transform.SetParent(category, false); part.transform.localPosition = mesh.bounds.center;
                    bool fine = mesh.name.Contains("Lattice") || mesh.name.Contains("Strap") || mesh.name.Contains("Handle") || mesh.name.Contains("Bracket") || mesh.name.Contains("Hanger") || mesh.name.Contains("Rib") || mesh.name.Contains("Facings");
                    var surface = Surface(part.transform, mesh, Mat(suffix), !fine, false);
                    surface.localPosition = -mesh.bounds.center;
                    bool collider = (prefix == "Stairs" && !mesh.name.Contains("Facings")) || mesh.name.Contains("Foundation") || mesh.name.Contains("Platform") || mesh.name.Contains("Deck") || mesh.name.Contains("Wall") || mesh.name.Contains("FrontPanel") || mesh.name.Contains("Roof") || mesh.name.Contains("DoorLeaf") || (mesh.name.Contains("SlidingScreen") && mesh.name.EndsWith("_Paper")) || mesh.name.Contains("Column") || mesh.name.Contains("PorchPost") || mesh.name.Contains("Bench") || mesh.name.Contains("TeaTable") || mesh.name.Contains("Step");
                    if (collider) surface.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                }
                SamuraiStairRampCollisionSetup.EnsurePrefabContents(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static Transform Surface(Transform parent, Mesh mesh, Material material, bool outline, bool fine)
        {
            var surface = new GameObject("Surface - " + mesh.name); surface.transform.SetParent(parent, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = surface.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(surface, StaticEditorFlags.BatchingStatic);
            if (outline)
            {
                ComicOutlineHullFactory.Diagnostics info;
                var shell = new GameObject("Comic ink contour - 随部件描边"); shell.transform.SetParent(surface.transform, false);
                shell.AddComponent<MeshFilter>().sharedMesh = ComicOutlineHullFactory.GetOrCreate(mesh, Root + "/Meshes/Contours", out info);
                var edge = shell.AddComponent<MeshRenderer>(); edge.sharedMaterial = Ink(fine); edge.shadowCastingMode = ShadowCastingMode.Off; edge.receiveShadows = false; edge.lightProbeUsage = LightProbeUsage.Off; edge.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(shell, StaticEditorFlags.BatchingStatic);
            }
            return surface.transform;
        }
        struct Block
        {
            public Vector3 p, s;
            public Block(Vector3 position, Vector3 size) { p = position; s = size; }
        }
        static Transform Blocks(Transform parent, string name, string key, string material, params Block[] blocks)
        {
            string path = Root + "/Meshes/Details/" + key + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                var cube = AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root + "/Meshes/Shapes/Block.asset");
                if (!cube) throw new InvalidOperationException("Missing courtyard cube mesh.");
                mesh = new Mesh { name = key };
                mesh.CombineMeshes(blocks.Select(b => new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(b.p, Quaternion.identity, b.s) }).ToArray(), true, true);
                mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path);
            }
            var part = new GameObject(name); part.transform.SetParent(parent, false);
            return Surface(part.transform, mesh, Mat(material), false, false);
        }
        static int RefineHall(Transform hall, float width, float depth, string assetName)
        {
            var refined = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + assetName + ".prefab");
            if (SamuraiCourtyardHallRefinementSetup.IsRefined(refined))
                return SamuraiCourtyardHallRefinementSetup.ConnectLegacyHall(hall, refined);
            if (!hall || PrefabUtility.IsPartOfPrefabInstance(hall.gameObject)) return 0;
            var detail = Folder(hall, "Architecture refinements - 殿堂细部");
            string key = width > 12 ? "GrandHall" : "EasternHall";
            float front = -depth * .5f;
            if (!detail.Find("01 Double sliding doors - 双扇木门"))
            {
                Blocks(detail, "01 Double sliding doors - 双扇木门", key + "_Doors", "Timber",
                    new Block(new Vector3(-1.12f, 1.9f, front + .18f), new Vector3(2.12f, 2.75f, .16f)),
                    new Block(new Vector3(1.12f, 1.9f, front + .18f), new Vector3(2.12f, 2.75f, .16f)));
                var trim = new List<Block>();
                foreach (float x in new[] { -2.24f, 0, 2.24f }) trim.Add(new Block(new Vector3(x, 1.9f, front + .05f), new Vector3(.10f, 2.93f, .12f)));
                foreach (float y in new[] { .48f, 3.33f }) trim.Add(new Block(new Vector3(0, y, front + .05f), new Vector3(4.6f, .10f, .12f)));
                foreach (float x in new[] { -.18f, .18f }) trim.Add(new Block(new Vector3(x, 1.85f, front - .04f), new Vector3(.055f, .3f, .055f)));
                Blocks(detail, "02 Door frame and pulls - 门框与把手", key + "_DoorTrim", "Gold", trim.ToArray());
                var side = new List<Block>(); var framing = new List<Block>();
                foreach (int sign in new[] { -1, 1 })
                {
                    float x = sign * (width * .5f + .035f);
                    foreach (float z in new[] { -.7f, 1.2f })
                    {
                        side.Add(new Block(new Vector3(x, 2.45f, z), new Vector3(.08f, 1.5f, 1.25f)));
                        foreach (float zz in new[] { z - .68f, z, z + .68f }) framing.Add(new Block(new Vector3(x + sign * .06f, 2.45f, zz), new Vector3(.10f, 1.65f, .065f)));
                        foreach (float y in new[] { 1.63f, 2.45f, 3.27f }) framing.Add(new Block(new Vector3(x + sign * .06f, y, z), new Vector3(.10f, .065f, 1.42f)));
                    }
                    framing.Add(new Block(new Vector3(x, .95f, .5f), new Vector3(.12f, .95f, depth - .75f)));
                    framing.Add(new Block(new Vector3(x, 3.8f, .5f), new Vector3(.17f, .16f, depth - .6f)));
                }
                Blocks(detail, "03 Side window paper - 侧窗纸面", key + "_SidePaper", "Paper", side.ToArray());
                Blocks(detail, "04 Side lattice and skirts - 侧窗与护墙", key + "_SideTimber", "Timber", framing.ToArray());
                var rear = new List<Block>();
                foreach (float x in new[] { -width * .47f, -width * .235f, 0, width * .235f, width * .47f }) rear.Add(new Block(new Vector3(x, 2.1f, depth * .5f - .08f), new Vector3(.16f, 3.85f, .15f)));
                foreach (float y in new[] { .6f, 3.9f }) rear.Add(new Block(new Vector3(0, y, depth * .5f - .08f), new Vector3(width, .16f, .15f)));
                Blocks(detail, "05 Rear timber frame - 背立面木框", key + "_RearFrame", "Timber", rear.ToArray());
                Blocks(detail, "06 Entrance veranda - 入殿廊台", key + "_Veranda", "Timber", new Block(new Vector3(0, .22f, front - .45f), new Vector3(width - .5f, .16f, 1.05f)));
                Blocks(detail, "07 Stone approach - 殿前踏步", key + "_Approach", "Stone", new Block(new Vector3(0, .02f, front - 1.16f), new Vector3(4.9f, .22f, .75f)));
                var collision = new GameObject("08 Solid shell collision - 殿身碰撞"); collision.transform.SetParent(detail, false);
                var c = collision.AddComponent<BoxCollider>(); c.center = new Vector3(0, 2.1f, .5f); c.size = new Vector3(width, 3.8f, depth - .8f);
            }
            string path = Root + "/Prefabs/" + assetName + ".prefab";
            foreach (var filter in hall.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh || filter.transform.parent.GetComponent<Renderer>() || filter.GetComponent<Collider>()) continue;
                string partPath = AnimationUtility.CalculateTransformPath(filter.transform, hall);
                if (partPath.Contains("Foundation") || partPath.Contains("Roof") || partPath.Contains("Veranda") || partPath.Contains("Approach"))
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab)
            {
                var preview = EditorSceneManager.NewPreviewScene();
                var clone = UnityEngine.Object.Instantiate(hall.gameObject); SceneManager.MoveGameObjectToScene(clone, preview);
                clone.name = assetName; clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); clone.transform.localScale = Vector3.one;
                try { prefab = PrefabUtility.SaveAsPrefabAsset(clone, path); }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
            }
            var settings = new ConvertToPrefabInstanceSettings { objectMatchMode = ObjectMatchMode.ByHierarchy, changeRootNameToAssetName = false, componentsNotMatchedBecomesOverride = true, gameObjectsNotMatchedBecomesOverride = true, recordPropertyOverridesOfMatches = true };
            PrefabUtility.ConvertToPrefabInstance(hall.gameObject, prefab, settings, InteractionMode.UserAction);
            return 1;
        }
        static float Ground(float x, float z)
        {
            float highest = float.NegativeInfinity;
            var mountain = GameObject.Find("Courtyard dressing").transform.Find("05 Mountain environment - 山岳环境/01 Summit crags - 庭院承托山体");
            foreach (var c in mountain.GetComponentsInChildren<MeshCollider>())
            {
                RaycastHit hit;
                if (c.Raycast(new Ray(new Vector3(x, 30, z), Vector3.down), out hit, 250)) highest = Mathf.Max(highest, hit.point.y);
            }
            if (float.IsNegativeInfinity(highest)) throw new InvalidOperationException("No summit terrain at " + x + "," + z);
            return highest;
        }
        static int ClearApproaches(Transform dressing)
        {
            int count = 0;
            count += MoveKnownScenery(dressing, "Mountain pine 042", new Vector2(30.7336f, 1.0372f), new Vector2(36.4f, -8), 0);
            count += MoveKnownScenery(dressing, "Talus stone 128", new Vector2(18.60f, -27.01f), new Vector2(17.2f, -27.2f), -.45f);
            count += MoveKnownScenery(dressing, "Talus stone 131", new Vector2(21.81f, -26.45f), new Vector2(27, -27.45f), -.45f);
            count += MoveKnownScenery(dressing, "Talus stone 132", new Vector2(18.90f, -21.88f), new Vector2(16.9f, -21.88f), -.45f);
            return count;
        }
        static int MoveKnownScenery(Transform dressing, string name, Vector2 original, Vector2 target, float groundOffset)
        {
            var t = dressing.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (!t || Vector2.Distance(new Vector2(t.position.x, t.position.z), original) > .015f) return 0;
            Undo.RecordObject(t, "Clear building and stair approaches");
            t.position = new Vector3(target.x, Ground(target.x, target.y) + groundOffset, target.y);
            return 1;
        }
        static int Place(GameObject prefab, Transform parent, string name, Vector3 location, float yaw, Transform sites, Vector2 footprint)
        {
            if (parent.Find(name)) return 0;
            var rotation = Quaternion.Euler(0, yaw, 0); float top = float.NegativeInfinity;
            foreach (float x in new[] { -footprint.x * .5f, 0, footprint.x * .5f }) foreach (float z in new[] { -footprint.y * .5f, 0, footprint.y * .5f })
            { var p = location + rotation * new Vector3(x, 0, z); top = Mathf.Max(top, Ground(p.x, p.z)); }
            location.y = top + .04f;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent); Undo.RegisterCreatedObjectUndo(go, "Place reusable courtyard house");
            go.name = name; go.transform.SetPositionAndRotation(location, rotation); PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            Site(go.transform, sites, footprint, 0);
            return 1;
        }
        static int HallSite(Transform hall, Transform sites, Vector2 footprint)
        {
            if (!hall || sites.Find(hall.name + " - 地形石基")) return 0;
            Site(hall, sites, footprint, -.545f); return 1;
        }
        static void Site(Transform building, Transform sites, Vector2 footprint, float baseOffset)
        {
            string name = building.name + " - 地形石基"; if (sites.Find(name)) return;
            var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Ground courtyard building"); go.transform.SetParent(sites, false);
            go.transform.SetPositionAndRotation(building.position, building.rotation);
            float lowest = float.PositiveInfinity;
            foreach (float x in new[] { -footprint.x * .5f, 0, footprint.x * .5f }) foreach (float z in new[] { -footprint.y * .5f, 0, footprint.y * .5f })
            { var p = building.TransformPoint(new Vector3(x, 0, z)); lowest = Mathf.Min(lowest, Ground(p.x, p.z)); }
            float top = building.position.y + baseOffset, bottom = lowest - .18f, height = Mathf.Max(.15f, top - bottom);
            var surface = Blocks(go.transform, "01 Terrain retaining stone - 接地挡石", "Site_" + building.name.Split(' ')[0] + "_" + Mathf.RoundToInt(building.position.x * 10), "Slate", new Block(new Vector3(0, top - building.position.y - height * .5f, 0), new Vector3(footprint.x, height, footprint.y)));
            surface.gameObject.AddComponent<MeshCollider>().sharedMesh = surface.GetComponent<MeshFilter>().sharedMesh;
        }
    }
}
