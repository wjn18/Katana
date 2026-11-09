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
    public static class SamuraiJapaneseSetting
    {
        const string Asset = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/BlenderAssembly/JapaneseSetting.asset";
        const string DetailRoot = "Temple setting details - 山寺院落细部";
        const float Tolerance = .0012f;

        [Serializable] sealed class Model
        {
            public Piece[] groups;
            public Finish[] finishes;
            public Style[] palette;
            public Identity[] nodes;
        }
        [Serializable] sealed class Identity { public int index; public string path; public float[] center; }
        [Serializable] sealed class Piece
        {
            public int sourceIndex;
            public string name, material;
            public float[] points;
            public int[] triangles;
            public bool replace;
        }
        [Serializable] sealed class Finish { public int sourceIndex, slot; public string material; }
        [Serializable] sealed class Style
        {
            public string name, color, shadow;
            public float pattern, strength, paving;
            public float[] anchor;
        }

        sealed class Original
        {
            public Transform node;
            public MeshRenderer renderer;
            public Bounds bounds;
            public Face[] faces;
            public SamuraiOcclusionGroup fade;
            public int visibleTriangles;
        }

        readonly struct Face
        {
            readonly Vector3 origin, normal, first, second;
            readonly float aa, bb, ab, inverse;
            public Face(Vector3 a, Vector3 b, Vector3 c)
            {
                origin = a;
                first = b - a;
                second = c - a;
                normal = Vector3.Cross(first, second).normalized;
                aa = Vector3.Dot(first, first);
                bb = Vector3.Dot(second, second);
                ab = Vector3.Dot(first, second);
                float determinant = aa * bb - ab * ab;
                inverse = determinant > 0 ? 1f / determinant : 0;
            }
            public bool Contains(Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
            {
                if (inverse == 0 || Mathf.Abs(Vector3.Dot(normal, facing)) < .99f ||
                    Mathf.Abs(Vector3.Dot(normal, a - origin)) > Tolerance ||
                    Mathf.Abs(Vector3.Dot(normal, b - origin)) > Tolerance ||
                    Mathf.Abs(Vector3.Dot(normal, c - origin)) > Tolerance) return false;
                Vector3 delta = (a + b + c) / 3 - origin;
                float x = Vector3.Dot(delta, first), y = Vector3.Dot(delta, second);
                float u = (x * bb - y * ab) * inverse;
                float v = (y * aa - x * ab) * inverse;
                return u >= -.004f && v >= -.004f && u + v <= 1.004f;
            }
        }

        sealed class Batch
        {
            public MeshRenderer renderer;
            public readonly Dictionary<int, HashSet<int>> removed = new();
        }

        public static string Apply(string path, bool dryRun = true)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.name != "SamuraiCombatDemo_BlenderAssembly")
                throw new InvalidOperationException("Open BlenderAssembly in Edit mode.");
            bool installed = AssetDatabase.LoadMainAssetAtPath(Asset);
            if (installed && !dryRun)
                throw new InvalidOperationException("The current setting has already been installed.");
            var model = JsonUtility.FromJson<Model>(File.ReadAllText(path));
            var live = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var byPath = live.GroupBy(FullPath).ToDictionary(group => group.Key, group => group.ToArray());
            var identities = model.nodes.ToDictionary(entry => entry.index);
            var required = model.groups.Select(piece => piece.sourceIndex)
                .Concat(model.finishes.Select(finish => finish.sourceIndex)).Distinct();
            var nodes = new Dictionary<int, Transform>();
            foreach (int index in required)
            {
                var entry = identities[index];
                if (!byPath.TryGetValue(entry.path, out var candidates))
                    continue;
                if (candidates.Length == 1)
                {
                    nodes.Add(index, candidates[0]);
                    continue;
                }
                var matching = candidates.Where(node =>
                {
                    var filter = node.GetComponent<MeshFilter>();
                    return filter && filter.sharedMesh && entry.center.Length == 3 &&
                        (filter.transform.TransformPoint(filter.sharedMesh.bounds.center) -
                        new Vector3(entry.center[0], entry.center[1], entry.center[2])).sqrMagnitude <= .000004f;
                }).ToArray();
                if (matching.Length != 1)
                    throw new InvalidOperationException("The scene placement changed after survey: " + entry.path);
                nodes.Add(index, matching[0]);
            }
            model.groups = model.groups.Where(piece => nodes.ContainsKey(piece.sourceIndex)).ToArray();
            model.finishes = model.finishes.Where(finish => nodes.ContainsKey(finish.sourceIndex)).ToArray();
            var allFades = live.Select(node => node.GetComponent<SamuraiOcclusionGroup>()).Where(group => group).ToArray();
            var originals = model.groups.Where(piece => piece.replace).Select(piece => piece.sourceIndex).Distinct()
                .ToDictionary(index => index, index => ReadOriginal(nodes[index]));
            var grid = new Dictionary<Vector2Int, List<Original>>();
            foreach (var original in originals.Values)
                for (int x = Mathf.FloorToInt(original.bounds.min.x); x <= Mathf.FloorToInt(original.bounds.max.x); x++)
                    for (int z = Mathf.FloorToInt(original.bounds.min.z); z <= Mathf.FloorToInt(original.bounds.max.z); z++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!grid.TryGetValue(key, out var list)) grid.Add(key, list = new List<Original>());
                        list.Add(original);
                    }

            var batches = new List<Batch>();
            foreach (var renderer in live.Select(node => node.GetComponent<MeshRenderer>()).Where(renderer =>
                renderer && renderer.enabled && renderer.gameObject.activeInHierarchy))
            {
                if (installed && renderer.GetComponentsInParent<Transform>().Any(node => node.name == DetailRoot))
                    continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                var batch = new Batch { renderer = renderer };
                var mesh = filter.sharedMesh;
                var direct = originals.Values.FirstOrDefault(original => original.renderer == renderer ||
                    (renderer.transform.parent == original.node && renderer.sharedMaterials.Any(material =>
                        material && material.shader.name == "OC Design/Comic Lighting/Outline")));
                if (direct != null)
                {
                    for (int slot = 0; slot < mesh.subMeshCount; slot++)
                    {
                        var indices = mesh.GetTriangles(slot);
                        if (indices.Length > 0)
                        {
                            batch.removed.Add(slot, Enumerable.Range(0, indices.Length / 3).Select(index => index * 3).ToHashSet());
                            direct.visibleTriangles += indices.Length / 3;
                        }
                    }
                    if (batch.removed.Count > 0) batches.Add(batch);
                    continue;
                }
                if (!mesh.isReadable) continue;
                var points = mesh.vertices.Select(renderer.localToWorldMatrix.MultiplyPoint3x4).ToArray();
                for (int slot = 0; slot < mesh.subMeshCount; slot++)
                {
                    var indices = mesh.GetTriangles(slot);
                    var removed = new HashSet<int>();
                    for (int start = 0; start < indices.Length; start += 3)
                    {
                        Vector3 a = points[indices[start]], b = points[indices[start + 1]], c = points[indices[start + 2]];
                        Vector3 center = (a + b + c) / 3;
                        if (!grid.TryGetValue(new Vector2Int(Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.z)), out var nearby)) continue;
                        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                        foreach (var original in nearby)
                        {
                            if (!original.bounds.Contains(a) || !original.bounds.Contains(b) || !original.bounds.Contains(c) ||
                                !original.faces.Any(face => face.Contains(a, b, c, normal))) continue;
                            removed.Add(start);
                            original.visibleTriangles++;
                            if (!original.fade)
                                original.fade = allFades.Where(group => group.surfaces.Any(surface =>
                                    surface.renderer == renderer && (surface.materialIndex < 0 || surface.materialIndex == slot)))
                                    .OrderBy(group => group.surfaces.Length).FirstOrDefault();
                            break;
                        }
                    }
                    if (removed.Count > 0) batch.removed.Add(slot, removed);
                }
                if (batch.removed.Count > 0) batches.Add(batch);
            }
            var unmatched = originals.Where(pair => pair.Value.visibleTriangles == 0).Select(pair =>
                pair.Key + ":" + FullPath(pair.Value.node)).ToArray();
            if (installed)
            {
                int residual = originals.Values.Sum(original => original.visibleTriangles);
                if (residual > 0) throw new InvalidOperationException("Old replacement triangles remain visible: " + residual);
                return "No retained presentation triangles overlap the retired source pieces.";
            }
            if (unmatched.Length > 0) throw new InvalidOperationException("Unmatched existing presentation\n" + string.Join("\n", unmatched));
            foreach (var piece in model.groups)
                if (piece.points.Length % 3 != 0 || piece.triangles.Length % 3 != 0 ||
                    piece.triangles.Any(index => index < 0 || index >= piece.points.Length / 3))
                    throw new InvalidDataException("Invalid setting mesh " + piece.name);
            var styleNames = model.palette.Select(style => style.name).ToHashSet();
            if (model.groups.Any(piece => !styleNames.Contains(piece.material)) ||
                model.finishes.Any(finish => !styleNames.Contains(finish.material)))
                throw new InvalidDataException("The setting palette is incomplete.");
            foreach (var finish in model.finishes)
            {
                var renderer = nodes[finish.sourceIndex].GetComponent<MeshRenderer>();
                if (!renderer || finish.slot >= renderer.sharedMaterials.Length)
                    throw new InvalidDataException("Invalid material target");
            }
            string summary = $"{originals.Count} replaced pieces, {batches.Count} existing render meshes, " +
                $"{model.groups.Length} detail meshes, {model.finishes.Length} material slots";
            if (dryRun) return summary;

            string baseline = Path.Combine(Path.GetDirectoryName(path), "apply-before.unity");
            if (!EditorSceneManager.SaveScene(scene, baseline, true))
                throw new IOException("The current live scene could not be backed up.");

            var template = live.Select(node => node.GetComponent<MeshRenderer>()).Where(renderer => renderer)
                .SelectMany(renderer => renderer.sharedMaterials).First(material => material &&
                    material.name == "Ivory_plaster_CourtyardFinish");
            var palette = model.palette.ToDictionary(style => style.name, style => CreateMaterial(template, style));
            var asset = palette.Values.First();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refine Japanese courtyard setting");
            bool createdAsset = false;
            try
            {
                AssetDatabase.CreateAsset(asset, Asset);
                createdAsset = true;
                foreach (var material in palette.Values.Skip(1)) AssetDatabase.AddObjectToAsset(material, asset);
                var inkTemplate = live.Select(node => node.GetComponent<MeshRenderer>()).Where(renderer => renderer)
                    .SelectMany(renderer => renderer.sharedMaterials).First(material => material &&
                        material.shader.name == "OC Design/Comic Lighting/Outline");
                var ink = new Material(inkTemplate) { name = "Temple furnishing ink" };
                ink.SetFloat("_OutlineWidth", .007f);
                ink.SetFloat("_MinOutlinePixels", .7f);
                ink.SetFloat("_BoxOutline", 0);
                AssetDatabase.AddObjectToAsset(ink, asset);
                foreach (var finish in model.finishes)
                {
                    var renderer = nodes[finish.sourceIndex].GetComponent<MeshRenderer>();
                    var materials = renderer.sharedMaterials;
                    materials[finish.slot] = palette[finish.material];
                    Undo.RecordObject(renderer, "Refinish courtyard detail");
                    renderer.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                foreach (var batch in batches)
                {
                    var filter = batch.renderer.GetComponent<MeshFilter>();
                    var before = filter.sharedMesh;
                    var after = before.isReadable ? Object.Instantiate(before) : EditableCopy(before);
                    after.name = "Retained setting - " + batch.renderer.name;
                    foreach (var cut in batch.removed)
                    {
                        var indices = before.GetTriangles(cut.Key);
                        var retained = new List<int>();
                        for (int start = 0; start < indices.Length; start += 3)
                            if (!cut.Value.Contains(start))
                            {
                                retained.Add(indices[start]); retained.Add(indices[start + 1]); retained.Add(indices[start + 2]);
                            }
                        after.SetTriangles(retained, cut.Key, false);
                    }
                    after.bounds = before.bounds;
                    AssetDatabase.AddObjectToAsset(after, asset);
                    Undo.RecordObject(filter, "Replace detail presentation only");
                    filter.sharedMesh = after;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                    foreach (var fade in allFades.Where(group => group.surfaces.Any(surface => surface.renderer == batch.renderer)))
                    {
                        Undo.RecordObject(fade, "Keep current camera fade mesh");
                        for (int i = 0; i < fade.surfaces.Length; i++)
                            if (fade.surfaces[i].renderer == batch.renderer) fade.surfaces[i].probeMesh = after;
                        EditorUtility.SetDirty(fade);
                    }
                }
                foreach (var source in model.groups.GroupBy(piece => piece.sourceIndex))
                {
                    var original = nodes[source.Key];
                    var parent = new GameObject(DetailRoot);
                    Undo.RegisterCreatedObjectUndo(parent, "Add local courtyard details");
                    parent.transform.SetParent(original, false);
                    parent.layer = original.gameObject.layer;
                    var additions = new List<SamuraiOcclusionGroup.Surface>();
                    foreach (var piece in source)
                    {
                        var vertices = new List<Vector3>();
                        var originalBounds = original.GetComponent<MeshFilter>().sharedMesh.bounds;
                        for (int i = 0; i < piece.points.Length; i += 3)
                            vertices.Add(originalBounds.center + Vector3.Scale(originalBounds.extents,
                                new Vector3(piece.points[i], piece.points[i + 1], piece.points[i + 2])));
                        var mesh = new Mesh { name = piece.name, indexFormat = IndexFormat.UInt32 };
                        mesh.SetVertices(vertices);
                        mesh.SetTriangles(piece.triangles, 0);
                        mesh.RecalculateNormals();
                        AssetDatabase.AddObjectToAsset(mesh, asset);
                        var child = new GameObject(piece.name) { layer = parent.layer };
                        child.transform.SetParent(parent.transform, false);
                        child.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = child.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = palette[piece.material];
                        renderer.lightProbeUsage = LightProbeUsage.Off;
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        additions.Add(new SamuraiOcclusionGroup.Surface(renderer, 0, mesh));
                        if (piece.material == "Stone" || piece.material == "Weathered stone" || piece.material == "Aged bronze")
                        {
                            var hull = new GameObject("Temple furnishing outline") { layer = parent.layer };
                            hull.transform.SetParent(child.transform, false);
                            hull.AddComponent<MeshFilter>().sharedMesh = mesh;
                            var outline = hull.AddComponent<MeshRenderer>();
                            outline.sharedMaterial = ink;
                            outline.shadowCastingMode = ShadowCastingMode.Off;
                            outline.receiveShadows = false;
                            outline.lightProbeUsage = LightProbeUsage.Off;
                            outline.reflectionProbeUsage = ReflectionProbeUsage.Off;
                            additions.Add(new SamuraiOcclusionGroup.Surface(outline, 0, mesh));
                        }
                    }
                    var target = originals.TryGetValue(source.Key, out var replaced) ? replaced.fade :
                        original.GetComponentInParent<SamuraiOcclusionGroup>();
                    if (target)
                    {
                        Undo.RecordObject(target, "Fade courtyard detail with its original assembly");
                        target.surfaces = target.surfaces.Concat(additions).ToArray();
                        EditorUtility.SetDirty(target);
                    }
                }
                Undo.CollapseUndoOperations(undo);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                return summary;
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                if (createdAsset) AssetDatabase.DeleteAsset(Asset);
                foreach (var material in palette.Values)
                    if (material && !EditorUtility.IsPersistent(material)) Object.DestroyImmediate(material);
                throw;
            }
        }

        static Original ReadOriginal(Transform node)
        {
            var renderer = node.GetComponent<MeshRenderer>();
            var mesh = node.GetComponent<MeshFilter>().sharedMesh;
            var points = mesh.vertices.Select(node.TransformPoint).ToArray();
            var indices = mesh.triangles;
            var original = new Original { node = node, renderer = renderer, bounds = new Bounds(points[0], Vector3.zero),
                faces = new Face[indices.Length / 3], fade = node.GetComponentInParent<SamuraiOcclusionGroup>() };
            foreach (Vector3 point in points) original.bounds.Encapsulate(point);
            original.bounds.Expand(Tolerance * 2);
            for (int i = 0; i < indices.Length; i += 3)
                original.faces[i / 3] = new Face(points[indices[i]], points[indices[i + 1]], points[indices[i + 2]]);
            return original;
        }

        static Material CreateMaterial(Material template, Style style)
        {
            if (!ColorUtility.TryParseHtmlString(style.color, out var color) ||
                !ColorUtility.TryParseHtmlString(style.shadow, out var shadow))
                throw new InvalidDataException("Invalid palette " + style.name);
            var material = new Material(template) { name = style.name };
            material.SetColor("_BaseColor", color);
            material.SetColor("_ShadowColor", shadow);
            material.SetFloat("_GardenMoss", 0);
            material.DisableKeyword("_SAMURAI_GARDEN_MOSS");
            material.SetFloat("_TempleSurface", style.pattern);
            material.SetFloat("_TempleDetailStrength", style.strength);
            material.SetFloat("_PaperStrength", .045f);
            material.SetFloat("_PavingStrength", style.paving);
            material.SetFloat("_SamuraiOcclusionFade", 0);
            material.SetFloat("_TextureStrength", 0);
            material.SetFloat("_AlphaClip", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", 2);
            if (style.anchor != null && style.anchor.Length >= 2)
                material.SetVector("_TempleAnchor", new Vector4(style.anchor[0], style.anchor[1], 0, 0));
            return material;
        }

        static Mesh EditableCopy(Mesh source)
        {
            var copy = new Mesh { indexFormat = source.indexFormat, vertices = source.vertices,
                normals = source.normals, tangents = source.tangents, colors32 = source.colors32,
                subMeshCount = source.subMeshCount };
            for (int channel = 0; channel < 8; channel++)
            {
                var coordinates = new List<Vector4>();
                source.GetUVs(channel, coordinates);
                if (coordinates.Count > 0) copy.SetUVs(channel, coordinates);
            }
            for (int slot = 0; slot < source.subMeshCount; slot++)
                copy.SetIndices(source.GetIndices(slot), source.GetTopology(slot), slot, false);
            copy.bounds = source.bounds;
            return copy;
        }

        public static void ExportInstalled(string planPath, string outputPath)
        {
            var model = JsonUtility.FromJson<Model>(File.ReadAllText(planPath));
            var live = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).GroupBy(FullPath)
                .ToDictionary(group => group.Key, group => group.ToArray());
            var identities = model.nodes.ToDictionary(entry => entry.index);
            var exported = new List<Piece>();
            foreach (var piece in model.groups)
            {
                string path = identities[piece.sourceIndex].path;
                if (!live.TryGetValue(path, out var sources)) continue;
                var source = sources.SingleOrDefault(node => node.Find(DetailRoot));
                if (!source) continue;
                var part = source.Find(DetailRoot).Find(piece.name);
                var filter = part.GetComponent<MeshFilter>();
                var mesh = filter.sharedMesh;
                piece.points = mesh.vertices.SelectMany(vertex =>
                {
                    var world = part.TransformPoint(vertex);
                    return new[] { world.x, world.y, world.z };
                }).ToArray();
                piece.triangles = mesh.triangles;
                exported.Add(piece);
            }
            model.groups = exported.ToArray();
            File.WriteAllText(outputPath, JsonUtility.ToJson(model));
        }

        static string FullPath(Transform node) => node.parent ? FullPath(node.parent) + "/" + node.name : node.name;
    }
}
