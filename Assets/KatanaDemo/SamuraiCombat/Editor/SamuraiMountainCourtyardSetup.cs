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
    /// <summary>Additive scenery installer. Existing combat, layout and hand editing are preserved.</summary>
    public static class SamuraiMountainCourtyardSetup
    {
        public const string Root = SamuraiComicStyleSetup.Root + "/Mountains";
        public const string RootName = "05 Mountain environment - 山岳环境";
        public const string Model = Root + "/Models/Mountain_Courtyard.fbx";
        [Serializable] public sealed class Layout { public MeshEntry[] meshes; public Placement[] placements; public Palette[] palette; }
        [Serializable] public sealed class MeshEntry { public string name; public string[] roles; public bool prototype; }
        [Serializable] public sealed class Placement { public string mesh, group; public Vector3 position; public float yaw, scale; }
        [Serializable] public sealed class Palette { public string name, lit, shade; }

        [MenuItem("OC Design/Samurai Combat/Install Mountain Courtyard Surroundings")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == SamuraiCourtyardDecorationSetup.RootName);
            if (!dressing) throw new InvalidOperationException("The existing courtyard dressing is required.");
            if (dressing.transform.Find(RootName)) return "Mountain environment already exists; all transforms, materials, visibility and camera tuning preserved.";
            var layoutAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Mountain_layout.json");
            if (!layoutAsset) throw new InvalidOperationException("Import the mountain layout and Blender model first.");
            var layout = JsonUtility.FromJson<Layout>(layoutAsset.text);
            var meshes = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Mesh>().ToDictionary(m => m.name);
            foreach (var entry in layout.meshes)
                if (!meshes.TryGetValue(entry.name, out var mesh) || mesh.subMeshCount != entry.roles.Length)
                    throw new InvalidOperationException("Missing mesh/material slots: " + entry.name);
            if (Mathf.Abs(meshes["Upper_Sanctuary_Crag"].bounds.center.x + 29) > 8)
                throw new InvalidOperationException("The FBX import axes must match Unity world coordinates.");

            foreach (string folder in new[] { "Materials", "Contours" }) Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            var palettes = layout.palette.ToDictionary(p => p.name, MakeMaterial);
            var ink = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Materials/Ink.mat");
            var detailInk = AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardDecorationSetup.Root + "/Materials/Ink_Detail.mat");
            if (!ink || !detailInk) throw new InvalidOperationException("The existing courtyard ink palette is required.");
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Install mountain courtyard surroundings");
            var root = Create(RootName, dressing.transform);
            var summits = Create("01 Summit crags - 庭院承托山体", root);
            var near = Create("02 Inner ridges - 近山与峡谷", root);
            var middle = Create("03 Encircling ranges - 环绕山岭", root);
            var far = Create("04 Distant ranges - 层叠远山", root);
            var river = Create("05 Ravine stream - 山涧河流", root);
            var trees = Create("06 Pine groves - 山松林", root);
            var stones = Create("07 Talus stones - 山脚落石", root);
            var outcrops = Create("08 Summit outcrops - 山顶风化石", root);
            var lookup = layout.meshes.ToDictionary(e => e.name);
            int surfaces = 0, hulls = 0;
            foreach (var entry in layout.meshes.Where(e => !e.prototype))
            {
                Mesh mesh = meshes[entry.name];
                bool crag = entry.name.EndsWith("_Crag");
                var folder = crag ? summits : entry.name.StartsWith("Landscape_0") ? near : entry.name.StartsWith("Landscape_1") ? middle : entry.name.StartsWith("Landscape_2") ? far : river;
                var part = Create(Readable(entry.name), folder);
                Vector3 pivot = crag ? new Vector3(entry.name.StartsWith("Upper") ? -29 : 17, entry.name.StartsWith("Upper") ? -2.1f : -3.55f, 0) : mesh.bounds.center;
                part.localPosition = pivot;
                Surface(part, mesh, entry.roles.Select(r => palettes[r]).ToArray(), -pivot, crag, crag ? ink : null, crag);
                surfaces++; if (crag) hulls++;
            }
            var treeGroups = new Dictionary<string, Transform>();
            foreach (var placement in layout.placements)
            {
                bool tree = placement.mesh.StartsWith("Pine_");
                Transform category = placement.group == "Summit outcrops" ? outcrops : stones;
                if (tree && !treeGroups.TryGetValue(placement.group, out category))
                {
                    category = Create(placement.group == "Upper rim" ? "01 Upper summit pines - 上层山松" : placement.group == "Lower rim" ? "02 Lower summit pines - 下层山松" : "03 Hillside groves - 山腰松林", trees);
                    treeGroups.Add(placement.group, category);
                }
                var part = Create((tree ? "Mountain pine " : "Talus stone ") + (surfaces + 1).ToString("000"), category);
                part.localPosition = placement.position;
                part.localRotation = Quaternion.Euler(0, placement.yaw, 0);
                part.localScale = Vector3.one * placement.scale;
                var entry = lookup[placement.mesh];
                bool outline = tree && placement.group != "Mountain groves";
                Surface(part, meshes[placement.mesh], entry.roles.Select(r => palettes[r]).ToArray(), Vector3.zero, false, outline ? detailInk : null, outline);
                surfaces++; if (outline) hulls++;
            }
            GroundScenery(root);
            SamuraiMountainRockStyleSetup.Apply(root);
            // Keep the previous silhouette study available as history, without double layering it.
            foreach (var t in dressing.GetComponentsInChildren<Transform>(true))
                if (t.name == "Quiet northern peaks") { Undo.RecordObject(t.gameObject, "Retain old backdrop disabled"); t.gameObject.SetActive(false); }
            var camera = Camera.main;
            if (camera && camera.farClipPlane < 1050f)
            { Undo.RecordObject(camera, "Extend scenic mountain view distance"); camera.farClipPlane = 1050f; }
            if (camera && camera.TryGetComponent<ComicScreenOutline>(out var screen)) screen.RefreshSources();
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undo);
            return $"Mountain scenery installed: {surfaces} editable surfaces, {hulls} ink hulls, two summit colliders. Existing courts and bridge preserved.";
        }

        static Material MakeMaterial(Palette palette)
        {
            string shared = palette.name == "Timber" ? "Old_honey_timber" : palette.name == "PineDark" ? "Pine_dark_teal" : palette.name == "PineSage" ? "Pine_sage" : null;
            if (shared != null)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Material>(SamuraiCourtyardDecorationSetup.Root + "/Materials/" + shared + ".mat");
                if (existing) return existing;
            }
            string path = Root + "/Materials/" + palette.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(Shader.Find("OC Design/Comic Lighting/Surface")) { name = "Mountain " + palette.name, enableInstancing = true };
            Color lit, shade; ColorUtility.TryParseHtmlString(palette.lit, out lit); ColorUtility.TryParseHtmlString(palette.shade, out shade);
            material.SetColor("_BaseColor", lit); material.SetColor("_ShadowColor", shade);
            bool distant = palette.name.StartsWith("Far") || palette.name == "Horizon";
            bool rock = palette.name == "RockLight" || palette.name == "RockShade" || palette.name == "RockWarm";
            material.SetFloat("_Threshold", rock ? .38f : distant ? -.08f : .12f);
            material.SetFloat("_Feather", distant ? .10f : .04f);
            material.SetFloat("_PaperStrength", .025f); material.SetFloat("_PaperScale", .12f);
            material.SetFloat("_ShadowStrength", distant ? 0 : 1);
            material.SetFloat("_Cull", palette.name.StartsWith("Water") ? 0 : 2);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void GroundScenery(Transform root)
        {
            var temporary = new List<MeshCollider>();
            var supports = new List<Collider>(root.GetComponentsInChildren<Collider>(true));
            try
            {
                // Only used while authoring: distant scenery remains non-physical afterwards.
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh.name.StartsWith("Landscape_")) continue;
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    temporary.Add(collider); supports.Add(collider);
                }
                Physics.SyncTransforms();
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    bool tree = filter.sharedMesh.name.StartsWith("Pine_");
                    bool stone = filter.sharedMesh.name.StartsWith("Boulder_");
                    if ((!tree && !stone) || filter.transform.parent.GetComponent<Renderer>()) continue;
                    var part = filter.transform.parent;
                    var position = part.position;
                    var ray = new Ray(new Vector3(position.x, 300, position.z), Vector3.down);
                    float highest = float.NegativeInfinity;
                    foreach (var collider in supports)
                        if (collider.Raycast(ray, out var hit, 600)) highest = Mathf.Max(highest, hit.point.y);
                    if (float.IsNegativeInfinity(highest)) throw new InvalidOperationException("Missing scenic support: " + part.name);
                    position.y = highest - (tree ? .12f : .4f) * part.lossyScale.y;
                    part.position = position;
                }
            }
            finally
            {
                foreach (var collider in temporary) UnityEngine.Object.DestroyImmediate(collider);
                Physics.SyncTransforms();
            }
        }

        static void Surface(Transform parent, Mesh mesh, Material[] materials, Vector3 offset, bool collision, Material ink, bool castShadow)
        {
            var go = Create("Surface - " + mesh.name, parent);
            go.localPosition = offset;
            go.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(go.gameObject, StaticEditorFlags.BatchingStatic);
            if (collision) go.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (!ink) return;
            ComicOutlineHullFactory.Diagnostics diagnostics;
            var shell = Create("Comic ink contour - 描边随父部件", go);
            shell.gameObject.AddComponent<MeshFilter>().sharedMesh = ComicOutlineHullFactory.GetOrCreate(mesh, Root + "/Contours", out diagnostics);
            var edge = shell.gameObject.AddComponent<MeshRenderer>();
            edge.sharedMaterials = Enumerable.Repeat(ink, mesh.subMeshCount).ToArray();
            edge.shadowCastingMode = ShadowCastingMode.Off; edge.receiveShadows = false;
            edge.lightProbeUsage = LightProbeUsage.Off; edge.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(shell.gameObject, StaticEditorFlags.BatchingStatic);
            SceneVisibilityManager.instance.DisablePicking(shell.gameObject, false);
        }

        static string Readable(string name)
        {
            if (name == "Upper_Sanctuary_Crag") return "Upper sanctuary crag - 上庭岩峰";
            if (name == "Lower_Sanctuary_Crag") return "Lower sanctuary crag - 下庭岩峰";
            if (name == "Ravine_River") return "Valley stream - 谷底溪流";
            return "Ridge sector " + name.Substring("Landscape_".Length);
        }
        static Transform Create(string name, Transform parent)
        {
            var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Add mountain scenery component");
            go.transform.SetParent(parent, false); return go.transform;
        }
    }
}
