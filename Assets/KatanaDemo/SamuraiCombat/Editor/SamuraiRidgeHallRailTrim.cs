using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Fits the existing high-path railings to the ridge hall entrances.</summary>
    public static class SamuraiRidgeHallRailTrim
    {
        const string TrimName = "High path railing transitions - 高路栏杆接续";
        const string CombinedName = "Combined timber - 合并静态木构";
        const float RidgeZ = 25.5f;
        const float RailOffset = .9f;

        public static void Trim(Transform oldHighPath, Transform houseRoot, string assetFolder, float westX, float eastX)
        {
            if (!oldHighPath || !houseRoot)
                throw new ArgumentException("Both the original high path and ridge hall are required.");
            if (houseRoot.Find(TrimName)) return;
            if (float.IsNaN(westX) || float.IsInfinity(westX) || float.IsNaN(eastX)
                || float.IsInfinity(eastX) || westX >= eastX)
                throw new ArgumentException("The ridge hall needs finite, ordered west and east limits.");

            Transform posts = oldHighPath.Find("01 Guard posts - 山路护柱");
            Transform rails = oldHighPath.Find("02 Continuous rails - 连续双道护栏");
            MeshRenderer oldPostSurface = FindSurface(posts);
            MeshRenderer oldRailSurface = FindSurface(rails);
            var block = AssetDatabase.LoadAssetAtPath<Mesh>(SamuraiCourtyardDecorationSetup.Root + "/Meshes/Shapes/Block.asset");
            if (!block) throw new InvalidOperationException("The high-path Block mesh could not be loaded.");

            var postMatrices = new List<Matrix4x4>();
            var railMatrices = new List<Matrix4x4>();
            var hiddenBoxes = new List<BoxCollider>();
            var splitBoxes = new List<RailPiece>();
            foreach (BoxCollider box in OriginalBoxes(posts))
            {
                Vector3 center = box.transform.TransformPoint(box.center);
                if (center.x >= westX && center.x <= eastX && Mathf.Abs(center.z - RidgeZ) < 1.25f)
                    hiddenBoxes.Add(box);
                else
                    postMatrices.Add(BoxMatrix(box, box.center, box.size));
            }

            foreach (BoxCollider box in OriginalBoxes(rails))
            {
                Vector3 half = Vector3.forward * (box.size.z * .5f);
                Vector3 a = box.transform.TransformPoint(box.center - half);
                Vector3 b = box.transform.TransformPoint(box.center + half);
                Vector3 axis = b - a;
                Vector3 center = (a + b) * .5f;
                bool ridgeRail = Mathf.Abs(axis.normalized.x) > .98f
                    && Mathf.Abs(Mathf.Abs(center.z - RidgeZ) - RailOffset) < .12f;
                if (!ridgeRail || Mathf.Min(a.x, b.x) >= eastX || Mathf.Max(a.x, b.x) <= westX)
                {
                    railMatrices.Add(BoxMatrix(box, box.center, box.size));
                    continue;
                }

                float west = (westX - a.x) / axis.x;
                float east = (eastX - a.x) / axis.x;
                float cutStart = Mathf.Clamp01(Mathf.Min(west, east));
                float cutEnd = Mathf.Clamp01(Mathf.Max(west, east));
                AddRailPiece(box, 0, cutStart, railMatrices, splitBoxes);
                AddRailPiece(box, cutEnd, 1, railMatrices, splitBoxes);
                hiddenBoxes.Add(box);
            }
            if (hiddenBoxes.Count == 0) return;

            // Build and persist both meshes before retiring any scene objects.
            string folder = EnsureAssetFolder(assetFolder);
            Matrix4x4 toHouse = houseRoot.worldToLocalMatrix;
            Mesh postMesh = SaveCombined(block, postMatrices, toHouse, folder, "RidgeHall_ExteriorPosts");
            Mesh railMesh = SaveCombined(block, railMatrices, toHouse, folder, "RidgeHall_ExteriorRails");

            var trim = new GameObject(TrimName);
            trim.transform.SetParent(houseRoot, false);
            Undo.RegisterCreatedObjectUndo(trim, "Add ridge hall rail transitions");
            AddSurface(trim.transform, "Retained exterior posts - 保留院外护柱", postMesh, oldPostSurface);
            AddSurface(trim.transform, "Retained exterior rails - 保留院外护栏", railMesh, oldRailSurface);
            for (int i = 0; i < splitBoxes.Count; i++) AddCollider(trim.transform, splitBoxes[i], i + 1);

            Hide(oldPostSurface.gameObject);
            Hide(oldRailSurface.gameObject);
            foreach (BoxCollider box in hiddenBoxes) Hide(box.gameObject);
        }

        static MeshRenderer FindSurface(Transform group)
        {
            Transform surface = group ? group.Find(CombinedName) : null;
            MeshRenderer renderer = surface ? surface.GetComponent<MeshRenderer>() : null;
            if (!renderer) throw new InvalidOperationException("Expected the original combined high-path railing surface.");
            return renderer;
        }

        static IEnumerable<BoxCollider> OriginalBoxes(Transform group)
        {
            foreach (Transform child in group)
            {
                if (!child.name.StartsWith("Collision ", StringComparison.Ordinal)) continue;
                BoxCollider box = child.GetComponent<BoxCollider>();
                if (box) yield return box;
            }
        }

        static Matrix4x4 BoxMatrix(BoxCollider box, Vector3 center, Vector3 size)
        {
            return box.transform.localToWorldMatrix * Matrix4x4.TRS(center, Quaternion.identity, size);
        }

        static void AddRailPiece(BoxCollider source, float start, float end,
            List<Matrix4x4> matrices, List<RailPiece> boxes)
        {
            if ((end - start) * source.size.z * Mathf.Abs(source.transform.lossyScale.z) < .0001f) return;
            Vector3 center = source.center + Vector3.forward * (source.size.z * ((start + end) * .5f - .5f));
            Vector3 size = source.size;
            size.z *= end - start;
            matrices.Add(BoxMatrix(source, center, size));
            if (source.gameObject.activeInHierarchy && source.enabled)
                boxes.Add(new RailPiece(source, center, size));
        }

        static void AddCollider(Transform parent, RailPiece piece, int index)
        {
            BoxCollider source = piece.Source;
            var go = new GameObject("Rail transition collision " + index);
            go.layer = source.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(source.transform.TransformPoint(piece.Center), source.transform.rotation);
            Vector3 size = Vector3.Scale(piece.Size, Absolute(source.transform.lossyScale));
            Vector3 scale = Absolute(go.transform.lossyScale);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x / scale.x, size.y / scale.y, size.z / scale.z);
            box.isTrigger = source.isTrigger;
            box.sharedMaterial = source.sharedMaterial;
            box.contactOffset = source.contactOffset;
            Undo.RegisterCreatedObjectUndo(go, "Add split high-path rail collision");
        }

        static Vector3 Absolute(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        static void AddSurface(Transform parent, string name, Mesh mesh, MeshRenderer source)
        {
            if (!mesh) return;
            var go = new GameObject(name);
            go.layer = source.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = source.sharedMaterial;
            renderer.shadowCastingMode = source.shadowCastingMode;
            renderer.receiveShadows = source.receiveShadows;
            renderer.lightProbeUsage = source.lightProbeUsage;
            renderer.reflectionProbeUsage = source.reflectionProbeUsage;
            renderer.enabled = source.enabled;
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(source.gameObject));
            Undo.RegisterCreatedObjectUndo(go, "Rebuild exterior high-path railing surface");
        }

        static void Hide(GameObject original)
        {
            if (!original.activeSelf) return;
            Undo.RecordObject(original, "Retain superseded high-path railing part");
            original.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(original);
        }

        static string EnsureAssetFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A railing asset folder is required.");
            string[] parts = path.Replace('\\', '/').TrimEnd('/').Split('/');
            if (parts[0] != "Assets") throw new ArgumentException("The railing asset folder must be under Assets.");
            string parent = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string name = parts[i];
                if (name.Length == 0 || name == "." || name == "..")
                    throw new ArgumentException("Invalid railing asset folder.");
                string child = parent + "/" + name;
                if (!AssetDatabase.IsValidFolder(child) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, name)))
                    throw new InvalidOperationException("Could not create railing asset folder " + child);
                parent = child;
            }
            return parent;
        }

        static Mesh SaveCombined(Mesh block, List<Matrix4x4> matrices, Matrix4x4 toHouse, string folder, string name)
        {
            if (matrices.Count == 0) return null;
            var instances = new CombineInstance[matrices.Count];
            var fingerprint = new StringBuilder(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(block)));
            for (int i = 0; i < matrices.Count; i++)
            {
                Matrix4x4 transform = toHouse * matrices[i];
                instances[i] = new CombineInstance { mesh = block, transform = transform };
                for (int value = 0; value < 16; value++)
                    fingerprint.Append('|').Append(transform[value].ToString("R", CultureInfo.InvariantCulture));
            }
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint.ToString()))).Replace("-", "").Substring(0, 12);
            string path = folder + "/" + name + "_" + hash + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved) return saved;

            var mesh = new Mesh { name = name };
            if ((long)block.vertexCount * instances.Length > 65535L) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(instances, true, true);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        readonly struct RailPiece
        {
            public readonly BoxCollider Source;
            public readonly Vector3 Center;
            public readonly Vector3 Size;

            public RailPiece(BoxCollider source, Vector3 center, Vector3 size)
            {
                Source = source;
                Center = center;
                Size = size;
            }
        }
    }
}
