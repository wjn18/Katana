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
    public static class SamuraiGardenBedPlanting
    {
        const string TreeDetails = "21 Tree details - 树根与幼松";
        const string AssetParent = SamuraiComicStyleSetup.Root + "/BlenderAssembly";
        const string AssetFolder = AssetParent + "/RockBedPlanting";
        const string RepairFolder = AssetParent + "/PlacedGardenPlanting";
        const int StripVertices = 5;
        const int DetailVertices = 18;
        const float GroundTolerance = .037f;
        const float RootBurial = .016f;

        static readonly string[] TerrainPatches =
        {
            "Upper west lawn", "Upper hall foundation", "West garden foot", "Tea garden slope",
            "Upper south shoulder", "Lower west approach", "Lower east approach",
            "East garden shoulder", "East residence garden", "Tower garden foot"
        };

        [MenuItem("Katana/Level/Seat Garden Planting On Stone")]
        public static string Replant()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiBlenderAssemblyImport.ScenePath)
                throw new InvalidOperationException("Open the Blender courtyard scene in Edit mode.");

            var roots = scene.GetRootGameObjects();
            var dressing = roots.Single(node => node.name == SamuraiCourtyardDecorationSetup.RootName).transform;
            var details = dressing.Cast<Transform>().FirstOrDefault(node =>
                node.name == TreeDetails && node.Find("Understory pines - 石边幼松"));
            var gardens = dressing.GetComponentsInChildren<Transform>()
                .Where(node => node.name.StartsWith("Copper maple garden ", StringComparison.Ordinal) ||
                               node.name.StartsWith("Ink pine garden ", StringComparison.Ordinal))
                .Where(node => node.Find("Editable components - 可调部件/02 Ground cover - 地被"))
                .Select(node => new Bed(node, details)).ToArray();
            var plantedBeds = roots.SelectMany(root => root.GetComponentsInChildren<MeshFilter>())
                .Where(filter => filter.name.StartsWith("Raised garden bed ", StringComparison.Ordinal)).ToArray();
            if (gardens.Length != 7 || plantedBeds.Length != gardens.Length)
                throw new InvalidOperationException("Expected seven tree gardens and seven planted beds.");

            foreach (var planted in plantedBeds)
            {
                Vector3 center = planted.GetComponent<MeshRenderer>().bounds.center;
                var nearest = gardens.OrderBy(bed => FlatDistanceSquared(bed.Center, center)).First();
                if (nearest.Planting || FlatDistanceSquared(nearest.Center, center) > 2.25f)
                    throw new InvalidOperationException($"Could not match {planted.name} to its original bed.");
                nearest.Planting = planted;
            }
            if (gardens.Any(bed => !bed.Planting))
                throw new InvalidOperationException("Missing grass detail for a garden bed.");

            var edited = new List<(MeshFilter Filter, Mesh Mesh, string Name)>();
            var counts = new Counts();
            try
            {
                foreach (var bed in gardens)
                {
                    if (!IsGrounded(bed.Planting))
                        edited.Add((bed.Planting, ReplantGrass(bed, counts), bed.Name + " grass"));
                    if (!IsGrounded(bed.Details))
                        edited.Add((bed.Details, ReplantTreeDetails(bed, counts), bed.Name + " roots and leaves"));
                }
                if (edited.Count == 0) return "Garden planting is already seated on its stone beds.";

                if (!AssetDatabase.IsValidFolder(AssetFolder))
                    AssetDatabase.CreateFolder(AssetParent, "RockBedPlanting");
                Undo.IncrementCurrentGroup();
                int undo = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Seat garden planting on its rock");
                try
                {
                    foreach (var entry in edited)
                    {
                        var mesh = Save(entry.Mesh, entry.Name);
                        Undo.RecordObject(entry.Filter, "Set grounded garden mesh");
                        entry.Filter.sharedMesh = mesh;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(entry.Filter);
                    }
                    AssetDatabase.SaveAssets();
                    EditorSceneManager.MarkSceneDirty(scene);
                    Undo.CollapseUndoOperations(undo);
                }
                catch
                {
                    Undo.RevertAllDownToGroup(undo);
                    throw;
                }
                return $"Seated {counts.Blades} grass blades, {counts.Fronds} fern fronds, {counts.Roots} roots and " +
                       $"{counts.Leaves} leaves on seven stone beds. Removed {counts.Rejected} placements at stones or steep edges.";
            }
            finally
            {
                foreach (var entry in edited)
                    if (entry.Mesh && !EditorUtility.IsPersistent(entry.Mesh))
                        Object.DestroyImmediate(entry.Mesh);
            }
        }

        [MenuItem("Katana/Level/Repair Moved Garden Planting")]
        public static string RepairAfterPlacement()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiBlenderAssemblyImport.ScenePath)
                throw new InvalidOperationException("Open the Blender courtyard scene in Edit mode.");

            var roots = scene.GetRootGameObjects();
            var dressing = roots.Single(node => node.name == SamuraiCourtyardDecorationSetup.RootName).transform;
            var gardens = dressing.GetComponentsInChildren<Transform>()
                .Where(t => t.Find("Editable components - 可调部件/02 Ground cover - 地被") &&
                    (t.name.StartsWith("Copper maple garden ", StringComparison.Ordinal) ||
                     t.name.StartsWith("Ink pine garden ", StringComparison.Ordinal)))
                .ToDictionary(t => t, t => new Bed(t, null));
            var filters = roots.SelectMany(root => root.GetComponentsInChildren<MeshFilter>())
                .Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>() &&
                    (f.name.StartsWith("Raised garden bed ", StringComparison.Ordinal) ||
                     TerrainPatches.Contains(f.name))).ToArray();
            var terrain = dressing.GetComponentsInChildren<MeshCollider>()
                .Where(c => c.enabled && c.sharedMesh &&
                    c.sharedMesh.name.EndsWith("_Sanctuary_Crag", StringComparison.Ordinal)).ToArray();
            if (gardens.Count != 7 || filters.Length != 17 || terrain.Length != 2)
                throw new InvalidOperationException("Expected seven garden beds, seventeen planting patches and both crags.");

            Physics.SyncTransforms();
            var counts = new Counts();
            var repairs = new List<(MeshFilter Filter, Mesh Mesh)>();
            try
            {
                foreach (var filter in filters)
                {
                    Bed bed = null;
                    for (var owner = filter.transform.parent; owner; owner = owner.parent)
                        if (gardens.TryGetValue(owner, out bed)) break;
                    bool raised = filter.name.StartsWith("Raised garden bed ", StringComparison.Ordinal);
                    if (raised && bed == null)
                        throw new InvalidOperationException($"Missing garden composition for {filter.name}.");
                    GroundSample sample = (Vector3 at, out float y, out float slope) =>
                        SamplePlacedGround(at, bed, terrain, out y, out slope);
                    var replacement = RepairGrass(filter, raised ? bed : null, sample, counts);
                    if (replacement) repairs.Add((filter, replacement));
                }
                foreach (var bed in gardens.Values)
                {
                    GroundSample sample = (Vector3 at, out float y, out float slope) =>
                        SamplePlacedGround(at, bed, terrain, out y, out slope);
                    var replacement = RepairTreeDetails(bed, sample, counts);
                    if (replacement) repairs.Add((bed.Details, replacement));
                }

                if (repairs.Count == 0) return "All seventeen grass patches and seven root/litter meshes were already seated.";
                if (!AssetDatabase.IsValidFolder(RepairFolder))
                    AssetDatabase.CreateFolder(AssetParent, "PlacedGardenPlanting");
                Undo.IncrementCurrentGroup();
                int undo = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Seat moved garden planting");
                try
                {
                    foreach (var entry in repairs)
                    {
                        string asset = AssetDatabase.GenerateUniqueAssetPath(
                            RepairFolder + "/" + entry.Filter.name.Replace(' ', '_') + ".asset");
                        AssetDatabase.CreateAsset(entry.Mesh, asset);
                        Undo.RecordObject(entry.Filter, "Seat moved garden planting");
                        entry.Filter.sharedMesh = entry.Mesh;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(entry.Filter);
                    }
                    AssetDatabase.SaveAssets();
                    EditorSceneManager.MarkSceneDirty(scene);
                    Undo.CollapseUndoOperations(undo);
                }
                catch
                {
                    Undo.RevertAllDownToGroup(undo);
                    throw;
                }
                return $"Repaired {repairs.Count} planting meshes: {counts.Blades} grass blades, {counts.Fronds} fern fronds, " +
                    $"{counts.Roots} root spokes and {counts.Leaves} leaves. Removed {counts.Rejected} unsupported or obstructed pieces.";
            }
            finally
            {
                foreach (var entry in repairs)
                    if (entry.Mesh && !EditorUtility.IsPersistent(entry.Mesh))
                        Object.DestroyImmediate(entry.Mesh);
            }
        }

        delegate bool GroundSample(Vector3 point, out float y, out float slope);

        static bool SamplePlacedGround(Vector3 point, Bed bed, MeshCollider[] terrain, out float y, out float slope)
        {
            y = float.NegativeInfinity;
            slope = 0f;
            if (bed != null) bed.Rock.Sample(point, out y, out slope);
            foreach (var collider in terrain)
            {
                var b = collider.bounds;
                if (point.x < b.min.x || point.x > b.max.x || point.z < b.min.z || point.z > b.max.z)
                    continue;
                var ray = new Ray(new Vector3(point.x, b.max.y + 1f, point.z), Vector3.down);
                if (!collider.Raycast(ray, out var hit, b.size.y + 2f) || hit.point.y <= y) continue;
                y = hit.point.y;
                slope = hit.normal.y;
            }
            return !float.IsNegativeInfinity(y);
        }

        static Mesh RepairGrass(MeshFilter filter, Bed bed, GroundSample sample, Counts counts)
        {
            var points = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
            if (points.Length % StripVertices != 0)
                throw new InvalidOperationException($"Unrecognized blade mesh on {filter.name}.");
            var keep = new bool[points.Length];
            bool changed = false;
            for (int first = 0; first < points.Length;)
            {
                bool fern = IsFern(points, first);
                int end = first + (fern ? 9 : 1) * StripVertices;
                Vector3 foot = StripRoot(points, first);
                if (!sample(foot, out float height, out float slope) || slope < .56f ||
                    (bed != null && (bed.BlocksPlant(foot, height, fern ? .12f : .055f) ||
                    FlatDistanceSquared(foot, bed.TrunkFoot) < .32f * .32f)))
                {
                    counts.Rejected++;
                    changed = true;
                    first = end;
                    continue;
                }
                float left = height;
                float right = height;
                if (sample(points[first], out float footHeight, out _)) left = footHeight;
                if (sample(points[first + 1], out footHeight, out _)) right = footHeight;
                float rise = (left + right) * .5f - foot.y - RootBurial;
                bool misplaced = Mathf.Abs(points[first].y - left + RootBurial) > GroundTolerance ||
                                  Mathf.Abs(points[first + 1].y - right + RootBurial) > GroundTolerance;
                for (int i = first; i < end; i++) keep[i] = true;
                if (misplaced)
                {
                    for (int i = first; i < end; i++) points[i].y += rise;
                    points[first].y = left - RootBurial;
                    points[first + 1].y = right - RootBurial;
                    if (fern) counts.Fronds++;
                    else counts.Blades++;
                    changed = true;
                }
                first = end;
            }
            return changed ? BuildMesh(filter, points, keep) : null;
        }

        static Mesh RepairTreeDetails(Bed bed, GroundSample sample, Counts counts)
        {
            var filter = bed.Details;
            var mesh = filter.sharedMesh;
            var points = mesh.vertices.Select(filter.transform.TransformPoint).ToArray();
            int rootVertices = mesh.GetIndices(0).Length;
            if (points.Length % DetailVertices != 0 || rootVertices % DetailVertices != 0)
                throw new InvalidOperationException($"Unrecognized root or leaf mesh on {bed.Name}.");
            var offset = Vector3.zero;
            if (rootVertices > 0)
            {
                var join = Vector3.zero;
                for (int i = 0; i < rootVertices; i += DetailVertices) join += points[i + 1];
                join /= rootVertices / DetailVertices;
                offset = bed.TrunkFoot - join;
                offset.y = 0f;
                if (offset.sqrMagnitude < .25f * .25f) offset = Vector3.zero;
            }

            var keep = new bool[points.Length];
            var heights = new float[DetailVertices];
            bool changed = false;
            for (int start = 0; start < points.Length; start += DetailVertices)
            {
                bool root = start < rootVertices;
                Vector3 shift = root ? offset : Vector3.zero;
                bool supported = true;
                for (int i = 0; i < DetailVertices; i++)
                    if (!sample(points[start + i] + shift, out heights[i], out float slope) || (!root && slope < .55f))
                    {
                        supported = false;
                        break;
                    }
                if (!supported || (!root && (bed.BlocksPlant(points[start], heights[0], .04f) ||
                    FlatDistanceSquared(points[start], bed.TrunkFoot) < .24f * .24f)))
                {
                    counts.Rejected++;
                    changed = true;
                    continue;
                }

                for (int i = 0; i < DetailVertices; i++) keep[start + i] = true;
                if (root)
                {
                    float crest = Mathf.Clamp(Mathf.Max(points[start + 1].y - points[start].y,
                        bed.TrunkFoot.y - heights[1] + .06f), .11f, .34f);
                    float[] relief = { -.020f, crest, crest * .48f, -.020f, crest * .48f, -.026f,
                        crest, -.030f, -.018f, crest, -.018f, crest * .48f, -.026f, crest * .48f,
                        -.035f, crest * .48f, -.018f, -.035f };
                    if (shift == Vector3.zero &&
                        Mathf.Abs(points[start].y - heights[0] - relief[0]) <= GroundTolerance &&
                        Mathf.Abs(points[start + 14].y - heights[14] - relief[14]) <= GroundTolerance &&
                        points[start + 1].y >= bed.TrunkFoot.y - .02f) continue;
                    for (int i = 0; i < DetailVertices; i++)
                    {
                        points[start + i] += shift;
                        points[start + i].y = heights[i] + relief[i];
                    }
                    counts.Roots++;
                }
                else
                {
                    bool misplaced = false;
                    for (int i = 0; i < DetailVertices; i++)
                    {
                        float gap = points[start + i].y - heights[i];
                        misplaced |= gap < -.025f || gap > .05f;
                    }
                    if (!misplaced) continue;
                    for (int i = 0; i < DetailVertices; i++) points[start + i].y = heights[i] + (i % 3 == 0 ? .021f : .010f);
                    counts.Leaves++;
                }
                changed = true;
            }
            return changed ? BuildMesh(filter, points, keep) : null;
        }

        sealed class Counts
        {
            public int Blades, Fronds, Roots, Leaves, Rejected;
        }

        sealed class Bed
        {
            public readonly string Name;
            public readonly Surface Rock;
            public readonly Surface OldCover;
            public readonly Bounds[] Stones;
            public readonly Vector3 Center;
            public readonly Bounds Trunk;
            public readonly Vector3 TrunkFoot;
            public readonly MeshFilter Details;
            public MeshFilter Planting;

            public Bed(Transform garden, Transform details)
            {
                Name = garden.name;
                MeshFilter[] filters = garden.GetComponentsInChildren<MeshFilter>(true);
                MeshFilter FilterFor(string prefix) => filters.Single(filter =>
                    filter.transform.parent && filter.transform.parent.name.StartsWith(prefix, StringComparison.Ordinal) &&
                    filter.GetComponent<MeshRenderer>() && filter.sharedMesh);
                var cover = FilterFor("02 Ground cover");
                Center = cover.GetComponent<MeshRenderer>().bounds.center;
                OldCover = new Surface(cover);
                Rock = new Surface(FilterFor("01 Rock bed"));
                var trunk = FilterFor("03 Trunk");
                Trunk = trunk.GetComponent<MeshRenderer>().bounds;
                var trunkPoints = trunk.sharedMesh.vertices.Select(trunk.transform.TransformPoint).ToArray();
                float lowest = trunkPoints.Min(p => p.y);
                var basePoints = trunkPoints.Where(p => p.y < lowest + .07f).ToArray();
                TrunkFoot = basePoints.Aggregate(Vector3.zero, (sum, p) => sum + p) / basePoints.Length;
                Stones = filters.Where(filter => filter.transform.parent &&
                    filter.transform.parent.name.StartsWith("Stone ", StringComparison.Ordinal))
                    .Select(filter => filter.GetComponent<MeshRenderer>().bounds).ToArray();
                string detailName = Name + " - 根盘与落叶";
                var detail = garden.Find(detailName);
                if (!detail && details) detail = details.Find(detailName);
                if (!detail || !detail.TryGetComponent(out Details) || !Details.sharedMesh)
                    throw new InvalidOperationException($"Missing roots and litter for {Name}.");
            }

            public bool BlocksPlant(Vector3 point, float y, float margin)
            {
                return Stones.Any(stone => stone.max.y > y + .035f &&
                    point.x > stone.min.x - margin && point.x < stone.max.x + margin &&
                    point.z > stone.min.z - margin && point.z < stone.max.z + margin);
            }
        }

        sealed class Surface
        {
            readonly Vector3[] points;
            readonly int[] triangles;
            readonly float[] slopes;

            public Surface(MeshFilter filter)
            {
                var mesh = filter.sharedMesh;
                points = mesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                triangles = mesh.triangles;
                slopes = new float[triangles.Length / 3];
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = points[triangles[i]];
                    Vector3 normal = Vector3.Cross(points[triangles[i + 1]] - a, points[triangles[i + 2]] - a);
                    slopes[i / 3] = Mathf.Abs(normal.normalized.y);
                }
            }

            public bool Sample(Vector3 point, out float height, out float slope)
            {
                height = float.NegativeInfinity;
                slope = 0f;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = points[triangles[i]];
                    Vector3 ab = points[triangles[i + 1]] - a;
                    Vector3 ac = points[triangles[i + 2]] - a;
                    Vector3 offset = point - a;
                    float det = ab.x * ac.z - ab.z * ac.x;
                    if (Mathf.Abs(det) < .00001f) continue;
                    float u = (offset.x * ac.z - offset.z * ac.x) / det;
                    float v = (ab.x * offset.z - ab.z * offset.x) / det;
                    if (u < -.00001f || v < -.00001f || u + v > 1.00001f) continue;
                    float y = a.y + u * ab.y + v * ac.y;
                    if (y <= height) continue;
                    height = y;
                    slope = slopes[i / 3];
                }
                return !float.IsNegativeInfinity(height);
            }
        }

        static Mesh ReplantGrass(Bed bed, Counts counts)
        {
            MeshFilter filter = bed.Planting;
            var points = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
            if (points.Length % StripVertices != 0)
                throw new InvalidOperationException($"Unrecognized blade mesh on {bed.Name}.");
            var keep = new bool[points.Length];
            for (int first = 0; first < points.Length;)
            {
                bool fern = IsFern(points, first);
                int end = first + (fern ? 9 : 1) * StripVertices;
                Vector3 foot = StripRoot(points, first);
                float stoneClearance = fern ? .18f : .08f;
                if (!bed.Rock.Sample(foot, out float height, out float slope) || slope < .70f ||
                    bed.BlocksPlant(foot, height, stoneClearance) ||
                    FlatDistanceSquared(foot, bed.Trunk.center) < .55f * .55f)
                {
                    counts.Rejected++;
                    first = end;
                    continue;
                }
                float rise = height - foot.y - .014f;
                for (int i = first; i < end; i++)
                {
                    points[i].y += rise;
                    keep[i] = true;
                }
                // The narrow pair of vertices that forms the actual blade or stem foot seats separately.
                for (int i = first; i < first + 2; i++)
                    if (bed.Rock.Sample(points[i], out float footY, out _)) points[i].y = footY - .012f;
                if (fern) counts.Fronds++;
                else counts.Blades++;
                first = end;
            }
            return BuildMesh(filter, points, keep);
        }

        static Vector3 StripRoot(Vector3[] points, int index) => (points[index] + points[index + 1]) * .5f;

        static bool IsFern(Vector3[] points, int start)
        {
            if (start + 9 * StripVertices > points.Length) return false;
            // Fern fronds have four pairs of pinnae sharing identical insertion points after the stem.
            for (int pair = 0; pair < 4; pair++)
            {
                int first = start + (1 + pair * 2) * StripVertices;
                if ((StripRoot(points, first) - StripRoot(points, first + StripVertices)).sqrMagnitude > .00000001f)
                    return false;
            }
            return true;
        }

        static Mesh ReplantTreeDetails(Bed bed, Counts counts)
        {
            var filter = bed.Details;
            var source = filter.sharedMesh;
            var points = source.vertices.Select(filter.transform.TransformPoint).ToArray();
            int rootVertices = source.GetIndices(0).Length;
            if (points.Length % DetailVertices != 0 || rootVertices % DetailVertices != 0)
                throw new InvalidOperationException($"Unrecognized root or leaf mesh on {bed.Name}.");
            var keep = new bool[points.Length];
            for (int start = 0; start < points.Length; start += DetailVertices)
            {
                bool root = start < rootVertices;
                bool seated = root ? SeatRoot(bed, points, start) : SeatLeaf(bed, points, start);
                if (!seated)
                {
                    counts.Rejected++;
                    continue;
                }
                for (int i = start; i < start + DetailVertices; i++) keep[i] = true;
                if (root) counts.Roots++;
                else counts.Leaves++;
            }
            return BuildMesh(filter, points, keep);
        }

        static bool SeatRoot(Bed bed, Vector3[] points, int start)
        {
            Vector3 join = points[start + 1];
            if (!bed.OldCover.Sample(join, out float oldY, out _) ||
                !bed.Rock.Sample(join, out float newY, out _)) return false;
            float crest = Mathf.Max(Mathf.Clamp(join.y - oldY, .11f, .24f),
                bed.Trunk.min.y - newY + .10f);
            if (crest > .48f) return false;
            // These are the positions written by SamuraiCourtyardTreeDetailSetup.DetailMesh.Root.
            float[] relief =
            {
                -.020f, crest, crest * .48f, -.020f, crest * .48f, -.026f,
                crest, -.030f, -.018f, crest, -.018f, crest * .48f,
                -.026f, crest * .48f, -.035f, crest * .48f, -.018f, -.035f
            };
            for (int i = 0; i < DetailVertices; i++)
            {
                if (!bed.Rock.Sample(points[start + i], out float y, out _)) return false;
                points[start + i].y = y + relief[i];
            }
            return true;
        }

        static bool SeatLeaf(Bed bed, Vector3[] points, int start)
        {
            Vector3 center = points[start];
            if (!bed.Rock.Sample(center, out float height, out float slope) || slope < .62f ||
                bed.BlocksPlant(center, height, .07f) ||
                FlatDistanceSquared(center, bed.Trunk.center) < .32f * .32f) return false;
            float lowest = float.PositiveInfinity;
            for (int i = start; i < start + DetailVertices; i++) lowest = Mathf.Min(lowest, points[i].y);
            for (int i = start; i < start + DetailVertices; i++)
            {
                if (!bed.Rock.Sample(points[i], out float y, out _)) return false;
                points[i].y = y + .012f + points[i].y - lowest;
            }
            return true;
        }

        static Mesh BuildMesh(MeshFilter filter, Vector3[] points, bool[] keep)
        {
            var source = filter.sharedMesh;
            var normals = source.normals;
            var newPoints = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var remap = new int[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                if (!keep[i]) continue;
                remap[i] = newPoints.Count;
                newPoints.Add(filter.transform.InverseTransformPoint(points[i]));
                newNormals.Add(normals[i]);
            }
            if (newPoints.Count == 0)
                throw new InvalidOperationException($"No placements could be seated on {filter.name}.");
            var mesh = new Mesh { name = source.name + " on rock", subMeshCount = source.subMeshCount };
            mesh.SetVertices(newPoints);
            mesh.SetNormals(newNormals);
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var indices = source.GetIndices(sub);
                var triangles = new List<int>(indices.Length);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    if (!keep[indices[i]] || !keep[indices[i + 1]] || !keep[indices[i + 2]]) continue;
                    triangles.Add(remap[indices[i]]);
                    triangles.Add(remap[indices[i + 1]]);
                    triangles.Add(remap[indices[i + 2]]);
                }
                mesh.SetTriangles(triangles, sub, false);
            }
            if (filter.name.EndsWith(" - 根盘与落叶", StringComparison.Ordinal)) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static float FlatDistanceSquared(Vector3 a, Vector3 b) =>
            (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);

        static bool IsGrounded(MeshFilter filter)
        {
            string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
            return path.StartsWith(AssetFolder + "/", StringComparison.Ordinal) ||
                path.StartsWith(RepairFolder + "/", StringComparison.Ordinal);
        }

        static Mesh Save(Mesh mesh, string name)
        {
            string path = AssetFolder + "/" + name.Replace(' ', '_') + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
