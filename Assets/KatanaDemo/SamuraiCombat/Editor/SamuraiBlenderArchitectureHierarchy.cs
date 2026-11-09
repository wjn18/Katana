using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Pairs the imported building meshes with their existing scene pieces.</summary>
    public static class SamuraiBlenderArchitectureHierarchy
    {
        const string Action = "Group courtyard assemblies";
        const string PlinthMeshes = SamuraiComicStyleSetup.Root + "/BlenderAssembly/LowerCourtPlinthPartition.asset";

        public static int Group(Scene scene, Transform dressing, Transform zones)
        {
            var display = scene.GetRootGameObjects()
                .Single(root => root.name == "Blender architecture - Blender 建筑装配").transform;
            var view = display.Cast<Transform>().ToDictionary(item => item.name, StringComparer.Ordinal);
            Transform Visual(string name)
            {
                if (!view.Remove(name, out var node))
                    throw new InvalidOperationException($"Missing or repeated Blender assembly {name}.");
                return node;
            }

            var upper = Child(zones, "01 Elite courtyard");
            var lower = Child(zones, "02 First courtyard");
            var mountain = Child(zones, "03 Mountain halls");
            var bridge = Child(zones, "04 Bridge crossing");
            var gardens = Child(zones, "05 Outer gardens");
            var terrain = Child(zones, "06 Terrain and setting");
            var upperLegacy = Child(dressing, "01 Upper courtyard");
            var lowerLegacy = Child(dressing, "02 Lower courtyard");
            var upperArchitecture = Child(upperLegacy, "02 Architecture");
            var lowerArchitecture = Child(lowerLegacy, "02 Architecture");
            var foundations = Child(dressing, "06 Architecture sites");
            var compound = Child(dressing, "07 Mountain compound");
            var access = Child(compound, "03 Inner court access");
            var gates = Child(compound, "04 Bridge and garden portals");
            var first = Child(dressing, "08 First courtyard");
            var rooms = Child(first, "01 Rooms and great hall");
            var circulation = Child(first, "02 Circulation and planted court");
            var treasures = Child(first, "03 Treasure reservations");
            var checkpoint = first.GetComponentsInChildren<SamuraiCourtyardCheckpoint>(true).Single();
            var roads = Child(dressing, "09 Continuous roads");
            var elite = Child(dressing, "13 Elite grand hall");
            var eliteWall = Child(dressing, "15 Elite court enclosure");
            var lowerHall = Child(dressing, "17 Lower grand hall");
            var route = dressing.GetComponentsInChildren<SamuraiLevelRoute>(true).Single();

            int count = 0;
            Transform GroupParts(Transform area, string name, Transform pivot, params Transform[] parts)
            {
                var kit = NewKit(area, name, pivot.position);
                Move(kit, parts);
                count++;
                return kit;
            }

            Transform Match(Transform area, string name, string displayName, Transform source, params Transform[] extras)
            {
                var visuals = Visual(displayName);
                var pivot = PickPivot(source, visuals);
                var kit = NewKit(area, name, pivot);
                Move(kit, new[] { source, visuals }.Concat(extras));
                count++;
                return kit;
            }

            var eliteHall = Match(upper, "Elite1 Grand Hall - 精英大殿与西阳台", "Elite1 Grand Hall",
                Child(elite, "01 Two-storey grand hall"), Child(foundations, "04 Main practice hall"));
            var drop = Child(elite, "04 West balcony grass drop");
            Move(eliteHall, new[] { Child(drop, "West balcony drop") });
            Match(upper, "Hall approach - 上殿石径", "Court Access Upper hall approach", Child(access, "Upper hall approach"));

            var inner = NewKit(upper, "Inner wall - 内院围墙",
                PickWallPivot(eliteWall, view.Where(entry => entry.Key.StartsWith("Elite1 Inner Wall ")).Select(entry => entry.Value)));
            foreach (var run in eliteWall.Cast<Transform>().Where(item => item.gameObject.activeInHierarchy).ToArray())
                Match(inner, run.name, "Elite1 Inner Wall " + run.name.Substring(0, 2), run);

            Match(mountain, "Elite1 Hall Passage - 主殿连廊", "Elite1 Hall Passage", Child(elite, "02 Recessed lodge link"));
            Match(mountain, "Ridge Viewing Lodge - 观峡大屋", "Ridge Viewing Lodge",
                Child(dressing, "11 Ridge trail lodge"), Child(elite, "03 Lodge rock contact"));
            Match(mountain, "Curved Mountain Gallery - 下院弧廊", "Curved Mountain Gallery", Child(lowerHall, "05 Curved mountain gallery"));

            var lowerKit = Match(lower, "Lower Grand Hall - 下院大殿", "Lower Grand Hall", Child(rooms, "Great hall"),
                Child(lowerHall, "01 Upper hall and eaves"), Child(lowerHall, "02 Grand hall stair"));
            var plinth = Child(circulation, "Great hall retaining plinth");
            SplitHallPlinth(lowerKit, plinth, Child(view["Lower Courtyard Hardscape"], "B009_Surface"));
            var island = Child(circulation, "Central planted island");
            var islandKit = NewKit(lower, "Central garden - 中庭孤松与石庭", GetBounds(island).center);
            SplitCentralIsland(islandKit, island, view["Lower Courtyard Hardscape"]);
            count++;
            GroupParts(lowerKit, "West keyed door - 西门与门轴", route.sideGateRoot, route.sideGateRoot);
            GroupParts(lowerKit, "Mountain Pass Key - 钥匙与拾取点", route.keyRoot, route.keyRoot);
            var lowerTreasure = Child(treasures, "02 Great hall treasury");
            GroupParts(lowerKit, "Treasury - 殿内藏物点", lowerTreasure, lowerTreasure);

            var prayer = Match(lower, "West prayer room - 西南祈愿室与柱廊", "West prayer room",
                Child(rooms, "West prayer room"), Child(rooms, "West covered walk -6"), Visual("West covered walk -6"));
            GroupParts(prayer, "Prayer shrine - 存档神龛与复活点", checkpoint.transform, checkpoint.transform, checkpoint.respawnAnchor);
            var store = Match(lower, "West store room - 西北藏物室与柱廊", "West store room",
                Child(rooms, "West store room"), Child(rooms, "West covered walk 5"), Visual("West covered walk 5"));
            var westTreasure = Child(treasures, "01 West store treasure");
            GroupParts(store, "Treasure - 藏物点", westTreasure, westTreasure);
            Match(lower, "East guard room - 东南守卫房与柱廊", "East guard room",
                Child(rooms, "East guard room"), Child(rooms, "East covered walk 0"), Visual("East covered walk 0"));
            Match(lower, "East study room - 东北书房与柱廊", "East study room",
                Child(rooms, "East study room"), Child(rooms, "East covered walk 7"), Visual("East covered walk 7"));
            Match(lower, "Landmark Pagoda - 引路高塔", "Landmark Pagoda", Child(dressing, "14 Hall landmark pagoda"));
            Match(lower, "Entry gate and stair - 主入口门楼与石阶", "Main Entrance Gate",
                Child(lowerArchitecture, "06 Entry gate"), Child(lowerArchitecture, "08 Entrance stone stairs"), Visual("Entrance Stone Stair"));

            var bridgeDeck = Child(dressing, "04 Connecting bridge");
            var crossing = NewKit(bridge, "Bridge and gates - 连桥与两端门楼", PickPivot(bridgeDeck, view["Courtyard Bridge"]));
            Match(crossing, "Courtyard Bridge - 木桥与两端支承", "Courtyard Bridge", bridgeDeck);
            Match(crossing, "Upper gate - 上桥口与内闩门", "Upper bridge portal",
                Child(gates, "Upper bridge portal"), route.bridgeGateRoot);
            Match(crossing, "Lower gate - 下桥口门楼", "Lower bridge portal", Child(gates, "Lower bridge portal"));

            Match(gardens, "Mountain Storehouse - 山侧仓房与石基", "Mountain Storehouse",
                Child(upperArchitecture, "09 Mountain storehouse"), Child(foundations, "09 Mountain storehouse"));
            Match(gardens, "Tea Pavilion - 茶亭与石基", "Tea Pavilion",
                Child(upperArchitecture, "10 Tea pavilion"), Child(foundations, "10 Tea pavilion"));
            Match(gardens, "Tea garden gate - 茶庭侧门与下行阶", "Tea garden side gate",
                Child(gates, "Tea garden side gate"), Child(access, "Garden descending steps"), Visual("Court Access Garden descending steps"));
            Match(gardens, "East Residence - 东侧廊屋与石基", "East Residence",
                Child(lowerArchitecture, "09 Side residence"), Child(foundations, "09 Side residence"));

            void GroupWall(string prefix, string label)
            {
                var oldWall = Child(compound, prefix);
                var wallDisplay = prefix.StartsWith("01") ? "Upper Outer Wall " : "Lower Outer Wall ";
                var destination = NewKit(gardens, label,
                    PickWallPivot(oldWall, view.Where(entry => entry.Key.StartsWith(wallDisplay)).Select(entry => entry.Value)));
                var spans = oldWall.Cast<Transform>().Where(item => item.gameObject.activeInHierarchy).ToArray();
                // Use the same span boundaries as the Blender exporter.
                for (int i = 0; i < spans.Length; i += 18)
                {
                    var displayName = wallDisplay + (i / 18 + 1).ToString("D2");
                    Match(destination, displayName, displayName, spans[i], spans.Skip(i + 1).Take(17).ToArray());
                }
            }
            GroupWall("01 Upper enclosure", "Upper enclosure - 上峰外墙");
            GroupWall("02 Lower enclosure", "Lower enclosure - 下峰外墙");

            // Keep continuous baked areas and the corresponding source pieces on the same parent.
            var gardenWalks = Child(compound, "05 Garden walks");
            Match(gardens, "Connected garden paths - 外园连续园径与台阶", "Garden Walks and Steps",
                gardenWalks, Child(roads, "02 Garden paths"), Visual("Continuous 02 Garden paths"));
            Match(lower, "Continuous paths - 下院连续路网", "Continuous 01 First courtyard network", Child(roads, "01 First courtyard network"));
            Match(terrain, "Courtyard Ground - 两院地坪与挡土基础", "Courtyard Ground and Retaining Walls",
                scene.GetRootGameObjects().Single(root => root.name == "Floor").transform);
            Match(upper, "Upper court hardscape - 上院场地石作", "Upper Courtyard Hardscape", Child(upperLegacy, "01 Structure"));
            Match(lower, "Lower court hardscape - 下院场地石作", "Lower Courtyard Hardscape",
                Child(lowerLegacy, "01 Structure"), circulation);

            var details = Child(compound, "06 Arrival details");
            var detailKit = Match(gardens, "Approach stonework - 入院石作与接地灯座", "Entry and Garden Stonework", details);
            Match(detailKit, "Upper site lantern set - 上院灯组", "Upper Courtyard Stone Lanterns", Child(upperLegacy, "03 Stone lanterns"));
            Match(detailKit, "Lower site lantern set - 下院灯组", "Lower Courtyard Stone Lanterns", Child(lowerLegacy, "03 Stone lanterns"));

            if (view.Count != 0)
                throw new InvalidOperationException("Unpaired Blender assemblies: " + string.Join(", ", view.Keys));
            return count;
        }

        static Transform Child(Transform parent, string prefix)
        {
            var matches = parent.Cast<Transform>()
                .Where(item => item.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected one {prefix} under {parent.name}, found {matches.Length}.");
            return matches[0];
        }

        static Transform NewKit(Transform parent, string name, Vector3 worldPivot)
        {
            var kit = new GameObject(name).transform;
            Undo.RegisterCreatedObjectUndo(kit.gameObject, Action);
            kit.SetParent(parent, false);
            kit.position = worldPivot;
            return kit;
        }

        static Vector3 PickPivot(Transform source, Transform display)
        {
            var bounds = GetBounds(display);
            var padded = bounds;
            padded.Expand(2f);
            return padded.Contains(source.position)
                ? source.position
                : new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        static Bounds GetBounds(Transform node)
        {
            var renderers = node.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) throw new InvalidOperationException($"No mesh bounds for {node.name}.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static Vector3 PickWallPivot(Transform source, IEnumerable<Transform> displays)
        {
            var pivot = source.position;
            if (pivot.x * pivot.x + pivot.z * pivot.z > .0001f) return pivot;
            var bounds = displays.Select(GetBounds).ToArray();
            var whole = bounds[0];
            foreach (var span in bounds.Skip(1)) whole.Encapsulate(span);
            var footings = bounds.Select(span => span.min.y).OrderBy(y => y).ToArray();
            return new Vector3(whole.center.x, footings[footings.Length / 2], whole.center.z);
        }

        static void Move(Transform parent, IEnumerable<Transform> items)
        {
            foreach (var item in items)
            {
                if (!item) throw new InvalidOperationException($"Missing source for {parent.name}.");
                var prefab = PrefabUtility.GetOutermostPrefabInstanceRoot(item.gameObject);
                if (prefab && prefab != item.gameObject)
                    throw new InvalidOperationException($"Cannot move only part of prefab {prefab.name} ({item.name}).");
                Undo.SetTransformParent(item, parent, Action);
                if (prefab) PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            }
        }

        static void SplitHallPlinth(Transform hall, Transform plinth, Transform assembled)
        {
            var collider = plinth.GetComponentsInChildren<BoxCollider>().Single();
            const float tolerance = .0001f;
            var localBounds = new Bounds(collider.center, collider.size);
            var expanded = localBounds;
            expanded.Expand(2f * tolerance);
            bool OnPlinth(Vector3 a, Vector3 b, Vector3 c)
            {
                var toPlinth = collider.transform.worldToLocalMatrix;
                a = toPlinth.MultiplyPoint3x4(a);
                b = toPlinth.MultiplyPoint3x4(b);
                c = toPlinth.MultiplyPoint3x4(c);
                if (!expanded.Contains(a) || !expanded.Contains(b) || !expanded.Contains(c)) return false;
                for (int axis = 0; axis < 3; axis++)
                {
                    bool OnPlane(float plane) =>
                        Mathf.Abs(a[axis] - plane) <= tolerance &&
                        Mathf.Abs(b[axis] - plane) <= tolerance &&
                        Mathf.Abs(c[axis] - plane) <= tolerance;
                    if (OnPlane(localBounds.min[axis]) || OnPlane(localBounds.max[axis])) return true;
                }
                return false;
            }
            var display = SplitDisplay(assembled, PlinthMeshes, "Stone plinth display - 石台渲染", 12, OnPlinth);
            if (!display.GetComponent<MeshRenderer>().sharedMaterials.Single().name.StartsWith("Foundation_slate", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected the stone material on the hall plinth.");
            Move(hall, new[] { plinth, display });
        }

        static void SplitCentralIsland(Transform kit, Transform island, Transform assembled)
        {
            var bounds = GetBounds(island);
            bounds.Expand(.003f);
            bool OnIsland(Vector3 a, Vector3 b, Vector3 c) => bounds.Contains(a) && bounds.Contains(b) && bounds.Contains(c);
            const string folder = SamuraiComicStyleSetup.Root + "/BlenderAssembly/";
            var bed = SplitDisplay(Child(assembled, "B009_Surface"), folder + "CentralIslandBedPartition.asset",
                "Island bed and pine - 石庭池身与孤松", 228, OnIsland);
            var originalStone = Child(assembled, "B010_Surface");
            var stone = SplitDisplay(originalStone, folder + "CentralIslandStonePartition.asset",
                "Island stone - 石庭散石", 36, OnIsland);
            var ink = SplitDisplay(Child(originalStone, "B127_Ink"), folder + "CentralIslandInkPartition.asset",
                "Island stone ink - 散石描边", 36, OnIsland);
            Move(stone, new[] { ink });
            Move(kit, new[] { island, bed, stone });
        }

        static Transform SplitDisplay(Transform assembled, string asset, string name, int expectedTriangles,
            Func<Vector3, Vector3, Vector3, bool> matches)
        {
            var filter = assembled.GetComponent<MeshFilter>();
            var surface = assembled.GetComponent<MeshRenderer>();
            var mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            var world = vertices.Select(assembled.TransformPoint).ToArray();
            var allIndices = Enumerable.Range(0, mesh.subMeshCount).Select(mesh.GetTriangles).ToArray();
            var keep = allIndices.Select(_ => new List<int>()).ToArray();
            var extracted = allIndices.Select(_ => new List<int>()).ToArray();
            for (int sub = 0; sub < allIndices.Length; sub++)
            {
                var triangles = allIndices[sub];
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    var destination = matches(world[a], world[b], world[c]) ? extracted[sub] : keep[sub];
                    destination.Add(a);
                    destination.Add(b);
                    destination.Add(c);
                }
            }
            int extractedCount = extracted.Sum(sub => sub.Count);
            if (extractedCount != 3 * expectedTriangles)
                throw new InvalidOperationException($"Expected {expectedTriangles} triangles for {name}, found {extractedCount / 3}.");
            var used = Enumerable.Range(0, extracted.Length).Where(sub => extracted[sub].Count > 0).ToArray();
            var meshes = AssetDatabase.LoadAllAssetsAtPath(asset).OfType<Mesh>().ToArray();
            Mesh remaining, separate;
            if (meshes.Length == 0)
            {
                remaining = UnityEngine.Object.Instantiate(mesh);
                remaining.name = "Remaining courtyard display";
                for (int sub = 0; sub < keep.Length; sub++)
                    remaining.SetTriangles(keep[sub], sub, false);

                var ids = extracted.SelectMany(sub => sub).Distinct().ToArray();
                var remap = ids.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index);
                var normals = mesh.normals;
                var uv = mesh.uv;
                separate = new Mesh
                {
                    name = name,
                    vertices = ids.Select(id => vertices[id]).ToArray(),
                    normals = ids.Select(id => normals[id]).ToArray(),
                    uv = ids.Select(id => uv[id]).ToArray(),
                    subMeshCount = used.Length,
                };
                for (int i = 0; i < used.Length; i++)
                    separate.SetTriangles(extracted[used[i]].Select(id => remap[id]).ToArray(), i);
                AssetDatabase.CreateAsset(remaining, asset);
                AssetDatabase.AddObjectToAsset(separate, asset);
                AssetDatabase.SaveAssets();
            }
            else
            {
                remaining = meshes.Single(item => item.name == "Remaining courtyard display");
                separate = meshes.Single(item => item.name == name);
                if (meshes.Length != 2 || remaining.triangles.Length != mesh.triangles.Length - extractedCount ||
                    separate.triangles.Length != extractedCount || separate.subMeshCount != used.Length)
                    throw new InvalidOperationException($"Existing partition has unexpected topology for {name}.");
            }

            Undo.RecordObject(filter, Action);
            filter.sharedMesh = remaining;
            var visual = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            Undo.RegisterCreatedObjectUndo(visual, Action);
            visual.transform.SetParent(assembled, false);
            visual.GetComponent<MeshFilter>().sharedMesh = separate;
            var display = visual.GetComponent<MeshRenderer>();
            display.sharedMaterials = used.Select(sub => surface.sharedMaterials[sub]).ToArray();
            display.shadowCastingMode = surface.shadowCastingMode;
            display.receiveShadows = surface.receiveShadows;
            display.lightProbeUsage = surface.lightProbeUsage;
            display.reflectionProbeUsage = surface.reflectionProbeUsage;
            display.renderingLayerMask = surface.renderingLayerMask;
            visual.layer = assembled.gameObject.layer;
            GameObjectUtility.SetStaticEditorFlags(visual, GameObjectUtility.GetStaticEditorFlags(assembled.gameObject));
            return visual.transform;
        }
    }
}
