using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    internal static class SamuraiJapaneseTempleAssembly
    {
        const string Finish = "Japanese temple architecture - 山寺木构";
        const string Destination = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/BlenderAssembly/JapaneseTemple.asset";

        [Serializable]
        sealed class Survey
        {
            public Piece[] groups;
            public Swatch[] palette;
        }

        [Serializable]
        sealed class Piece
        {
            public string assembly, fade, name, material;
            public float[] points;
            public int[] triangles;
        }

        [Serializable]
        sealed class Swatch
        {
            public string name, color;
        }

        public static string Install(string exportedPath)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.name != "SamuraiCombatDemo_BlenderAssembly")
                throw new InvalidOperationException("Open the BlenderAssembly scene in Edit mode.");

            var nodes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            if (nodes.Any(node => node.name == Finish) || AssetDatabase.LoadMainAssetAtPath(Destination))
                throw new InvalidOperationException("Temple architecture is already installed or its asset already exists.");
            var data = JsonUtility.FromJson<Survey>(File.ReadAllText(exportedPath));
            var composition = nodes.Single(node => node.name.StartsWith("00 Scene compositions", StringComparison.Ordinal));
            var renderRoots = composition.GetComponentsInChildren<Transform>(false);
            var anchors = data.groups.Select(piece => piece.assembly).Distinct()
                .ToDictionary(name => name, name => renderRoots.Single(node => node.name == name &&
                    node.Cast<Transform>().Any(child => child.name.StartsWith("B", StringComparison.Ordinal) && child.GetComponent<MeshRenderer>())));
            var fades = anchors.Values.SelectMany(anchor => anchor.GetComponentsInChildren<SamuraiOcclusionGroup>())
                .Where(group => group.name.StartsWith("Camera fade - ", StringComparison.Ordinal))
                .ToDictionary(group => group.transform.parent.name + "|" + group.name);
            var palette = data.palette.ToDictionary(swatch => swatch.name, swatch => swatch.color);
            var gate = fades["Main Entrance Gate|Camera fade - roof and eave beam"];
            foreach (var piece in data.groups)
            {
                if (!palette.ContainsKey(piece.material) || (piece.fade.Length > 0 && !fades.ContainsKey(piece.assembly + "|" + piece.fade)) ||
                    piece.points.Length % 3 != 0 || piece.triangles.Length % 3 != 0 ||
                    piece.triangles.Any(index => index < 0 || index >= piece.points.Length / 3))
                    throw new InvalidDataException("Invalid temple mesh " + piece.name);
            }
            var template = gate.surfaces.Select(surface => surface.renderer.sharedMaterials[surface.materialIndex])
                .First(material => material.name.StartsWith("Ink_blue_roof", StringComparison.Ordinal));
            var mats = new Dictionary<string, Material>();
            foreach (var item in palette)
            {
                if (!ColorUtility.TryParseHtmlString(item.Value, out var color))
                    throw new InvalidDataException("Invalid color " + item.Value);
                var material = new Material(template) { name = item.Key };
                material.SetColor("_BaseColor", color);
                material.SetColor("_ShadowColor", new Color(color.r * .53f, color.g * .60f, color.b * .73f, 1));
                mats.Add(item.Key, material);
            }

            var underlay = new Material(mats["Tile field"]) { name = "Weathered roof underlay" };
            var cedarRoof = new Material(mats["Cypress edge"]) { name = "Cypress bark underlay" };
            var trim = new Material(mats["Tile ridge"]) { name = "Blackened ridge and trim" };
            AssetDatabase.CreateAsset(underlay, Destination);
            foreach (var material in mats.Values.Concat(new[] { cedarRoof, trim }))
                AssetDatabase.AddObjectToAsset(material, underlay);

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install Japanese temple architecture");
            RetireGateRoof(gate, underlay);
            foreach (var fade in fades.Values)
                ApplyRoofPalette(fade, underlay, cedarRoof, trim);
            FinishConnectedRoofs(composition, underlay, trim);

            int meshes = 0;
            int triangles = 0;
            foreach (var building in data.groups.GroupBy(piece => piece.assembly))
            {
                var anchor = anchors[building.Key];
                var parent = CreateChild(anchor, Finish);
                foreach (var layer in building.GroupBy(piece => piece.fade))
                {
                    var fade = layer.Key.Length > 0 ? fades[building.Key + "|" + layer.Key] : null;
                    var renderers = new List<SamuraiOcclusionGroup.Surface>();
                    foreach (var parts in layer.GroupBy(piece => piece.material))
                    {
                        var name = parts.Count() == 1 ? parts.First().name :
                            (fade ? fade.name.Replace("Camera fade - ", "") + " / " : "Facade / ") + parts.Key;
                        var vertices = new List<Vector3>();
                        var indices = new List<int>();
                        var worldToParent = parent.worldToLocalMatrix;
                        foreach (var piece in parts)
                        {
                            int start = vertices.Count;
                            for (int i = 0; i < piece.points.Length; i += 3)
                                vertices.Add(worldToParent.MultiplyPoint3x4(new Vector3(piece.points[i], piece.points[i + 1], piece.points[i + 2])));
                            foreach (int index in piece.triangles) indices.Add(start + index);
                        }
                        var mesh = new Mesh { name = building.Key + " - " + name,
                            indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                        mesh.SetVertices(vertices);
                        mesh.SetTriangles(indices, 0);
                        mesh.RecalculateNormals();
                        AssetDatabase.AddObjectToAsset(mesh, underlay);
                        var child = CreateChild(parent, name);
                        child.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = child.gameObject.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = mats[parts.Key];
                        renderer.lightProbeUsage = LightProbeUsage.Off;
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        renderers.Add(new SamuraiOcclusionGroup.Surface(renderer, 0, mesh));
                        triangles += indices.Count / 3;
                        meshes++;
                    }
                    if (fade)
                    {
                        Undo.RecordObject(fade, "Keep temple details in the roof fade");
                        fade.surfaces = fade.surfaces.Concat(renderers).ToArray();
                        EditorUtility.SetDirty(fade);
                    }
                }
            }
            Undo.CollapseUndoOperations(undo);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return $"{anchors.Count} buildings, {meshes} render meshes, {triangles} triangles";
        }

        static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name) { layer = parent.gameObject.layer };
            Undo.RegisterCreatedObjectUndo(child, "Add temple architecture");
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        static void RetireGateRoof(SamuraiOcclusionGroup gate, Material asset)
        {
            Undo.RecordObject(gate, "Replace gate roof presentation");
            var surfaces = gate.surfaces.ToArray();
            for (int i = 0; i < surfaces.Length; i++)
            {
                var surface = surfaces[i];
                var material = surface.renderer.sharedMaterials[surface.materialIndex];
                if (material.name.StartsWith("Old_honey_timber", StringComparison.Ordinal)) continue;
                var filter = surface.renderer.GetComponent<MeshFilter>();
                var replacement = UnityEngine.Object.Instantiate(filter.sharedMesh);
                replacement.name = "Mountain gate retained " + surface.renderer.name;
                replacement.SetTriangles(Array.Empty<int>(), surface.materialIndex, true);
                AssetDatabase.AddObjectToAsset(replacement, asset);
                Undo.RecordObject(filter, "Replace gate roof presentation");
                filter.sharedMesh = replacement;
                surfaces[i].probeMesh = replacement;
                EditorUtility.SetDirty(filter);
            }
            gate.surfaces = surfaces;
            EditorUtility.SetDirty(gate);
        }

        static void ApplyRoofPalette(SamuraiOcclusionGroup fade, Material roof, Material cypress, Material trim)
        {
            foreach (var surface in fade.surfaces)
            {
                var renderer = surface.renderer;
                if (!renderer || surface.materialIndex < 0) continue;
                var materials = renderer.sharedMaterials;
                string previous = materials[surface.materialIndex].name;
                Material replacement = null;
                if (previous.StartsWith("Ink_blue_roof", StringComparison.Ordinal))
                    replacement = fade.transform.parent.name == "Tea Pavilion" ? cypress : roof;
                else if (previous == "Quiet_ochre" || previous == "Interior_Gold")
                    replacement = trim;
                if (!replacement) continue;
                SetRoofMaterial(renderer, materials, surface.materialIndex, replacement);
            }
        }

        public static void FinishConnectedRoofs()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.name != "SamuraiCombatDemo_BlenderAssembly")
                throw new InvalidOperationException("Open the BlenderAssembly scene in Edit mode.");
            var composition = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>())
                .Single(node => node.name.StartsWith("00 Scene compositions", StringComparison.Ordinal));
            var palette = AssetDatabase.LoadAllAssetsAtPath(Destination).OfType<Material>().ToArray();
            FinishConnectedRoofs(composition, AssetDatabase.LoadAssetAtPath<Material>(Destination),
                palette.Single(m => m.name == "Blackened ridge and trim"));
        }

        static void FinishConnectedRoofs(Transform composition, Material roof, Material trim)
        {
            var linked = new[] { "Elite1 Hall Passage", "Curved Mountain Gallery", "West covered walk -6",
                "West covered walk 5", "East covered walk 0", "East covered walk 7" };
            foreach (var fade in composition.GetComponentsInChildren<SamuraiOcclusionGroup>()
                .Where(group => group.name == "Camera fade - roof" && linked.Contains(group.transform.parent.name)))
                ApplyRoofPalette(fade, roof, roof, trim);

            var lodge = composition.GetComponentsInChildren<Transform>()
                .Single(node => node.name == "Ridge Viewing Lodge" && node.Find("B017_Surface"));
            foreach (string part in new[] { "B017_Surface", "B018_Surface" })
            {
                var renderer = lodge.Find(part).GetComponent<MeshRenderer>();
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                    if (materials[slot].name.StartsWith("Ink_blue_roof", StringComparison.Ordinal))
                        SetRoofMaterial(renderer, materials, slot, roof);
            }
        }

        static void SetRoofMaterial(Renderer renderer, Material[] materials, int slot, Material material)
        {
            Undo.RecordObject(renderer, "Refinish temple roof palette");
            materials[slot] = material;
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
        }
    }
}
