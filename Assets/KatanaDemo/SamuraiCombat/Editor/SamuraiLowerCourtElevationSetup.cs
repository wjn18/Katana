using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OCDesign.LookDev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiLowerCourtElevationSetup
    {
        public const string RootName = "16 Lower courtyard grade - 下院高程与接坡";
        const string Root = SamuraiComicStyleSetup.Root + "/LowerCourtGrade";
        const float Delta = -2.5f;

        [MenuItem("Katana/Level/Lower The First Courtyard")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var roots = scene.GetRootGameObjects();
            var dressing = roots.First(g => g.name == "Courtyard dressing").transform;
            if (dressing.Find(RootName)) return "Lower courtyard elevation already fitted. Existing edits retained.";

            var floor = roots.First(g => g.name == "Floor").transform;
            var mountain = Required(dressing, "05 Mountain environment - 山岳环境");
            var cragGroup = Required(mountain, "01 Summit crags - 庭院承托山体");
            var lowerCrag = Required(cragGroup, "Lower sanctuary crag - 下庭岩峰");
            var upperCrag = Required(cragGroup, "Upper sanctuary crag - 上庭岩峰");
            var lowerRock = lowerCrag.GetComponentInChildren<MeshCollider>();
            var upperRock = upperCrag.GetComponentInChildren<MeshCollider>();
            if (!lowerRock || !upperRock) throw new InvalidOperationException("Both summit rock colliders are required.");
            var compound = Required(dressing, SamuraiMountainCompoundSetup.RootName);
            var roads = Required(dressing, SamuraiCourtyardRoadSetup.RootName);
            var route = Required(dressing, SamuraiLevelFlowSetup.RootName);
            var linked = Required(dressing, SamuraiLinkedHallSetup.RootName);
            var bridge = Required(dressing, SamuraiCourtyardBridgeSetup.BridgeName);
            var originalAscent = Required(linked, "02 Lower interior ascent - 西翼室内登廊");
            var supportRoute = Required(route, "03 High maintenance path - 高处单向山路");
            var hallLink = Required(dressing, SamuraiLinkedHallSetup.RefinedRootName + "/02 Recessed lodge link - 山屋与主殿过厅");

            Physics.SyncTransforms();
            var moved = new HashSet<Transform>
            {
                Required(floor, "02 Lower court - 下层场地"), Required(dressing, "02 Lower courtyard - 下层庭院"),
                Required(dressing, SamuraiFirstCourtyardSetup.RootName), lowerCrag,
                Required(mountain, "06 Pine groves - 山松林/02 Lower summit pines - 下层山松"),
                Required(compound, "02 Lower enclosure - 下峰外院墙"),
                Required(compound, "04 Bridge and garden portals - 桥口与侧门/Lower bridge portal - 下桥口"),
                Required(roads, "01 First courtyard network - 第一庭院连续路网"),
                Required(route, "01 Gates - 钥匙侧门与内闩桥门/Keyed mountain gate - 山路钥匙门"),
                Required(route, "01 Gates - 钥匙侧门与内闩桥门/Mountain gate flank closures - 山路门两侧接挡"),
                Required(route, "02 Key search - 正殿内室寻钥匙"),
                Required(linked, "01 Lower hall west wing - 下院大殿西门厅")
            };

            // Mixed groups share one parent with upper-peak details, so move only their lower attachments.
            AddMatching(moved, Required(dressing, "06 Architecture sites - 建筑落地石基"), t => t.position.x > 0);
            AddMatching(moved, Required(compound, "06 Arrival details - 入院细部"), t => t.position.x > 0);
            AddMatching(moved, Required(compound, "05 Garden walks - 外院石径"), LowerGarden);
            AddMatching(moved, Required(roads, "02 Garden paths - 接地连续园径"), LowerGarden);
            AddMatching(moved, Required(compound, "03 Inner court access - 内院通口与石阶"), t => t.name.StartsWith("Eastern hall ", StringComparison.Ordinal));
            AddMatching(moved, Required(mountain, "08 Summit outcrops - 山顶风化石"), t => t.position.x > 0);
            AddMatching(moved, Required(mountain, "06 Pine groves - 山松林/03 Hillside groves - 山腰松林"), t => OnLowerRock(t, lowerRock, upperRock));
            AddMatching(moved, Required(mountain, "07 Talus stones - 山脚落石"), t => OnLowerRock(t, lowerRock, upperRock));

            foreach (var go in roots)
                if (go.GetComponent<SamuraiCombatant>() && go.transform.position.x > 0) moved.Add(go.transform);
            if (Camera.main && Camera.main.TryGetComponent<SamuraiOrbitCamera>(out var orbit) && orbit.target
                && moved.Any(t => orbit.target.IsChildOf(t))) moved.Add(Camera.main.transform);

            var tower = dressing.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("14 Hall landmark pagoda", StringComparison.Ordinal));
            if (tower && OnLowerRock(tower, lowerRock, upperRock)) moved.Add(tower);
            var rigidRoots = moved.Where(t => !moved.Any(other => other != t && t.IsChildOf(other))).ToArray();

            Directory.CreateDirectory(Root + "/Meshes");
            AssetDatabase.Refresh();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Lower the first courtyard and reconnect its routes");
            try
            {
                var marker = new GameObject(RootName).transform;
                marker.SetParent(dressing, false);
                Undo.RegisterCreatedObjectUndo(marker.gameObject, "Create lower courtyard elevation group");
                foreach (var root in rigidRoots) Translate(root, Delta);

                int changedBridgeMeshes = SamuraiLowerCourtBridgeGrade.Apply(bridge, Delta, Root + "/Meshes");
                var landmarks = Required(route, "05 Bridge landmark - 出生桥景引导");
                foreach (Transform t in landmarks)
                {
                    float fraction = Mathf.InverseLerp(-13.72f, .70f, t.position.x);
                    float change = Delta * Mathf.SmoothStep(0f, 1f, fraction);
                    if (Mathf.Abs(change) > .0001f) Translate(t, change);
                }

                SamuraiLinkedHallSetup.BuildLowerCourtAscent(marker, Delta);
                Undo.RecordObject(originalAscent.gameObject, "Retain the previous ascent geometry");
                originalAscent.gameObject.SetActive(false);
                Physics.SyncTransforms();
                RefitAscentSupports(supportRoute, hallLink, lowerRock, upperRock, .6f * Delta, "LowerCourt");

                if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var outline)) outline.RefreshSources();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(group);
                return $"Lowered the first courtyard by {-Delta:F2}m, moved {rigidRoots.Length} attached groups and regraded {changedBridgeMeshes} bridge meshes and the indoor ascent.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        internal static void RefitAscentSupports(Transform route, Transform hallLink, MeshCollider lowerRock, MeshCollider upperRock,
            float lowerLandingAdjustment, string stage)
        {
            var posts = Required(route, "05 Grounded supports - 接地承柱").GetComponentsInChildren<BoxCollider>();
            var oldPiers = Required(hallLink, "Original retained supports Piers").GetComponent<MeshFilter>();
            var braces = Required(hallLink, "Original retained supports Braces").GetComponent<MeshFilter>();
            var cube = AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root + "/Meshes/Shapes/Block.asset");
            if (!cube || posts.Length == 0 || !oldPiers || !braces) throw new InvalidOperationException("The ascent supports are missing.");

            var parts = new List<CombineInstance>();
            foreach (var box in posts)
            {
                var b = box.bounds;
                float top = b.max.y;
                if (b.center.x > -3f && b.center.z < 23f) top += lowerLandingAdjustment;
                float bottom = Mathf.Max(SurfaceHeight(lowerRock, b.center), SurfaceHeight(upperRock, b.center)) - .12f;
                if (float.IsNegativeInfinity(bottom) || top <= bottom)
                    throw new InvalidOperationException("An ascent pier cannot be attached to the rock at its new height.");
                var t = box.transform;
                Undo.RecordObject(t, "Refit ascent pier to the lowered rock");
                var scale = t.localScale;
                scale.y *= (top - bottom) / b.size.y;
                t.localScale = scale;
                var p = t.position;
                p.y = (top + bottom) * .5f;
                t.position = p;
                parts.Add(new CombineInstance
                {
                    mesh = cube,
                    transform = oldPiers.transform.worldToLocalMatrix * t.localToWorldMatrix
                        * Matrix4x4.TRS(box.center, Quaternion.identity, box.size)
                });
            }
            var postMesh = new Mesh();
            postMesh.CombineMeshes(parts.ToArray(), true, true);
            SetMesh(oldPiers, postMesh, stage + "_Regrounded_Ascent_Piers");

            var warped = UnityEngine.Object.Instantiate(braces.sharedMesh);
            var vertices = warped.vertices;
            int stride = cube.vertexCount;
            if (vertices.Length % stride != 0) throw new InvalidOperationException("Unexpected layout for the original ascent braces.");
            for (int first = 0; first < vertices.Length; first += stride)
            {
                Vector3 center = Vector3.zero;
                for (int i = first; i < first + stride; i++) center += braces.transform.TransformPoint(vertices[i]);
                center /= stride;
                if (center.x < -3f || center.z > 24.4f) continue;
                for (int i = first; i < first + stride; i++)
                {
                    var point = braces.transform.TransformPoint(vertices[i]);
                    float t = Mathf.InverseLerp(18.2f, 25.5f, point.z);
                    point.y += Mathf.Lerp(lowerLandingAdjustment, 0, t);
                    vertices[i] = braces.transform.InverseTransformPoint(point);
                }
            }
            warped.vertices = vertices;
            warped.RecalculateNormals();
            warped.RecalculateBounds();
            SetMesh(braces, warped, stage + "_Regraded_Ascent_Braces");
        }

        static void SetMesh(MeshFilter filter, Mesh mesh, string name)
        {
            string path = Root + "/Meshes/" + name + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!saved)
            {
                mesh.name = name;
                AssetDatabase.CreateAsset(mesh, path);
                saved = mesh;
            }
            else UnityEngine.Object.DestroyImmediate(mesh);
            Undo.RecordObject(filter, "Assign geometry for the lowered court");
            filter.sharedMesh = saved;
        }

        static void Translate(Transform transform, float delta)
        {
            Undo.RecordObject(transform, "Lower court attachment");
            transform.position += Vector3.up * delta;
            PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
        }

        static Transform Required(Transform parent, string path)
        {
            var t = parent.Find(path);
            if (!t) throw new InvalidOperationException("Missing court elevation group: " + path);
            return t;
        }

        static void AddMatching(HashSet<Transform> targets, Transform parent, Func<Transform, bool> predicate)
        {
            foreach (Transform child in parent)
                if (predicate(child)) targets.Add(child);
        }

        static bool LowerGarden(Transform t) => t.name.StartsWith("Eastern ", StringComparison.Ordinal)
            || t.name.StartsWith("Entry maple ", StringComparison.Ordinal);

        static bool OnLowerRock(Transform t, MeshCollider lower, MeshCollider upper)
        {
            var p = t.position;
            if (p.x < 1f || p.x > 49f || Mathf.Abs(p.z) > 42f || p.y < -30f) return false;
            float low = SurfaceHeight(lower, p);
            float high = SurfaceHeight(upper, p);
            return low > high && Mathf.Abs(low - p.y) < 4f;
        }

        static float SurfaceHeight(MeshCollider collider, Vector3 p)
        {
            var ray = new Ray(new Vector3(p.x, 65f, p.z), Vector3.down);
            return collider.Raycast(ray, out var hit, 240f) ? hit.point.y : float.NegativeInfinity;
        }
    }
}
