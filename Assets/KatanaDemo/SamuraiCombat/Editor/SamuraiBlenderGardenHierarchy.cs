using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiBlenderGardenHierarchy
    {
        const string TreeDetails = "21 Tree details - 树根与幼松";
        const string PlantingBeds = "Understory pines - 石边幼松";
        const string GardenParts = "Editable components - 可调部件";
        const string RockSurface = "01 Rock bed - 山石地基/Surface - Foundation_slate";
        const string RaisedGrassPrefix = "Raised garden bed ";
        const string RootDetailsSuffix = " - 根盘与落叶";
        const string MoveAction = "Organize local garden assemblies";

        static readonly Dictionary<string, string> NearbyGround = new(StringComparer.Ordinal)
        {
            { "West garden foot", "Copper maple garden 1" },
            { "Upper south shoulder", "Ink pine garden 1" },
            { "East garden shoulder", "Ink pine garden 3" },
            { "Tower garden foot", "Copper maple garden 4" }
        };

        public static string Group(Scene scene, Transform dressing, Transform zones)
        {
            if (scene.path != SamuraiBlenderAssemblyImport.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the Blender assembly scene in Edit mode.");
            var outer = Require(zones.Find("05 Outer gardens - 外园与围墙"), "outer garden zone");
            var mountain = Require(zones.Find("03 Mountain halls - 山间连廊"), "mountain hall zone");
            var roots = scene.GetRootGameObjects();
            var grassRoot = roots.FirstOrDefault(g => g.name == SamuraiBlenderGardenDetails.RootName)?.transform;
            var details = dressing.Cast<Transform>().FirstOrDefault(t => t.name == TreeDetails && t.Find(PlantingBeds));
            var gardens = dressing.GetComponentsInChildren<Transform>(true)
                .Where(t => IsGardenName(t.name) && t.Find(GardenParts))
                .ToDictionary(t => t.name, StringComparer.Ordinal);
            if (gardens.Count != 8)
                throw new InvalidOperationException("Expected the eight existing tree gardens.");
            if (!grassRoot && !details && gardens.Values.All(g => g.IsChildOf(zones)))
                return "Garden compositions already grouped.";
            Require(grassRoot, "grass details");
            Require(details, "tree details");
            var understory = Require(details.Find(PlantingBeds), "understory pine group");
            var pines = understory.Cast<Transform>().ToDictionary(t => t.name, StringComparer.Ordinal);
            var forest = dressing.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "Mountain pine 042" || t.name == "Mountain pine 043")
                .OrderBy(t => t.name, StringComparer.Ordinal).ToArray();
            if (forest.Length != 2 || pines.Count != 7)
                throw new InvalidOperationException("Expected the refined pine pair and seven understory pines.");

            var stoneBeds = gardens.Values.Where(g => g.Find(GardenParts + "/02 Ground cover - 地被")).ToArray();
            var raised = grassRoot.Cast<Transform>().Where(t => t.name.StartsWith(RaisedGrassPrefix, StringComparison.Ordinal)).ToArray();
            var paired = new Dictionary<Transform, Transform>();
            foreach (var grass in raised)
            {
                var closest = stoneBeds.OrderBy(bed => GroundDistance(BedCenter(bed), MeshCenter(grass))).First();
                if (paired.ContainsKey(closest) || GroundDistance(BedCenter(closest), MeshCenter(grass)) > 1.5f)
                    throw new InvalidOperationException($"No unique stone bed for {grass.name}.");
                paired.Add(closest, grass);
            }
            if (paired.Count != stoneBeds.Length || stoneBeds.Length != 7)
                throw new InvalidOperationException("Expected seven planted stone beds.");
            var rootsByGarden = stoneBeds.ToDictionary(g => g, g => Require(details.Find(g.name + RootDetailsSuffix), g.name + " roots"));

            Transform upper = CreateGroup(outer, "01 Upper gardens - 上峰庭园", new Vector3(-30f, 1.45f, 0f));
            Transform lower = CreateGroup(outer, "02 Lower gardens - 下峰庭园", new Vector3(15f, -2.45f, 0f));
            Transform mountainGarden = CreateGroup(mountain, "Ridge pine garden - 山廊松庭", gardens["Ink pine garden 2"].position);
            foreach (var entry in gardens)
            {
                Transform parent = entry.Key == "Ink pine garden 2" ? mountainGarden : entry.Value.position.x < -20f ? upper : lower;
                Move(entry.Value, parent);
            }
            foreach (var bed in stoneBeds)
            {
                Move(paired[bed], bed);
                Move(rootsByGarden[bed], bed);
                SamuraiCourtyardRockMossSetup.AttachProjectionToRock(
                    Require(bed.Find(GardenParts + "/" + RockSurface), bed.name + " stone surface").GetComponent<MeshRenderer>());
            }

            foreach (var patch in grassRoot.Cast<Transform>().ToArray())
            {
                if (NearbyGround.TryGetValue(patch.name, out var nearby))
                    Move(patch, gardens[nearby]);
                else if (patch.name == "Upper west lawn")
                    Move(patch, gardens["Copper maple garden 2"]);
                else
                {
                    var parent = MeshCenter(patch).x < -20f ? upper : lower;
                    var site = CreateGroup(parent, PatchName(patch.name), MeshCenter(patch));
                    Move(patch, site);
                    if (patch.name == "Lower west approach") Move(pines["Understory pine 04"], site);
                }
            }

            Move(pines["Understory pine 01"], upper);
            Move(pines["Understory pine 02"], mountainGarden);
            Move(pines["Understory pine 03"], gardens["Ink pine garden 1"]);
            Move(pines["Understory pine 05"], gardens["Ink pine garden 3"]);
            Move(pines["Understory pine 06"], gardens["Copper maple garden 4"]);
            Move(pines["Understory pine 07"], gardens["Copper maple garden 5"]);
            var pair = CreateGroup(gardens["Copper maple garden 3"], "Foreground pine pair - 前景双松",
                (forest[0].position + forest[1].position) * .5f);
            foreach (var pine in forest) Move(pine, pair);

            RemoveIfEmpty(understory);
            RemoveIfEmpty(details);
            RemoveIfEmpty(grassRoot);
            return "Grouped eight tree gardens, seven beds with their grass and roots, ten ground patches, seven young pines, and the foreground pine pair.";
        }

        static bool IsGardenName(string name) =>
            name.StartsWith("Copper maple garden ", StringComparison.Ordinal) ||
            name.StartsWith("Ink pine garden ", StringComparison.Ordinal);

        static string PatchName(string patch) => patch switch
        {
            "Upper hall foundation" => "Hall approach planting - 上殿阶旁草地",
            "Tea garden slope" => "Tea garden slope - 茶庭坡地",
            "Lower west approach" => "West entrance planting - 山门西坡",
            "Lower east approach" => "East entrance planting - 山门东坡",
            "East residence garden" => "East residence garden - 东厢外庭",
            _ => patch
        };

        static Transform Require(Transform node, string label) =>
            node ? node : throw new InvalidOperationException($"Missing {label}.");

        static Vector3 BedCenter(Transform bed) => Require(
            bed.Find(GardenParts + "/02 Ground cover - 地被/Surface - Garden_dry_sage"), bed.name + " cover")
            .GetComponent<MeshRenderer>().bounds.center;

        static Vector3 MeshCenter(Transform patch) => patch.GetComponent<MeshRenderer>().bounds.center;

        static float GroundDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static Transform CreateGroup(Transform parent, string name, Vector3 worldPosition)
        {
            var group = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(group, MoveAction);
            group.transform.SetParent(parent, false);
            group.transform.position = worldPosition;
            return group.transform;
        }

        static void Move(Transform item, Transform parent) => Undo.SetTransformParent(item, parent, MoveAction);

        static void RemoveIfEmpty(Transform group)
        {
            if (group.childCount == 0) Undo.DestroyObjectImmediate(group.gameObject);
        }
    }
}
