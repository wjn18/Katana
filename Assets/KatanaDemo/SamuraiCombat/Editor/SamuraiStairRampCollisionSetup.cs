using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Separate walkable collision from the visible stair treads. No runtime component is required.</summary>
    public static class SamuraiStairRampCollisionSetup
    {
        public const string Root = SamuraiComicStyleSetup.Root + "/Collision/StairRamps";
        public const string GroupName = "Smooth ramp collision - 平滑斜坡碰撞";
        static readonly string[] Prefabs = {
            "Courtyard_EntranceStairs", "Courtyard_GrandPracticeHall", "Courtyard_EasternHall",
            "Courtyard_SideResidence", "Courtyard_Storehouse", "Courtyard_TeaPavilion"
        };
        static readonly string[] CompoundStairs = {
            "Upper hall approach - 上殿石径", "Eastern hall approach - 中殿石径",
            "Garden descending steps - 茶庭下行阶", "Storehouse descending gallery - 仓房外廊阶",
            "Eastern garden descending steps - 东园下行阶"
        };

        [MenuItem("Katana/Level/Use Smooth Stair Ramp Collisions")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            int prefabs = EnsurePrefabAssets(), stairs = 0;
            var dressing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Courtyard dressing");
            if (dressing)
            {
                var compound = dressing.transform.Find("07 Mountain compound - 合围山院");
                if (compound)
                    foreach (var t in compound.GetComponentsInChildren<Transform>(true).Where(t => CompoundStairs.Contains(t.name)))
                        stairs += EnsureCompoundStair(t);
            }
            if (prefabs + stairs > 0)
            {
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return "Smooth stair collision: " + prefabs + " Prefabs and " + stairs + " compound stairs updated. Existing ramps retained.";
        }

        public static int EnsurePrefabAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode only.");
            int count = 0;
            foreach (string file in Prefabs)
            {
                string path = SamuraiCourtyardArchitectureSetup.Root + "/Prefabs/" + file + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab || prefab.transform.Find(GroupName)) continue;
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (EnsurePrefabContents(contents) == 0) continue;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    count++;
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            return count;
        }

        /// <summary>Called by the architecture builders, and once for the known existing assets.</summary>
        public static int EnsurePrefabContents(GameObject root)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode only.");
            if (root.transform.Find(GroupName)) return 0;
            var colliders = root.GetComponentsInChildren<MeshCollider>(true);
            var main = colliders.FirstOrDefault(c => c.sharedMesh && c.sharedMesh.name == "Stairs_01_ContinuousSteps_Stone");
            if (main)
            {
                // The two flights retain their flat middle and upper landings. Each ramp meets the tread noses.
                var profile = new[] { P(-.35f, 0), P(4.55f, 2.8f), P(7.2f, 2.8f), P(8.8f, 3.6f), P(13.94634f, 3.6f) };
                var group = Child(root.transform, GroupName);
                Ramp(group, "01 Walking surface - 行走坡面", profile, -1.8f, 1.8f, -.2f, "Entrance_Center");
                Ramp(group, "02 Left stone edge - 左侧石沿", profile.Select(p => P(p.x, p.y + .14f)).ToArray(), -2, -1.8f, -.2f, "Entrance_Left");
                Ramp(group, "03 Right stone edge - 右侧石沿", profile.Select(p => P(p.x, p.y + .14f)).ToArray(), 1.8f, 2, -.2f, "Entrance_Right");
                foreach (var c in colliders.Where(c => c.sharedMesh && c.sharedMesh.name.StartsWith("Stairs_") && !c.sharedMesh.name.Contains("Facings"))) c.enabled = false;
                return 1;
            }
            string prefix = null;
            Vector2[] top = null;
            float width = 0;
            if (colliders.Any(c => c.sharedMesh && c.sharedMesh.name == "GrandHall_04_EntryStep_Stone"))
            { prefix = "GrandHall_"; width = 3.75f; top = new[] { P(-4.75f, 0), P(-3.81f, .3f), P(-3.51f, .3f) }; }
            else if (colliders.Any(c => c.sharedMesh && c.sharedMesh.name == "EasternHall_04_EntryStep_Stone"))
            { prefix = "EasternHall_"; width = 3.75f; top = new[] { P(-4.35f, 0), P(-3.41f, .3f), P(-3.11f, .3f) }; }
            else if (colliders.Any(c => c.sharedMesh && c.sharedMesh.name == "Residence_26_PorchStepLower_Stone"))
            { prefix = "Residence_"; width = 3.5f; top = new[] { P(-4.85f, 0), P(-4.16f, .27f), P(-3.355f, .51f), P(-3.105f, .51f) }; }
            else if (colliders.Any(c => c.sharedMesh && c.sharedMesh.name == "Storehouse_15_ApproachStepLower_Stone"))
            { prefix = "Storehouse_"; width = 2.75f; top = new[] { P(-3.38f, 0), P(-2.65f, .4f), P(-2.175f, .58f), P(-2.05f, .58f) }; }
            else if (colliders.Any(c => c.sharedMesh && c.sharedMesh.name == "Pavilion_18_ApproachStepLower_Stone"))
            { prefix = "Pavilion_"; width = 2.65f; top = new[] { P(-3.33f, 0), P(-2.61f, .33f), P(-2.14f, .42f), P(-1.89f, .42f) }; }
            if (top == null) return 0;
            var old = colliders.Where(c => c.sharedMesh && c.sharedMesh.name.StartsWith(prefix) && c.sharedMesh.name.Contains("Step")).ToArray();
            if (old.Length == 0) return 0;
            var entry = Child(root.transform, GroupName);
            Ramp(entry, "01 Entry to veranda - 踏步至廊台坡面", top, -width * .5f, width * .5f, -.2f, prefix + "Entry");
            foreach (var c in old) c.enabled = false;
            return 1;
        }

        public static int EnsureCompoundStair(Transform stair)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode only.");
            if (stair.Find(GroupName)) return 0;
            string prefix = stair.name.Replace(' ', '_');
            var old = stair.GetComponentsInChildren<MeshCollider>(true).FirstOrDefault(c => c.sharedMesh && c.sharedMesh.name.StartsWith(prefix));
            if (!old) return 0;
            var matrix = stair.worldToLocalMatrix * old.transform.localToWorldMatrix;
            var v = old.sharedMesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
            var indices = old.sharedMesh.triangles;
            var treadVertices = new List<Vector3>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = v[indices[i]]; var b = v[indices[i + 1]]; var c = v[indices[i + 2]];
                if (Vector3.Cross(b - a, c - a).normalized.y < .9999f) continue;
                treadVertices.Add(a); treadVertices.Add(b); treadVertices.Add(c);
            }
            var treads = treadVertices.GroupBy(p => Math.Round(p.y, 4))
                .Select(g => new { y = g.Average(p => p.y), start = g.Min(p => p.z), end = g.Max(p => p.z) }).OrderBy(t => t.start).ToArray();
            if (treads.Length < 2) throw new InvalidOperationException("Expected a stepped profile: " + stair.name);
            var first = treads[0]; var last = treads[treads.Length - 1];
            // Rising flights connect the leading noses; descending flights connect the trailing noses.
            // This remains above every tread without introducing a vertical jump at either endpoint.
            var top = last.y > first.y
                ? new[] { P(first.start, first.y), P(last.start, last.y), P(last.end, last.y) }
                : new[] { P(first.start, first.y), P(first.end, first.y), P(last.end, last.y) };
            var group = Child(stair, GroupName);
            Ramp(group, "01 Continuous walking slope - 连续行走坡面", top, treadVertices.Min(p => p.x), treadVertices.Max(p => p.x), v.Min(p => p.y), "Compound_" + prefix);
            old.enabled = false;
            return 1;
        }

        static Vector2 P(float z, float y) { return new Vector2(z, y); }
        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer; return go.transform;
        }
        static void Ramp(Transform parent, string name, Vector2[] top, float x0, float x1, float bottom, string key)
        {
            string fingerprint = x0.ToString("R", CultureInfo.InvariantCulture) + "|" + x1.ToString("R", CultureInfo.InvariantCulture) + "|" + bottom.ToString("R", CultureInfo.InvariantCulture)
                + "|" + string.Join(";", top.Select(p => p.x.ToString("R", CultureInfo.InvariantCulture) + "," + p.y.ToString("R", CultureInfo.InvariantCulture)));
            using (var sha = SHA256.Create()) key += "_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint))).Replace("-", "").Substring(0, 12);
            // Use a stable compact asset name even when the scene component has a bilingual label.
            key = new string(key.Where(c => c < 128 && (char.IsLetterOrDigit(c) || c == '_')).ToArray());
            string path = Root + "/" + key + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                if (!Directory.Exists(Root)) { Directory.CreateDirectory(Root); AssetDatabase.Refresh(); }
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                foreach (var p in top)
                {
                    vertices.Add(new Vector3(x0, bottom, p.x)); vertices.Add(new Vector3(x1, bottom, p.x));
                    vertices.Add(new Vector3(x0, p.y, p.x)); vertices.Add(new Vector3(x1, p.y, p.x));
                }
                for (int i = 0; i < top.Length - 1; i++)
                {
                    int k = i * 4;
                    foreach (int n in new[] { 2, 6, 7, 2, 7, 3, 0, 1, 5, 0, 5, 4, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 }) triangles.Add(k + n);
                }
                triangles.AddRange(new[] { 0, 2, 3, 0, 3, 1 });
                int end = (top.Length - 1) * 4;
                triangles.AddRange(new[] { end, end + 1, end + 3, end, end + 3, end + 2 });
                mesh = new Mesh { name = key }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, path);
            }
            var child = Child(parent, name);
            child.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
    }
}
