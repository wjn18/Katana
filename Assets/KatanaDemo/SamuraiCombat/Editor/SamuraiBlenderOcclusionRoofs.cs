using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Reconnects assembled roof surfaces to their architectural pieces after Blender merging.</summary>
    internal static class SamuraiBlenderOcclusionRoofs
    {
        const string Folder = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/BlenderAssembly/Occlusion";
        const string CreaseName = "Architectural crease ink";
        const float PositionTolerance = .00035f;
        const float CellSize = .001f;

        sealed class Piece
        {
            public readonly Transform assembly;
            public readonly string name;
            public readonly MeshRenderer[] sources;
            public readonly Collider[] blockers;
            public readonly List<SamuraiOcclusionGroup.Surface> surfaces = new List<SamuraiOcclusionGroup.Surface>();
            public Matrix4x4 heightSpace;
            public float minHeight = float.NegativeInfinity;
            public float maxHeight = float.PositiveInfinity;

            public Piece(Transform assembly, string name, IEnumerable<MeshRenderer> sources)
            {
                this.assembly = assembly;
                this.name = "Camera fade - " + name;
                this.sources = sources.Distinct().ToArray();
                blockers = this.sources.SelectMany(r => r.GetComponents<Collider>())
                    .Where(c => c.enabled && !c.isTrigger).Distinct().ToArray();
            }

            public bool Includes(Vector3 a, Vector3 b, Vector3 c)
            {
                if (float.IsNegativeInfinity(minHeight) && float.IsPositiveInfinity(maxHeight)) return true;
                float y = heightSpace.MultiplyPoint3x4((a + b + c) / 3f).y;
                return y >= minHeight && y < maxHeight;
            }
        }

        readonly struct Vertex
        {
            public readonly Vector3 position;
            public readonly Vector3 normal;

            public Vertex(Vector3 position, Vector3 normal)
            {
                this.position = position;
                this.normal = normal;
            }
        }

        sealed class Part
        {
            public readonly MeshRenderer renderer;
            public readonly Vector3[] vertices;
            public readonly Vector3[] normals;
            public readonly Bounds bounds;
            readonly Dictionary<Vector3Int, List<Vertex>> buckets;

            public Part(MeshRenderer renderer, bool buildIndex)
            {
                this.renderer = renderer;
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                vertices = mesh.vertices;
                normals = mesh.normals;
                bool haveNormals = normals.Length == vertices.Length;
                var matrix = renderer.localToWorldMatrix;
                var normalMatrix = matrix.inverse.transpose;
                if (buildIndex) buckets = new Dictionary<Vector3Int, List<Vertex>>();
                Bounds worldBounds = default;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 position = matrix.MultiplyPoint3x4(vertices[i]);
                    vertices[i] = position;
                    if (haveNormals) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                    if (i == 0) worldBounds = new Bounds(position, Vector3.zero);
                    else worldBounds.Encapsulate(position);
                    if (!buildIndex) continue;
                    Vector3Int cell = Cell(position);
                    if (!buckets.TryGetValue(cell, out var candidates))
                        buckets.Add(cell, candidates = new List<Vertex>());
                    candidates.Add(new Vertex(position, haveNormals ? normals[i] : Vector3.zero));
                }
                worldBounds.Expand(PositionTolerance * 2f);
                bounds = worldBounds;
            }

            public bool Matches(Vector3 position, Vector3 normal)
            {
                if (!bounds.Contains(position)) return false;
                Vector3Int low = Cell(position - Vector3.one * PositionTolerance);
                Vector3Int high = Cell(position + Vector3.one * PositionTolerance);
                for (int x = low.x; x <= high.x; x++)
                    for (int y = low.y; y <= high.y; y++)
                        for (int z = low.z; z <= high.z; z++)
                        {
                            if (!buckets.TryGetValue(new Vector3Int(x, y, z), out var candidates)) continue;
                            foreach (var candidate in candidates)
                                if ((candidate.position - position).sqrMagnitude <= PositionTolerance * PositionTolerance &&
                                    (normal == Vector3.zero || candidate.normal == Vector3.zero ||
                                     Vector3.Dot(candidate.normal, normal) > .9975f))
                                    return true;
                        }
                return false;
            }

            static Vector3Int Cell(Vector3 position) => Vector3Int.FloorToInt(position / CellSize);
        }

        readonly struct Edge : IEquatable<Edge>
        {
            readonly Vector3Int a, b;

            public Edge(Vector3 first, Vector3 second)
            {
                var x = Vector3Int.RoundToInt(first * 1000f);
                var y = Vector3Int.RoundToInt(second * 1000f);
                if (x.x < y.x || x.x == y.x && (x.y < y.y || x.y == y.y && x.z <= y.z))
                {
                    a = x;
                    b = y;
                }
                else
                {
                    a = y;
                    b = x;
                }
            }

            public bool Equals(Edge other) => a == other.a && b == other.b;
            public override bool Equals(object value) => value is Edge edge && Equals(edge);
            public override int GetHashCode() => unchecked(a.GetHashCode() * 397 ^ b.GetHashCode());
        }

        sealed class Slice
        {
            public Piece piece;
            public MeshRenderer renderer;
            public int sourceSlot;
            public HashSet<int> starts;
            public Material replacement;
        }

        internal static string Install(bool dryRun = false)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SamuraiBlenderAssemblyImport.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open BlenderAssembly in Edit mode.");

            var all = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(node => node.gameObject.activeInHierarchy).ToArray();
            var pieces = CollectPieces(all);
            int existing = pieces.Count(piece => piece.assembly.Find(piece.name));
            foreach (var piece in pieces.Where(item => item.assembly.Find(item.name)))
            {
                var group = piece.assembly.Find(piece.name).GetComponent<SamuraiOcclusionGroup>();
                if (!group || !group.useVisualProbes || group.surfaces.Length == 0)
                    throw new InvalidOperationException("An existing roof group is incomplete: " + piece.assembly.name + "/" + piece.name);
            }
            var pending = pieces.Where(piece => !piece.assembly.Find(piece.name)).ToArray();
            if (pending.Length == 0) return $"{existing} roof groups already installed.";

            var slices = new List<Slice>();
            var creaseMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/RidgeLodge_FadingInk.mat");
            if (!creaseMaterial || creaseMaterial.FindPass("SamuraiOcclusionForeground") < 0)
                throw new InvalidOperationException("The validated roof crease fade material is unavailable.");
            foreach (var piece in pending)
                MatchPiece(piece, creaseMaterial, slices);

            var perRenderer = slices.GroupBy(slice => slice.renderer).ToArray();
            foreach (var entry in perRenderer)
            {
                var claimed = new HashSet<(int, int)>();
                foreach (var slice in entry)
                    if (slice.starts.Any(index => !claimed.Add((slice.sourceSlot, index))))
                        throw new InvalidOperationException("Overlapping architectural roof slices on " + entry.Key.name);
            }

            if (!dryRun && !AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/BlenderAssembly", "Occlusion");
            foreach (var entry in perRenderer)
            {
                var renderer = entry.Key;
                var items = entry.ToArray();
                var partitions = items.Select(slice => new SamuraiOcclusionMeshSplit.Slice
                {
                    sourceSlot = slice.sourceSlot,
                    triangleStarts = slice.starts
                }).ToArray();
                string key = new string((renderer.transform.parent.name + "_" + renderer.name)
                    .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                var assigned = SamuraiOcclusionMeshSplit.Apply(renderer, partitions, Folder + "/Roofs_" + key + ".asset", dryRun);
                var materials = dryRun ? null : renderer.sharedMaterials;
                bool changedMaterial = false;
                for (int i = 0; i < items.Length; i++)
                {
                    var slice = items[i];
                    slice.piece.surfaces.Add(new SamuraiOcclusionGroup.Surface(renderer, assigned[i]));
                    if (dryRun || !slice.replacement) continue;
                    materials[assigned[i]] = slice.replacement;
                    changedMaterial = true;
                }
                if (!changedMaterial) continue;
                Undo.RecordObject(renderer, "Restore the selected crease color in the fade pass");
                renderer.sharedMaterials = materials;
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (mesh.normals.Length != mesh.vertexCount)
                {
                    mesh.RecalculateNormals();
                    EditorUtility.SetDirty(mesh);
                    AssetDatabase.SaveAssetIfDirty(mesh);
                }
            }
            if (!dryRun)
            {
                foreach (var piece in pending)
                {
                    var owner = new GameObject(piece.name);
                    Undo.RegisterCreatedObjectUndo(owner, "Add roof camera fade group");
                    owner.transform.SetParent(piece.assembly, false);
                    var group = Undo.AddComponent<SamuraiOcclusionGroup>(owner);
                    group.blockers = piece.blockers;
                    group.surfaces = piece.surfaces.ToArray();
                    group.useVisualProbes = true;
                    EditorUtility.SetDirty(group);
                }
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return $"{(dryRun ? "Validated" : "Installed")} {pending.Length} roof groups, {slices.Count} material slices, " +
                   $"{slices.Sum(slice => slice.starts.Count)} triangles, {existing} existing retained.\n" +
                   string.Join("\n", pending.Select(piece => $"{piece.assembly.name}/{piece.name}: " +
                       $"{piece.sources.Length} original parts, {piece.surfaces.Count} slots, {piece.blockers.Length} existing colliders."));
        }

        static List<Piece> CollectPieces(Transform[] all)
        {
            var result = new List<Piece>();
            Transform Visual(string name) => all.Single(node => node.name == name &&
                node.Cast<Transform>().Any(child => child.name.StartsWith("B", StringComparison.Ordinal) &&
                    child.name.EndsWith("_Surface", StringComparison.Ordinal)));

            Transform Source(string name, Func<Transform, bool> condition = null)
            {
                var matches = all.Where(node => NameStarts(node, name) && !node.Cast<Transform>().Any(child =>
                    child.name.StartsWith("B", StringComparison.Ordinal) && child.name.EndsWith("_Surface", StringComparison.Ordinal)) &&
                    (condition == null || condition(node))).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Expected one roof source {name}, found {matches.Length}.");
                return matches[0];
            }

            IEnumerable<MeshRenderer> Parts(Transform root) => root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.gameObject.activeInHierarchy && renderer.TryGetComponent<MeshFilter>(out var mesh) && mesh.sharedMesh);

            void Add(string assembly, string label, IEnumerable<MeshRenderer> sources)
            {
                var piece = new Piece(Visual(assembly), label, sources);
                if (piece.sources.Length == 0)
                    throw new InvalidOperationException("Missing authored roof surfaces: " + assembly + "/" + label);
                result.Add(piece);
            }

            Transform Child(Transform root, string prefix)
            {
                var matches = root.Cast<Transform>().Where(node => NameStarts(node, prefix)).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Expected one {prefix} under {root.name}, found {matches.Length}.");
                return matches[0];
            }
            bool HasChild(Transform root, string prefix) => root.Cast<Transform>().Any(node => NameStarts(node, prefix));

            Add("Mountain Storehouse", "roof", Parts(Child(Source("09 Mountain storehouse", node => HasChild(node, "05 Roof")), "05 Roof")));
            Add("Tea Pavilion", "roof", Parts(Child(Source("10 Tea pavilion", node => HasChild(node, "05 Roof")), "05 Roof")));

            var gate = Child(Source("06 Entry gate", node => HasChild(node, "Editable components")), "Editable components");
            Add("Main Entrance Gate", "roof and eave beam",
                Parts(Child(gate, "05 Roof")).Concat(Parts(Child(gate, "04 Eave beam"))));

            var residence = Source("09 Side residence", node => HasChild(node, "05 Roof"));
            var residenceRoof = Child(residence, "05 Roof");
            Add("East Residence", "main roof", Parts(residenceRoof).Where(renderer => !AncestorStarts(renderer.transform, "22_PorchRoof")));
            Add("East Residence", "porch roof", Parts(Child(residenceRoof, "22_PorchRoof"))
                .Concat(Parts(residence).Where(renderer => AncestorStarts(renderer.transform, "23_PorchFascia"))));

            var eliteRoof = Source("Roof silhouette");
            Add("Elite1 Grand Hall", "upper roof", Parts(eliteRoof).Where(renderer =>
                !AncestorStarts(renderer.transform, "Lower eave rafters") && !AncestorStarts(renderer.transform, "Continuous lower eave")));
            Add("Elite1 Grand Hall", "waist eave", Parts(Child(eliteRoof, "Lower eave rafters"))
                .Concat(Parts(Child(eliteRoof, "Continuous lower eave"))));

            var lowerRoof = Source("Two layered roofs");
            Add("Lower Grand Hall", "upper roof", Parts(lowerRoof).Where(renderer =>
                !AncestorStarts(renderer.transform, "LowerHall_TwoStorey_Continuous_Waist_Eave")));
            Add("Lower Grand Hall", "waist eave", Parts(Child(lowerRoof, "LowerHall_TwoStorey_Continuous_Waist_Eave")));

            void AddCanopy(string assembly, string root, string shell, string ridge)
            {
                var source = Source(root);
                Add(assembly, "roof", Parts(Child(source, shell)).Concat(Parts(Child(source, ridge))));
            }
            AddCanopy("Elite1 Hall Passage", "02 Recessed lodge link", "EliteHallLink_Continuous_Tiled_Roof", "EliteHallLink_Ochre_Ridge_Inlay");
            AddCanopy("Curved Mountain Gallery", "05 Curved mountain gallery",
                "LowerHall_CurvedMountainLink_TiledRoof", "LowerHall_CurvedMountainLink_Ridge");

            foreach (string portal in new[] { "Upper bridge portal", "Lower bridge portal", "Tea garden side gate" })
                AddCanopy(portal, portal, "07_ContinuousThickRoof_Roof", "08_Ridge_Gold");

            foreach (string room in new[] { "West prayer room", "West store room", "East guard room", "East study room" })
                Add(room, "roof and ceiling", Parts(Child(Source(room, node => HasChild(node, "05 Roof")), "05 Roof")));
            foreach (string walk in new[] { "West covered walk -6", "West covered walk 5", "East covered walk 0", "East covered walk 7" })
                Add(walk, "roof", Parts(Child(Source(walk, node => HasChild(node, "05 Roof")), "05 Roof")));

            Transform towerRoof = Source("HallLandmarkTower_Roof");
            var tower = Visual("Landmark Pagoda");
            // The four disconnected roof shells share one original material batch.
            // These values lie in the open spaces between the authored tiers.
            float[] split = { float.NegativeInfinity, 7.1f, 11.1f, 14.7f, float.PositiveInfinity };
            for (int i = 0; i < 4; i++)
                result.Add(new Piece(tower, "roof tier " + (i + 1), Parts(towerRoof))
                {
                    heightSpace = towerRoof.worldToLocalMatrix,
                    minHeight = split[i],
                    maxHeight = split[i + 1]
                });

            return result;
        }

        static bool NameStarts(Transform node, string prefix) => node.name.StartsWith(prefix, StringComparison.Ordinal);

        static bool AncestorStarts(Transform node, string prefix)
        {
            for (Transform item = node; item; item = item.parent)
                if (NameStarts(item, prefix)) return true;
            return false;
        }

        static void MatchPiece(Piece piece, Material creaseMaterial, List<Slice> slices)
        {
            var originals = piece.sources.Select(renderer => new Part(renderer, true)).ToArray();
            var matched = new HashSet<MeshRenderer>();
            var edges = new HashSet<Edge>();
            int before = slices.Count;
            var renderers = piece.assembly.GetComponentsInChildren<MeshRenderer>().Where(renderer =>
                renderer.enabled && renderer.name.StartsWith("B", StringComparison.Ordinal) &&
                (renderer.name.EndsWith("_Surface", StringComparison.Ordinal) || renderer.name.EndsWith("_Ink", StringComparison.Ordinal)));
            foreach (var renderer in renderers)
            {
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                var part = new Part(renderer, false);
                var sourceParts = originals.Where(source => source.bounds.Intersects(part.bounds)).ToArray();
                if (sourceParts.Length == 0) continue;
                var matches = new Dictionary<Part, bool[]>();
                foreach (var source in sourceParts)
                    matches.Add(source, part.vertices.Select((vertex, i) => source.Matches(vertex,
                        part.normals.Length == part.vertices.Length ? part.normals[i] : Vector3.zero)).ToArray());

                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < mesh.subMeshCount; slot++)
                {
                    if (!materials[slot] || materials[slot].FindPass("SamuraiOcclusionForeground") < 0) continue;
                    var triangles = mesh.GetTriangles(slot);
                    var selected = new HashSet<int>();
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                        if (!piece.Includes(part.vertices[a], part.vertices[b], part.vertices[c])) continue;
                        bool found = false;
                        foreach (var pair in matches)
                        {
                            if (!pair.Value[a] || !pair.Value[b] || !pair.Value[c]) continue;
                            found = true;
                            matched.Add(pair.Key.renderer);
                        }
                        if (!found) continue;
                        selected.Add(i);
                        if (!renderer.name.EndsWith("_Surface", StringComparison.Ordinal)) continue;
                        edges.Add(new Edge(part.vertices[a], part.vertices[b]));
                        edges.Add(new Edge(part.vertices[b], part.vertices[c]));
                        edges.Add(new Edge(part.vertices[c], part.vertices[a]));
                    }
                    if (selected.Count > 0)
                        slices.Add(new Slice { piece = piece, renderer = renderer, sourceSlot = slot, starts = selected });
                }
            }

            var missing = originals.Where(source => !matched.Contains(source.renderer)).Select(source => source.renderer.name).ToArray();
            if (slices.Count == before || missing.Length > 0)
                throw new InvalidOperationException($"Roof provenance mismatch at {piece.assembly.name}/{piece.name}: " +
                    string.Join(", ", missing));

            var crease = piece.assembly.Cast<Transform>().SingleOrDefault(node => NameStarts(node, CreaseName));
            if (crease)
                MatchCreases(piece, crease.GetComponent<MeshRenderer>(), edges, creaseMaterial, slices);
        }

        static void MatchCreases(Piece piece, MeshRenderer renderer, HashSet<Edge> edges, Material fade, List<Slice> slices)
        {
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var material = renderer.sharedMaterials[0];
            if (mesh.vertexCount % 8 != 0 || !material.HasProperty("_BaseColor") ||
                material.GetColor("_BaseColor") != fade.GetColor("_BaseColor"))
                throw new InvalidOperationException("Unexpected crease stroke layout or color: " + piece.assembly.name);
            var vertices = mesh.vertices;
            var matrix = renderer.localToWorldMatrix;
            var matches = new bool[vertices.Length / 8];
            for (int i = 0; i < matches.Length; i++)
            {
                Vector3 a = Vector3.zero, b = Vector3.zero;
                for (int side = 0; side < 8; side += 2)
                {
                    a += vertices[i * 8 + side];
                    b += vertices[i * 8 + side + 1];
                }
                matches[i] = edges.Contains(new Edge(matrix.MultiplyPoint3x4(a * .25f), matrix.MultiplyPoint3x4(b * .25f)));
            }
            var indices = mesh.GetTriangles(0);
            var selected = new HashSet<int>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                int tube = indices[i] / 8;
                if (indices[i + 1] / 8 != tube || indices[i + 2] / 8 != tube)
                    throw new InvalidOperationException("A crease triangle crosses stroke ownership.");
                if (matches[tube]) selected.Add(i);
            }
            if (selected.Count > 0)
                slices.Add(new Slice { piece = piece, renderer = renderer, sourceSlot = 0, starts = selected, replacement = fade });
        }
    }
}
