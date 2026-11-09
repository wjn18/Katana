using System;
using System.Collections.Generic;
using System.Linq;
using OCDesign.LookDev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiCourtyardSpatialHierarchy
    {
        public const string RootName = "00 Scene compositions - 场景组合";
        const string Action = "Group courtyard by site";
        const string MaterialFolder = SamuraiComicStyleSetup.Root + "/BlenderAssembly/FinishMaterials/";

        [MenuItem("Katana/Level/Group Blender Courtyard By Site")]
        static void InstallFromMenu() => Debug.Log(Install());

        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != SamuraiBlenderAssemblyImport.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the Blender assembly scene in Edit mode.");
            var dressing = scene.GetRootGameObjects().Single(root => root.name == "Courtyard dressing").transform;
            if (dressing.Find(RootName)) return "The courtyard is already grouped by site.";

            var before = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToDictionary(t => t, t => t.localToWorldMatrix);
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(Action);
            try
            {
                var zones = Kit(dressing, RootName, dressing.position);
                zones.SetSiblingIndex(0);
                foreach (string name in new[] { "01 Elite courtyard - 上院", "02 First courtyard - 下院",
                    "03 Mountain halls - 山间连廊", "04 Bridge crossing - 连桥",
                    "05 Outer gardens - 外园与围墙", "06 Terrain and setting - 山体与布景" })
                    Kit(zones, name, dressing.position);

                int architecture = SamuraiBlenderArchitectureHierarchy.Group(scene, dressing, zones);
                string gardens = SamuraiBlenderGardenHierarchy.Group(scene, dressing, zones);
                int pieces = PlaceSiteDetails(dressing, zones);
                GroupScenery(dressing, zones);

                var archive = Kit(dressing, "99 Build references - 原搭建与辅助", dressing.position);
                var route = dressing.GetComponentInChildren<SamuraiLevelRoute>(true);
                if (route && route.name.StartsWith("21 Tree details", StringComparison.Ordinal))
                {
                    Undo.RecordObject(route.gameObject, Action);
                    route.gameObject.name = "Summit route progression - 钥匙与内闩桥门机关";
                }
                foreach (var original in dressing.Cast<Transform>().Where(t => t != zones && t != archive).ToArray())
                    Move(original, archive);
                var oldDisplay = scene.GetRootGameObjects().Single(g => g.name == "Blender architecture - Blender 建筑装配").transform;
                if (oldDisplay.childCount == 0) Move(oldDisplay, archive);

                foreach (var entry in before)
                {
                    // Empty legacy containers can be removed by the garden migration.
                    if (!entry.Key) continue;
                    var actual = entry.Key.localToWorldMatrix;
                    for (int i = 0; i < 16; i++)
                        if (Mathf.Abs(actual[i] - entry.Value[i]) > .0002f)
                            throw new InvalidOperationException($"A placed object moved during grouping: {entry.Key.name}.");
                }
                foreach (var ink in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ComicScreenOutline>(true)))
                    ink.RefreshSources();
                AssetDatabase.SaveAssets();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return $"{architecture} architecture kits. {gardens} Reassigned {pieces} path and furnishing pieces to their sites.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
        }

        static int PlaceSiteDetails(Transform dressing, Transform zones)
        {
            var upper = Child(zones, "01 Elite courtyard");
            var lower = Child(zones, "02 First courtyard");
            var outer = Child(zones, "05 Outer gardens");
            var oldUpper = Child(dressing, "01 Upper courtyard");
            var oldLower = Child(dressing, "02 Lower courtyard");
            var upperGarden = Child(outer, "01 Upper gardens");
            var lowerGarden = Child(outer, "02 Lower gardens");
            var hall = Child(upper, "Elite1 Grand Hall");
            var hallPath = Child(upper, "Hall approach");
            var entry = Child(lower, "Entry gate and stair");
            var store = Child(outer, "Mountain Storehouse");
            var tea = Child(outer, "Tea Pavilion");
            var teaGate = Child(outer, "Tea garden gate");
            var residence = Child(outer, "East Residence");
            var eastLanding = Kit(lowerGarden, "East garden stairs - 东园入口与台阶", new Vector3(25.2f, -3.3f, -18.3f));
            var entryPlanting = Child(lowerGarden, "West entrance planting");

            var lamps = Child(outer, "Approach stonework");
            var oldDetails = Child(lamps, "06 Arrival details");
            var upperSet = Child(lamps, "Upper site lantern set");
            var lowerSet = Child(lamps, "Lower site lantern set");
            var oldUpperLamps = Child(upperSet, "03 Stone lanterns");
            var oldLowerLamps = Child(lowerSet, "03 Stone lanterns");
            var walks = Child(outer, "Connected garden paths");
            var oldWalks = Child(walks, "05 Garden walks");
            var continuous = Child(walks, "02 Garden paths");

            // Restore the pre-assembly per-site rendering for the five distributed prop batches.
            // The source meshes were retained on their original objects during assembly import.
            RestoreSiteRenderers(oldDetails, Child(lamps, "Entry and Garden Stonework"), 79);
            RestoreSiteRenderers(oldUpperLamps, Child(upperSet, "Upper Courtyard Stone Lanterns"), 64);
            RestoreSiteRenderers(oldLowerLamps, Child(lowerSet, "Lower Courtyard Stone Lanterns"), 32);
            RestoreSiteRenderers(oldWalks, Child(walks, "Garden Walks and Steps"), 35);
            RestoreSiteRenderers(continuous, Child(walks, "Continuous 02 Garden paths"), 5);

            int count = 0;
            var lampSites = new[] { store, Child(upperGarden, "Copper maple garden 2"), hall, hallPath, entry, residence, entryPlanting, residence };
            var lanterns = oldUpperLamps.Cast<Transform>().Concat(oldLowerLamps.Cast<Transform>()).ToArray();
            for (int i = 0; i < lampSites.Length; i++)
            {
                string name = "Stone lantern " + (i + 1);
                var lamp = lanterns.Single(t => t.name == name);
                Move(lamp, lampSites[i]);
                var support = oldDetails.Cast<Transform>().SingleOrDefault(t => t.name.StartsWith(name + " - ", StringComparison.Ordinal));
                if (support) Move(support, lamp.gameObject.activeInHierarchy ? lamp : lampSites[i]);
                count++;
            }
            foreach (var part in oldDetails.Cast<Transform>().ToArray())
            {
                Transform site = part.name.StartsWith("Garden step") ? teaGate
                    : part.name.StartsWith("Storehouse") ? store
                    : part.name.StartsWith("Eastern walk") ? residence
                    : part.name.Contains("drum stone") ? entry : entryPlanting;
                Move(part, site);
                count++;
            }
            foreach (var part in oldWalks.Cast<Transform>().Concat(continuous.Cast<Transform>()).ToArray())
            {
                Transform site = part.name.StartsWith("Tea pavilion") ? tea
                    : part.name.StartsWith("Storehouse") ? store
                    : part.name.StartsWith("Eastern residence") ? residence
                    : part.name.StartsWith("Eastern") ? eastLanding : entryPlanting;
                Move(part, site);
                count++;
            }
            Move(lamps, Child(dressing, "07 Mountain compound"));
            Move(walks, Child(dressing, "07 Mountain compound"));

            var upperWalls = Child(upper, "Inner wall");
            var westWall = Child(upperWalls, "01 West");
            var northEast = Child(upperWalls, "05 North east");
            foreach (var flag in Child(oldUpper, "04 Banners").Cast<Transform>().ToArray())
                Move(flag, flag.name.EndsWith("4", StringComparison.Ordinal) ? northEast : westWall);
            var lowerFlags = Child(oldLower, "04 Banners");
            Move(Child(lowerFlags, "Crest banner 5"), Child(lower, "East guard room"));
            Move(Child(lowerFlags, "Crest banner 6"), Child(lower, "East study room"));
            Move(Child(lowerFlags, "Crest banner 7"), Child(lower, "West prayer room"));
            Move(Child(lowerFlags, "Crest banner 8"), Child(lower, "Lower Grand Hall"));
            Move(Child(oldUpper, "06 Court marks"), Child(upper, "Upper court hardscape"));
            return count;
        }

        static void RestoreSiteRenderers(Transform source, Transform assembly, int expected)
        {
            var originals = source.GetComponentsInChildren<MeshRenderer>().Where(r => !r.enabled && !r.forceRenderingOff &&
                r.TryGetComponent<MeshFilter>(out var mesh) && mesh.sharedMesh).ToArray();
            if (originals.Length != expected)
                throw new InvalidOperationException($"Expected {expected} source meshes for {assembly.name}, found {originals.Length}.");
            var merged = assembly.GetComponentsInChildren<MeshRenderer>();
            var materials = merged.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
            foreach (var renderer in originals)
            {
                Undo.RecordObject(renderer, Action);
                renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                {
                    if (!original || materials.Contains(original)) return original;
                    var finish = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + original.name + "_CourtyardFinish.mat");
                    return finish && materials.Contains(finish) ? finish : original;
                }).ToArray();
                renderer.enabled = true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            foreach (var renderer in merged)
            {
                Undo.RecordObject(renderer, Action);
                renderer.enabled = false;
            }
        }

        static void GroupScenery(Transform dressing, Transform zones)
        {
            var setting = Child(zones, "06 Terrain and setting");
            var outdoor = Child(zones, "05 Outer gardens");
            var upper = Child(outdoor, "01 Upper gardens");
            var lower = Child(outdoor, "02 Lower gardens");
            var mountain = Child(dressing, "05 Mountain environment");
            var groves = Child(mountain, "06 Pine groves");
            var stones = Child(mountain, "08 Summit outcrops");
            var talus = Child(mountain, "07 Talus stones");
            var upperPines = Child(groves, "01 Upper summit pines").Cast<Transform>().ToArray();
            var lowerPines = Child(groves, "02 Lower summit pines").Cast<Transform>().ToArray();
            var nearStones = stones.Cast<Transform>().Concat(talus.Cast<Transform>().Where(t => t.name == "Talus stone 113")).ToArray();

            var west = Kit(upper, "West shoulder - 西仓岩肩", new Vector3(-43f, -2f, -9f));
            var northWest = Kit(upper, "Veranda shoulder - 西阳台岩肩", new Vector3(-42.9f, -2.1f, 20.7f));
            var north = Kit(upper, "North ridge garden - 北坡松石", new Vector3(-26f, -2.1f, 27f));
            var south = Kit(upper, "South shoulder - 西南坡松石", new Vector3(-34f, -2.1f, -24f));
            var tea = Child(upper, "Tea garden slope");
            var lowerWest = Child(lower, "West entrance planting");
            var lowerEast = Child(lower, "East entrance planting");
            var residence = Child(lower, "East residence garden");
            var tower = Kit(lower, "Tower shoulder - 高塔后坡松石", new Vector3(25f, -5.8f, 23f));

            foreach (var prop in upperPines.Concat(nearStones.Where(t => t.position.x < -5f)))
            {
                var p = prop.position;
                Transform site = p.z > 12f ? (p.x < -36f ? northWest : north)
                    : p.z < -20f ? south : p.x < -38f ? west : tea;
                Move(prop, site);
            }
            foreach (var prop in lowerPines.Concat(nearStones.Where(t => t.position.x >= -5f)))
            {
                var p = prop.position;
                Transform site = p.z > 10f ? tower : p.z > -14f ? residence : p.x < 23f ? lowerWest : lowerEast;
                Move(prop, site);
            }
            Move(mountain, setting);
            Move(Child(dressing, "03 Background"), setting);
            Move(Child(Child(dressing, "12 Linked mountain halls"), "05 Backing mountain"), setting);
            var westDrop = Child(Child(dressing, "13 Elite grand hall"), "04 West balcony grass drop");
            Move(Child(westDrop, "Outer courtyard landing"), Child(upper, "Copper maple garden 2"));
        }

        static Transform Child(Transform parent, string prefix)
        {
            return parent.Cast<Transform>().Single(t => t.name.StartsWith(prefix, StringComparison.Ordinal));
        }

        static Transform Kit(Transform parent, string name, Vector3 pivot)
        {
            var node = new GameObject(name).transform;
            Undo.RegisterCreatedObjectUndo(node.gameObject, Action);
            node.SetParent(parent, false);
            node.position = pivot;
            return node;
        }

        static void Move(Transform item, Transform parent)
        {
            var prefab = PrefabUtility.GetOutermostPrefabInstanceRoot(item.gameObject);
            if (prefab && prefab != item.gameObject)
                throw new InvalidOperationException($"Cannot move only part of prefab {prefab.name} ({item.name}).");
            Undo.SetTransformParent(item, parent, Action);
            if (prefab) PrefabUtility.RecordPrefabInstancePropertyModifications(item);
        }
    }
}
