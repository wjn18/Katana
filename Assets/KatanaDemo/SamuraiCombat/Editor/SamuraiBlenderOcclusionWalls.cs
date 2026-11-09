using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiBlenderOcclusionWalls
    {
        const string Output = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/BlenderAssembly/Occlusion";
        const float PositionTolerance = .0006f;
        static readonly Regex BayName = new Regex(@"^(Upper|LowerNE|LowerSW|Inner) wall [0-9]+$", RegexOptions.CultureInvariant);
        static readonly Regex WallName = new Regex(
            @"(^East Wall$|_Wall(?:Body)?_Plaster$|Wall_Plaster$|_Plaster_Window_Base_-?1$|_Spandrel_(Left|Right)$|Window[0-9]*_RecessedPaper_Paper$)",
            RegexOptions.CultureInvariant);
        const float RidgeLengthScale = 10.9f / 14.42f;
        static readonly float[] RidgePosts = { -7.06f, -4.7f, -2.35f, 0f, 2.35f, 4.7f, 7.06f };

        sealed class Wall
        {
            public Transform root;
            public string childName;
            public Collider[] blockers;
            public readonly List<SamuraiOcclusionGroup.Surface> surfaces = new List<SamuraiOcclusionGroup.Surface>();
        }

        sealed class Source
        {
            public Wall wall;
            public Transform transform;
            public Bounds bounds;
            public Face[] faces;
            public int matched;
        }

        readonly struct Face
        {
            public readonly Vector3 a, b, c, normal;
            readonly Vector3 ab, ac;
            readonly float aa, bb, cross, inverse;

            public Face(Vector3 a, Vector3 b, Vector3 c)
            {
                this.a = a; this.b = b; this.c = c;
                ab = b - a; ac = c - a;
                normal = Vector3.Cross(ab, ac).normalized;
                aa = Vector3.Dot(ab, ab);
                bb = Vector3.Dot(ac, ac);
                cross = Vector3.Dot(ab, ac);
                float determinant = aa * bb - cross * cross;
                inverse = determinant > 0f ? 1f / determinant : 0f;
            }

            public bool Contains(Vector3 point, Vector3 facing)
            {
                if (inverse == 0f || Vector3.Dot(normal, facing) < .985f) return false;
                Vector3 delta = point - a;
                if (Mathf.Abs(Vector3.Dot(normal, delta)) > PositionTolerance) return false;
                float x = Vector3.Dot(delta, ab), y = Vector3.Dot(delta, ac);
                float u = (x * bb - y * cross) * inverse;
                float v = (y * aa - x * cross) * inverse;
                return u >= -.004f && v >= -.004f && u + v <= 1.004f;
            }
        }

        sealed class Batch
        {
            public MeshRenderer renderer;
            public string path;
            public readonly List<SamuraiOcclusionMeshSplit.Slice> slices = new List<SamuraiOcclusionMeshSplit.Slice>();
            public readonly List<Wall> owners = new List<Wall>();
        }

        /// <summary>Partition live Blender batches while retaining their geometry and the original collision.</summary>
        public static string Install(string manifestPath, bool dryRun = false)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiBlenderAssemblyImport.ScenePath)
                throw new InvalidOperationException("Open BlenderAssembly in Edit mode first.");
            string[] rows = File.ReadAllLines(manifestPath);
            if (rows.Length < 2 || !rows[0].StartsWith("key\tassembly\tlayer\tshadow\treceive\tmembers", StringComparison.Ordinal))
                throw new InvalidDataException("Expected the groups.tsv manifest used to build BlenderAssembly.");
            GameObject[] roots = scene.GetRootGameObjects();
            Transform assembly = roots.Single(root => root.name.StartsWith("Blender architecture - ", StringComparison.Ordinal)).transform;
            var nodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (GameObject root in roots)
                if (root.transform != assembly) Index(root.transform, root.transform.GetSiblingIndex().ToString(CultureInfo.InvariantCulture), nodes);
            var existing = roots.SelectMany(root => root.GetComponentsInChildren<SamuraiOcclusionGroup>(true)).ToArray();
            var installed = new HashSet<Transform>(existing.Select(group => group.transform));
            var reserved = existing.SelectMany(group => group.surfaces).Where(surface => surface.renderer)
                .GroupBy(surface => surface.renderer).ToDictionary(group => group.Key, group => group.Select(surface => surface.materialIndex).ToHashSet());
            var walls = new Dictionary<Transform, Wall>();
            var ridgeWindows = new List<Wall>();
            var batches = new List<Batch>();
            var unmatched = new List<string>();
            int ambiguous = 0;
            foreach (string row in rows.Skip(1).Where(row => !string.IsNullOrWhiteSpace(row)))
            {
                string[] fields = row.Split('\t');
                if (fields.Length < 6) throw new InvalidDataException("Incomplete Blender group row.");
                bool compound = fields[1].Contains("Outer Wall") || fields[1].Contains("Inner Wall");
                string key = fields[0];
                string[] members = fields[5].Split('|');
                var parts = new List<(Transform transform, Wall wall, int divided)>();
                foreach (string member in members)
                {
                    if (!nodes.TryGetValue(member, out Transform node) ||
                        !node.TryGetComponent(out MeshRenderer original) ||
                        !node.TryGetComponent(out MeshFilter filter) || !filter.sharedMesh)
                    {
                        if (compound) throw new InvalidOperationException($"Wall source {member} for {key} changed.");
                        continue;
                    }
                    Transform piece = GetWallRoot(node, compound);
                    int divided = node.gameObject.activeInHierarchy && !original.enabled ? DividedWall(node.name) : 0;
                    Wall wall = null;
                    if (piece && node.gameObject.activeInHierarchy && !original.enabled && !installed.Contains(piece))
                    {
                        if (!walls.TryGetValue(piece, out wall))
                        {
                            Collider[] blockers = compound ? CompoundColliders(piece) : ExactCollider(piece);
                            wall = new Wall { root = piece, blockers = blockers };
                            walls.Add(piece, wall);
                        }
                    }
                    parts.Add((node, wall, divided));
                }
                if (!parts.Any(part => part.wall != null || part.divided != 0)) continue;
                Transform batchRoot = assembly.Find(fields[1]);
                if (!batchRoot) throw new InvalidOperationException($"Missing Blender assembly group {fields[1]}.");
                MeshRenderer renderer = batchRoot.GetComponentsInChildren<MeshRenderer>(true).Single(part => part.name == key);
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    throw new InvalidOperationException($"The active Blender renderer {key} is unavailable.");
                reserved.TryGetValue(renderer, out HashSet<int> reservedSlots);
                if (reservedSlots != null && reservedSlots.Contains(-1))
                    throw new InvalidOperationException($"Another camera fade owns every slot of {key}.");
                var sources = parts.SelectMany(part =>
                {
                    Source source = CreateSource(part.transform, part.wall);
                    return part.divided == 0 ? new[] { source } : Divide(source, part.divided, walls, ridgeWindows, installed);
                }).ToArray();
                Batch batch = PlanBatch(renderer, sources, reservedSlots, ref ambiguous);
                if (batch.slices.Count != 0) batches.Add(batch);
                unmatched.AddRange(sources.Where(source => source.wall != null && source.matched == 0)
                    .Select(source => $"{key}/{source.transform.name}"));
            }

            // Validate all geometry partitions before creating any assets or components.
            foreach (Batch batch in batches)
            {
                if (AssetDatabase.LoadMainAssetAtPath(batch.path))
                    throw new InvalidOperationException($"Existing wall fade mesh needs inspection: {batch.path}");
                SamuraiOcclusionMeshSplit.Apply(batch.renderer, batch.slices, batch.path, true);
            }
            int covered = batches.SelectMany(batch => batch.owners).Distinct().Count();
            int triangles = batches.Sum(batch => batch.slices.Sum(slice => slice.triangleStarts.Count));
            int compoundCount = batches.SelectMany(batch => batch.owners).Distinct().Count(wall => BayName.IsMatch(wall.root.name));
            string summary = $"{covered} walls ({compoundCount} compound bays), {batches.Count} mesh partitions, " +
                $"{triangles} triangles, {ambiguous} ambiguous faces left opaque, {unmatched.Count} unmatched source parts.";
            if (unmatched.Count != 0) summary += " Unmatched: " + string.Join(" | ", unmatched.Take(24));
            if (dryRun || batches.Count == 0) return summary;
            if (!AssetDatabase.IsValidFolder(Output))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(Output).Replace('\\', '/'), Path.GetFileName(Output));

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install Blender wall occlusion groups");
            var created = new List<string>();
            try
            {
                foreach (Batch batch in batches)
                {
                    created.Add(batch.path);
                    int[] slots = SamuraiOcclusionMeshSplit.Apply(batch.renderer, batch.slices, batch.path, false);
                    for (int i = 0; i < slots.Length; i++)
                        batch.owners[i].surfaces.Add(new SamuraiOcclusionGroup.Surface(batch.renderer, slots[i]));
                }
                foreach (Wall wall in walls.Values.Concat(ridgeWindows).Where(wall => wall.surfaces.Count != 0))
                {
                    Transform target = wall.root;
                    if (!string.IsNullOrEmpty(wall.childName))
                    {
                        var child = new GameObject(wall.childName);
                        Undo.RegisterCreatedObjectUndo(child, "Create one ridge window wall group");
                        target = child.transform;
                        target.SetParent(wall.root, false);
                    }
                    var group = Undo.AddComponent<SamuraiOcclusionGroup>(target.gameObject);
                    group.blockers = wall.blockers;
                    group.surfaces = wall.surfaces.ToArray();
                    group.useVisualProbes = wall.blockers.Length == 0;
                    EditorUtility.SetDirty(group);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(group);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return summary;
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                foreach (string path in created)
                    if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
                throw;
            }
        }

        static void Index(Transform node, string path, Dictionary<string, Transform> nodes)
        {
            nodes.Add(path, node);
            for (int i = 0; i < node.childCount; i++)
                Index(node.GetChild(i), path + "." + i.ToString(CultureInfo.InvariantCulture), nodes);
        }

        static Transform GetWallRoot(Transform node, bool compound)
        {
            if (compound)
            {
                for (Transform child = node; child.parent; child = child.parent)
                    if (BayName.IsMatch(child.parent.name))
                        return IsCompoundWallPart(child.name) ? child.parent : null;
                return null;
            }
            for (Transform candidate = node; candidate && candidate != node.parent?.parent?.parent; candidate = candidate.parent)
                if (candidate.GetComponent<MeshRenderer>() && WallName.IsMatch(candidate.name)) return candidate;
            return null;
        }

        static bool IsCompoundWallPart(string name) => name.Contains("Body_Plaster") ||
            name.Contains("EaveBinding") || name.Contains("FrameLattice") || name.Contains("ContinuousTileCap");

        static Collider[] CompoundColliders(Transform root) => root.Cast<Transform>()
            .Where(part => part.name.Contains("Body_Plaster") || part.name.Contains("ContinuousTileCap"))
            .SelectMany(part => part.GetComponentsInChildren<Collider>(true))
            .Where(collider => collider.enabled && !collider.isTrigger).ToArray();

        static Collider[] ExactCollider(Transform root)
        {
            MeshFilter mesh = root.GetComponent<MeshFilter>();
            return root.GetComponents<MeshCollider>().Where(collider =>
                collider.enabled && !collider.isTrigger && collider.sharedMesh == mesh.sharedMesh).Cast<Collider>().ToArray();
        }

        static int DividedWall(string name)
        {
            if (name == "EliteGrandHall_West_Grass_Veranda_Body_Plaster") return 1;
            if (name == "LowerHall_Upper_Timber_And_Plaster_Plaster") return 2;
            if (name.StartsWith("RidgeHall_Window_Bay_", StringComparison.Ordinal) &&
                !name.EndsWith("_Stone", StringComparison.Ordinal) && !name.EndsWith("_Red", StringComparison.Ordinal)) return 3;
            if (name.StartsWith("RidgeHall_Open_Entrance_", StringComparison.Ordinal) &&
                (name.EndsWith("_Plaster", StringComparison.Ordinal) || name.EndsWith("_Timber", StringComparison.Ordinal) ||
                    name.EndsWith("_Dark", StringComparison.Ordinal))) return 4;
            return 0;
        }

        // The procedural halls combine many wall pieces into one palette mesh.
        // Their authoring builders retained individual boxes for the wall solids.
        static Source[] Divide(Source source, int kind, Dictionary<Transform, Wall> walls, List<Wall> ridgeWindows,
            HashSet<Transform> installed)
        {
            Transform parent = source.transform.parent;
            if (kind == 3) return DivideRidgeWindows(source, parent, ridgeWindows, installed);
            BoxCollider[] colliders = parent.GetComponentsInChildren<BoxCollider>()
                .Where(collider => collider.enabled && !collider.isTrigger && IsWallBox(collider.name, kind)).ToArray();
            var pieces = new List<Source>();
            var assigned = new HashSet<int>();
            foreach (BoxCollider collider in colliders)
            {
                Matrix4x4 local = collider.transform.worldToLocalMatrix;
                var bounds = new Bounds(collider.center, collider.size);
                bounds.Expand(.002f);
                var faces = new List<Face>();
                for (int i = 0; i < source.faces.Length; i++)
                {
                    Face face = source.faces[i];
                    if (assigned.Contains(i) || !bounds.Contains(local.MultiplyPoint3x4(face.a)) ||
                        !bounds.Contains(local.MultiplyPoint3x4(face.b)) || !bounds.Contains(local.MultiplyPoint3x4(face.c))) continue;
                    assigned.Add(i);
                    faces.Add(face);
                }
                if (faces.Count == 0) continue;
                Wall wall = installed.Contains(collider.transform) ? null :
                    GetWall(walls, collider.transform, kind == 4 ? Array.Empty<Collider>() : new Collider[] { collider });
                pieces.Add(WithFaces(source, wall, faces.ToArray()));
            }
            AddRemainder(source, pieces, assigned);
            return pieces.ToArray();
        }

        static bool IsWallBox(string name, int kind) => kind == 1 ? name.EndsWith(" plaster wall", StringComparison.Ordinal) :
            kind == 2 ? name.EndsWith(" paneled knee wall", StringComparison.Ordinal) ||
                name.EndsWith(" clerestory solid", StringComparison.Ordinal) : name == "Ridge doorway flanking wall";

        // Ridge's physical window collision spans the full side. Use the exact
        // visual bay geometry between original posts, never that shared collider.
        static Source[] DivideRidgeWindows(Source source, Transform parent, List<Wall> ridgeWindows,
            HashSet<Transform> installed)
        {
            var pieces = new List<Source>();
            var assigned = new HashSet<int>();
            Matrix4x4 local = source.transform.worldToLocalMatrix;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int bay = 0; bay < RidgePosts.Length - 1; bay++)
                {
                    float left = (RidgePosts[bay] + .095f) * RidgeLengthScale;
                    float right = (RidgePosts[bay + 1] - .095f) * RidgeLengthScale;
                    var bounds = new Bounds(new Vector3((left + right) * .5f, 2.3f, side * 2.34f),
                        new Vector3(right - left, 4.8f, .65f));
                    var faces = new List<Face>();
                    for (int i = 0; i < source.faces.Length; i++)
                    {
                        Face face = source.faces[i];
                        if (assigned.Contains(i) || !bounds.Contains(local.MultiplyPoint3x4(face.a)) ||
                            !bounds.Contains(local.MultiplyPoint3x4(face.b)) || !bounds.Contains(local.MultiplyPoint3x4(face.c))) continue;
                        assigned.Add(i);
                        faces.Add(face);
                    }
                    if (faces.Count == 0) continue;
                    string name = $"Camera fade ridge {(side < 0 ? "south" : "north")} window bay {bay + 1:00}";
                    Transform existing = parent.Find(name);
                    Wall wall = null;
                    if (!existing || !installed.Contains(existing))
                    {
                        wall = ridgeWindows.FirstOrDefault(candidate => candidate.root == parent && candidate.childName == name);
                        if (wall == null)
                        {
                            wall = new Wall { root = parent, childName = name, blockers = Array.Empty<Collider>() };
                            ridgeWindows.Add(wall);
                        }
                    }
                    pieces.Add(WithFaces(source, wall, faces.ToArray()));
                }
            }
            AddRemainder(source, pieces, assigned);
            return pieces.ToArray();
        }

        static Wall GetWall(Dictionary<Transform, Wall> walls, Transform root, Collider[] blockers)
        {
            if (!walls.TryGetValue(root, out Wall wall))
                walls.Add(root, wall = new Wall { root = root, blockers = blockers });
            return wall;
        }

        static void AddRemainder(Source source, List<Source> pieces, HashSet<int> assigned)
        {
            if (assigned.Count == 0) pieces.Add(source);
            else if (assigned.Count != source.faces.Length)
                pieces.Add(WithFaces(source, null, source.faces.Where((face, i) => !assigned.Contains(i)).ToArray()));
        }

        static Source WithFaces(Source source, Wall wall, Face[] faces)
        {
            var bounds = new Bounds(faces[0].a, Vector3.zero);
            foreach (Face face in faces)
            {
                bounds.Encapsulate(face.a);
                bounds.Encapsulate(face.b);
                bounds.Encapsulate(face.c);
            }
            bounds.Expand(PositionTolerance * 4f);
            return new Source { wall = wall, transform = source.transform, faces = faces, bounds = bounds };
        }

        static Source CreateSource(Transform node, Wall wall)
        {
            Mesh mesh = node.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Matrix4x4 matrix = node.localToWorldMatrix;
            var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                bounds.Encapsulate(vertices[i]);
            }
            bounds.Expand(PositionTolerance * 4f);
            var faces = new List<Face>();
            bool reverse = matrix.determinant < 0f;
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                if (mesh.GetTopology(slot) != MeshTopology.Triangles) continue;
                int[] indices = mesh.GetTriangles(slot);
                for (int i = 0; i < indices.Length; i += 3)
                    faces.Add(new Face(vertices[indices[i]], vertices[indices[i + (reverse ? 2 : 1)]],
                        vertices[indices[i + (reverse ? 1 : 2)]]));
            }
            return new Source { wall = wall, transform = node, faces = faces.ToArray(), bounds = bounds };
        }

        static Batch PlanBatch(MeshRenderer renderer, Source[] sources, HashSet<int> reserved, ref int ambiguous)
        {
            var batch = new Batch { renderer = renderer, path = $"{Output}/WallFade_{renderer.name}.asset" };
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = renderer.localToWorldMatrix.MultiplyPoint3x4(vertices[i]);
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                if (reserved != null && reserved.Contains(slot)) continue;
                var byWall = new Dictionary<Wall, HashSet<int>>();
                int[] indices = mesh.GetTriangles(slot);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var face = new Face(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
                    Vector3 centroid = (face.a + face.b + face.c) / 3f;
                    Source match = null;
                    bool conflict = false;
                    foreach (Source source in sources)
                    {
                        if (!source.bounds.Contains(face.a) || !source.bounds.Contains(face.b) || !source.bounds.Contains(face.c) ||
                            !PointOnSource(source.faces, centroid, face.normal) ||
                            !PointOnSource(source.faces, face.a, face.normal) ||
                            !PointOnSource(source.faces, face.b, face.normal) ||
                            !PointOnSource(source.faces, face.c, face.normal)) continue;
                        if (match == null) match = source;
                        else if (match.wall != source.wall) { conflict = true; break; }
                    }
                    if (conflict) { ambiguous++; continue; }
                    if (match == null || match.wall == null) continue;
                    match.matched++;
                    if (!byWall.TryGetValue(match.wall, out HashSet<int> starts))
                        byWall.Add(match.wall, starts = new HashSet<int>());
                    starts.Add(i);
                }
                foreach (var entry in byWall.OrderBy(entry => entry.Key.root.name, StringComparer.Ordinal))
                {
                    batch.slices.Add(new SamuraiOcclusionMeshSplit.Slice { sourceSlot = slot, triangleStarts = entry.Value });
                    batch.owners.Add(entry.Key);
                }
            }
            return batch;
        }

        static bool PointOnSource(Face[] faces, Vector3 point, Vector3 normal)
        {
            foreach (Face face in faces)
                if (face.Contains(point, normal)) return true;
            return false;
        }
    }
}
