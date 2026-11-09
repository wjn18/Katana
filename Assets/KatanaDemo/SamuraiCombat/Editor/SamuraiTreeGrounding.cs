using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Seats planted timber against the authored rock surface without moving the planting in plan.</summary>
    public static class SamuraiTreeGrounding
    {
        const string Action = "Seat courtyard tree roots";
        const float GapTolerance = .025f;
        const float MaximumDrop = 15f;

        [Serializable]
        sealed class Entry
        {
            public string tree, surface;
            public float gap, drop;
        }

        [Serializable]
        sealed class Report
        {
            public int inspected, grounded, changed;
            public List<Entry> adjustments = new List<Entry>();
            public List<string> unresolved = new List<string>();
        }

        [MenuItem("Katana/Level/Seat Blender Courtyard Tree Roots")]
        static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Audit() => Run(false);
        public static string Apply() => Run(true);

        static string Run(bool apply)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != SamuraiBlenderAssemblyImport.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the Blender courtyard scene in Edit mode.");

            var roots = scene.GetRootGameObjects();
            var dressing = roots.Single(g => g.name == SamuraiCourtyardDecorationSetup.RootName).transform;
            var filters = roots.SelectMany(g => g.GetComponentsInChildren<MeshFilter>())
                .Where(f => f.sharedMesh && f.TryGetComponent(out MeshRenderer r) && r.enabled && !r.forceRenderingOff)
                .ToArray();
            var terrain = filters.Where(f => IsTerrain(f.name)).Select(f => new Surface(f)).ToArray();
            var backdrop = terrain.Where(s => s.Name.StartsWith("Backing_Mountain_", StringComparison.Ordinal)).ToArray();
            var exterior = terrain.Except(backdrop).ToArray();
            if (!exterior.Any(s => s.Name.Contains("Upper_Sanctuary_Crag")) ||
                !exterior.Any(s => s.Name.Contains("Lower_Sanctuary_Crag")))
                throw new InvalidOperationException("The two visible summit surfaces are required.");
            var courtFloors = roots.SelectMany(g => g.GetComponentsInChildren<Collider>())
                .Where(c => c.enabled && !c.isTrigger &&
                    (c.name == "Training Floor" || c.name == "Training Floor (1)")).ToArray();
            Physics.SyncTransforms();

            var report = new Report();
            var changes = new List<(Transform[] Pieces, float Drop)>();
            foreach (var body in filters.Where(f => IsPineMesh(f.sharedMesh.name)))
            {
                var tree = body.name.StartsWith("Surface - ", StringComparison.Ordinal) ? body.transform.parent : body.transform;
                if (!tree) continue;
                report.inspected++;
                if (!TryFoot(body, true, out var foot))
                {
                    report.unresolved.Add(tree.name + ": timber foot mesh missing");
                    continue;
                }
                bool onLedge = tree.name.StartsWith("Ledge pine", StringComparison.Ordinal);
                var support = PickSupport(onLedge ? backdrop : exterior, foot.Center);
                if (!onLedge)
                {
                    var garden = GardenAncestor(tree);
                    var localBed = garden ? BedFilter(garden) : null;
                    if (localBed)
                    {
                        var bed = new Surface(localBed);
                        if (bed.TryHeight(foot.Center, out float y) &&
                            (support == null || !support.TryHeight(foot.Center, out float groundY) || y > groundY))
                            support = bed;
                    }
                }
                Collider floor = null;
                if (!onLedge)
                {
                    foreach (var candidate in courtFloors)
                    {
                        if (!FloorHeight(candidate, foot.Center, out float y) ||
                            (support != null && support.TryHeight(foot.Center, out float groundY) && y <= groundY) ||
                            (floor && FloorHeight(floor, foot.Center, out float floorY) && y <= floorY)) continue;
                        floor = candidate;
                    }
                }
                if (!floor && support == null)
                {
                    report.unresolved.Add(tree.name + ": no support at its current XZ");
                    continue;
                }
                Plan(tree.name, new[] { tree }, foot, support, floor, report, changes);
            }

            foreach (var garden in dressing.GetComponentsInChildren<Transform>().Where(IsGarden))
            {
                report.inspected++;
                var parts = garden.Find("Editable components - 可调部件");
                var trunk = parts ? parts.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("03 Trunk", StringComparison.Ordinal)) : null;
                var body = trunk ? trunk.GetComponentsInChildren<MeshFilter>()
                    .FirstOrDefault(f => f.TryGetComponent(out MeshRenderer r) && r.enabled) : null;
                var bed = BedFilter(garden);
                if (!body || !bed || !TryFoot(body, false, out var foot))
                {
                    report.unresolved.Add(garden.name + ": missing tree or stone bed");
                    continue;
                }
                var pieces = parts.Cast<Transform>().Where(t => t == trunk ||
                    t.name.StartsWith("Branch ", StringComparison.Ordinal) ||
                    t.name.StartsWith("Canopy ", StringComparison.Ordinal) ||
                    t.name.StartsWith("Crown ", StringComparison.Ordinal)).ToArray();
                Plan(garden.name, pieces, foot, new Surface(bed), null, report, changes);
            }

            if (apply && changes.Count > 0)
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(Action);
                try
                {
                    foreach (var change in changes)
                    {
                        Undo.RecordObjects(change.Pieces, Action);
                        foreach (var piece in change.Pieces)
                        {
                            piece.position -= Vector3.up * change.Drop;
                            if (PrefabUtility.IsPartOfPrefabInstance(piece))
                                PrefabUtility.RecordPrefabInstancePropertyModifications(piece);
                        }
                    }
                    report.changed = changes.Count;
                    Physics.SyncTransforms();
                    EditorSceneManager.MarkSceneDirty(scene);
                    Undo.CollapseUndoOperations(group);
                }
                catch
                {
                    Undo.RevertAllDownToGroup(group);
                    throw;
                }
            }
            return JsonUtility.ToJson(report);
        }

        internal static bool IsTerrain(string name) =>
            name.StartsWith("Surface - Landscape_", StringComparison.Ordinal) ||
            name == "Surface - Upper_Sanctuary_Crag" || name == "Surface - Lower_Sanctuary_Crag" ||
            name.StartsWith("Backing_Mountain_", StringComparison.Ordinal);

        static bool IsPineMesh(string name) =>
            name == "Pine_A" || name == "Pine_B" || name == "Pine_C" ||
            name.EndsWith(" - split bough crown", StringComparison.Ordinal);

        static bool IsGarden(Transform t) =>
            (t.name.StartsWith("Copper maple garden ", StringComparison.Ordinal) ||
                t.name.StartsWith("Ink pine garden ", StringComparison.Ordinal)) &&
            t.Find("Editable components - 可调部件");

        static Transform GardenAncestor(Transform t)
        {
            for (var p = t.parent; p; p = p.parent)
                if (IsGarden(p)) return p;
            return null;
        }

        static MeshFilter BedFilter(Transform garden)
        {
            var part = garden.Find("Editable components - 可调部件/01 Rock bed - 山石地基");
            return part ? part.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f =>
                f.sharedMesh && f.TryGetComponent(out MeshRenderer r) && r.enabled && !r.forceRenderingOff) : null;
        }

        sealed class Foot
        {
            public Vector3 Center;
            public Vector3[] Ring;
        }

        static bool TryFoot(MeshFilter body, bool barkOnly, out Foot foot)
        {
            foot = null;
            var mesh = body.sharedMesh;
            if (!mesh || mesh.vertexCount < 3) return false;
            var timber = body.GetComponent<MeshRenderer>().sharedMaterials;
            using var meshData = MeshUtility.AcquireReadOnlyMeshData(mesh);
            var data = meshData[0];
            using var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
            data.GetVertices(vertices);
            var world = body.transform.localToWorldMatrix;
            var points = new HashSet<Vector3>();
            for (int slot = 0; slot < data.subMeshCount; slot++)
            {
                if (barkOnly && (slot >= timber.Length || !timber[slot] ||
                    !timber[slot].name.StartsWith("Old_honey_timber", StringComparison.Ordinal))) continue;
                var submesh = data.GetSubMesh(slot);
                using var indices = new NativeArray<int>(submesh.indexCount, Allocator.Temp);
                data.GetIndices(indices, slot, true);
                foreach (int index in indices) points.Add(world.MultiplyPoint3x4(vertices[index]));
            }
            if (points.Count == 0) return false;
            float minimum = points.Min(p => p.y);
            float ringHeight = Mathf.Clamp((points.Max(p => p.y) - minimum) * .025f, .045f, .12f);
            var ring = points.Where(p => p.y <= minimum + ringHeight).ToArray();
            var center = new Vector3(ring.Average(p => p.x), minimum, ring.Average(p => p.z));
            foot = new Foot { Center = center, Ring = ring };
            return true;
        }

        static Surface PickSupport(IEnumerable<Surface> surfaces, Vector3 point)
        {
            Surface nearest = null;
            float highest = float.NegativeInfinity;
            foreach (var candidate in surfaces)
            {
                if (!candidate.TryHeight(point, out float y) || y <= highest) continue;
                highest = y;
                nearest = candidate;
            }
            return nearest;
        }

        static bool FloorHeight(Collider floor, Vector3 point, out float height)
        {
            var ray = new Ray(new Vector3(point.x, floor.bounds.max.y + .3f, point.z), Vector3.down);
            if (floor.Raycast(ray, out var hit, floor.bounds.size.y + .6f) && hit.normal.y > .55f)
            {
                height = hit.point.y;
                return true;
            }
            height = 0;
            return false;
        }

        static void Plan(string name, Transform[] pieces, Foot foot, Surface surface, Collider floor,
            Report report, List<(Transform[] Pieces, float Drop)> changes)
        {
            bool Sample(Vector3 point, out float y) =>
                floor ? FloorHeight(floor, point, out y) : surface.TryHeight(point, out y);
            if (!Sample(foot.Center, out float centerY))
            {
                report.unresolved.Add(name + ": trunk is outside the supporting surface");
                return;
            }
            float gap = foot.Center.y - centerY;
            // Check the low edge of a tilted cut as well as its centre. A root can
            // touch uphill and still show a visible underside from the trail.
            foreach (var point in foot.Ring)
                if (Sample(point, out float y)) gap = Mathf.Max(gap, point.y - y);
            if (gap <= GapTolerance)
            {
                report.grounded++;
                return;
            }
            if (gap > MaximumDrop)
            {
                report.unresolved.Add($"{name}: support is {gap:F2} m below the trunk");
                return;
            }
            float diameter = Mathf.Max(foot.Ring.Max(p => p.x) - foot.Ring.Min(p => p.x),
                foot.Ring.Max(p => p.z) - foot.Ring.Min(p => p.z));
            float drop = gap + Mathf.Clamp(.25f * diameter, .035f, .14f);
            report.adjustments.Add(new Entry { tree = name, surface = floor ? floor.name : surface.Name, gap = gap, drop = drop });
            changes.Add((pieces, drop));
        }

        internal sealed class Surface
        {
            public readonly string Name;
            readonly Bounds bounds;
            readonly Vector3[] vertices;
            readonly List<int[]> triangles = new List<int[]>();

            public Surface(MeshFilter filter)
            {
                Name = filter.name;
                bounds = filter.GetComponent<MeshRenderer>().bounds;
                var mesh = filter.sharedMesh;
                using var meshData = MeshUtility.AcquireReadOnlyMeshData(mesh);
                var data = meshData[0];
                using var source = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
                data.GetVertices(source);
                vertices = new Vector3[source.Length];
                var matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(source[i]);
                for (int slot = 0; slot < data.subMeshCount; slot++)
                {
                    var submesh = data.GetSubMesh(slot);
                    if (submesh.topology != MeshTopology.Triangles) continue;
                    using var indices = new NativeArray<int>(submesh.indexCount, Allocator.Temp);
                    data.GetIndices(indices, slot, true);
                    triangles.Add(indices.ToArray());
                }
            }

            public bool TryHeight(Vector3 point, out float highest)
            {
                highest = float.NegativeInfinity;
                var min = bounds.min;
                var max = bounds.max;
                if (point.x < min.x || point.x > max.x || point.z < min.z || point.z > max.z) return false;
                foreach (var indices in triangles)
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a = vertices[indices[i]];
                    var b = vertices[indices[i + 1]];
                    var c = vertices[indices[i + 2]];
                    if (point.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) || point.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x)) ||
                        point.z < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) || point.z > Mathf.Max(a.z, Mathf.Max(b.z, c.z))) continue;
                    var ab = b - a;
                    var ac = c - a;
                    float determinant = ab.x * ac.z - ab.z * ac.x;
                    if (Mathf.Abs(determinant) < .000001f) continue;
                    float u = ((point.x - a.x) * ac.z - (point.z - a.z) * ac.x) / determinant;
                    float v = (ab.x * (point.z - a.z) - ab.z * (point.x - a.x)) / determinant;
                    if (u < -.00001f || v < -.00001f || u + v > 1.00001f) continue;
                    highest = Mathf.Max(highest, a.y + u * ab.y + v * ac.y);
                }
                return !float.IsNegativeInfinity(highest);
            }
        }
    }
}
