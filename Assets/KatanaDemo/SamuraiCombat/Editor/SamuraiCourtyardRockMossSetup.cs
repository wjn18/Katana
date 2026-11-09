using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static class SamuraiCourtyardRockMossSetup
    {
        const string AssemblyFolder = SamuraiComicStyleSetup.Root + "/BlenderAssembly";
        const string MossFolder = AssemblyFolder + "/RockMoss";
        const string Sample = "Copper maple garden 3";
        const string MossKeyword = "_SAMURAI_GARDEN_MOSS";
        const string BakedProjection = "_GardenMossBakedProjection";
        const string EditableParts = "Editable components - 可调部件";
        const string GroundCover = "02 Ground cover - 地被";

        [MenuItem("Katana/Level/Paint Moss On Blender Courtyard Stone")]
        static void InstallFromMenu() => Debug.Log(InstallAll());

        public static string InstallSample() => Install(Sample);
        public static string InstallAll() => Install(null);

        static string Install(string gardenName)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != SamuraiBlenderAssemblyImport.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the Blender assembly scene in Edit mode.");

            var dressing = scene.GetRootGameObjects().Single(root => root.name == "Courtyard dressing");
            var gardens = dressing.GetComponentsInChildren<Transform>().Where(node => gardenName != null
                ? node.name == gardenName
                : node.name.StartsWith("Copper maple garden ", StringComparison.Ordinal)
                    || node.name.StartsWith("Ink pine garden ", StringComparison.Ordinal))
                .Where(node => node.Find(EditableParts + "/" + GroundCover)).ToArray();
            if (gardens.Length == 0) throw new InvalidOperationException("No matching courtyard gardens found.");

            var pending = new List<(Transform Garden, MeshRenderer Rock, MeshRenderer Cover)>();
            foreach (var garden in gardens)
            {
                var editable = garden.Find(EditableParts);
                var rock = GardenSurface(editable, "01 Rock bed - 山石地基", "Surface - Foundation_slate");
                var cover = GardenSurface(editable, GroundCover, "Surface - Garden_dry_sage");
                if (!rock.enabled) throw new InvalidOperationException($"The stone bed is hidden in {garden.name}.");
                if (rock.sharedMaterial.IsKeywordEnabled(MossKeyword) && !cover.enabled) continue;
                pending.Add((garden, rock, cover));
            }
            if (pending.Count == 0) return $"Moss is already painted on {gardens.Length} stone beds.";

            if (!AssetDatabase.IsValidFolder(MossFolder)) AssetDatabase.CreateFolder(AssemblyFolder, "RockMoss");
            var created = new List<string>();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint moss on courtyard stone");
            try
            {
                foreach (var bed in pending)
                {
                    string path = MossFolder + "/" + bed.Garden.name + " - Moss on stone.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!material)
                    {
                        material = PaintMaterial(bed.Garden.name, bed.Rock, bed.Cover);
                        AssetDatabase.CreateAsset(material, path);
                        created.Add(path);
                    }
                    if (!material.IsKeywordEnabled(MossKeyword))
                        throw new InvalidOperationException($"The material at {path} has no moss mask.");
                    Undo.RecordObjects(new UnityEngine.Object[] { bed.Rock, bed.Cover }, "Paint moss on courtyard stone");
                    bed.Rock.sharedMaterial = material;
                    bed.Cover.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(bed.Rock);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(bed.Cover);
                    AttachProjectionToRock(bed.Rock);
                }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return $"Painted moss on {pending.Count} of {gardens.Length} stone beds and retired their raised green covers.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                throw;
            }
        }

        static MeshRenderer GardenSurface(Transform editable, string part, string surface)
        {
            var node = editable.Find(part + "/" + surface);
            var renderer = node ? node.GetComponent<MeshRenderer>() : null;
            if (!renderer || !renderer.sharedMaterial || !renderer.sharedMaterial.HasProperty("_GardenMoss"))
                throw new InvalidOperationException($"Expected a comic surface on {editable.parent.name}/{part}.");
            return renderer;
        }

        static Material PaintMaterial(string gardenName, MeshRenderer rock, MeshRenderer cover)
        {
            var stone = new Material(rock.sharedMaterial) { name = gardenName + " - Moss on stone" };
            stone.SetFloat("_GardenMoss", 1f);
            stone.EnableKeyword(MossKeyword);
            Bounds footprint = cover.bounds;
            stone.SetVector("_GardenMossAnchor", new Vector4(footprint.center.x, footprint.center.z,
                1f / Mathf.Max(0.2f, footprint.extents.x), 1f / Mathf.Max(0.2f, footprint.extents.z)));
            stone.SetColor("_GardenMossColor", cover.sharedMaterial.GetColor("_BaseColor"));
            stone.SetColor("_GardenMossShadow", cover.sharedMaterial.GetColor("_ShadowColor"));
            return stone;
        }

        internal static void AttachProjectionToRock(MeshRenderer rock)
        {
            var material = rock.sharedMaterial;
            if (!material.IsKeywordEnabled(MossKeyword) || material.GetFloat(BakedProjection) > .5f)
                return;
            var filter = rock.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh)
                throw new InvalidOperationException("A moss-covered rock is missing its mesh.");
            var editable = rock.transform.parent.parent;
            var garden = editable.parent;
            string path = MossFolder + "/" + garden.name + " - Moss projection.asset";
            var projected = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!projected)
            {
                var original = filter.sharedMesh;
                if (original.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord3))
                    throw new InvalidOperationException($"The moss projection channel is occupied on {garden.name}.");
                projected = UnityEngine.Object.Instantiate(original);
                projected.name = garden.name + " - Moss projection";
                // Use the pre-move positions so the painted pattern stays identical at its original location.
                projected.SetUVs(3, original.vertices.Select(rock.transform.TransformPoint).ToList());
                AssetDatabase.CreateAsset(projected, path);
            }
            Undo.RecordObject(filter, "Attach the moss projection to the rock mesh");
            Undo.RecordObject(material, "Use the mesh's moss projection");
            filter.sharedMesh = projected;
            material.SetFloat(BakedProjection, 1f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            EditorUtility.SetDirty(material);
        }
    }
}
