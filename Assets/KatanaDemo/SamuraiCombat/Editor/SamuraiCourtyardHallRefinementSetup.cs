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
    /// <summary>Upgrades the original hall assets in place; subsequent artist edits are retained.</summary>
    public static class SamuraiCourtyardHallRefinementSetup
    {
        public const string Root = SamuraiCourtyardArchitectureSetup.Root;
        public const string ModelPath = Root + "/Models/Courtyard_RefinedHalls.fbx";
        public const string ContentName = "Refined hall - 精修殿堂";
        static readonly Dictionary<string, string> Palette = new Dictionary<string, string> {
            {"Stone","Warm_sandstone"},{"Slate","Foundation_slate"},{"Plaster","Ivory_plaster"},
            {"Timber","Old_honey_timber"},{"Red","Vermilion_lacquer"},{"Roof","Ink_blue_roof"},
            {"Paper","Warm_lantern_paper"},{"Gold","Quiet_ochre"}
        };

        [MenuItem("OC Design/Samurai Combat/Refine Original Courtyard Halls")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            int updated = EnsurePrefabs();
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            updated += ConnectLegacyHall(all.FirstOrDefault(t => t.name == "04 Main practice hall"), AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Courtyard_GrandPracticeHall.prefab"));
            updated += ConnectLegacyHall(all.FirstOrDefault(t => t.name == "05 Eastern pavilion"), AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Courtyard_EasternHall.prefab"));
            if (updated > 0)
            {
                foreach (var renderer in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
                    if (renderer.transform.parent && renderer.transform.parent.GetComponent<Renderer>())
                        SceneVisibilityManager.instance.DisablePicking(renderer.gameObject, false);
                if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var outline)) outline.RefreshSources();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return "Original courtyard halls: " + updated + " Prefab assets refined. Existing refined assets and scene placements retained.";
        }

        public static bool IsRefined(GameObject prefab)
        {
            return prefab && prefab.transform.Find(ContentName);
        }
        public static int EnsurePrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Refine hall assets in Edit mode only.");
            string grandPath = Root + "/Prefabs/Courtyard_GrandPracticeHall.prefab";
            string easternPath = Root + "/Prefabs/Courtyard_EasternHall.prefab";
            var grand = AssetDatabase.LoadAssetAtPath<GameObject>(grandPath);
            var eastern = AssetDatabase.LoadAssetAtPath<GameObject>(easternPath);
            if (IsRefined(grand) && IsRefined(eastern)) return RefineRoofInk(grandPath) + RefineRoofInk(easternPath);
            var meshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Mesh>().OrderBy(m => m.name).ToArray();
            foreach (string prefix in new[] { "GrandHall_", "EasternHall_" })
                if (!meshes.Any(m => m.name.StartsWith(prefix))) throw new InvalidOperationException("Import the complete refined hall FBX first.");
            foreach (var mesh in meshes)
            {
                if (mesh.subMeshCount != 1) throw new InvalidOperationException("Expected one material per functional hall mesh: " + mesh.name);
                MaterialFor(mesh.name);
            }
            if (!grand || !eastern) throw new InvalidOperationException("Install the original courtyard hall Prefabs first.");
            Directory.CreateDirectory(Root + "/Meshes/HallContours"); AssetDatabase.Refresh();
            return Upgrade(grandPath, "GrandHall_", meshes) + Upgrade(easternPath, "EasternHall_", meshes);
        }

        static int Upgrade(string path, string prefix, Mesh[] meshes)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (IsRefined(prefab)) return 0;
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // Only remove the two known old geometry groups; unrelated artist additions survive.
                foreach (var child in contents.transform.Cast<Transform>().Where(t => t.name == "Editable components - 可调部件" || t.name == "Architecture refinements - 殿堂细部").ToArray())
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                var body = Child(contents.transform, ContentName);
                foreach (var mesh in meshes.Where(m => m.name.StartsWith(prefix)))
                {
                    var category = body.Find(Category(mesh.name));
                    if (!category) category = Child(body, Category(mesh.name));
                    var part = Child(category, mesh.name.Substring(prefix.Length));
                    part.localPosition = mesh.bounds.center;
                    var surface = Child(part, "Surface - " + mesh.name);
                    surface.localPosition = -mesh.bounds.center;
                    surface.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = surface.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = MaterialFor(mesh.name);
                    renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    GameObjectUtility.SetStaticEditorFlags(surface.gameObject, StaticEditorFlags.BatchingStatic);
                    if (NeedsCollision(mesh.name)) surface.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                    if (!Fine(mesh.name))
                    {
                        ComicOutlineHullFactory.Diagnostics diagnostics;
                        var shell = Child(surface, "Comic ink contour - 随部件描边");
                        shell.gameObject.AddComponent<MeshFilter>().sharedMesh = ComicOutlineHullFactory.GetOrCreate(mesh, Root + "/Meshes/HallContours", out diagnostics);
                        var edge = shell.gameObject.AddComponent<MeshRenderer>(); edge.sharedMaterial = mesh.name.EndsWith("_Roof") ? RoofInk() : OriginalInk();
                        edge.shadowCastingMode = ShadowCastingMode.Off; edge.receiveShadows = false;
                        edge.lightProbeUsage = LightProbeUsage.Off; edge.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        GameObjectUtility.SetStaticEditorFlags(shell.gameObject, StaticEditorFlags.BatchingStatic);
                    }
                }
                SamuraiStairRampCollisionSetup.EnsurePrefabContents(contents);
                // Load/save the existing root to retain its fileIDs and the existing Prefab GUID.
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return 1;
        }

        public static int ConnectLegacyHall(Transform hall, GameObject prefab)
        {
            if (!hall || !IsRefined(prefab) || PrefabUtility.IsPartOfPrefabInstance(hall.gameObject)) return 0;
            Vector3 position = hall.position, scale = hall.localScale; Quaternion rotation = hall.rotation;
            string name = hall.name; bool active = hall.gameObject.activeSelf;
            var extras = hall.Cast<Transform>().Where(t => t.name != "Editable components - 可调部件" && t.name != "Architecture refinements - 殿堂细部").ToArray();
            foreach (var extra in extras) Undo.SetTransformParent(extra, hall.parent, "Preserve additional hall objects");
            var settings = new ConvertToPrefabInstanceSettings {
                objectMatchMode = ObjectMatchMode.ByHierarchy, changeRootNameToAssetName = false,
                componentsNotMatchedBecomesOverride = true, gameObjectsNotMatchedBecomesOverride = false,
                recordPropertyOverridesOfMatches = false
            };
            try { PrefabUtility.ConvertToPrefabInstance(hall.gameObject, prefab, settings, InteractionMode.UserAction); }
            finally
            {
                if (hall)
                {
                    hall.SetPositionAndRotation(position, rotation); hall.localScale = scale;
                    hall.name = name; hall.gameObject.SetActive(active);
                    foreach (var extra in extras) if (extra) Undo.SetTransformParent(extra, hall, "Restore additional hall objects");
                }
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(hall);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hall.gameObject);
            return 1;
        }
        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        static Material OriginalInk()
        {
            return AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Materials/Ink.mat");
        }
        static Material RoofInk()
        {
            string path = Root + "/Materials/Hall_roof_ink.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing) return existing;
            Directory.CreateDirectory(Root + "/Materials"); AssetDatabase.Refresh();
            var material = new Material(OriginalInk()) { name = "Hall roof ink", enableInstancing = true };
            // Ink first, opaque roof second: visible backfaces cannot leave coplanar black speckles.
            material.SetFloat("_MinOutlinePixels", .75f); material.SetFloat("_OutlineWidth", .012f);
            material.renderQueue = 1999;
            AssetDatabase.CreateAsset(material, path); return material;
        }
        static int RefineRoofInk(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab.GetComponentsInChildren<MeshRenderer>(true).Any(IsOldRoofInk)) return 0;
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var edge in contents.GetComponentsInChildren<MeshRenderer>(true).Where(IsOldRoofInk)) edge.sharedMaterial = RoofInk();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return 1;
        }
        static bool IsOldRoofInk(MeshRenderer renderer)
        {
            var parent = renderer.transform.parent;
            var mesh = parent ? parent.GetComponent<MeshFilter>() : null;
            return renderer.sharedMaterial == OriginalInk() && parent && parent.GetComponent<Renderer>() && mesh && mesh.sharedMesh && mesh.sharedMesh.name.EndsWith("_Roof");
        }
        static Material MaterialFor(string name)
        {
            string suffix = name.Substring(name.LastIndexOf('_') + 1);
            string material;
            if (!Palette.TryGetValue(suffix, out material)) throw new InvalidOperationException("Unknown hall material: " + name);
            var value = AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardDecorationSetup.Root + "/Materials/" + material + ".mat");
            if (!value) throw new InvalidOperationException("Missing courtyard palette material: " + material);
            return value;
        }
        static bool Fine(string name)
        {
            return name.Contains("Lattice") || name.Contains("Rafter") || name.Contains("Bracket") || name.Contains("Hardware") || name.Contains("DoorPull") || name.Contains("GableVent") || name.Contains("ColumnCollar") || name.Contains("Hanger") || name.Contains("Rib") || name.EndsWith("_Paper") || name.Contains("Groove");
        }
        static bool NeedsCollision(string name)
        {
            if (Fine(name)) return name.EndsWith("_Paper") && (name.Contains("Window") || name.Contains("Door"));
            return name.Contains("Foundation") || name.Contains("Platform") || name.Contains("Step") || name.Contains("Deck") || name.Contains("Wall") || name.Contains("Column") || name.Contains("Door") || name.Contains("Roof") || name.Contains("Window") || name.Contains("Post");
        }
        static string Category(string name)
        {
            if (name.Contains("Foundation") || name.Contains("Platform") || name.Contains("Step") || name.Contains("Deck") || name.Contains("Plinth")) return "01 Base and veranda - 石基与廊台";
            if (name.Contains("Lantern")) return "06 Lanterns - 檐下灯具";
            if (name.Contains("Roof") || name.Contains("Ridge") || name.Contains("Eave") || name.Contains("Fascia")) return "05 Roof and eaves - 连续屋顶与檐口";
            if (name.Contains("Window") || name.Contains("Lattice") || name.Contains("Door") || name.Contains("Hardware")) return "04 Doors and windows - 凹窗与门扇";
            if (name.Contains("Wall") || name.Contains("Gable") || name.Contains("Skirt")) return "02 Walls and gables - 墙身与山墙";
            return "03 Timber structure - 梁柱与托架";
        }
    }
}
