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
    public static partial class SamuraiLinkedHallSetup
    {
        const string CurvedLinkName = "05 Curved mountain gallery - 弧形山间连廊";
        const string OldWestPorticoName = "03 West porch and gate sill - 西侧过厅与二层门槛";
        const string WestThresholdName = "03 West hall threshold - 西门贴合过厅";
        const string OldLowerLinkName = "04 Upper hall to ridge lodge - 二层山间连楼";
        const string CurvedJoineryName = "Continuous window joinery - 连续窗格";
        const int BendSamples = 40;

        [MenuItem("Katana/Level/Refine Lower Hall Curved Link")]
        public static string RefineLowerHallCurvedLink()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().Single(g => g.name == "Courtyard dressing").transform;
            var hall = dressing.Find(LowerSecondStoreyRootName);
            if (!hall) throw new InvalidOperationException("Install the lower hall second storey first.");
            if (hall.Find(CurvedLinkName)) return "Curved gallery already exists. Existing edits retained.";
            var oldGallery = hall.Find(OldLowerLinkName);
            var oldPortico = hall.Find(OldWestPorticoName);
            var route = dressing.Find(SamuraiLevelFlowSetup.RootName).GetComponent<SamuraiLevelRoute>();
            var ink = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Architecture/Materials/Hall_roof_ink.mat");
            if (!oldGallery || !oldPortico || !route || !ink) throw new InvalidOperationException("The original gallery attachments are incomplete.");

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refine the lower hall and mountain gallery join");
            try
            {
                BuildCurvedLowerLink(dressing, hall, route, ink);
                Retire(oldGallery);
                Retire(oldPortico);
                if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var outline)) outline.RefreshSources();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return "Replaced the lower hall dogleg with a continuous curved gallery and integrated its keyed gate.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
        }

        [MenuItem("Katana/Level/Fit Lower Hall Gate To Doorway")]
        public static string FitLowerHallGateToDoorway()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().Single(g => g.name == "Courtyard dressing").transform;
            var gallery = dressing.Find(LowerSecondStoreyRootName + "/" + CurvedLinkName);
            if (!gallery) throw new InvalidOperationException("Install the curved gallery first.");
            if (gallery.Find(CurvedJoineryName)) return "The gate is already fitted to the hall. Existing edits retained.";
            var old = gallery.Find("Window posts and timber fittings - 连续窗格与收口");
            var route = dressing.Find(SamuraiLevelFlowSetup.RootName).GetComponent<SamuraiLevelRoute>();
            var samples = gallery.Find("Route samples - 弧线中线");
            var threshold = gallery.parent.Find(WestThresholdName);
            if (!old || !route || !samples || !threshold) throw new InvalidOperationException("The existing gallery attachments are incomplete.");
            var path = samples.Cast<Transform>().Select(t => t.position).ToArray();

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fit the keyed gate into the hall doorway");
            try
            {
                BuildCurvedJoinery(gallery, path, CurvedLinkWidths(path.Length));
                BuildHallDoorwaySill(threshold, path[0]);
                FitHallDoorwayGate(route, path[0]);
                Retire(old);
                if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var outline)) outline.RefreshSources();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return "Fitted the existing keyed doors into the hall opening and removed the duplicate frame from the bend.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
        }

        static Vector3[] LowerHallBend()
        {
            var path = new Vector3[BendSamples + 3];
            var entrance = new Vector3(3.42f, LowerHallUpper, 15f);
            var lodge = new Vector3(-1.50f, 4.90f, LodgeZ);
            var a = new Vector3(-1.60f, 0, entrance.z);
            var b = new Vector3(2.30f, 0, lodge.z);
            for (int i = 0; i <= BendSamples; i++)
            {
                float t = (float)i / BendSamples;
                float s = 1f - t;
                var p = s * s * s * entrance + 3f * s * s * t * a + 3f * s * t * t * b + t * t * t * lodge;
                p.y = Mathf.Lerp(entrance.y, lodge.y, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.33f, 1f, t)));
                path[i] = p;
            }
            path[BendSamples + 1] = new Vector3(-1.9f, LodgeHeight(-1.9f), LodgeZ);
            path[BendSamples + 2] = new Vector3(-2.6f, LodgeHeight(-2.6f), LodgeZ);
            return path;
        }

        static void BuildCurvedLowerLink(Transform dressing, Transform hall, SamuraiLevelRoute route, Material ink)
        {
            var threshold = Group(hall, WestThresholdName);
            Undo.RegisterCreatedObjectUndo(threshold.gameObject, "Build the west hall threshold");
            BuildElevatedWestPortico(threshold);
            var gallery = Group(hall, CurvedLinkName);
            Undo.RegisterCreatedObjectUndo(gallery.gameObject, "Build a continuous mountain gallery");
            var path = LowerHallBend();
            var widths = CurvedLinkWidths(path.Length);
            var markers = Group(gallery, "Route samples - 弧线中线");
            for (int i = 0; i < path.Length; i++) Group(markers, "Centre " + i.ToString("D2")).localPosition = path[i];
            markers.gameObject.SetActive(false);

            const string tag = "LowerHall_CurvedMountainLink";
            Collide(Render(gallery, tag + "_Deck", Loft(path, i => FloorSection(widths[i], -.23f, 0f)), "Deck"));
            foreach (int sign in new[] { -1, 1 })
            {
                string suffix = sign < 0 ? "_Left" : "_Right";
                Mesh Band(float half, float bottom, float top) =>
                    Loft(path, i => Section(sign * widths[i] - half, sign * widths[i] + half, bottom, top));
                Render(gallery, tag + "_Spandrel" + suffix, Band(.088f, -.06f, .91f), "Plaster");
                Render(gallery, tag + "_Sill" + suffix, Band(.109f, .89f, 1.01f), "Dark");
                Render(gallery, tag + "_Head" + suffix, Band(.105f, 2.47f, 2.71f), "Dark");
                Render(gallery, tag + "_Frieze" + suffix, Band(.087f, 2.70f, 3.15f), "Plaster");
                var wall = Group(gallery, "Window wall collision" + suffix);
                var collision = Band(.081f, -.06f, 3.17f);
                if (!CourtyardRoadMeshBuilder.Audit(collision).closedPositiveVolume)
                    throw new InvalidOperationException("Invalid continuous window wall.");
                wall.gameObject.AddComponent<MeshCollider>().sharedMesh = Save(tag + "_WindowWall" + suffix, collision);
            }
            BuildCurvedJoinery(gallery, path, widths);
            BuildHallDoorwaySill(threshold, path[0]);
            FitHallDoorwayGate(route, path[0]);
            var roofPath = (Vector3[])path.Clone();
            roofPath[0].x -= .025f;
            roofPath[roofPath.Length - 1].x += .54f;
            var roof = Render(gallery, tag + "_TiledRoof", Loft(roofPath, i => RoofSection(widths[i] + .22f, 3.11f)), "Roof");
            Collide(roof);
            var outline = Group(roof, "Comic ink contour - 漫画描边");
            var hull = ComicOutlineHullFactory.GetOrCreate(roof.GetComponent<MeshFilter>().sharedMesh, Root + "/Contours", out _);
            RenderOn(outline, hull, ink, false);
            Render(gallery, tag + "_Ridge", Loft(roofPath, _ => Section(-.045f, .045f, 4.11f, 4.19f)), "Gold");
            RealignLowerLinkSupports(dressing, path);
        }

        static float[] CurvedLinkWidths(int count)
        {
            return Enumerable.Range(0, count).Select(i =>
            {
                float t = (float)i / BendSamples;
                float entrySweep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .17f, t))
                    * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.30f, .62f, t)));
                return Mathf.Lerp(.95f, 1.08f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t))) + .45f * entrySweep;
            }).ToArray();
        }

        static void BuildCurvedJoinery(Transform gallery, Vector3[] path, float[] widths)
        {
            var group = Group(gallery, CurvedJoineryName);
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Restore continuous window joinery at the bend");
            var posts = new PaletteBatch(group);
            foreach (int sign in new[] { -1, 1 })
            {
                for (int i = 3; i < BendSamples; i += 2)
                {
                    var edge = Edge(path, i, sign * widths[i]);
                    var rotation = LinkFacing(path, i);
                    bool post = i % 6 == 3;
                    posts.Box(post ? "Lacquered window post" : "Recessed window lattice", post ? "Red" : "Timber",
                        edge + Vector3.up * 1.74f, post ? new Vector3(.20f, 1.55f, .19f) : new Vector3(.065f, 1.48f, .062f), rotation, false);
                }
            }
            foreach (int sign in new[] { -1, 1 })
                posts.Box("Flush lodge receiving jamb", "Timber",
                    Edge(path, BendSamples, sign * (widths[BendSamples] + .04f)) + Vector3.up * 1.54f,
                    new Vector3(.21f, 3.08f, .15f), LinkFacing(path, BendSamples), false);
            posts.Flush("LowerHall_CurvedGallery_WindowJoinery");
        }

        static Quaternion LinkFacing(Vector3[] path, int i)
        {
            if (i == 0 || i >= BendSamples) return Quaternion.LookRotation(Vector3.left);
            var direction = path[i + 1] - path[i - 1];
            direction.y = 0;
            return Quaternion.LookRotation(direction);
        }

        static void FitHallDoorwayGate(SamuraiLevelRoute route, Vector3 threshold)
        {
            const float scale = .89f, openingHeight = 2.54f;
            var gate = route.sideGateRoot;
            var blocker = (BoxCollider)route.sideGateBlocker;
            Undo.RecordObject(gate, "Seat the keyed door leaves inside the hall jambs");
            // The wall is 0.22 m deep. Seat the hinges at its inner face so the
            // leaves open into the hall without striking the curved window wall.
            gate.SetPositionAndRotation(threshold + Vector3.right * .29f, Quaternion.LookRotation(Vector3.left));
            gate.localScale = Vector3.one * scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(gate);
            Retire(gate.Find("Mountain gate frame - 山路门框"));
            Undo.RecordObject(blocker, "Fit the closed gate collision to the doorway");
            blocker.center = Vector3.up * (openingHeight * .5f / scale);
            blocker.size = new Vector3(2.00f, openingHeight, .22f) / scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(blocker);
            Undo.RecordObject(route, "Swing the existing gates inward into the upper hall");
            route.sideGateOpenAngles = new[] { 90f, -90f };
            PrefabUtility.RecordPrefabInstancePropertyModifications(route);
        }

        static void BuildHallDoorwaySill(Transform parent, Vector3 threshold)
        {
            // A shallow bevel covers the retained first-storey wall cap and
            // bridges the two adjoining floor meshes below the door leaves.
            var path = new[]
            {
                threshold + Vector3.left * .19f, threshold + new Vector3(-.09f, .026f, 0),
                threshold + new Vector3(.29f, .026f, 0), threshold + Vector3.right * .40f
            };
            var mesh = Loft(path, _ => FloorSection(.986f, -.060f, 0f));
            var sill = Render(parent, "LowerHall_West_Doorway_Sill", mesh, "Timber");
            Undo.RegisterCreatedObjectUndo(sill.gameObject, "Fit a continuous door sill");
            Collide(sill);
        }

        static void RealignLowerLinkSupports(Transform dressing, Vector3[] path)
        {
            var route = dressing.Find(SamuraiLevelFlowSetup.RootName + "/03 High maintenance path - 高处单向山路/05 Grounded supports - 接地承柱");
            var link = dressing.Find(RefinedRootName + "/02 Recessed lodge link - 山屋与主殿过厅");
            var piers = link.Find("Original retained supports Piers").GetComponent<MeshFilter>();
            var braces = link.Find("Original retained supports Braces").GetComponent<MeshFilter>();
            var rocks = dressing.GetComponentsInChildren<MeshCollider>().Where(c =>
                c.name == "Surface - Lower_Sanctuary_Crag" || c.name == "Surface - Upper_Sanctuary_Crag").ToArray();
            var cube = AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root + "/Meshes/Shapes/Block.asset");
            var parts = new List<CombineInstance>();
            foreach (var box in route.GetComponentsInChildren<BoxCollider>())
            {
                var bounds = box.bounds;
                if (bounds.center.x > -3f)
                {
                    var point = path.OrderBy(p => Mathf.Abs(p.z - bounds.center.z)).ThenBy(p => Mathf.Abs(p.x + 1.5f)).First();
                    float bottom = rocks.Select(rock =>
                        rock.Raycast(new Ray(new Vector3(point.x, 60f, point.z), Vector3.down), out var hit, 220f)
                            ? hit.point.y : float.NegativeInfinity).Max() - .12f;
                    float height = point.y - .14f - bottom;
                    if (float.IsInfinity(height) || height <= 0) throw new InvalidOperationException("The curved gallery pier must reach the cliff.");
                    var t = box.transform;
                    Undo.RecordObject(t, "Reposition lower gallery pier");
                    var scale = t.localScale;
                    scale.y *= height / bounds.size.y;
                    t.localScale = scale;
                    t.position = new Vector3(point.x, bottom + height * .5f, point.z);
                }
                parts.Add(new CombineInstance
                {
                    mesh = cube, transform = piers.transform.worldToLocalMatrix * box.transform.localToWorldMatrix *
                        Matrix4x4.TRS(box.center, Quaternion.identity, box.size)
                });
            }
            var combined = new Mesh();
            combined.CombineMeshes(parts.ToArray(), true, true);
            Undo.RecordObject(piers, "Refit the gallery pier render mesh");
            piers.sharedMesh = Save("LowerHall_CurvedLink_Piers", combined);

            // Each original brace is a separately indexed timber block in the combined mesh.
            var kept = UnityEngine.Object.Instantiate(braces.sharedMesh);
            var vertices = kept.vertices;
            var indices = kept.triangles;
            int stride = cube.vertexCount;
            if (vertices.Length % stride != 0) throw new InvalidOperationException("The original braces are not separated into timber blocks.");
            var retained = new bool[vertices.Length / stride];
            for (int part = 0; part < retained.Length; part++)
            {
                float centerX = 0;
                for (int j = 0; j < stride; j++) centerX += braces.transform.TransformPoint(vertices[part * stride + j]).x;
                retained[part] = centerX / stride < -3f;
            }
            kept.triangles = indices.Where(i => retained[i / stride]).ToArray();
            Undo.RecordObject(braces, "Retain braces under the lodge and Elite hall only");
            braces.sharedMesh = Save("LowerHall_CurvedLink_RetainedLodgeBraces", kept);
        }
    }
}
