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
    /// <summary>Installs the editable, static bridge between the two existing courts.</summary>
    public static class SamuraiCourtyardBridgeSetup
    {
        public const string Root = SamuraiCourtyardDecorationSetup.Root;
        public const string ModelPath = Root + "/Models/Courtyard_Bridge.fbx";
        public const string BridgeName = "04 Connecting bridge - 庭院连桥";
        public const string LegacyName = "Legacy short ramps - 旧连接坡道（保留关闭）";

        [MenuItem("Katana/Level/Install Courtyard Bridge")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == SamuraiCourtyardDecorationSetup.RootName);
            if (!dressing) throw new InvalidOperationException("Install the courtyard dressing first.");
            var existing = dressing.transform.Find(BridgeName);
            if (existing) return "Courtyard bridge already exists. All transforms, materials and enabled states preserved.";

            var meshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Mesh>().OrderBy(m => m.name).ToArray();
            if (!meshes.Any(m => m.name == "Deck_WarmTimber") || !meshes.Any(m => m.name == "Rail_Left_Vermilion") || !meshes.Any(m => m.name == "Rail_Right_Vermilion"))
                throw new InvalidOperationException("Import the complete Blender bridge model first.");
            var deckMesh = meshes.First(m => m.name == "Deck_WarmTimber");
            if (Mathf.Abs(deckMesh.bounds.min.x) > .01f || Mathf.Abs(deckMesh.bounds.max.x - 14.42f) > .01f)
                throw new InvalidOperationException("The imported bridge must run along Unity +X from 0 to 14.42 metres.");
            var materials = new Dictionary<string, Material>
            {
                { "WarmTimber", Material("Old_honey_timber") },
                { "DarkTimber", PlankJointMaterial() },
                { "Vermilion", Material("Vermilion_lacquer") },
                { "Slate", Material("Foundation_slate") },
                { "Stone", Material("Warm_sandstone") },
                { "Ochre", Material("Quiet_ochre") }
            };
            var ink = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Materials/Ink.mat");
            if (!ink) throw new InvalidOperationException("Missing courtyard ink material.");
            var detailInk = Material("Ink_Detail");
            // Validate every material before starting an undoable scene mutation.
            foreach (var mesh in meshes) PaletteFor(mesh.name, materials);
            string contours = Root + "/Meshes/BridgeContours";
            Directory.CreateDirectory(contours);
            AssetDatabase.Refresh();

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install courtyard connecting bridge");
            var bridge = Create(BridgeName, dressing.transform);
            bridge.position = new Vector3(-13.72f, 0f, -1.8f);
            var deck = Create("01 Deck - 连续木桥面", bridge);
            var left = Create("02 Left balustrade - 左侧栏杆", bridge);
            var right = Create("03 Right balustrade - 右侧栏杆", bridge);
            var beams = Create("04 Supporting beams - 承重梁", bridge);
            var stones = Create("05 Stone abutments - 两端石座", bridge);
            int colliders = 0;
            foreach (var mesh in meshes)
            {
                bool walkingDeck = mesh.name == "Deck_WarmTimber";
                bool railing = mesh.name.StartsWith("Rail_", StringComparison.Ordinal);
                bool seams = mesh.name.IndexOf("Seam", StringComparison.OrdinalIgnoreCase) >= 0 || mesh.name.IndexOf("Joints", StringComparison.Ordinal) >= 0;
                Transform category = mesh.name.StartsWith("Abutment_") ? stones : mesh.name.StartsWith("Beam_") ? beams : mesh.name.IndexOf("Left", StringComparison.Ordinal) >= 0 ? left : mesh.name.IndexOf("Right", StringComparison.Ordinal) >= 0 ? right : deck;
                var part = Create(ReadableName(mesh.name), category);
                // Imported vertices retain bridge-local coordinates; center each editable pivot.
                part.localPosition = mesh.bounds.center;
                var surface = Create("Surface - " + mesh.name, part);
                surface.localPosition = -mesh.bounds.center;
                surface.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = surface.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = PaletteFor(mesh.name, materials);
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(surface.gameObject, StaticEditorFlags.BatchingStatic);
                if (walkingDeck || railing)
                {
                    var collider = surface.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh;
                    collider.convex = false;
                    colliders++;
                }
                if (!seams)
                {
                    ComicOutlineHullFactory.Diagnostics diagnostics;
                    var shell = Create("Comic ink contour - 描边随父部件", surface);
                    shell.gameObject.AddComponent<MeshFilter>().sharedMesh = ComicOutlineHullFactory.GetOrCreate(mesh, contours, out diagnostics);
                    var edge = shell.gameObject.AddComponent<MeshRenderer>();
                    edge.sharedMaterial = railing || mesh.name.IndexOf("Cap", StringComparison.OrdinalIgnoreCase) >= 0 ? detailInk : ink;
                    edge.shadowCastingMode = ShadowCastingMode.Off;
                    edge.receiveShadows = false;
                    edge.lightProbeUsage = LightProbeUsage.Off;
                    edge.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    GameObjectUtility.SetStaticEditorFlags(shell.gameObject, StaticEditorFlags.BatchingStatic);
                    SceneVisibilityManager.instance.DisablePicking(shell.gameObject, false);
                }
            }
            PreserveOldRamps(scene);
            if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var screen)) screen.RefreshSources();
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);
            return $"Courtyard bridge installed: {meshes.Length} editable meshes, {colliders} continuous static colliders. Original short ramps retained disabled.";
        }

        static Material Material(string name)
        {
            var value = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + name + ".mat");
            if (!value) throw new InvalidOperationException("Missing courtyard material: " + name);
            return value;
        }

        static Material PlankJointMaterial()
        {
            string path = Root + "/Materials/Bridge_plank_joint.mat";
            var value = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (value) return value;
            value = new Material(Material("Old_honey_timber")) { name = "Bridge plank joint" };
            value.SetColor("_BaseColor", new Color(0.32549f, 0.29020f, 0.23922f, 1f));
            value.SetColor("_ShadowColor", new Color(0.22353f, 0.24314f, 0.26275f, 1f));
            AssetDatabase.CreateAsset(value, path);
            return value;
        }

        static Material PaletteFor(string meshName, Dictionary<string, Material> palette)
        {
            foreach (var pair in palette)
                if (meshName.EndsWith("_" + pair.Key, StringComparison.Ordinal)) return pair.Value;
            throw new InvalidOperationException("Unknown bridge material suffix: " + meshName);
        }

        static string ReadableName(string name)
        {
            if (name.IndexOf("Joints", StringComparison.Ordinal) >= 0) return "Fine plank joints - 细木板缝";
            if (name.StartsWith("Deck_")) return "Continuous timber deck - 一体缓坡桥面";
            if (name.StartsWith("Rail_")) return "Continuous balustrade - 一体栏杆";
            if (name.StartsWith("Beam_")) return "Load bearing beam - 承重梁";
            if (name.StartsWith("Abutment_Upper")) return "Upper stone landing - 上层石座";
            if (name.StartsWith("Abutment_Lower")) return "Lower stone landing - 下层石座";
            if (name.IndexOf("Seam", StringComparison.OrdinalIgnoreCase) >= 0) return "Fine plank seams - 细木板缝";
            return name.Replace('_', ' ');
        }

        static Transform Create(string name, Transform parent)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Add editable bridge component");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void PreserveOldRamps(Scene scene)
        {
            var floor = scene.GetRootGameObjects().First(g => g.name == "Floor");
            var passage = floor.transform.Find("03 Connecting passage - 连接通道");
            if (!passage) throw new InvalidOperationException("The existing connecting passage is missing.");
            var old = Create(LegacyName, passage);
            foreach (var t in floor.GetComponentsInChildren<Transform>(true))
                if (t.name == "South Wall (3)" || t.name == "South Wall (4)") Undo.SetTransformParent(t, old, "Preserve original short ramp");
            var enabledStates = old.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.enabled);
            old.gameObject.SetActive(false);
            // Existing contour followers disable their hull Renderer in OnDisable.
            // The inactive container is sufficient; preserve each original Renderer toggle too.
            foreach (var pair in enabledStates) pair.Key.enabled = pair.Value;
        }
    }
}
