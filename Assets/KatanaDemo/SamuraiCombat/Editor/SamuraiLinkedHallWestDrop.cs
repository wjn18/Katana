using System;
using System.Linq;
using OCDesign.LookDev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    public static partial class SamuraiLinkedHallSetup
    {
        const string WestHallBodyName = "Hall body and west grass veranda - 殿身与西侧落草坪阳台";

        [MenuItem("Katana/Level/Move Elite Hall Drop To West Outer Court")]
        public static string MoveEliteHallDropWest()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().Single(g => g.name == "Courtyard dressing").transform;
            var root = dressing.Find(RefinedRootName);
            var hall = root ? root.Find("01 Two-storey grand hall - 两层主殿与正阳台") : null;
            if (!hall) throw new InvalidOperationException("Install the two-storey Elite grand hall first.");
            if (hall.Find(WestHallBodyName)) return "West veranda already exists. Existing edits retained.";
            var oldBody = hall.Find("Hall body and balcony joinery - 殿身门窗与正面阳台");
            var oldFootings = hall.Find("Hall terrace footings - 台基与阳台接地石础");
            var terraceVersion = hall.Find("Hall body and west veranda - 殿身与西侧落院阳台");
            var terrace = root.Find("04 West outer court terrace - 西侧外院落地石台");
            if (!oldBody || !oldFootings) throw new InvalidOperationException("The original hall and footings are missing.");

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Keep the west balcony and restore the original lawn");
            try
            {
                if (terrace)
                {
                    if (terrace.Find("Southwest maple pocket - 西南角枫树花池")) RestoreTerraceMaple(dressing);
                    Undo.DestroyObjectImmediate(terrace.gameObject);
                }
                if (terraceVersion) Undo.DestroyObjectImmediate(terraceVersion.gameObject);
                BuildEliteWestHallBody(hall, dressing);
                BuildWestGrassDrop(root, dressing);
                Retire(oldBody);
                Retire(oldFootings);
                Retire(root.Find("Main hall balcony drop - 主殿正阳台落沿"));
                Retire(root.Find("Clear courtyard landing - 庭院安全落地中心"));
                if (Camera.main && Camera.main.TryGetComponent<ComicScreenOutline>(out var outline)) outline.RefreshSources();
                Physics.SyncTransforms();
                EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undo);
                return "Kept the west balcony, restored the south opening, and returned the landing to the original lawn.";
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
        }

        static void BuildEliteWestHallBody(Transform hall, Transform dressing)
        {
            var bodyRoot = Group(hall, WestHallBodyName);
            Undo.RegisterCreatedObjectUndo(bodyRoot.gameObject, "Build the west veranda facing the lawn");
            var body = new PaletteBatch(bodyRoot);
            SamuraiEliteGrandHallBody.Build(body.Box, (x, z) => RockGround(dressing, x, z), body.CollisionBox);
            body.Flush("EliteGrandHall_West_Grass_Veranda_Body");

            var foundation = new PaletteBatch(Group(bodyRoot, "Main hall stone foundation - 主殿石台基"));
            foundation.Box("Main hall terrace foundation", "Slate", new Vector3(-25.40f, 1.064f, 18.77f),
                new Vector3(16.68f, .58f, 7.48f), true);
            foundation.Box("Main hall terrace sandstone coping", "Stone", new Vector3(-25.40f, 1.312f, 18.77f),
                new Vector3(16.88f, .12f, 7.57f));
            foundation.Flush("EliteGrandHall_West_Veranda_Foundation");
        }

        static void BuildWestGrassDrop(Transform root, Transform dressing)
        {
            var markers = Group(root, "04 West balcony grass drop - 西阳台草坪落点");
            Undo.RegisterCreatedObjectUndo(markers.gameObject, "Mark the original lawn below the balcony");
            var lip = Group(markers, "West balcony drop - 西侧阳台落沿");
            lip.position = SamuraiEliteGrandHallBody.WestDropLip;
            lip.rotation = Quaternion.LookRotation(Vector3.left);
            const float x = -37.90f, z = SamuraiEliteGrandHallBody.WestDropZ;
            Group(markers, "Outer courtyard landing - 外院落地中心").position =
                new Vector3(x, RockGround(dressing, x, z), z);
        }

        static void RestoreTerraceMaple(Transform dressing)
        {
            var maple = dressing.Find("01 Upper courtyard - 上层庭院/05 Gardens - 庭园/Copper maple garden 2/Editable components - 可调部件");
            if (!maple) return;
            var planterAnchor = new Vector3(-39.62f, 1.45f + .23f, 13.94f);
            const float previousScale = .72f;
            foreach (Transform part in maple)
            {
                if (!part.name.StartsWith("03 Trunk", StringComparison.Ordinal)
                    && !part.name.StartsWith("Branch ", StringComparison.Ordinal)
                    && !part.name.StartsWith("Canopy ", StringComparison.Ordinal)
                    && !part.name.StartsWith("Crown ", StringComparison.Ordinal)) continue;
                Undo.RecordObject(part, "Restore the maple to the original garden");
                part.position = maple.position + (part.position - planterAnchor) / previousScale;
                part.localScale /= previousScale;
                PrefabUtility.RecordPrefabInstancePropertyModifications(part);
            }
        }
    }
}
