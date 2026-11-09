using System;
using System.Collections.Generic;
using System.Linq;
using OCDesign.LookDev;
using OCDesign.LookDev.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiLowerRimPineRefinement
    {
        const string Assets = SamuraiComicStyleSetup.Root + "/BlenderAssembly/PineCrownDetails";
        static readonly Vector2[] SouthSites =
        {
            new Vector2(-.16f, -1.52f), new Vector2(-.28f, -1.35f),
            new Vector2(.08f, -1.37f), new Vector2(-.25f, -1.17f)
        };

        [MenuItem("Katana/Level/Refine Lower Rim Pine Crowns")]
        public static string Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != SamuraiBlenderAssemblyImport.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the Blender courtyard scene in Edit mode.");

            Transform dressing = scene.GetRootGameObjects().Single(g => g.name == SamuraiCourtyardDecorationSetup.RootName).transform;
            var nodes = dressing.GetComponentsInChildren<Transform>(true);
            Transform mountain = nodes.SingleOrDefault(t => t.name == SamuraiMountainCourtyardSetup.RootName);
            var trees = nodes.Where(t => t.name == "Mountain pine 042" || t.name == "Mountain pine 043")
                .OrderBy(t => t.name, StringComparer.Ordinal).ToArray();
            if (trees.Length != 2 || trees.Any(tree => !tree.gameObject.activeInHierarchy))
                throw new InvalidOperationException("The two foreground pines must be active.");

            var bodies = new[] { trees[0].Find("Surface - Pine_A"), trees[1].Find("Surface - Pine_B") }
                .Select(t => t ? t.GetComponent<MeshFilter>() : null).ToArray();
            if (bodies.Any(f => !f || !f.sharedMesh || f.GetComponent<MeshRenderer>().sharedMaterials.Length != 3))
                throw new InvalidOperationException("Unexpected material or mesh on the two foreground pines.");
            var hulls = bodies.Select(f => f.transform.Cast<Transform>().Single().GetComponent<MeshFilter>()).ToArray();
            if (hulls.Any(f => !f || !f.sharedMesh))
                throw new InvalidOperationException("The original pine outline renderers are required.");
            if (bodies.All(f => AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(Assets + "/", StringComparison.Ordinal)))
                return "The two lower rim pine crowns are already refined.";
            if (bodies.Any(f => f.sharedMesh.name != "Pine_A" && f.sharedMesh.name != "Pine_B"))
                throw new InvalidOperationException("The pine meshes have already been edited outside this tool.");
            if (!mountain) throw new InvalidOperationException("The lower summit is required.");

            MeshCollider ground = mountain.GetComponentsInChildren<MeshCollider>().Single(c =>
                c.enabled && c.sharedMesh && c.sharedMesh.name.EndsWith("Lower_Sanctuary_Crag", StringComparison.Ordinal));
            Physics.SyncTransforms();
            Vector3 original = trees[0].position;
            Vector3 next = FindRoot(ground, original, trees[1].position);
            float before = FlatDistance(original, trees[1].position);
            float after = FlatDistance(next, trees[1].position);
            EnsureFolder(SamuraiComicStyleSetup.Root + "/BlenderAssembly", "PineCrownDetails");
            EnsureFolder(Assets, "Contours");

            var meshes = new Mesh[2];
            var inks = new Mesh[2];
            for (int i = 0; i < trees.Length; i++)
            {
                string path = Assets + "/LowerPine_" + (42 + i).ToString("000") + ".asset";
                meshes[i] = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (!meshes[i])
                {
                    meshes[i] = CreateCrown(trees[i].rotation, i == 0);
                    meshes[i].name = "Lower pine " + (42 + i).ToString("000") + " - split bough crown";
                    AssetDatabase.CreateAsset(meshes[i], path);
                }
                inks[i] = ComicOutlineHullFactory.GetOrCreate(meshes[i], Assets + "/Contours", out _);
            }

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refine lower rim pine crowns");
            try
            {
                Undo.RecordObject(trees[0], "Separate the pine roots");
                trees[0].position = next;
                for (int i = 0; i < trees.Length; i++)
                {
                    Undo.RecordObject(bodies[i], "Fit separated pine branches");
                    Undo.RecordObject(hulls[i], "Update pine ink contour");
                    bodies[i].sharedMesh = meshes[i];
                    hulls[i].sharedMesh = inks[i];
                }

                foreach (var outline in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ComicScreenOutline>()))
                    outline.RefreshSources();
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return $"Refined two lower rim pines. Root spacing {before:F2} m to {after:F2} m. Pine 042 now at {next:F2}.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
        }

        static void EnsureFolder(string parent, string folder)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + folder))
                AssetDatabase.CreateFolder(parent, folder);
        }

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static Vector3 FindRoot(MeshCollider ground, Vector3 oldRoot, Vector3 otherRoot)
        {
            if (!Sample(ground, oldRoot, out RaycastHit oldSurface))
                throw new InvalidOperationException("Pine 042 is no longer seated on the lower crag.");
            float burial = Mathf.Clamp(oldSurface.point.y - oldRoot.y, .075f, .19f);
            foreach (Vector2 shift in SouthSites)
            {
                var site = oldRoot + new Vector3(shift.x, 0f, shift.y);
                if (FlatDistance(site, otherRoot) < 1.7f || !Sample(ground, site, out RaycastHit hit)
                    || hit.normal.y < .72f || Mathf.Abs(hit.point.y - oldSurface.point.y) > .55f) continue;

                if (Physics.OverlapCapsule(hit.point + Vector3.up * .30f, hit.point + Vector3.up * 3.8f, .18f,
                        ~0, QueryTriggerInteraction.Ignore).Any(c => c != ground)) continue;
                return hit.point - Vector3.up * burial;
            }
            throw new InvalidOperationException("Could not find a clear, grounded position to separate the lower rim pines.");
        }

        static bool Sample(MeshCollider ground, Vector3 position, out RaycastHit hit) =>
            ground.Raycast(new Ray(position + Vector3.up * 8f, Vector3.down), out hit, 16f);

        static Mesh CreateCrown(Quaternion treeRotation, bool tall)
        {
            var builder = new PineMesh();
            float height = tall ? 5.92f : 5.38f;
            float direction = tall ? -90f : 84f;
            var random = new System.Random(tall ? 4263 : 4351);
            Vector3 Trunk(float y) => new Vector3(.12f * Mathf.Sin(y * .67f) + y * .03f, y, y * -.025f);
            builder.Branch(Trunk(-.045f), Trunk(height * .39f), .152f, .109f);
            builder.Branch(Trunk(height * .38f), Trunk(height * .69f), .113f, .062f);
            builder.Branch(Trunk(height * .68f), Trunk(height * .96f), .066f, .022f);

            var levels = tall ? new[] { .44f, .57f, .71f, .84f } : new[] { .48f, .63f, .76f, .87f };
            var heading = tall ? new[] { 30f, -108f, -33f, 151f } : new[] { -32f, 145f, 36f, -98f };
            var lengths = tall ? new[] { .96f, 1.22f, .90f, .62f } : new[] { .87f, 1.04f, .79f, .49f };
            for (int i = 0; i < levels.Length; i++)
            {
                float y = height * levels[i];
                Vector3 along = Direction(direction + heading[i], treeRotation);
                Vector3 side = Vector3.Cross(Vector3.up, along);
                Vector3 start = Trunk(y - .36f);
                Vector3 bend = Trunk(y - .13f) + along * (lengths[i] * .49f);
                Vector3 tip = Trunk(y) + along * lengths[i];
                builder.Branch(start, bend, .065f - i * .008f, .044f - i * .006f);
                builder.Branch(bend, tip, .044f - i * .006f, .016f);
                int main = i == 1 || i == 3 ? 2 : 1;
                builder.Pad(tip + Vector3.up * .045f, along,
                    .53f - i * .05f, .43f - i * .04f, .245f - i * .018f, main, random);

                for (int flank = -1; flank <= 1; flank += 2)
                {
                    if (i == 3 && flank == -1) continue;
                    Vector3 offshoot = (along + side * (flank * .68f)).normalized;
                    Vector3 offTip = bend + offshoot * (.51f - i * .048f) + Vector3.up * (.09f + flank * .055f);
                    builder.Branch(bend, offTip, .027f, .011f);
                    builder.Pad(offTip + Vector3.up * .048f, offshoot,
                        .39f - i * .044f, .33f - i * .035f, .20f - i * .019f,
                        flank == -1 ? main : 3 - main, random);
                }

                if (i != 1) continue;
                Vector3 back = -along;
                Vector3 rear = Trunk(y + .08f) + back * (tall ? .81f : .69f);
                builder.Branch(Trunk(y - .28f), rear, .042f, .015f);
                builder.Pad(rear + Vector3.up * .06f, back, .50f, .38f, .22f, 1, random);
            }
            builder.Pad(Trunk(height * .945f), Direction(direction, treeRotation), .40f, .32f, .25f, 2, random);
            return builder.Finish(treeRotation);
        }

        static Vector3 Direction(float degrees, Quaternion treeRotation)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return Quaternion.Inverse(treeRotation) * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        sealed class PineMesh
        {
            const int BranchSides = 6;
            const int PadSides = 9;
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };

            public void Branch(Vector3 start, Vector3 end, float startRadius, float endRadius)
            {
                Vector3 axis = (end - start).normalized;
                Vector3 a = Vector3.Cross(axis, Mathf.Abs(axis.y) > .92f ? Vector3.right : Vector3.up).normalized;
                Vector3 b = Vector3.Cross(axis, a);
                int first = vertices.Count;
                for (int i = 0; i < BranchSides; i++)
                {
                    float angle = i * Mathf.PI * 2f / BranchSides;
                    var radial = a * Mathf.Cos(angle) + b * Mathf.Sin(angle);
                    vertices.Add(start + radial * startRadius);
                    vertices.Add(end + radial * endRadius);
                }
                int startCap = vertices.Count;
                vertices.Add(start);
                int endCap = vertices.Count;
                vertices.Add(end);
                for (int i = 0; i < BranchSides; i++)
                {
                    int left = first + i * 2;
                    int right = first + (i + 1) % BranchSides * 2;
                    Vector3 outward = (vertices[left] + vertices[right]) * .5f - start;
                    Face(0, left, right, right + 1, outward);
                    Face(0, left, right + 1, left + 1, outward);
                    Face(0, startCap, left, right, -axis);
                    Face(0, endCap, left + 1, right + 1, axis);
                }
            }

            public void Pad(Vector3 center, Vector3 along, float length, float width, float depth, int material, System.Random random)
            {
                Vector3 side = Vector3.Cross(Vector3.up, along);
                int start = vertices.Count;
                var radii = new[] { .64f, 1f, .72f };
                var levels = new[] { -.58f, -.08f, .57f };
                var shape = Enumerable.Range(0, PadSides).Select(_ => Mathf.Lerp(.82f, 1.17f, (float)random.NextDouble())).ToArray();
                float phase = (float)random.NextDouble() * .8f;
                for (int row = 0; row < 3; row++)
                    for (int i = 0; i < PadSides; i++)
                    {
                        float angle = phase + i * Mathf.PI * 2f / PadSides;
                        float reach = Mathf.Cos(angle);
                        var point = along * (reach * length) + side * (Mathf.Sin(angle) * width);
                        float y = (levels[row] + .09f * reach + (shape[i] - 1f) * .36f) * depth;
                        vertices.Add(center + point * (shape[i] * radii[row]) + Vector3.up * y);
                    }

                int lower = vertices.Count;
                vertices.Add(center - along * .13f * length - Vector3.up * depth * .78f);
                int upper = vertices.Count;
                vertices.Add(center - along * .16f * length + Vector3.up * depth * .83f);
                for (int i = 0; i < PadSides; i++)
                {
                    int next = (i + 1) % PadSides;
                    Face(material, lower, start + i, start + next, Vector3.down);
                    Face(material, upper, start + PadSides * 2 + i, start + PadSides * 2 + next, Vector3.up);
                    for (int row = 0; row < 2; row++)
                    {
                        int a = start + row * PadSides + i;
                        int b = start + row * PadSides + next;
                        Vector3 outward = (vertices[a] + vertices[b]) * .5f - center;
                        outward.y = 0f;
                        Face(material, a, b, b + PadSides, outward);
                        Face(material, a, b + PadSides, a + PadSides, outward);
                    }
                }
            }

            void Face(int material, int a, int b, int c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), outward) < 0f)
                    (b, c) = (c, b);
                triangles[material].Add(a);
                triangles[material].Add(b);
                triangles[material].Add(c);
            }

            public Mesh Finish(Quaternion rotation)
            {
                Quaternion inverse = Quaternion.Inverse(rotation);
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 offset = rotation * vertices[i];
                    offset.x *= .77f;
                    vertices[i] = inverse * offset;
                }

                var mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i, false);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
