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
    public static class SamuraiTerrainRedesign
    {
        const string Destination = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/Mountains/TerrainRedesign.asset";

        [Serializable] public sealed class Model
        {
            public Landform[] meshes;
            public Anchor[] anchors;
        }
        [Serializable] public sealed class Landform
        {
            public string name, path;
            public float[] points, matrix;
            public Layer[] submeshes;
            public bool collision;
        }
        [Serializable] public sealed class Layer
        {
            public string material;
            public int[] triangles;
        }
        [Serializable] public sealed class Anchor
        {
            public string path, family;
            public Vector3 position;
        }
        [Serializable] sealed class Fused
        {
            public string name;
            public float[] points;
            public FusedLayer[] submeshes;
        }
        [Serializable] sealed class FusedLayer
        {
            public string sourcePath;
            public int[] triangles;
        }

        public static string Export(string output)
        {
            var scene = RequireScene();
            var nodes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var forms = new List<Landform>();
            foreach (var renderer in nodes.Select(node => node.GetComponent<MeshRenderer>())
                .Where(renderer => renderer && renderer.enabled && renderer.gameObject.activeInHierarchy))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh || renderer.sharedMaterials.Any(material =>
                    material && material.shader.name == "OC Design/Comic Lighting/Outline")) continue;
                var mesh = filter.sharedMesh;
                string name = mesh.name;
                if (name != "Upper_Sanctuary_Crag" && name != "Lower_Sanctuary_Crag" &&
                    !name.StartsWith("Landscape_", StringComparison.Ordinal) && name != "Ravine_River" &&
                    name != "Backing_Mountain_Broken_Strata") continue;
                var matrix = renderer.localToWorldMatrix;
                forms.Add(new Landform
                {
                    name = name,
                    path = FullPath(renderer.transform),
                    matrix = Enumerable.Range(0, 16).Select(index => matrix[index]).ToArray(),
                    points = mesh.vertices.SelectMany(vertex =>
                    {
                        Vector3 p = matrix.MultiplyPoint3x4(vertex);
                        return new[] { p.x, p.y, p.z };
                    }).ToArray(),
                    submeshes = Enumerable.Range(0, mesh.subMeshCount).Select(slot =>
                        new Layer { material = renderer.sharedMaterials[slot].name, triangles = mesh.GetTriangles(slot) }).ToArray(),
                    collision = renderer.GetComponents<MeshCollider>().Any(collider => collider.enabled && !collider.isTrigger)
                });
            }
            var anchors = new List<Anchor>();
            foreach (var node in nodes.Where(node => node.gameObject.activeInHierarchy))
            {
                string family = null;
                if (node.name.StartsWith("Ledge pine ", StringComparison.Ordinal) && HasAncestor(node, "Refined shoulder pines"))
                    family = "ridge";
                else if (node.name.StartsWith("Mountain pine ", StringComparison.Ordinal) && HasAncestor(node, "03 Hillside groves"))
                    family = "grove";
                else if (node.name.StartsWith("Talus stone ", StringComparison.Ordinal) && HasAncestor(node, "07 Talus stones"))
                    family = "talus";
                if (family != null) anchors.Add(new Anchor { path = FullPath(node), family = family, position = node.position });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(new Model { meshes = forms.ToArray(), anchors = anchors.ToArray() }));
            return $"{forms.Count} landforms, {forms.Count(form => form.collision)} terrain colliders, {anchors.Count} scenic anchors";
        }

        public static string Apply(string input)
        {
            var scene = RequireScene();
            var model = JsonUtility.FromJson<Model>(File.ReadAllText(input));
            var nodes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var lookup = nodes.GroupBy(FullPath).ToDictionary(group => group.Key, group => group.First());
            var renderers = model.meshes.ToDictionary(form => form.path, form => lookup[form.path].GetComponent<MeshRenderer>());
            foreach (var form in model.meshes)
            {
                var renderer = renderers[form.path];
                var matrix = renderer.localToWorldMatrix;
                if (form.matrix.Length != 16 || Enumerable.Range(0, 16).Any(index => Mathf.Abs(matrix[index] - form.matrix[index]) > .0001f))
                    throw new InvalidOperationException("The terrain placement changed after survey: " + form.path);
                if (form.points.Length % 3 != 0 || form.submeshes.Length != renderer.sharedMaterials.Length ||
                    form.submeshes.Any(layer => layer.triangles.Length % 3 != 0 ||
                    layer.triangles.Any(index => index < 0 || index >= form.points.Length / 3)))
                    throw new InvalidDataException("Invalid landform " + form.name);
            }
            var existing = AssetDatabase.LoadAllAssetsAtPath(Destination);
            var nearSource = AssetDatabase.LoadAssetAtPath<Material>(SamuraiMountainCourtyardSetup.Root + "/Materials/Rock_Strata_Near.mat");
            var middleSource = AssetDatabase.LoadAssetAtPath<Material>(SamuraiMountainCourtyardSetup.Root + "/Materials/Rock_Strata_Middle.mat");
            var farSource = AssetDatabase.LoadAssetAtPath<Material>(SamuraiMountainCourtyardSetup.Root + "/Materials/Rock_Strata_Far.mat");
            if (!nearSource || !middleSource || !farSource) throw new InvalidOperationException("The rock palettes are missing.");
            bool firstInstall = existing.Length == 0;
            var near = existing.OfType<Material>().FirstOrDefault(material => material.name == "TerrainRedesign") ??
                RockPalette(nearSource, "TerrainRedesign", .055f, .055f);
            var middle = existing.OfType<Material>().FirstOrDefault(material => material.name == "Refined middle mountain") ??
                RockPalette(middleSource, "Refined middle mountain", .04f, .04f);
            var far = existing.OfType<Material>().FirstOrDefault(material => material.name == "Refined distant mountain") ??
                RockPalette(farSource, "Refined distant mountain", .025f, .025f);
            string baseline = Path.Combine(Path.GetDirectoryName(input), "apply-before.unity");
            if (firstInstall && !EditorSceneManager.SaveScene(scene, baseline, true))
                throw new IOException("Could not preserve the live terrain scene.");
            var normals = WorldNormals(model.meshes);
            var oldProxies = new List<MeshCollider>();
            var newProxies = new List<MeshCollider>();
            var temporary = new List<GameObject>();
            var temporaryMeshes = new List<Mesh>();
            var allGroups = nodes.Select(node => node.GetComponent<SamuraiOcclusionGroup>()).Where(group => group).ToArray();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refine mountain silhouettes and valley");
            try
            {
                if (firstInstall)
                {
                    AssetDatabase.CreateAsset(near, Destination);
                    AssetDatabase.AddObjectToAsset(middle, near);
                    AssetDatabase.AddObjectToAsset(far, near);
                }
                foreach (var form in model.meshes)
                {
                    var renderer = renderers[form.path];
                    var filter = renderer.GetComponent<MeshFilter>();
                    var before = filter.sharedMesh;
                    oldProxies.Add(Proxy(renderer.transform, before, temporary, temporaryMeshes));
                    var points = Enumerable.Range(0, form.points.Length / 3)
                        .Select(index => new Vector3(form.points[index * 3], form.points[index * 3 + 1], form.points[index * 3 + 2])).ToArray();
                    var local = points.Select(renderer.transform.InverseTransformPoint).ToList();
                    if (form.name.EndsWith("_Sanctuary_Crag", StringComparison.Ordinal) && before.vertexCount == points.Length)
                    {
                        var originalPoints = before.vertices;
                        for (int index = 0; index < points.Length; index++)
                            if (points[index].y >= -8) local[index] = originalPoints[index];
                    }
                    var mesh = new Mesh { name = "Refined " + form.name, indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(local);
                    mesh.subMeshCount = form.submeshes.Length;
                    for (int slot = 0; slot < form.submeshes.Length; slot++)
                        mesh.SetTriangles(form.submeshes[slot].triangles, slot, false);
                    mesh.RecalculateBounds();
                    var worldToNormal = renderer.localToWorldMatrix.transpose;
                    var smooth = points.Select(point => worldToNormal.MultiplyVector(normals[Cell(point)]).normalized).ToArray();
                    var bodyNormals = smooth.ToArray();
                    if (form.name.EndsWith("_Sanctuary_Crag", StringComparison.Ordinal) && before.vertexCount == points.Length)
                    {
                        var originalNormals = before.normals;
                        for (int index = 0; index < points.Length; index++)
                            if (points[index].y >= -8 && originalNormals.Length == points.Length)
                                bodyNormals[index] = originalNormals[index];
                    }
                    mesh.normals = bodyNormals;
                    Mesh persistent = Store(mesh, existing, near);
                    Undo.RecordObject(filter, "Replace mountain render mesh");
                    filter.sharedMesh = persistent;
                    RebindGroups(allGroups, renderer, persistent);
                    foreach (var collider in renderer.GetComponents<MeshCollider>())
                    {
                        if (!collider.enabled || collider.isTrigger) continue;
                        if (collider.sharedMesh != before)
                            throw new InvalidOperationException("A hand-edited terrain collider needs a separate survey.");
                        Undo.RecordObject(collider, "Keep mountain collision on the visible surface");
                        collider.sharedMesh = null;
                        collider.sharedMesh = persistent;
                    }
                    foreach (var hull in renderer.transform.Cast<Transform>().Select(child => child.GetComponent<MeshRenderer>())
                        .Where(hull => hull && hull.sharedMaterials.Any(material => material &&
                            material.shader.name == "OC Design/Comic Lighting/Outline")))
                    {
                        var outline = Object.Instantiate(persistent);
                        outline.name = "Refined outline " + form.name;
                        outline.normals = smooth;
                        var outlineMesh = Store(outline, existing, near);
                        Undo.RecordObject(hull.GetComponent<MeshFilter>(), "Follow the new mountain silhouette");
                        hull.GetComponent<MeshFilter>().sharedMesh = outlineMesh;
                        RebindGroups(allGroups, hull, outlineMesh);
                    }
                    if (form.name != "Ravine_River")
                    {
                        var palette = form.name.StartsWith("Landscape_2_", StringComparison.Ordinal) ? far :
                            form.name.StartsWith("Landscape_1_", StringComparison.Ordinal) ? middle : near;
                        Undo.RecordObject(renderer, "Unify connected mountain stone");
                        renderer.sharedMaterials = Enumerable.Repeat(palette, form.submeshes.Length).ToArray();
                    }
                    newProxies.Add(Proxy(renderer.transform, persistent, temporary, temporaryMeshes));
                }
                // The apron is part of the lodge batch and keeps its exact contact geometry.
                foreach (var renderer in nodes.Select(node => node.GetComponent<MeshRenderer>()).Where(renderer => renderer &&
                    renderer.enabled && renderer.gameObject.activeInHierarchy && !renderers.Values.Contains(renderer)))
                {
                    var materials = renderer.sharedMaterials;
                    if (!materials.Any(material => material && (material.name == "Mountain_Rock_Ledges" || material.name == "Mountain Rock Ledges"))) continue;
                    Undo.RecordObject(renderer, "Match the lodge's embedded rock to the hillside");
                    for (int slot = 0; slot < materials.Length; slot++)
                        if (materials[slot] && (materials[slot].name == "Mountain_Rock_Ledges" || materials[slot].name == "Mountain Rock Ledges"))
                            materials[slot] = near;
                    renderer.sharedMaterials = materials;
                }
                Physics.SyncTransforms();
                int planted = 0;
                foreach (var anchor in model.anchors)
                {
                    if (!lookup.TryGetValue(anchor.path, out var node)) continue;
                    Vector3 p = node.position;
                    if (!Ground(oldProxies, p, out float previous) || !Ground(newProxies, p, out float updated))
                        throw new InvalidOperationException("A scenic anchor has no ground: " + anchor.path);
                    float delta = updated - previous;
                    if (Mathf.Abs(delta) < .0005f) continue;
                    Undo.RecordObject(node, "Seat the scenic vegetation on the new slope");
                    node.position = p + Vector3.up * delta;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(node);
                    planted++;
                }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return $"{model.meshes.Length} mountain and valley meshes, 2 terrain collisions, {planted} scenic anchors reseated";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                if (firstInstall && AssetDatabase.LoadMainAssetAtPath(Destination)) AssetDatabase.DeleteAsset(Destination);
                throw;
            }
            finally
            {
                foreach (var item in temporary) if (item) Object.DestroyImmediate(item);
                foreach (var mesh in temporaryMeshes) if (mesh) Object.DestroyImmediate(mesh);
                Physics.SyncTransforms();
            }
        }

        static Material RockPalette(Material source, string name, float strata, float seams)
        {
            var material = new Material(source) { name = name };
            material.SetFloat("_StrataStrength", strata);
            material.SetFloat("_SeamStrength", seams);
            material.SetFloat("_ErosionStrength", .04f);
            material.SetFloat("_Feather", .06f);
            return material;
        }

        public static string FuseVisibleSurfaces(string input)
        {
            var scene = RequireScene();
            var form = JsonUtility.FromJson<Fused>(File.ReadAllText(input));
            if (form.submeshes.Length != 3 || form.points.Length % 3 != 0)
                throw new InvalidDataException("Expected the three fused summit surfaces.");
            var nodes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var lookup = nodes.GroupBy(FullPath).ToDictionary(group => group.Key, group => group.First());
            var sources = form.submeshes.Select(layer => lookup[layer.sourcePath].GetComponent<MeshRenderer>()).ToArray();
            var zone = nodes.Single(node => node.name == "06 Terrain and setting - 山体与布景");
            var existing = AssetDatabase.LoadAllAssetsAtPath(Destination);
            var palette = AssetDatabase.LoadAssetAtPath<Material>(Destination);
            if (!palette) throw new InvalidOperationException("Install the revised landforms first.");
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Fuse the connected summit surface");
            var root = zone.Find("Integrated mountain skin - 连体山岳表面");
            if (!root)
            {
                var created = new GameObject("Integrated mountain skin - 连体山岳表面");
                Undo.RegisterCreatedObjectUndo(created, "Create the integrated mountain surface");
                created.transform.SetParent(zone, false);
                root = created.transform;
            }
            var points = Enumerable.Range(0, form.points.Length / 3)
                .Select(index => new Vector3(form.points[index * 3], form.points[index * 3 + 1], form.points[index * 3 + 2])).ToArray();
            var adjacent = Enumerable.Range(0, points.Length).Select(index => new List<Vector3>()).ToArray();
            foreach (var layer in form.submeshes)
                for (int index = 0; index < layer.triangles.Length; index += 3)
                {
                    int a = layer.triangles[index], b = layer.triangles[index + 1], c = layer.triangles[index + 2];
                    var normal = Unit(Vector3.Cross(points[b] - points[a], points[c] - points[a]));
                    int[] corners = { a, b, c };
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = corners[corner];
                        Vector3 first = Unit(points[corners[(corner + 1) % 3]] - points[vertex]);
                        Vector3 second = Unit(points[corners[(corner + 2) % 3]] - points[vertex]);
                        float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(first, second), -1, 1));
                        adjacent[vertex].Add(normal * angle);
                    }
                }
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var hullNormals = new List<Vector3>();
            var indices = new List<int[]>();
            var normalMatrix = root.localToWorldMatrix.transpose;
            float crease = Mathf.Cos(65 * Mathf.Deg2Rad);
            foreach (var layer in form.submeshes)
            {
                var slot = new List<int>();
                for (int index = 0; index < layer.triangles.Length; index += 3)
                {
                    var a = points[layer.triangles[index]];
                    var b = points[layer.triangles[index + 1]];
                    var c = points[layer.triangles[index + 2]];
                    var face = Unit(Vector3.Cross(b - a, c - a));
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int source = layer.triangles[index + corner];
                        Vector3 smooth = Vector3.zero, silhouette = Vector3.zero;
                        foreach (var neighbor in adjacent[source])
                        {
                            silhouette += neighbor;
                            if (Vector3.Dot(Unit(neighbor), face) >= crease) smooth += neighbor;
                        }
                        slot.Add(vertices.Count);
                        vertices.Add(root.InverseTransformPoint(points[source]));
                        normals.Add(Unit(normalMatrix.MultiplyVector(smooth)));
                        hullNormals.Add(Unit(normalMatrix.MultiplyVector(silhouette)));
                    }
                }
                indices.Add(slot.ToArray());
            }
            var mesh = new Mesh { name = "Unified summit surface", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = indices.Count;
            for (int index = 0; index < indices.Count; index++) mesh.SetTriangles(indices[index], index, false);
            mesh.RecalculateBounds();
            var outlineMesh = Object.Instantiate(mesh);
            outlineMesh.name = "Unified summit silhouette";
            outlineMesh.SetNormals(hullNormals);
            Mesh persistent = Store(mesh, existing, palette);
            Mesh outline = Store(outlineMesh, existing, palette);
            var body = root.Find("Unified mountain surface");
            if (!body)
            {
                var created = new GameObject("Unified mountain surface") { layer = sources[0].gameObject.layer };
                created.transform.SetParent(root, false);
                created.AddComponent<MeshFilter>();
                created.AddComponent<MeshRenderer>();
                body = created.transform;
            }
            var bodyRenderer = body.GetComponent<MeshRenderer>();
            Undo.RecordObject(body.GetComponent<MeshFilter>(), "Set unified mountain mesh");
            body.GetComponent<MeshFilter>().sharedMesh = persistent;
            bodyRenderer.sharedMaterials = Enumerable.Repeat(palette, 3).ToArray();
            bodyRenderer.lightProbeUsage = LightProbeUsage.Off;
            bodyRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var ink = existing.OfType<Material>().FirstOrDefault(material => material.name == "Mountain silhouette ink");
            if (!ink)
            {
                var template = sources[0].GetComponentsInChildren<MeshRenderer>()
                    .SelectMany(renderer => renderer.sharedMaterials).First(material => material &&
                        material.shader.name == "OC Design/Comic Lighting/Outline");
                ink = new Material(template) { name = "Mountain silhouette ink" };
                ink.SetColor("_OutlineColor", new Color(.12f, .16f, .18f, 1));
                AssetDatabase.AddObjectToAsset(ink, palette);
            }
            // The screen-space contour supplies the distant silhouette without
            // an expanded shell surfacing inside the mountain's concave gullies.
            ink.SetFloat("_OutlineWidth", 0);
            ink.SetFloat("_MinOutlinePixels", 0);
            ink.SetFloat("_BoxOutline", 0);
            var hull = body.Find("Unified mountain outline");
            if (!hull)
            {
                var created = new GameObject("Unified mountain outline") { layer = body.gameObject.layer };
                created.transform.SetParent(body, false);
                created.AddComponent<MeshFilter>();
                created.AddComponent<MeshRenderer>();
                hull = created.transform;
            }
            var hullRenderer = hull.GetComponent<MeshRenderer>();
            Undo.RecordObject(hull.GetComponent<MeshFilter>(), "Set unified silhouette mesh");
            hull.GetComponent<MeshFilter>().sharedMesh = outline;
            hullRenderer.sharedMaterials = Enumerable.Repeat(ink, 3).ToArray();
            hullRenderer.shadowCastingMode = ShadowCastingMode.Off;
            hullRenderer.receiveShadows = false;
            var oldSlots = new Dictionary<Renderer, int>();
            for (int index = 0; index < sources.Length; index++)
            {
                oldSlots.Add(sources[index], index);
                foreach (var renderer in sources[index].GetComponentsInChildren<MeshRenderer>().Where(renderer =>
                    renderer != sources[index] && renderer.sharedMaterials.Any(material => material &&
                        material.shader.name == "OC Design/Comic Lighting/Outline")))
                    oldSlots.Add(renderer, index);
            }
            foreach (var group in nodes.Select(node => node.GetComponent<SamuraiOcclusionGroup>()).Where(group =>
                group && group.surfaces.Any(surface => surface.renderer && oldSlots.ContainsKey(surface.renderer))))
            {
                var slots = group.surfaces.Where(surface => surface.renderer && oldSlots.ContainsKey(surface.renderer))
                    .Select(surface => oldSlots[surface.renderer]).Distinct().ToArray();
                var retained = group.surfaces.Where(surface => !surface.renderer || !oldSlots.ContainsKey(surface.renderer)).ToList();
                foreach (int slot in slots)
                {
                    retained.Add(new SamuraiOcclusionGroup.Surface(bodyRenderer, slot, persistent));
                    retained.Add(new SamuraiOcclusionGroup.Surface(hullRenderer, slot, outline));
                }
                Undo.RecordObject(group, "Preserve individual mountain camera fades");
                group.surfaces = retained.ToArray();
                EditorUtility.SetDirty(group);
            }
            foreach (var renderer in oldSlots.Keys)
            {
                Undo.RecordObject(renderer, "Retain the previous separate mountain skin");
                renderer.enabled = false;
            }
            var fadeGroups = nodes.Select(node => node.GetComponent<SamuraiOcclusionGroup>()).Where(group => group);
            RebindGroups(fadeGroups, bodyRenderer, persistent);
            RebindGroups(fadeGroups, hullRenderer, outline);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return $"One integrated visual surface, {vertices.Count / 3} triangles, three independent camera-fade sections";
        }

        public static string CheckUpperGround(string originalSurvey)
        {
            var scene = RequireScene();
            var model = JsonUtility.FromJson<Model>(File.ReadAllText(originalSurvey));
            var nodes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .GroupBy(FullPath).ToDictionary(group => group.Key, group => group.First());
            var temporary = new List<GameObject>();
            var temporaryMeshes = new List<Mesh>();
            int count = 0;
            float maximum = 0;
            var differences = new List<string>();
            try
            {
                foreach (var form in model.meshes.Where(form => form.collision))
                {
                    var source = new Mesh { name = "Original ground reference", indexFormat = IndexFormat.UInt32,
                        vertices = Enumerable.Range(0, form.points.Length / 3).Select(index =>
                            new Vector3(form.points[index * 3], form.points[index * 3 + 1], form.points[index * 3 + 2])).ToArray(),
                        triangles = form.submeshes.SelectMany(layer => layer.triangles).ToArray() };
                    temporaryMeshes.Add(source);
                    var referenceObject = new GameObject("Temporary old summit reference") { hideFlags = HideFlags.HideAndDontSave };
                    temporary.Add(referenceObject);
                    var reference = referenceObject.AddComponent<MeshCollider>();
                    reference.sharedMesh = source;
                    var actual = nodes[form.path].GetComponent<MeshCollider>();
                    var originalPoints = source.vertices;
                    var originalIndices = source.triangles;
                    Physics.SyncTransforms();
                    var locations = new HashSet<Vector2Int>();
                    foreach (var point in source.vertices.Where(point => point.y >= -8))
                    {
                        if (!locations.Add(new Vector2Int(Mathf.RoundToInt(point.x * 1000), Mathf.RoundToInt(point.z * 1000)))) continue;
                        var ray = new Ray(new Vector3(point.x, 100, point.z), Vector3.down);
                        bool before = reference.Raycast(ray, out var oldHit, 200);
                        bool after = actual.Raycast(ray, out var newHit, 200);
                        if (before != after) throw new InvalidOperationException("A summit foothold was added or removed.");
                        if (!before) continue;
                        float difference = Mathf.Abs(oldHit.point.y - newHit.point.y);
                        if (difference > .0002f)
                            differences.Add(form.name + "\t" + point + "\t" + difference.ToString("R") + "\t" + oldHit.normal.y.ToString("R"));
                        if (oldHit.normal.y < .55f) continue;
                        // Faces spanning the fixed upper band and the reshaped
                        // cliff are allowed to change below that boundary.
                        int triangle = oldHit.triangleIndex * 3;
                        if (triangle < 0 || triangle + 2 >= originalIndices.Length ||
                            originalPoints[originalIndices[triangle]].y < -8 ||
                            originalPoints[originalIndices[triangle + 1]].y < -8 ||
                            originalPoints[originalIndices[triangle + 2]].y < -8) continue;
                        maximum = Mathf.Max(maximum, difference);
                        count++;
                    }
                }
                File.WriteAllLines(Path.Combine(Path.GetDirectoryName(originalSurvey), "ground-edge-differences.tsv"), differences);
                if (maximum > .0002f) throw new InvalidOperationException("Walkable summit ground height changed: " + maximum);
                return $"{count} protected upper-surface rays matched, maximum height difference {maximum:R} metres";
            }
            finally
            {
                foreach (var item in temporary) if (item) Object.DestroyImmediate(item);
                foreach (var mesh in temporaryMeshes) if (mesh) Object.DestroyImmediate(mesh);
                Physics.SyncTransforms();
            }
        }

        static Mesh Store(Mesh mesh, Object[] existing, Object asset)
        {
            var previous = existing.OfType<Mesh>().FirstOrDefault(candidate => candidate.name == mesh.name);
            if (previous)
            {
                Undo.RecordObject(previous, "Refine the terrain preview");
                // Update through Mesh so an existing renderer refreshes its native
                // vertex buffer immediately, while keeping the asset reference stable.
                var positions = mesh.vertices;
                var normals = mesh.normals;
                var uv = mesh.uv;
                var indices = Enumerable.Range(0, mesh.subMeshCount).Select(mesh.GetTriangles).ToArray();
                previous.Clear();
                previous.indexFormat = mesh.indexFormat;
                previous.vertices = positions;
                previous.normals = normals;
                if (uv.Length == positions.Length) previous.uv = uv;
                previous.subMeshCount = indices.Length;
                for (int index = 0; index < indices.Length; index++) previous.SetTriangles(indices[index], index, false);
                previous.RecalculateBounds();
                previous.UploadMeshData(false);
                EditorUtility.SetDirty(previous);
                Object.DestroyImmediate(mesh);
                return previous;
            }
            AssetDatabase.AddObjectToAsset(mesh, asset);
            return mesh;
        }

        static void RebindGroups(IEnumerable<SamuraiOcclusionGroup> groups, Renderer renderer, Mesh mesh)
        {
            foreach (var group in groups.Where(group => group.surfaces.Any(surface => surface.renderer == renderer)))
            {
                Undo.RecordObject(group, "Keep the mountain camera probes current");
                for (int index = 0; index < group.surfaces.Length; index++)
                    if (group.surfaces[index].renderer == renderer) group.surfaces[index].probeMesh = mesh;
                EditorUtility.SetDirty(group);
            }
        }

        static MeshCollider Proxy(Transform source, Mesh mesh, ICollection<GameObject> temporary, ICollection<Mesh> temporaryMeshes)
        {
            var item = new GameObject("Temporary terrain surface query") { hideFlags = HideFlags.HideAndDontSave };
            var world = new Mesh { name = "Temporary world terrain", indexFormat = IndexFormat.UInt32,
                vertices = mesh.vertices.Select(source.TransformPoint).ToArray(), triangles = mesh.triangles };
            world.RecalculateBounds();
            var collider = item.AddComponent<MeshCollider>();
            collider.sharedMesh = world;
            temporary.Add(item);
            temporaryMeshes.Add(world);
            return collider;
        }

        static bool Ground(IEnumerable<MeshCollider> terrain, Vector3 position, out float height)
        {
            height = float.NegativeInfinity;
            var ray = new Ray(new Vector3(position.x, 300, position.z), Vector3.down);
            foreach (var collider in terrain)
                if (collider.Raycast(ray, out var hit, 800)) height = Mathf.Max(height, hit.point.y);
            return !float.IsNegativeInfinity(height);
        }

        static Vector3Int Cell(Vector3 point) => Vector3Int.RoundToInt(point * 1000);

        static Vector3 Unit(Vector3 vector)
        {
            float length = vector.magnitude;
            return length > 0 ? vector / length : Vector3.up;
        }

        static Dictionary<Vector3Int, Vector3> WorldNormals(IEnumerable<Landform> forms)
        {
            var normals = new Dictionary<Vector3Int, Vector3>();
            foreach (var form in forms)
            {
                var points = Enumerable.Range(0, form.points.Length / 3)
                    .Select(index => new Vector3(form.points[index * 3], form.points[index * 3 + 1], form.points[index * 3 + 2])).ToArray();
                foreach (var layer in form.submeshes)
                    for (int index = 0; index < layer.triangles.Length; index += 3)
                    {
                        Vector3 a = points[layer.triangles[index]], b = points[layer.triangles[index + 1]], c = points[layer.triangles[index + 2]];
                        var normal = Vector3.Cross(b - a, c - a);
                        foreach (var point in new[] { a, b, c })
                        {
                            var key = Cell(point);
                            normals.TryGetValue(key, out var previous);
                            normals[key] = previous + normal;
                        }
                    }
            }
            return normals.ToDictionary(pair => pair.Key, pair => pair.Value.normalized);
        }

        static Scene RequireScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.name != "SamuraiCombatDemo_BlenderAssembly")
                throw new InvalidOperationException("Open BlenderAssembly in Edit mode.");
            return scene;
        }

        static bool HasAncestor(Transform node, string prefix)
        {
            for (var parent = node.parent; parent; parent = parent.parent)
                if (parent.name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        static string FullPath(Transform node) => node.parent ? FullPath(node.parent) + "/" + node.name : node.name;
    }
}
