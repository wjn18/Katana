using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>Identifies one architectural piece with existing collision or its visible triangles.</summary>
    public sealed class SamuraiOcclusionGroup : MonoBehaviour
    {
        [Serializable]
        public struct Surface
        {
            public Renderer renderer;
            [Tooltip("-1 fades all material slots on this renderer.")]
            public int materialIndex;
            [Tooltip("Authored mesh or readable camera-only copy, before any runtime static batching.")]
            public Mesh probeMesh;

            public Surface(Renderer renderer, int materialIndex = -1, Mesh probeMesh = null)
            {
                this.renderer = renderer;
                this.materialIndex = materialIndex;
                this.probeMesh = probeMesh;
            }
        }

        [Tooltip("Existing solid colliders used only to test the camera's view.")]
        public Collider[] blockers = Array.Empty<Collider>();
        public Surface[] surfaces = Array.Empty<Surface>();
        [Tooltip("Test the selected visible mesh slots when the physics collider does not match this piece.")]
        public bool useVisualProbes;
        [Tooltip("Hide light decoration completely when it obstructs the camera.")]
        public bool hideWhenOccluding;

        static readonly Dictionary<Collider, SamuraiOcclusionGroup> Groups = new Dictionary<Collider, SamuraiOcclusionGroup>();
        internal static readonly HashSet<SamuraiOcclusionGroup> VisualGroups = new HashSet<SamuraiOcclusionGroup>();

        readonly List<VisualPart> visualParts = new List<VisualPart>();
        Bounds visualBounds;

        internal static bool HasColliderProbes => Groups.Count != 0;
        internal static bool HasBlockers => HasColliderProbes || VisualGroups.Count != 0;

        struct Triangle
        {
            public Vector3 start, edge1, edge2;
        }

        sealed class VisualPart
        {
            public Renderer renderer;
            public Bounds bounds;
            public Triangle[] triangles;
        }

        internal static bool TryFind(Collider collider, out SamuraiOcclusionGroup group) =>
            Groups.TryGetValue(collider, out group) && group && group.isActiveAndEnabled;

        void OnEnable()
        {
            if (useVisualProbes)
            {
                BuildVisualParts();
                if (visualParts.Count > 0)
                {
                    VisualGroups.Add(this);
                    return;
                }
            }
            foreach (Collider blocker in blockers)
                if (blocker) Groups[blocker] = this;
        }

        void OnDisable()
        {
            VisualGroups.Remove(this);
            visualParts.Clear();
            foreach (Collider blocker in blockers)
                if (blocker && Groups.TryGetValue(blocker, out var group) && group == this)
                    Groups.Remove(blocker);
        }

        void BuildVisualParts()
        {
            visualParts.Clear();
            AddVisualParts(false);
            // Standalone ink can obstruct too. Avoid testing expanded ink hulls
            // when the group already has an actual surface to test.
            if (visualParts.Count == 0)
                AddVisualParts(true);
        }

        void AddVisualParts(bool includeInk)
        {
            var materials = new List<Material>();
            Matrix4x4 worldToGroup = transform.worldToLocalMatrix;
            foreach (Surface surface in surfaces)
            {
                Renderer renderer = surface.renderer;
                if (!(renderer is MeshRenderer) || !renderer.TryGetComponent(out MeshFilter filter))
                    continue;

                Mesh mesh = surface.probeMesh ? surface.probeMesh : filter.sharedMesh;
                if (!mesh || !mesh.isReadable) continue;
                renderer.GetSharedMaterials(materials);
                int first = Mathf.Max(0, surface.materialIndex);
                int end = Mathf.Min(mesh.subMeshCount, materials.Count);
                if (surface.materialIndex >= 0) end = Mathf.Min(end, first + 1);
                Matrix4x4 meshToGroup = worldToGroup * renderer.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                for (int index = first; index < end; index++)
                {
                    Material material = materials[index];
                    if (!material || material.FindPass("SamuraiOcclusionForeground") < 0 ||
                        (!includeInk && material.shader.name == "OC Design/Comic Lighting/Outline") ||
                        mesh.GetTopology(index) != MeshTopology.Triangles)
                        continue;

                    int[] indices = mesh.GetTriangles(index);
                    if (indices.Length == 0) continue;
                    var part = new VisualPart
                    {
                        renderer = renderer,
                        bounds = new Bounds(meshToGroup.MultiplyPoint3x4(vertices[indices[0]]), Vector3.zero),
                        triangles = new Triangle[indices.Length / 3]
                    };
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        Vector3 a = meshToGroup.MultiplyPoint3x4(vertices[indices[i]]);
                        Vector3 b = meshToGroup.MultiplyPoint3x4(vertices[indices[i + 1]]);
                        Vector3 c = meshToGroup.MultiplyPoint3x4(vertices[indices[i + 2]]);
                        part.bounds.Encapsulate(a);
                        part.bounds.Encapsulate(b);
                        part.bounds.Encapsulate(c);
                        part.triangles[i / 3] = new Triangle { start = a, edge1 = b - a, edge2 = c - a };
                    }
                    if (visualParts.Count == 0) visualBounds = part.bounds;
                    else visualBounds.Encapsulate(part.bounds);
                    visualParts.Add(part);
                }
            }
        }

        internal bool IntersectsVisualSegment(Vector3 from, Vector3 to, int cameraMask)
        {
            // Authored probes are relative to the piece's motion anchor. A moving
            // door retains the same local triangles while its hinge rotates.
            Matrix4x4 inverse = transform.worldToLocalMatrix;
            Vector3 origin = inverse.MultiplyPoint3x4(from);
            return IntersectsLocalSegment(origin, inverse.MultiplyPoint3x4(to) - origin, cameraMask);
        }

        internal bool IntersectsVisualSegments(Vector3 from, Vector3[] targets, int count, Bounds viewBounds, int cameraMask)
        {
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            Vector3 localExtent = visualBounds.extents;
            var worldExtent = new Vector3(
                Mathf.Abs(localToWorld.m00) * localExtent.x + Mathf.Abs(localToWorld.m01) * localExtent.y + Mathf.Abs(localToWorld.m02) * localExtent.z,
                Mathf.Abs(localToWorld.m10) * localExtent.x + Mathf.Abs(localToWorld.m11) * localExtent.y + Mathf.Abs(localToWorld.m12) * localExtent.z,
                Mathf.Abs(localToWorld.m20) * localExtent.x + Mathf.Abs(localToWorld.m21) * localExtent.y + Mathf.Abs(localToWorld.m22) * localExtent.z);
            if (!viewBounds.Intersects(new Bounds(localToWorld.MultiplyPoint3x4(visualBounds.center), 2f * worldExtent)))
                return false;

            Matrix4x4 inverse = transform.worldToLocalMatrix;
            Vector3 origin = inverse.MultiplyPoint3x4(from);
            for (int i = 0; i < count; i++)
                if (IntersectsLocalSegment(origin, inverse.MultiplyPoint3x4(targets[i]) - origin, cameraMask))
                    return true;
            return false;
        }

        bool IntersectsLocalSegment(Vector3 origin, Vector3 segment, int cameraMask)
        {
            float length = segment.magnitude;
            if (length <= .0001f) return false;
            var ray = new Ray(origin, segment / length);
            if (!visualBounds.IntersectRay(ray, out float groupDistance) || groupDistance > length) return false;

            foreach (VisualPart part in visualParts)
            {
                Renderer renderer = part.renderer;
                if (!renderer || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                    (cameraMask & (1 << renderer.gameObject.layer)) == 0 ||
                    !part.bounds.IntersectRay(ray, out float partDistance) || partDistance > length)
                    continue;
                foreach (Triangle triangle in part.triangles)
                    if (IntersectsTriangle(origin, segment, triangle))
                        return true;
            }
            return false;
        }

        static bool IntersectsTriangle(Vector3 origin, Vector3 segment, Triangle triangle)
        {
            Vector3 cross = Vector3.Cross(segment, triangle.edge2);
            float determinant = Vector3.Dot(triangle.edge1, cross);
            if (Mathf.Abs(determinant) < .0000001f) return false;
            float inverse = 1f / determinant;
            Vector3 start = origin - triangle.start;
            float u = Vector3.Dot(start, cross) * inverse;
            if (u < 0f || u > 1f) return false;
            Vector3 edge = Vector3.Cross(start, triangle.edge1);
            float v = Vector3.Dot(segment, edge) * inverse;
            if (v < 0f || u + v > 1f) return false;
            float distance = Vector3.Dot(triangle.edge2, edge) * inverse;
            return distance >= 0f && distance <= 1f;
        }
    }
}
