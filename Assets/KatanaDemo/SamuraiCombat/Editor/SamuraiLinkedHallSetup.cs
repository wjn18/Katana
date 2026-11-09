using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OCDesign.LookDev;
using OCDesign.LookDev.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static partial class SamuraiLinkedHallSetup
    {
        public const string RootName = "12 Linked mountain halls - 靠山双殿与二层落院";
        public const string RefinedRootName = "13 Elite grand hall - 精英院两层主殿";
        const string Root = SamuraiComicStyleSetup.Root + "/LinkedHalls";
        const float UpperFloor = 1.3671f;
        const float BalconyFloor = 5.3f;
        const float LodgeZ = 25.5f;

        static readonly Dictionary<string, string> Decoration = new Dictionary<string, string>
        {
            { "Roof", "Ink_blue_roof" }, { "Gold", "Quiet_ochre" }, { "Timber", "Old_honey_timber" },
            { "Red", "Vermilion_lacquer" }, { "Plaster", "Ivory_plaster" }, { "Stone", "Warm_sandstone" },
            { "Slate", "Foundation_slate" }, { "Paper", "Warm_lantern_paper" }
        };

        [MenuItem("Katana/Level/Link Mountain Halls And Balcony")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().First(g => g.name == "Courtyard dressing").transform;
            if (dressing.Find(RootName)) return "Linked mountain halls already exist. Existing edits retained.";

            var lodge = dressing.Find(SamuraiRidgeHallSetup.RootName);
            var route = dressing.Find(SamuraiLevelFlowSetup.RootName);
            var high = route ? route.Find("03 High maintenance path - 高处单向山路") : null;
            var upper = dressing.GetComponentsInChildren<Transform>().Single(t => t.name == "04 Main practice hall");
            if (!lodge || !high) throw new InvalidOperationException("Install the original keyed route and ridge lodge first.");
            var roofInk = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Architecture/Materials/Hall_roof_ink.mat");
            if (!roofInk) throw new InvalidOperationException("Missing architectural roof outline material.");

            Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory(Root + "/Contours");
            AssetDatabase.Refresh();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Link mountain halls and courtyard balcony");
            try
            {
                var root = Group(dressing, RootName);
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Create mountain hall extension");
                var lower = Group(root, "01 Lower hall west wing - 下院大殿西门厅");
                BuildLowerVestibule(lower);

                var east = new[]
                {
                    new Vector3(1.9f, .05f, 16.4f), new Vector3(1.9f, .65f, 18.2f),
                    new Vector3(-1.5f, 1.55f, 18.2f), new Vector3(-1.5f, 4.9f, LodgeZ),
                    new Vector3(-2.6f, LodgeHeight(-2.6f), LodgeZ)
                };
                var eastWing = Group(root, "02 Lower interior ascent - 西翼室内登廊");
                BuildGallery(eastWing, "Ascent", east, new[] { 1.51f, 1.51f, 1.12f, .98f, .98f }, true, roofInk);

                var west = new[]
                {
                    new Vector3(-13.5f, LodgeHeight(-13.5f), LodgeZ), new Vector3(-14.6f, 5.2f, LodgeZ),
                    new Vector3(-14.6f, WestHeight(21.9f), 21.9f), new Vector3(-14.6f, WestHeight(16f), 16f),
                    new Vector3(-14.6f, WestHeight(12.05f), 12.05f), new Vector3(-14.6f, BalconyFloor, 7.2f),
                    new Vector3(-15.65f, BalconyFloor, 7.2f)
                };
                var upperWing = Group(root, "03 Upper hall east wing - 上院大殿两层东翼");
                BuildGallery(upperWing, "EastWing", west, new[] { .98f, .98f, 1.06f, 1.06f, 1.06f, .90f, 1.02f }, false, roofInk);
                BuildUpperAnnex(upperWing, upper);

                var balcony = new PaletteBatch(Group(root, "04 Second floor balcony - 二层落院阳台"));
                SamuraiLinkedHallBalcony.Build(balcony.Box);
                balcony.Flush("Balcony");

                var mountain = Group(root, "05 Backing mountain - 贴殿山体");
                mountain.localPosition = new Vector3(-8.05f, 0f, 36f);
                var rock = Render(mountain, "Backing_Mountain_Strata", SamuraiRidgeBackdropMesh.Build(), "Rock");
                AddBackingPines(rock, dressing);

                Retire(high.Find("Continuous walking deck - 连续高路行走面"));
                Retire(high.Find("01 Guard posts - 山路护柱"));
                Retire(high.Find("02 Continuous rails - 连续双道护栏"));
                Retire(high.Find("03 Plank joints - 紧密木板细缝"));
                Retire(lodge.Find("High path railing transitions - 高路栏杆接续"));
                var drop = route.Find("04 Drop arrival - 跳落进入精英院");
                if (drop)
                    foreach (Transform child in drop)
                        if (child.name == "Drop direction inlay") Retire(child);

                if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var outline)) outline.RefreshSources();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return "Installed attached mountain, both hall extensions, indoor galleries and the second floor balcony.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
        }

        static float LodgeHeight(float x) => Mathf.Lerp(4.9f, 5.2f, (-1.5f - x) / 13.1f) + .006f;
        static float WestHeight(float z) => Mathf.Lerp(5.2f, BalconyFloor, (LodgeZ - z) / 18.3f);

        internal static Transform BuildLowerCourtAscent(Transform parent, float delta)
        {
            var roofInk = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Architecture/Materials/Hall_roof_ink.mat");
            if (!roofInk) throw new InvalidOperationException("Missing architectural roof outline material.");
            var path = new[]
            {
                new Vector3(1.9f, .05f + delta, 16.4f), new Vector3(1.9f, .65f + delta, 18.2f),
                new Vector3(-1.5f, 1.55f + .6f * delta, 18.2f), new Vector3(-1.5f, 4.9f, LodgeZ),
                new Vector3(-2.6f, LodgeHeight(-2.6f), LodgeZ)
            };
            var wing = Group(parent, "Regraded indoor ascent - 下院登廊接坡");
            BuildGallery(wing, "LowerCourtRegradedAscent", path, new[] { 1.51f, 1.51f, 1.12f, .98f, .98f }, true, roofInk);
            return wing;
        }

        [MenuItem("Katana/Level/Refine Elite Grand Hall And Mountain Contact")]
        public static string RefineEliteHall()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().First(g => g.name == "Courtyard dressing").transform;
            if (dressing.Find(RefinedRootName)) return "Refined Elite grand hall already exists. Existing edits retained.";

            var linked = dressing.Find(RootName);
            var original = dressing.GetComponentsInChildren<Transform>().SingleOrDefault(t => t.name == "04 Main practice hall");
            var mountain = linked ? linked.Find("05 Backing mountain - 贴殿山体") : null;
            var oldRock = mountain ? mountain.Find("Backing_Mountain_Strata") : null;
            if (!original || !oldRock) throw new InvalidOperationException("Install the linked mountain halls first.");
            var ink = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Architecture/Materials/Hall_roof_ink.mat");
            if (!ink) throw new InvalidOperationException("Missing architectural outline material.");

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refine Elite courtyard grand hall and mountain contact");
            try
            {
                var root = Group(dressing, RefinedRootName);
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Create Elite grand hall extension");
                var hall = Group(root, "01 Two-storey grand hall - 两层主殿与正阳台");
                BuildEliteWestHallBody(hall, dressing);
                BuildEliteHallRoof(hall, ink);

                var connector = Group(root, "02 Recessed lodge link - 山屋与主殿过厅");
                var route = new[]
                {
                    new Vector3(-13.5f, LodgeHeight(-13.5f), LodgeZ), new Vector3(-14.6f, 5.2f, LodgeZ),
                    new Vector3(-14.6f, 5.30f, 20.45f), new Vector3(-17.23f, 5.30f, 20.45f)
                };
                BuildGallery(connector, "EliteHallLink", route, new[] { .98f, .98f, 1.08f, 1.02f }, false, ink, .18f);
                BuildConnectorJoinery(connector, dressing);

                var support = Group(root, "03 Lodge rock contact - 山屋收边与嵌岩基座");
                BuildLodgeRockContact(support);
                var rock = Render(mountain, "Backing_Mountain_Broken_Strata", SamuraiRidgeBackdropMesh.Build(), "Rock");
                Undo.RegisterCreatedObjectUndo(rock.gameObject, "Refine broken strata on the backing mountain");
                Retire(mountain.Find("Broken ledge pines - 岩肩松林"));
                AddBackingPines(rock, dressing, true);
                Retire(oldRock);

                Retire(original);
                Retire(linked.Find("03 Upper hall east wing - 上院大殿两层东翼"));
                Retire(linked.Find("04 Second floor balcony - 二层落院阳台"));
                var progression = dressing.Find(SamuraiLevelFlowSetup.RootName);
                if (progression)
                {
                    Retire(progression.Find("04 Drop arrival - 跳落进入精英院"));
                    var walk = progression.Find("03 High maintenance path - 高处单向山路");
                    if (walk)
                    {
                        TrimOldWestSupports(walk.Find("04 Underdeck trusses - 高路承托斜梁"), connector, "Braces");
                        TrimOldWestSupports(walk.Find("05 Grounded supports - 接地承柱"), connector, "Piers");
                    }
                }
                BuildWestGrassDrop(root, dressing);

                if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var outline)) outline.RefreshSources();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return "Refined backing rock and built the two-storey Elite hall with a west balcony above the original lawn.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
        }

        static void BuildEliteHallRoof(Transform hall, Material ink)
        {
            var roof = Group(hall, "Roof silhouette - 双檐瓦顶");
            SamuraiRidgeHallRoof.Build((name, mesh, material) =>
            {
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var v = vertices[i];
                    vertices[i] = new Vector3(-25.4f + v.x * 1.22f, 8.93f + (v.y - 5.10f) * 1.035f, 18.47f + v.z * 1.62f);
                }
                mesh.vertices = vertices;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var surface = Render(roof, name.Replace("RidgeHall", "EliteGrandHall"), mesh, material);
                if (!name.EndsWith("CurvedGableRoof", StringComparison.Ordinal)) return;
                Collide(surface);
                var outline = Group(surface, "Comic ink contour - 漫画描边");
                var hull = ComicOutlineHullFactory.GetOrCreate(surface.GetComponent<MeshFilter>().sharedMesh, Root + "/Contours", out _);
                RenderOn(outline, hull, ink, false);
            });

            var lowerEaves = new PaletteBatch(Group(roof, "Lower eave rafters - 腰檐椽梁"));
            var skirt = Group(roof, "Continuous lower eave - 四面连续腰檐");
            Render(skirt, "EliteGrandHall_Wraparound_Eave", EaveSkirt(), "Roof");
            foreach (float x in new[] { -33.1f, -31.6f, -30.1f, -28.6f, -27.1f, -25.4f, -23.7f, -22.2f, -20.7f, -19.2f, -17.7f })
            {
                lowerEaves.Beam("Front exposed rafter tail", "Timber", new Vector3(x, 5.07f, 15.19f), new Vector3(x, 4.62f, 14.10f), .15f, .13f);
                lowerEaves.Beam("Rear exposed rafter tail", "Timber", new Vector3(x, 5.07f, 22.25f), new Vector3(x, 4.62f, 23.10f), .15f, .13f);
            }
            lowerEaves.Box("Front fascia gold fillet", "Gold", new Vector3(-25.4f, 4.68f, 14.04f), new Vector3(18.1f, .047f, .073f));
            lowerEaves.Flush("EliteGrandHall_Lower_Eave_Trim");
        }

        static Mesh EaveSkirt()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var rings = new[]
            {
                new Vector4(-34.46f, -16.34f, 14.03f, 23.16f),
                new Vector4(-34.10f, -16.70f, 14.34f, 22.87f),
                new Vector4(-33.43f, -17.37f, 14.91f, 22.37f)
            };
            float[] heights = { 4.72f, 4.81f, 5.27f };
            for (int layer = 0; layer < 2; layer++)
            for (int i = 0; i < rings.Length; i++)
            {
                var r = rings[i];
                float y = heights[i] - .17f * layer;
                float midX = (r.x + r.y) * .5f, midZ = (r.z + r.w) * .5f;
                vertices.AddRange(new[]
                {
                    new Vector3(r.x, y + .09f, r.z), new Vector3(r.x, y, midZ),
                    new Vector3(r.x, y + .09f, r.w), new Vector3(midX, y, r.w),
                    new Vector3(r.y, y + .09f, r.w), new Vector3(r.y, y, midZ),
                    new Vector3(r.y, y + .09f, r.z), new Vector3(midX, y, r.z)
                });
            }
            void Quad(int a, int b, int c, int d)
            {
                triangles.AddRange(new[] { a, b, c, a, c, d });
            }
            for (int j = 0; j < 8; j++)
            {
                int k = (j + 1) % 8;
                for (int i = 0; i < 2; i++)
                {
                    int a = 8 * i;
                    Quad(a + j, a + k, a + 8 + k, a + 8 + j);
                    Quad(a + 24 + j, a + 32 + j, a + 32 + k, a + 24 + k);
                }
                Quad(k, j, j + 24, k + 24);
                Quad(16 + j, 16 + k, 40 + k, 40 + j);
            }
            var mesh = MeshFrom(vertices, triangles);
            var audit = CourtyardRoadMeshBuilder.Audit(mesh);
            if (audit.signedVolume < 0)
            {
                for (int i = 0; i < triangles.Count; i += 3)
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = MeshFrom(vertices, triangles);
            }
            if (!CourtyardRoadMeshBuilder.Audit(mesh).closedPositiveVolume)
                throw new InvalidOperationException("Invalid grand hall lower eave mesh.");
            return mesh;
        }

        static void BuildConnectorJoinery(Transform parent, Transform dressing)
        {
            var b = new PaletteBatch(Group(parent, "Inset link portals - 连廊榫接门框"));
            foreach (float z in new[] { 19.42f, 21.48f })
            {
                b.Box("Hall link stone shoe", "Stone", new Vector3(-17.11f, 5.47f, z), new Vector3(.38f, .34f, .27f));
                b.Box("Hall link receiving column", "Red", new Vector3(-17.13f, 6.96f, z), new Vector3(.28f, 3.35f, .24f));
                b.Box("Hall link receiving capital", "Dark", new Vector3(-17.13f, 8.52f, z), new Vector3(.48f, .20f, .35f));
                b.Beam("Corbel to hall post", "Timber", new Vector3(-16.84f, 8.13f, z), new Vector3(-17.28f, 8.56f, z), .18f, .16f);
            }
            b.Box("Hall link carved header", "Dark", new Vector3(-17.13f, 8.52f, 20.45f), new Vector3(.30f, .20f, 2.24f));
            b.Box("Hall link header gold inlay", "Gold", new Vector3(-16.97f, 8.46f, 20.45f), new Vector3(.037f, .037f, 1.85f));
            foreach (float z in new[] { 19.76f, 21.14f })
            {
                const float x = -14.08f;
                float ground = RockGround(dressing, x, z);
                const float cap = 5.03f;
                b.Box("Link stone pier foot", "Slate", new Vector3(x, ground + .23f, z), new Vector3(.71f, .58f, .73f), true);
                b.Box("Link red support post", "Red", new Vector3(x, (cap + ground + .46f) * .5f, z), new Vector3(.30f, cap - ground - .46f, .30f), true);
                b.Beam("Pier corbel below link", "Dark", new Vector3(x, 4.15f, z), new Vector3(-15.08f, 5.10f, z), .21f, .22f);
            }
            b.Beam("Link longitudinal floor beam", "Dark", new Vector3(-17.24f, 5.02f, 19.68f), new Vector3(-14.08f, 5.02f, 19.68f), .22f, .28f);
            b.Beam("Link longitudinal floor beam", "Dark", new Vector3(-17.24f, 5.02f, 21.23f), new Vector3(-14.08f, 5.02f, 21.23f), .22f, .28f);
            b.Flush("EliteHallLink_Portal_Joinery");
        }

        static float RockGround(Transform dressing, float x, float z)
        {
            float height = float.NegativeInfinity;
            var ray = new Ray(new Vector3(x, 65, z), Vector3.down);
            foreach (var rock in dressing.GetComponentsInChildren<MeshCollider>())
                if (rock.name.Contains("Sanctuary_Crag") && rock.Raycast(ray, out var hit, 180)) height = Mathf.Max(height, hit.point.y);
            if (float.IsNegativeInfinity(height)) throw new InvalidOperationException("Missing summit support surface.");
            return height;
        }

        static void TrimOldWestSupports(Transform group, Transform destination, string tag)
        {
            if (!group) return;
            var surface = group.Find("Combined timber - 合并静态木构");
            if (!surface) return;
            var mesh = surface.GetComponent<MeshFilter>().sharedMesh;
            int stride = AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root + "/Meshes/Shapes/Block.asset").vertexCount;
            if (mesh.vertexCount % stride != 0) throw new InvalidOperationException("Expected high-route supports combined from the authored block mesh.");
            var vertices = mesh.vertices;
            var accepted = new List<Vector3>();
            var newIndex = new int[vertices.Length];
            for (int first = 0; first < vertices.Length; first += stride)
            {
                var bounds = new Bounds(surface.TransformPoint(vertices[first]), Vector3.zero);
                for (int i = first + 1; i < first + stride; i++) bounds.Encapsulate(surface.TransformPoint(vertices[i]));
                bool keep = !OldWestSupport(bounds.center);
                for (int i = first; i < first + stride; i++)
                {
                    newIndex[i] = keep ? accepted.Count : -1;
                    if (keep) accepted.Add(destination.InverseTransformPoint(surface.TransformPoint(vertices[i])));
                }
            }
            var triangles = new List<int>();
            var oldTriangles = mesh.triangles;
            for (int i = 0; i < oldTriangles.Length; i += 3)
            {
                int a = newIndex[oldTriangles[i]], b = newIndex[oldTriangles[i + 1]], c = newIndex[oldTriangles[i + 2]];
                if (a >= 0 && b >= 0 && c >= 0) triangles.AddRange(new[] { a, b, c });
            }
            if (accepted.Count == vertices.Length || accepted.Count == 0) return;
            var retained = Group(destination, "Original retained supports " + tag);
            var stored = Save("EliteHall_Original_Ridge_Supports_" + tag, MeshFrom(accepted, triangles));
            RenderOn(retained, stored, surface.GetComponent<MeshRenderer>().sharedMaterial, true);
            Retire(surface);
            foreach (var collider in group.GetComponentsInChildren<BoxCollider>(true))
                if (OldWestSupport(collider.transform.TransformPoint(collider.center))) Retire(collider.transform);
        }

        static bool OldWestSupport(Vector3 point) => point.x < -12.9f && point.z < 22.8f;

        static void BuildLodgeRockContact(Transform parent)
        {
            var xs = new[] { -15.65f, -14.1f, -12.15f, -9.85f, -7.65f, -5.0f, -2.95f, -1.55f };
            var path = xs.Select(x => new Vector3(x, 0, 28.65f)).ToArray();
            var apron = Loft(path, i =>
            {
                float taper = (i == 0 || i == xs.Length - 1) ? .74f : 0f;
                float offset = Mathf.Sin(i * 1.84f) * .24f;
                float foot = i < 5 ? -3.20f : -8f - (i - 4) * 5.5f;
                return new[]
                {
                    new Vector2(-1.07f + offset + taper, 2.74f - taper),
                    new Vector2(-.53f + offset + taper, 4.58f - taper),
                    new Vector2(.35f + offset, 4.98f - taper),
                    new Vector2(1.61f + offset, 4.37f - taper * .6f),
                    new Vector2(2.83f + offset, 2.62f - taper),
                    new Vector2(2.83f + offset, foot), new Vector2(1.61f + offset, foot - .37f),
                    new Vector2(.35f + offset, foot - .58f), new Vector2(-.53f + offset + taper, foot - .49f),
                    new Vector2(-1.07f + offset + taper, foot)
                };
            });
            Render(parent, "Lodge_Irregular_Rock_Apron", apron, "Rock");

            var masonry = new PaletteBatch(Group(parent, "Stepped foundation masonry - 顺山短挡墙与收口石"));
            for (int i = 0; i < 7; i++)
            {
                float x = -12.82f + i * 1.46f;
                float crown = LodgeHeight(x) - .14f;
                float yaw = Mathf.Sin(i * 2.3f) * 4f;
                float z = 27.89f + Mathf.Cos(i * 1.55f) * .11f;
                masonry.Box("Backing foundation stone", "Slate", new Vector3(x, crown - .60f, z), new Vector3(1.46f, 1.02f, .61f), Quaternion.Euler(0, yaw, 0), false);
                masonry.Box("Backing seam cap", "Stone", new Vector3(x, crown - .08f, z + .09f), new Vector3(1.51f, .19f, .65f), Quaternion.Euler(0, yaw, 0), false);
                masonry.Box("Recessed masonry belt", "Stone", new Vector3(x + .13f, crown - .39f, z + .30f), new Vector3(1.20f, .12f, .09f), Quaternion.Euler(0, yaw, 0), false);
            }
            masonry.Flush("Lodge_Contact_Masonry");
        }

        static void BuildLowerVestibule(Transform parent)
        {
            var b = new PaletteBatch(parent);
            b.Box("Slate threshold", "Slate", new Vector3(1.94f, -.12f, 14.56f), new Vector3(2.72f, .34f, 3.76f), true);
            b.Box("West retaining skirting", "Slate", new Vector3(.53f, .23f, 14.58f), new Vector3(.29f, .42f, 3.70f), true);
            b.Box("West paneled half wall", "Plaster", new Vector3(.55f, .85f, 14.58f), new Vector3(.19f, .87f, 3.63f), true);
            b.Box("West window cap", "Dark", new Vector3(.55f, 1.34f, 14.58f), new Vector3(.25f, .13f, 3.66f), true);
            foreach (float z in new[] { 12.80f, 13.62f, 14.53f, 15.44f, 16.32f })
                b.Box("Vestibule west pillar", "Red", new Vector3(.54f, 2.05f, z), new Vector3(.24f, 4.12f, .23f), true);
            foreach (float z in new[] { 13.04f, 13.26f, 13.49f, 13.84f, 14.06f, 14.29f, 14.75f, 14.98f, 15.19f, 15.65f, 15.87f, 16.10f })
                b.Box("Vestibule window lattice", "Timber", new Vector3(.55f, 2.30f, z), new Vector3(.12f, 1.86f, .057f));
            b.Box("Vestibule top plate", "Dark", new Vector3(.54f, 3.62f, 14.56f), new Vector3(.30f, .36f, 3.79f));
            b.Box("Open entry post", "Red", new Vector3(3.17f, 1.99f, 12.78f), new Vector3(.22f, 3.94f, .23f), true);
            b.Box("Open entry beam", "Dark", new Vector3(1.86f, 3.84f, 12.78f), new Vector3(2.86f, .28f, .29f));
            b.Box("Entry lantern body", "Paper", new Vector3(2.91f, 3.24f, 13.10f), new Vector3(.29f, .39f, .29f));
            b.Box("Entry lantern lower cap", "Dark", new Vector3(2.91f, 3.02f, 13.10f), new Vector3(.35f, .06f, .35f));
            b.Box("Entry lantern upper cap", "Dark", new Vector3(2.91f, 3.47f, 13.10f), new Vector3(.35f, .06f, .35f));
            b.Flush("Vestibule");

            var path = new[] { new Vector3(1.94f, .05f, 12.42f), new Vector3(1.94f, .05f, 16.49f) };
            var canopy = Loft(path, (i) => new[]
            {
                new Vector2(-1.67f, 3.85f), new Vector2(0, 4.61f), new Vector2(1.72f, 4.80f),
                new Vector2(1.72f, 4.58f), new Vector2(0, 4.39f), new Vector2(-1.67f, 3.63f)
            });
            var roof = Render(parent, "Lower_Annex_Connecting_Roof", canopy, "Roof");
            Collide(roof);
        }

        static void BuildGallery(Transform parent, string tag, Vector3[] path, float[] widths, bool ascent, Material roofInk,
            float endOverhang = 1.08f)
        {
            var floorMesh = Loft(path, i => FloorSection(widths[i], -.24f, 0f));
            var deck = Render(parent, tag + "_Indoor_Continuous_Floor", floorMesh, "Deck");
            Collide(deck);
            var trim = new PaletteBatch(Group(parent, "Paneled walls and timber ceiling - 护壁花窗与梁架"));
            foreach (int sign in new[] { -1, 1 })
            {
                float Offset(int i) => sign * widths[i];
                var panels = Loft(path, i => Section(Offset(i) - .087f, Offset(i) + .087f, -.07f, 1.00f));
                Render(parent, tag + "_Plaster_Window_Base_" + sign, panels, "Plaster");
                var sill = Loft(path, i => Section(Offset(i) - .11f, Offset(i) + .11f, .93f, 1.07f));
                Render(parent, tag + "_Continuous_Timber_Sill_" + sign, sill, "Dark");
                var header = Loft(path, i => Section(Offset(i) - .094f, Offset(i) + .094f, 2.64f, 3.17f));
                Render(parent, tag + "_Frieze_" + sign, header, "Plaster");
                var wallCollider = Loft(path, i => Section(Offset(i) - .085f, Offset(i) + .085f, -.08f, 3.17f));
                var guard = Group(parent, sign < 0 ? "Left wall collision - 左墙碰撞" : "Right wall collision - 右墙碰撞");
                guard.gameObject.AddComponent<MeshCollider>().sharedMesh = Save(tag + "_Wall_Collision_" + sign, wallCollider);
                for (int i = 0; i < path.Length - 1; i++)
                {
                    var a = Edge(path, i, Offset(i));
                    var b = Edge(path, i + 1, Offset(i + 1));
                    trim.Beam("Wall base strip", "Dark", a + Vector3.up * .16f, b + Vector3.up * .16f, .20f, .27f);
                    trim.Beam("Window head beam", "Red", a + Vector3.up * 2.64f, b + Vector3.up * 2.64f, .22f, .17f);
                    trim.Beam("Outer eave plate", "Dark", a + Vector3.up * 3.23f, b + Vector3.up * 3.23f, .24f, .24f);
                    int bays = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 1.85f));
                    for (int j = 0; j < bays; j++)
                    {
                        var post = Vector3.Lerp(a, b, j / (float)bays);
                        trim.Box("Red bay pillar", "Red", post + Vector3.up * 1.63f, new Vector3(.22f, 3.27f, .22f));
                        trim.Box("Carved post footing", "Stone", post + Vector3.up * .12f, new Vector3(.30f, .27f, .30f));
                        trim.Box("Capital", "Gold", post + Vector3.up * 3.06f, new Vector3(.26f, .09f, .26f));
                        for (int k = 1; k <= 4; k++)
                        {
                            var bar = Vector3.Lerp(a, b, (j + k / 5f) / bays);
                            trim.Box("Window lattice upright", "Timber", bar + Vector3.up * 1.86f, new Vector3(.065f, 1.57f, .065f));
                        }
                        if (j % 2 == 0)
                        {
                            Vector3 light = Vector3.Lerp(a, b, (j + .5f) / bays);
                            Vector3 intoHall = Vector3.Lerp(path[i], path[i + 1], (j + .5f) / bays) - light;
                            intoHall.y = 0;
                            AddLantern(trim, light + intoHall.normalized * .27f + Vector3.up * 2.65f);
                        }
                    }
                }
            }

            for (int i = 0; i < path.Length - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                var direction = b - a;
                direction.y = 0;
                var side = Vector3.Cross(direction.normalized, Vector3.up);
                int count = Mathf.Max(1, Mathf.CeilToInt(direction.magnitude / 1.87f));
                for (int j = 1; j <= count; j++)
                {
                    float t = (j - .5f) / count;
                    Vector3 p = Vector3.Lerp(a, b, t) + Vector3.up * 3.09f;
                    float width = Mathf.Lerp(widths[i], widths[i + 1], t);
                    trim.Beam("Ceiling tie beam", "Timber", p - side * width, p + side * width, .18f, .23f);
                }
            }
            trim.Flush(tag + "_Joinery");

            var roofPath = (Vector3[])path.Clone();
            if (ascent) roofPath[roofPath.Length - 1].x -= .30f;
            else
            {
                roofPath[0].x += .30f;
                roofPath[roofPath.Length - 1].x -= endOverhang;
            }
            float Eave(int index) => ascent && index < 2 ? (index == 0 ? 4.03f : 3.75f) : 3.27f;
            var roof = Loft(roofPath, i =>
            {
                float half = widths[i] + (widths[i] > 1.4f ? .14f : .23f);
                return RoofSection(half, Eave(i));
            });
            var renderedRoof = Render(parent, tag + "_Continuous_Tiled_Roof", roof, "Roof");
            Collide(renderedRoof);
            var shell = Group(renderedRoof, "Comic ink contour - 漫画描边");
            var roofMesh = renderedRoof.GetComponent<MeshFilter>().sharedMesh;
            var hull = ComicOutlineHullFactory.GetOrCreate(roofMesh, Root + "/Contours", out _);
            RenderOn(shell, hull, roofInk, false);
            var ridge = Loft(roofPath, i =>
            {
                float eave = Eave(i);
                return Section(-.06f, .06f, eave + 1.00f, eave + 1.10f);
            });
            Render(parent, tag + "_Ochre_Ridge_Inlay", ridge, "Gold");
        }

        static void BuildUpperAnnex(Transform upperWing, Transform originalHall)
        {
            var b = new PaletteBatch(Group(upperWing, "Ground annex and hall doorway - 一层东偏殿与通门"));
            const float floor = 1.58f;
            b.Box("Ground story deck", "Timber", new Vector3(-15.32f, floor - .13f, 18.99f), new Vector3(4.39f, .26f, 6.0f), true);
            b.Box("Ground story stone plinth", "Slate", new Vector3(-15.26f, 1.12f, 19f), new Vector3(4.31f, .64f, 6.0f), true);
            b.Box("Ground story lintel and ceiling", "Dark", new Vector3(-15.36f, 4.95f, 19), new Vector3(4.45f, .19f, 6.10f), true);
            b.Box("East ground exterior wall", "Plaster", new Vector3(-13.20f, 3.29f, 19f), new Vector3(.24f, 3.43f, 6.0f), true);
            b.Box("Ground exterior wall base", "Dark", new Vector3(-13.20f, 1.93f, 19), new Vector3(.29f, .63f, 6.0f));
            b.Box("North ground exterior wall", "Plaster", new Vector3(-15.33f, 3.29f, 21.99f), new Vector3(4.47f, 3.43f, .23f), true);
            b.Box("Upper hall veranda connection", "Timber", new Vector3(-15.68f, 1.474f, 15.55f), new Vector3(3.25f, .20f, 1.04f), true);
            b.Box("Upper hall entry step", "Stone", new Vector3(-15.68f, 1.38f, 14.97f), new Vector3(2.30f, .13f, .45f), true);
            foreach (float x in new[] { -17.40f, -16.37f, -14.54f, -13.24f })
            {
                b.Box("Ground entrance support", "Red", new Vector3(x, 3.24f, 16.03f), new Vector3(.24f, 3.33f, .24f), true);
                b.Box("Ground entrance stone foot", "Stone", new Vector3(x, 1.72f, 16.03f), new Vector3(.34f, .28f, .34f));
            }
            b.Box("Ground entry west return", "Plaster", new Vector3(-16.89f, 3.28f, 16.03f), new Vector3(1.03f, 3.40f, .19f), true);
            b.Box("Ground entry east return", "Plaster", new Vector3(-13.88f, 3.28f, 16.03f), new Vector3(1.29f, 3.40f, .19f), true);
            b.Box("Ground entry lintel", "Dark", new Vector3(-15.39f, 4.43f, 16.03f), new Vector3(4.45f, .22f, .28f));
            b.Box("Ground entry valance", "Gold", new Vector3(-15.39f, 4.27f, 15.87f), new Vector3(1.65f, .07f, .06f));
            AddLantern(b, new Vector3(-15.38f, 4.02f, 16.46f));

            foreach (float z in new[] { 9.36f, 11.14f, 13.30f, 15.33f })
            {
                float top = WestHeight(z) - .16f;
                foreach (float x in new[] { -15.49f, -13.72f })
                {
                    b.Box("Gallery stone pier", "Stone", new Vector3(x, UpperFloor + .17f, z), new Vector3(.39f, .38f, .38f), true);
                    b.Box("Gallery lower column", "Red", new Vector3(x, (UpperFloor + .30f + top) * .5f, z),
                        new Vector3(.22f, top - UpperFloor - .30f, .24f), true);
                    b.Box("Gallery timber bracket", "Dark", new Vector3(x, top - .18f, z), new Vector3(.39f, .30f, .44f));
                }
                b.Box("Gallery transverse sill", "Timber", new Vector3(-14.60f, top, z), new Vector3(2.35f, .24f, .33f));
            }

            // Expand the original eastern paper window into a real doorway.
            foreach (var t in originalHall.GetComponentsInChildren<Transform>(true))
                if (t.name == "Surface - GrandHall_07_RightWall_Plaster" || t.name.StartsWith("Surface - GrandHall_08_RightWindow1_")
                    || t.name == "Surface - GrandHall_20_LowerWallBelts_Timber") Retire(t);
            WallPanel(b, 16.12f, 17.22f, floor, 5.40f);
            WallPanel(b, 17.22f, 19.16f, 4.21f, 5.40f);
            WallPanel(b, 19.16f, 19.42f, floor, 5.40f);
            WallPanel(b, 19.42f, 21.08f, floor, 2.77f);
            WallPanel(b, 19.42f, 21.08f, 4.50f, 5.40f);
            WallPanel(b, 21.08f, 21.88f, floor, 5.40f);
            foreach (float z in new[] { 17.22f, 19.16f })
                b.Box("East hall portal jamb", "Red", new Vector3(-17.52f, 2.89f, z), new Vector3(.33f, 2.62f, .19f), true);
            b.Box("East hall portal lintel", "Timber", new Vector3(-17.52f, 4.23f, 18.19f), new Vector3(.37f, .21f, 2.15f));
            b.Box("East hall flush threshold", "Stone", new Vector3(-17.50f, 1.54f, 18.19f), new Vector3(.52f, .086f, 1.80f), true);
            b.Box("Left restored wall belt", "Timber", new Vector3(-33.425f, 2.217f, 19), new Vector3(.12f, .30f, 5.80f), true);
            b.Box("Rear restored wall belt", "Timber", new Vector3(-25.40f, 2.217f, 22.025f), new Vector3(15.80f, .30f, .12f), true);
            b.Box("East belt before doorway", "Timber", new Vector3(-17.375f, 2.217f, 16.65f), new Vector3(.12f, .30f, 1.10f), true);
            b.Box("East belt after doorway", "Timber", new Vector3(-17.375f, 2.217f, 20.56f), new Vector3(.12f, .30f, 2.68f), true);
            b.Flush("Upper_Annex");
        }

        static void WallPanel(PaletteBatch batch, float near, float far, float low, float high)
            => batch.Box("Upper hall doorway plaster", "Plaster", new Vector3(-17.52f, (low + high) * .5f, (near + far) * .5f),
                new Vector3(.24f, high - low, far - near), true);

        static void AddBackingPines(Transform rock, Transform dressing, bool brokenStrata = false)
        {
            var pine = dressing.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Mountain pine 036");
            if (!pine) return;
            var trees = Group(rock.parent, brokenStrata ? "Refined shoulder pines - 新岩脊松林" : "Broken ledge pines - 岩肩松林");
            if (brokenStrata) Undo.RegisterCreatedObjectUndo(trees.gameObject, "Place mountain shoulder pines");
            var points = brokenStrata ? new[]
            {
                new Vector2(-25,32), new Vector2(-23,34), new Vector2(-21,36), new Vector2(-19,32.5f),
                new Vector2(-16.5f,33.3f), new Vector2(-15,36.2f), new Vector2(-11,31.8f), new Vector2(-8,36.7f),
                new Vector2(-5,32.5f), new Vector2(-3,35.5f), new Vector2(1,33.6f), new Vector2(4,36.2f),
                new Vector2(7,37), new Vector2(9,41)
            } : new[]
            {
                new Vector2(-22,34), new Vector2(-22,35.5f), new Vector2(-19,32.5f), new Vector2(-16,32.5f),
                new Vector2(-8,31), new Vector2(-4,35.5f), new Vector2(0,32.5f), new Vector2(4,35.5f),
                new Vector2(4,37), new Vector2(8,39)
            };
            var surface = rock.gameObject.AddComponent<MeshCollider>();
            surface.sharedMesh = rock.GetComponent<MeshFilter>().sharedMesh;
            Physics.SyncTransforms();
            try
            {
                for (int i = 0; i < points.Length; i++)
                {
                    var origin = new Vector3(points[i].x, 41, points[i].y);
                    if (!surface.Raycast(new Ray(origin, Vector3.down), out var hit, 40f) || hit.normal.y < .48f
                        || (brokenStrata && (hit.point.y < 4.8f || hit.point.y > 25f))) continue;
                    var tree = UnityEngine.Object.Instantiate(pine.gameObject, trees);
                    tree.name = "Ledge pine " + (i + 1).ToString("D2");
                    tree.transform.SetPositionAndRotation(hit.point - Vector3.up * .11f, Quaternion.Euler(0, 47f * i, 0));
                    tree.transform.localScale = pine.lossyScale * (.31f + .052f * (i % 5));
                    foreach (var collider in tree.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(surface); }
        }

        static void AddLantern(PaletteBatch batch, Vector3 center)
        {
            batch.Box("Paper lantern", "Paper", center, new Vector3(.22f, .34f, .22f));
            foreach (int sign in new[] { -1, 1 })
                batch.Box("Lantern cap", "Dark", center + Vector3.up * sign * .19f, new Vector3(.27f, .055f, .27f));
            batch.Box("Lantern hanger", "Dark", center + Vector3.up * .36f, new Vector3(.033f, .30f, .033f));
        }

        static Vector2[] FloorSection(float half, float bottom, float top) => new[]
        {
            new Vector2(-half, top), new Vector2(0, top), new Vector2(half, top),
            new Vector2(half, bottom), new Vector2(0, bottom), new Vector2(-half, bottom)
        };

        static Vector2[] Section(float left, float right, float bottom, float top) => new[]
        {
            new Vector2(left, top), new Vector2(right, top), new Vector2(right, bottom), new Vector2(left, bottom)
        };

        static Vector2[] RoofSection(float half, float eave) => new[]
        {
            new Vector2(-half, eave + .03f), new Vector2(-half * .66f, eave + .24f), new Vector2(0, eave + 1.02f),
            new Vector2(half * .66f, eave + .24f), new Vector2(half, eave + .03f),
            new Vector2(half, eave - .15f), new Vector2(half * .66f, eave + .06f), new Vector2(0, eave + .84f),
            new Vector2(-half * .66f, eave + .06f), new Vector2(-half, eave - .15f)
        };

        static Vector3 Edge(Vector3[] path, int i, float offset)
        {
            Vector3 incoming = i == 0 ? path[1] - path[0] : path[i] - path[i - 1];
            Vector3 outgoing = i == path.Length - 1 ? incoming : path[i + 1] - path[i];
            incoming.y = outgoing.y = 0;
            Vector3 a = Vector3.Cross(incoming.normalized, Vector3.up);
            Vector3 b = Vector3.Cross(outgoing.normalized, Vector3.up);
            Vector3 miter = (a + b).normalized;
            return path[i] + miter * (offset / Vector3.Dot(miter, b));
        }

        static Mesh Loft(Vector3[] path, Func<int, Vector2[]> section)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            int width = section(0).Length;
            for (int i = 0; i < path.Length; i++)
                foreach (var point in section(i)) vertices.Add(Edge(path, i, point.x) + Vector3.up * point.y);
            for (int i = 0; i < path.Length - 1; i++)
                for (int j = 0; j < width; j++)
                {
                    int a = i * width + j, b = i * width + (j + 1) % width;
                    triangles.AddRange(new[] { a, b, b + width, a, b + width, a + width });
                }
            // Each section is two corresponding polylines. Cap matching spans, including the roof ridge.
            int last = (path.Length - 1) * width;
            for (int j = 0; j < width / 2 - 1; j++)
            {
                int k = width - 1 - j;
                triangles.AddRange(new[] { j, k, k - 1, j, k - 1, j + 1 });
                triangles.AddRange(new[] { last + j, last + j + 1, last + k - 1, last + j, last + k - 1, last + k });
            }
            return MeshFrom(vertices, triangles);
        }

        static Mesh MeshFrom(List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh();
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Transform Group(Transform parent, string name)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            return node;
        }

        static void Retire(Transform item)
        {
            if (!item || !item.gameObject.activeSelf) return;
            Undo.RecordObject(item.gameObject, "Retain replaced architectural component");
            item.gameObject.SetActive(false);
        }

        static void Collide(Transform surface)
        {
            var mesh = surface.GetComponent<MeshFilter>().sharedMesh;
            if (!CourtyardRoadMeshBuilder.Audit(mesh).closedPositiveVolume)
                throw new InvalidOperationException("Invalid architectural collision mesh: " + mesh.name);
            surface.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        static Mesh Save(string name, Mesh mesh)
        {
            string path = Root + "/Meshes/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                return existing;
            }
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material MaterialFor(string key)
        {
            if (key == "Rock") return BackingStone();
            string path = key == "Deck" ? SamuraiLevelFlowSetup.Root + "/Materials/Bridge_landmark_timber.mat"
                : key == "Dark" ? SamuraiLevelFlowSetup.Root + "/Materials/Deep_gate_timber.mat"
                : SamuraiCourtyardDecorationSetup.Root + "/Materials/" + Decoration[key] + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) throw new InvalidOperationException("Missing hall material: " + path);
            return material;
        }

        static Material BackingStone()
        {
            const string path = Root + "/Mountain_Rock_Ledges.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            var source = AssetDatabase.LoadAssetAtPath<Material>(SamuraiComicStyleSetup.Root + "/Mountains/Materials/Rock_Strata_Near.mat");
            if (!source) throw new InvalidOperationException("Missing near mountain rock material.");
            material = new Material(source) { name = "Mountain Rock Ledges" };
            material.SetColor("_BaseColor", new Color(.56f, .59f, .55f));
            material.SetFloat("_StrataStrength", .18f);
            material.SetFloat("_StrataScale", .15f);
            material.SetFloat("_SeamStrength", .19f);
            material.SetFloat("_ErosionStrength", .12f);
            material.SetFloat("_MossAmount", .75f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Transform Render(Transform parent, string name, Mesh mesh, string palette)
        {
            var node = Group(parent, name);
            RenderOn(node, Save(name, mesh), MaterialFor(palette), true);
            return node;
        }

        static void RenderOn(Transform node, Mesh mesh, Material material, bool cast)
        {
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = cast;
            GameObjectUtility.SetStaticEditorFlags(node.gameObject, StaticEditorFlags.BatchingStatic);
        }

        sealed class PaletteBatch
        {
            readonly Transform root;
            readonly Dictionary<string, List<CombineInstance>> colors = new Dictionary<string, List<CombineInstance>>();
            readonly Mesh cube;

            public PaletteBatch(Transform parent)
            {
                root = parent;
                cube = AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root + "/Meshes/Shapes/Block.asset");
                if (!cube) throw new InvalidOperationException("Missing courtyard Block mesh.");
            }

            public void Box(string name, string palette, Vector3 center, Vector3 size, bool collider = false)
                => Box(name, palette, center, size, Quaternion.identity, collider);

            public void Box(string name, string palette, Vector3 center, Vector3 size, Quaternion rotation, bool collider)
            {
                if (!colors.TryGetValue(palette, out var parts)) colors.Add(palette, parts = new List<CombineInstance>());
                parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(center, rotation, size) });
                if (collider) CollisionBox(name, center, size, rotation);
            }

            public void CollisionBox(string name, Vector3 center, Vector3 size, Quaternion rotation)
            {
                var node = Group(root, name);
                node.localPosition = center;
                node.localRotation = rotation;
                node.gameObject.AddComponent<BoxCollider>().size = size;
            }

            public void Beam(string name, string palette, Vector3 a, Vector3 b, float depth, float height)
            {
                Vector3 direction = b - a;
                Box(name, palette, (a + b) * .5f, new Vector3(depth, height, direction.magnitude), Quaternion.LookRotation(direction), false);
            }

            public void Flush(string tag)
            {
                foreach (var color in colors)
                {
                    var mesh = new Mesh();
                    if (color.Value.Count * cube.vertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.CombineMeshes(color.Value.ToArray(), true, true);
                    Render(root, tag + "_" + color.Key, mesh, color.Key);
                }
            }
        }
    }
}
