using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiCourtyardStoneGrounding
    {
        const string Action = "Seat garden stones in the ground";
        static readonly Vector2[] Footprint =
        {
            Vector2.zero, new Vector2(-.18f, 0f), new Vector2(.18f, 0f),
            new Vector2(0f, -.18f), new Vector2(0f, .18f)
        };

        [Serializable]
        sealed class Change
        {
            public string name, support;
            public float initialBurial, targetBurial, drop;
        }

        [Serializable]
        sealed class Report
        {
            public int gardenStones, talusStones, changed;
            public List<Change> adjustments = new List<Change>();
            public List<string> unresolved = new List<string>();
        }

        [MenuItem("Katana/Level/Seat Blender Courtyard Stones")]
        static void ApplyFromMenu() => Debug.Log(Apply());

        public static string Audit() => Run(false);
        public static string Apply() => Run(true);

        static string Run(bool apply)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != SamuraiBlenderAssemblyImport.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the Blender courtyard scene in Edit mode.");
            var dressing = scene.GetRootGameObjects().Single(g => g.name == SamuraiCourtyardDecorationSetup.RootName).transform;
            var terrain = dressing.GetComponentsInChildren<MeshFilter>()
                .Where(f => f.sharedMesh && SamuraiTreeGrounding.IsTerrain(f.name) &&
                    f.TryGetComponent(out MeshRenderer renderer) && renderer.enabled && !renderer.forceRenderingOff)
                .Select(f => new SamuraiTreeGrounding.Surface(f)).ToArray();
            if (terrain.Length < 2) throw new InvalidOperationException("Missing the visible courtyard rock surfaces.");

            var report = new Report();
            var moves = new List<(Transform node, float drop)>();
            var nodes = dressing.GetComponentsInChildren<Transform>();
            foreach (var parts in nodes.Where(t => t.name == "Editable components - 可调部件"))
            {
                var bedNode = parts.Find("01 Rock bed - 山石地基");
                if (!bedNode) continue;
                var bed = new SamuraiTreeGrounding.Surface(VisibleMesh(bedNode));
                foreach (var stone in parts.Cast<Transform>().Where(t =>
                    t.gameObject.activeInHierarchy && t.name.StartsWith("Stone ", StringComparison.Ordinal)))
                {
                    report.gardenStones++;
                    Plan(stone, bed, terrain, report, moves);
                }
            }
            foreach (var stone in nodes.Where(t => t.name.StartsWith("Talus stone ", StringComparison.Ordinal)))
            {
                report.talusStones++;
                Plan(stone, null, terrain, report, moves);
            }

            if (apply && moves.Count > 0)
            {
                Undo.IncrementCurrentGroup();
                int undo = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(Action);
                try
                {
                    Undo.RecordObjects(moves.Select(m => m.node).ToArray(), Action);
                    foreach (var move in moves)
                    {
                        move.node.position -= Vector3.up * move.drop;
                        if (PrefabUtility.IsPartOfPrefabInstance(move.node))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(move.node);
                    }
                    Physics.SyncTransforms();
                    EditorSceneManager.MarkSceneDirty(scene);
                    Undo.CollapseUndoOperations(undo);
                    report.changed = moves.Count;
                }
                catch
                {
                    Undo.RevertAllDownToGroup(undo);
                    throw;
                }
            }
            return JsonUtility.ToJson(report);
        }

        static MeshFilter VisibleMesh(Transform node) => node.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f =>
            f.sharedMesh && f.TryGetComponent(out MeshRenderer renderer) && renderer.enabled && !renderer.forceRenderingOff &&
            renderer.sharedMaterials.Any(m => m && !m.shader.name.EndsWith("/Outline", StringComparison.Ordinal)));

        static void Plan(Transform stone, SamuraiTreeGrounding.Surface bed, SamuraiTreeGrounding.Surface[] terrain,
            Report report, List<(Transform node, float drop)> moves)
        {
            var mesh = VisibleMesh(stone);
            var name = bed == null ? stone.name : stone.parent.parent.name + "/" + stone.name;
            if (!mesh)
            {
                report.unresolved.Add(name + ": no visible stone mesh");
                return;
            }
            Bounds bounds = mesh.GetComponent<MeshRenderer>().bounds;
            float height = bounds.size.y;
            if (height < .05f) return;
            Vector3 center = bounds.center;
            float maxDrop = height * 2.5f + .5f;
            var support = bed;
            if (support == null || !support.TryHeight(center, out _))
            {
                // The nearby rock at the current elevation wins where summit and distant ridge meshes overlap.
                float best = float.PositiveInfinity;
                foreach (var surface in terrain)
                {
                    if (!surface.TryHeight(center, out float y) || y > bounds.max.y + .15f) continue;
                    float difference = Mathf.Abs(center.y - y);
                    if (difference >= best) continue;
                    best = difference;
                    support = surface;
                }
            }
            if (support == null || !support.TryHeight(center, out float centerY))
            {
                report.unresolved.Add(name + ": no visible ground at its current XZ");
                return;
            }

            var ground = new List<float>(Footprint.Length) { centerY };
            foreach (Vector2 offset in Footprint.Skip(1))
            {
                var point = center + new Vector3(offset.x * bounds.size.x, 0f, offset.y * bounds.size.z);
                if (support.TryHeight(point, out float y)) ground.Add(y);
            }
            ground.Sort();
            float level = ground[ground.Count / 2];
            float burial = (level - bounds.min.y) / height;
            if (burial >= .42f) return;
            const float target = .50f;
            float drop = height * (target - burial);
            if (drop > maxDrop)
            {
                report.unresolved.Add(name + $": supporting terrain is too far below ({drop:F2} m)");
                return;
            }
            moves.Add((stone, drop));
            report.adjustments.Add(new Change { name = name, support = support.Name, initialBurial = burial, targetBurial = target, drop = drop });
        }
    }
}
