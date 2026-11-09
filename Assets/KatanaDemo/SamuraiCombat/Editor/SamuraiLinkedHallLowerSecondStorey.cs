using System;
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
        public const string LowerSecondStoreyRootName = "17 Lower grand hall - 下院双层主殿与登廊";
        const float LowerHallGround = -2.43f;
        const float LowerHallUpper = 2.08f;
        const float StairCenterZ = 19.60f;

        [MenuItem("Katana/Level/Add Lower Hall Second Storey")]
        public static string AddLowerHallSecondStorey()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().First(g => g.name == "Courtyard dressing").transform;
            if (dressing.Find(LowerSecondStoreyRootName)) return "Lower hall second storey already exists. Existing edits retained.";

            var grade = dressing.Find(SamuraiLowerCourtElevationSetup.RootName);
            var linked = dressing.Find(RootName);
            var route = dressing.Find(SamuraiLevelFlowSetup.RootName);
            if (!grade || !linked || !route) throw new InvalidOperationException("Install the lowered courtyard and linked halls first.");
            var previousAscent = grade.Find("Regraded indoor ascent - 下院登廊接坡");
            var lowerCanopy = linked.Find("01 Lower hall west wing - 下院大殿西门厅/Lower_Annex_Connecting_Roof");
            var originalHall = dressing.GetComponentsInChildren<Transform>().Single(t => t.name == "Great hall - 可进入大殿");
            var progression = route.GetComponent<SamuraiLevelRoute>();
            var high = route.Find("03 High maintenance path - 高处单向山路");
            var hallLink = dressing.Find(RefinedRootName + "/02 Recessed lodge link - 山屋与主殿过厅");
            var ink = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Architecture/Materials/Hall_roof_ink.mat");
            if (!progression || !progression.sideGateRoot || !previousAscent || !lowerCanopy || !high || !hallLink || !ink)
                throw new InvalidOperationException("The lowered hall attachments are incomplete.");
            if (Mathf.Abs(originalHall.position.y + 2.50f) > .02f || Mathf.Abs(progression.sideGateRoot.position.y + 2.45f) > .02f)
                throw new InvalidOperationException("Expected the lowered hall and the original ground-level gate.");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add the lower hall second floor and indoor mountain access");
            try
            {
                var root = Group(dressing, LowerSecondStoreyRootName);
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Create lower hall second floor");
                var hall = Group(root, "01 Upper hall and eaves - 二层正殿与腰檐");
                BuildLowerHallUpperBody(Group(hall, "Walkable upper floor and openings - 可进入二层门窗楼板"));
                BuildLowerHallRoof(hall, ink);
                BuildLowerHallGableInfill(Group(hall, "Upper gables and eave infill - 山墙与檐下封板"));
                BuildLowerHallStair(Group(root, "02 Grand hall stair - 殿内实木楼梯"));

                RetireLowerHallRoof(originalHall);
                Retire(lowerCanopy);
                Retire(previousAscent);
                BuildCurvedLowerLink(dressing, root, progression, ink);

                if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var outline)) outline.RefreshSources();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(group);
                return "Added the lower hall second floor, interior stair, and upper keyed passage to the mountain lodge.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        static void BuildLowerHallUpperBody(Transform parent)
        {
            var body = new PaletteBatch(parent);
            SamuraiLowerHallUpperBody.Build(body.Box);
            body.Flush("LowerHall_Upper_Timber_And_Plaster");
        }

        static void RetireLowerHallRoof(Transform hall)
        {
            string[] oldParts =
            {
                "18_UpperBeams_Timber", "19_InteriorCeilingBeams_Timber", "20_Ceiling_Timber",
                "21_LowerEaves_Roof", "22_UpperRoof_Roof", "23_Clerestory_Timber",
                "24_EastGable_Plaster", "24_WestGable_Plaster", "25_EastGableTrim_Timber", "25_WestGableTrim_Timber",
                "26_Ridge_Gold", "27_RidgeEnds_Red"
            };
            foreach (var part in hall.GetComponentsInChildren<Transform>(true))
                if (oldParts.Contains(part.name)) Retire(part);
        }

        static void BuildLowerHallStair(Transform parent)
        {
            var wood = new PaletteBatch(parent);
            const float footX = 16.72f;
            const float landingX = 9.52f;
            const int steps = 22;
            float tread = (footX - landingX) / steps;
            for (int i = 0; i < steps; i++)
            {
                float top = Mathf.Lerp(LowerHallGround, LowerHallUpper, (i + 1f) / steps);
                float center = footX - (i + .5f) * tread;
                wood.Box("Oak stair tread", "Timber", new Vector3(center, top - .124f, StairCenterZ), new Vector3(tread + .002f, .248f, 1.82f));
                wood.Box("Stair tread lip", "Dark", new Vector3(center + tread * .43f, top - .024f, StairCenterZ), new Vector3(.055f, .05f, 1.83f));
            }
            foreach (float z in new[] { StairCenterZ - .946f, StairCenterZ + .946f })
            {
                var bottom = new Vector3(footX, LowerHallGround, z);
                var top = new Vector3(landingX, LowerHallUpper, z);
                wood.Beam("Oak stair stringer", "Dark", bottom - Vector3.up * .135f, top - Vector3.up * .135f, .15f, .21f);
                var railStart = bottom + Vector3.up * .90f;
                var railEnd = top + Vector3.up * .90f;
                var span = railEnd - railStart;
                wood.Box("Sloped stair handrail", "Dark", (railStart + railEnd) * .5f,
                    new Vector3(.080f, .09f, span.magnitude), Quaternion.LookRotation(span), true);
                for (int i = 0; i <= 7; i++)
                {
                    var point = Vector3.Lerp(bottom, top, i / 7f);
                    wood.Box("Stair rail post", "Red", point + Vector3.up * .47f, new Vector3(.085f, .94f, .085f));
                }
            }
            wood.Flush("LowerHall_Oak_Stairs");
            var path = new[] { new Vector3(footX, LowerHallGround, StairCenterZ), new Vector3(landingX, LowerHallUpper, StairCenterZ) };
            var ramp = Loft(path, i => FloorSection(.915f, -.21f, 0));
            if (!CourtyardRoadMeshBuilder.Audit(ramp).closedPositiveVolume) throw new InvalidOperationException("Invalid lower hall walking slope.");
            var slope = Group(parent, "Continuous lower hall stair ramp - 一二层楼梯连续行走面");
            slope.gameObject.AddComponent<MeshCollider>().sharedMesh = Save("LowerHall_Oak_Stair_Walking_Ramp", ramp);
        }

        static void BuildElevatedWestPortico(Transform parent)
        {
            var structure = new PaletteBatch(parent);
            structure.Box("Closed former ground gate throat", "Plaster", new Vector3(1.90f, -.32f, 16.43f), new Vector3(2.97f, 4.32f, .29f), true);
            structure.Box("Ground closure raised stone toe", "Slate", new Vector3(1.90f, -2.17f, 16.26f), new Vector3(3.08f, .52f, .23f));
            structure.Box("Raised porch foundation beam", "Dark", new Vector3(1.91f, 1.77f, 14.95f), new Vector3(3.02f, .26f, .31f));
            foreach (float x in new[] { .56f, 3.22f })
            {
                const float columnZ = 15.94f;
                structure.Box("Porch receiving capital", "Timber", new Vector3(x, 1.78f, columnZ), new Vector3(.41f, .36f, .46f));
                structure.Box("Porch column shoe", "Stone", new Vector3(x, -2.24f, columnZ), new Vector3(.45f, .42f, .44f));
                structure.Box("Red porch receiving column", "Red", new Vector3(x, -.32f, columnZ), new Vector3(.26f, 4.10f, .26f), true);
            }
            structure.Box("Porch offset bearing beam", "Dark", new Vector3(1.91f, 1.86f, 15.78f), new Vector3(3.03f, .22f, .44f));
            foreach (float z in new[] { 12.55f, 15.62f, 17.48f, 21.47f })
                structure.Box("Lower room visible ceiling timber", "Dark", new Vector3(13.0f, 1.79f, z), new Vector3(18.9f, .24f, .25f));
            structure.Flush("LowerHall_Flush_Threshold_Framing");
        }

        static void BuildLowerHallRoof(Transform hall, Material ink)
        {
            var roof = Group(hall, "Two layered roofs - 双层大殿瓦檐");
            SamuraiRidgeHallRoof.Build((name, mesh, palette) =>
            {
                RemapRoof(mesh, p => new Vector3(13f + p.x * 1.37f, 5.53f + (p.y - 5.10f) * 1.05f, 17.09f + p.z * 1.83f));
                var surface = Render(roof, name.Replace("RidgeHall", "LowerHall_TwoStorey"), mesh, palette);
                if (!name.EndsWith("CurvedGableRoof", StringComparison.Ordinal)) return;
                Collide(surface);
                var line = Group(surface, "Comic ink contour - 漫画描边");
                var hull = ComicOutlineHullFactory.GetOrCreate(surface.GetComponent<MeshFilter>().sharedMesh, Root + "/Contours", out _);
                RenderOn(line, hull, ink, false);
            });
            var eaves = EaveSkirt();
            RemapRoof(eaves, p => new Vector3(13f + (p.x + 25.4f) * 1.22f, p.y - 3.40f, 17f + (p.z - 18.60f) * 1.40f));
            Render(roof, "LowerHall_TwoStorey_Continuous_Waist_Eave", eaves, "Roof");
        }

        static void BuildLowerHallGableInfill(Transform parent)
        {
            const float baseY = 5.295f, half = .094f;
            foreach (float x in new[] { 3.53f, 22.46f })
            {
                var path = new[] { 12.09f, 12.2881f, 13.1079f, 14.162f, 15.2161f, 16.153f, 17.09f,
                    18.027f, 18.9639f, 20.018f, 21.0721f, 21.8919f, 22.09f };
                var centers = path.Select(z => new Vector3(x, baseY, z)).ToArray();
                var mesh = Loft(centers, i => Section(-half, half, 0f, LowerHallRoofBottom(centers[i]) - baseY));
                Collide(Render(parent, x < 13f ? "LowerHall_West_Gable_Infill" : "LowerHall_East_Gable_Infill", mesh, "Plaster"));
            }
            foreach (float z in new[] { 12.19f, 21.99f })
            {
                var centers = new[] { 3.53f, 5.465f, 8.89f, 13f, 17.11f, 20.535f, 22.46f }
                    .Select(x => new Vector3(x, baseY, z)).ToArray();
                var mesh = Loft(centers, i => Section(-half, half, 0f, LowerHallRoofBottom(centers[i]) - baseY));
                Collide(Render(parent, z < 17f ? "LowerHall_South_Eave_Infill" : "LowerHall_North_Eave_Infill", mesh, "Plaster"));
            }
        }

        static float LowerHallRoofBottom(Vector3 world)
        {
            float x = (world.x - 13f) / 1.37f;
            float z = (world.z - 17.09f) / 1.83f;
            return 5.575f + (SamuraiRidgeHallRoof.UndersideHeight(x, z) - 5.10f) * 1.05f;
        }

        static void RemapRoof(Mesh mesh, Func<Vector3, Vector3> map)
        {
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = map(vertices[i]);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
