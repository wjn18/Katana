using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.LookDev.Editor
{
    /// <summary>
    /// Generates a single joined, three-dimensional cloud surface. This factory only
    /// returns a Mesh: its caller owns asset saving, materials and scene placement.
    /// </summary>
    public static class ComicCloudMeshFactory
    {
        const int TriangleBudget = 110000;
        static readonly Vector3 DomainMin = new Vector3(-26f, -14f, -22f);
        static readonly Vector3 DomainMax = new Vector3(26f, 65f, 22f);
        static readonly int[,] Tetrahedra =
        {
            { 0, 5, 1, 6 }, { 0, 1, 2, 6 }, { 0, 2, 3, 6 },
            { 0, 3, 7, 6 }, { 0, 7, 4, 6 }, { 0, 4, 5, 6 }
        };

        struct Lobe
        {
            public Vector3 center, radii;
            public float blendWidth;
            public Vector3 inverseRadii;
            public float minRadius, maxRadius;
            public Quaternion rotation, inverseRotation;
            public Lobe(Vector3 center, Vector3 radii, float blendWidth = 0.65f, Quaternion? orientation = null)
            {
                this.center = center;
                this.radii = radii;
                this.blendWidth = blendWidth;
                inverseRadii = new Vector3(1f / radii.x, 1f / radii.y, 1f / radii.z);
                minRadius = Mathf.Min(radii.x, Mathf.Min(radii.y, radii.z));
                maxRadius = Mathf.Max(radii.x, Mathf.Max(radii.y, radii.z));
                rotation = orientation ?? Quaternion.identity;
                inverseRotation = Quaternion.Inverse(rotation);
            }
        }

        internal sealed class CloudField
        {
            readonly Lobe[] lobes;
            readonly Vector3 noiseOffset;
            readonly float mirror;
            public readonly Vector3 targetSize;

            public CloudField(int seed)
            {
                var random = new System.Random(seed);
                mirror = random.NextDouble() < 0.5 ? -1f : 1f;
                noiseOffset = new Vector3(Range(random, 2f, 80f), Range(random, 2f, 80f), Range(random, 2f, 80f));
                targetSize = new Vector3(Range(random, 28f, 34f), Range(random, 45f, 58f), Range(random, 22f, 30f));
                var primary = new List<Lobe>
                {
                    // Three overlapping broad feet establish a cloud wall, without
                    // one flattened ellipsoid running across the whole underside.
                    new Lobe(new Vector3(-8f, -0.4f, -0.7f), new Vector3(7.2f, 5.8f, 8.0f), 1.10f),
                    new Lobe(new Vector3(0f, -0.1f, 0.2f), new Vector3(9.1f, 6.2f, 8.4f), 1.15f),
                    new Lobe(new Vector3(8f, 0.4f, 1.1f), new Vector3(7.0f, 5.6f, 7.5f), 1.10f)
                };
                // Each plume is a continuous stack of broad, deeply overlapping
                // rolls. Unequal heights and slant produce an ascending cloud wall,
                // rather than isolated floating islands or a row of beads.
                AddPlume(primary, random, 7, new Vector3(-2.6f, 3f, -0.2f),
                    new Vector3(6.5f, 42f, 1.1f), new Vector3(7.0f, 5.6f, 7.0f), 0.77f);
                AddPlume(primary, random, 5, new Vector3(-8f, 2.0f, -1.4f),
                    new Vector3(-1.2f, 24f, -0.8f), new Vector3(6.4f, 5.2f, 6.1f), 0.76f);
                AddPlume(primary, random, 5, new Vector3(6.0f, 2.5f, 1.4f),
                    new Vector3(4.0f, 28f, -0.2f), new Vector3(6.6f, 5.5f, 6.4f), 0.75f);
                var pieces = new List<Lobe>(160);
                pieces.AddRange(primary);
                for (int i = 0; i < primary.Count; i++)
                    AddRollingShoulders(pieces, random, primary[i], i);
                lobes = pieces.ToArray();
            }

            static void AddPlume(List<Lobe> list, System.Random random, int count,
                Vector3 foot, Vector3 rise, Vector3 radii, float crownScale)
            {
                float phase = Range(random, 0f, Mathf.PI * 2f);
                for (int tier = 0; tier < count; tier++)
                {
                    float t = tier / (float)(count - 1);
                    // Stratified, unequal spacing breaks the repeated horizontal
                    // cadence while retaining enough overlap for one solid plume.
                    if (tier > 0 && tier < count - 1)
                        t += Range(random, -0.20f, 0.20f) / (count - 1);
                    float scale = Mathf.Lerp(1f, crownScale, t);
                    Vector3 center = foot + rise * t + new Vector3(
                        Mathf.Sin(phase + t * 5.2f) * 1.65f,
                        Range(random, -0.35f, 0.35f), Mathf.Cos(phase + t * 4.1f) * 1.15f);
                    Vector3 size = Vector3.Scale(radii, new Vector3(
                        scale * Range(random, 0.82f, 1.14f), Range(random, 0.98f, 1.22f),
                        scale * Range(random, 0.86f, 1.16f)));
                    Quaternion tilt = Quaternion.Euler(Range(random, -24f, 24f),
                        Range(random, -45f, 45f), Range(random, -32f, 32f));
                    list.Add(new Lobe(center, size, Range(random, 1.35f, 1.85f), tilt));
                }
            }

            static void AddRollingShoulders(List<Lobe> list, System.Random random, Lobe parent, int tier)
            {
                // Unequal, tilted shoulders spiral through different heights. There
                // is no fixed four-way collar or alternating pair of level caps.
                float phase = tier * 0.79f + Range(random, -0.80f, 0.80f);
                int count = random.Next(3, 6);
                for (int i = 0; i < count; i++)
                {
                    float angle = phase + i * Mathf.PI * 2f / count + Range(random, -0.25f, 0.25f);
                    float lift = 0.22f + 0.52f * Mathf.Sin(angle + phase * 0.5f) + Range(random, -0.20f, 0.20f);
                    Vector3 direction = new Vector3(Mathf.Cos(angle), lift, Mathf.Sin(angle)).normalized;
                    float surfaceFraction = Range(random, 0.72f, 0.92f);
                    Vector3 center = parent.center + parent.rotation * Vector3.Scale(direction, parent.radii) * surfaceFraction;
                    center.x += Mathf.Max(0f, direction.y) * 0.6f;
                    float radius = Range(random, 2.05f, 3.75f);
                    Vector3 radii = new Vector3(radius * Range(random, 0.84f, 1.25f),
                        radius * Range(random, 0.88f, 1.22f), radius * Range(random, 0.84f, 1.16f));
                    Quaternion tilt = parent.rotation * Quaternion.Euler(Range(random, -35f, 35f),
                        Range(random, -45f, 45f), Range(random, -35f, 35f));
                    var billow = new Lobe(center, radii, Range(random, 0.85f, 1.10f), tilt);
                    list.Add(billow);
                    // Third-scale folds are localized to selected shoulders. They
                    // remain substantial cloud folds, not geometric brush grains.
                    if (tier % 3 == 1 && i == count - 1)
                        AddEdgeCurls(list, random, billow, parent.rotation * direction);
                }
            }

            static void AddEdgeCurls(List<Lobe> list, System.Random random, Lobe billow, Vector3 outward)
            {
                Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
                Vector3 rise = Vector3.Cross(outward, side).normalized;
                const int count = 2;
                for (int i = 0; i < count; i++)
                {
                    float arc = Mathf.Lerp(-0.50f, 0.50f, i / (float)(count - 1));
                    Vector3 direction = (outward * 0.72f + side * arc + rise * 0.46f).normalized;
                    // Embedded centres and a soft join make a connected rolled rim,
                    // never loose marbles placed on top of the cloud.
                    Vector3 localDirection = billow.inverseRotation * direction;
                    Vector3 center = billow.center + billow.rotation * Vector3.Scale(localDirection, billow.radii) * 0.87f;
                    float radius = Range(random, 1.00f, 1.60f);
                    list.Add(new Lobe(center, new Vector3(radius * 1.10f, radius * 0.90f, radius), 0.50f, billow.rotation));
                }
            }

            public float Evaluate(Vector3 point)
            {
                point.x *= mirror;
                Vector3 q = point * 0.067f + noiseOffset;
                // Low-frequency domain distortion affects the actual silhouette.
                // There is no high-frequency surface grain or texture dependency.
                point += new Vector3(Noise(q), Noise(q + new Vector3(19.7f, 4.3f, 11.9f)),
                    Noise(q + new Vector3(3.1f, 31.2f, 7.4f))) * 1.10f;
                Vector3 curl = Vector3.Scale(point, new Vector3(0.29f, 0.24f, 0.32f)) + noiseOffset * 1.71f;
                point += new Vector3(Noise(curl), Noise(curl + new Vector3(8.3f, 16.1f, 4.9f)),
                    Noise(curl + new Vector3(23.4f, 3.2f, 12.6f))) * 0.72f;
                float below = Mathf.Max(0f, -point.y - 0.5f);
                point.y -= 0.22f * below * below / (below + 2f);
                float distance = Ellipsoid(point, lobes[0]);
                for (int i = 1; i < lobes.Length; i++)
                {
                    Lobe lobe = lobes[i];
                    // Conservative lower bound for the approximate ellipsoid SDF.
                    // Distant lobes cannot affect this smooth minimum; omitting them
                    // keeps normal/AO sampling practical for the larger cloud wall.
                    float reach = lobe.maxRadius + (Mathf.Max(distance, 0f) + lobe.blendWidth)
                        * lobe.maxRadius / lobe.minRadius;
                    if ((point - lobe.center).sqrMagnitude > reach * reach) continue;
                    distance = SmoothMin(distance, Ellipsoid(point, lobe), lobe.blendWidth);
                }
                return distance + 0.62f * Noise(point * 0.15f + noiseOffset * 0.73f)
                    + 0.78f * Noise(Vector3.Scale(point, new Vector3(0.44f, 0.36f, 0.48f)) + noiseOffset * 1.37f);
            }

            public Vector3 Gradient(Vector3 p)
            {
                const float e = 0.055f;
                var gradient = new Vector3(
                    Evaluate(p + Vector3.right * e) - Evaluate(p - Vector3.right * e),
                    Evaluate(p + Vector3.up * e) - Evaluate(p - Vector3.up * e),
                    Evaluate(p + Vector3.forward * e) - Evaluate(p - Vector3.forward * e));
                return gradient.sqrMagnitude > 1e-12f ? gradient.normalized : Vector3.up;
            }

            public float Accessibility(Vector3 p, Vector3 normal)
            {
                // R is accessibility (1 = open), deliberately restricted to a soft
                // 0.76..1 range. It must not create black fissures between cloud rolls.
                float origin = Evaluate(p);
                float occlusion = 0f;
                for (int i = 0; i < 4; i++)
                {
                    float distance = i == 0 ? 0.85f : i == 1 ? 1.8f : i == 2 ? 3.6f : 6f;
                    float weight = i == 0 ? 0.4f : i == 1 ? 0.3f : i == 2 ? 0.2f : 0.1f;
                    float clearance = Evaluate(p + normal * distance) - origin;
                    occlusion += weight * Mathf.Clamp01((distance - clearance) / distance);
                }
                return 1f - 0.24f * occlusion;
            }
        }

        /// <summary>
        /// Returns a deterministic, joined cloud, capped at 110,000 triangles.
        /// Local Y is the tall rising axis; X is width. Bounds center is (0, 2, 0), with
        /// size X 28-34, Y 45-58, Z 22-30 metres at unit transform scale.
        /// Vertex color R: gentle accessibility 0.76..1, G: normalized height 0..1.
        /// </summary>
        public static Mesh Create(int seed)
        {
            var field = new CloudField(seed);
            float cellSize = 0.72f;
            for (int attempt = 0; attempt < 8; attempt++, cellSize *= 1.16f)
            {
                Mesh result = Extract(field, cellSize);
                if (result == null) continue;
                result.name = "SculptedCloud_" + seed;
                return result;
            }
            throw new InvalidOperationException("Unable to fit the cloud mesh inside the 110,000 triangle budget.");
        }

        static Mesh Extract(CloudField field, float cellSize)
        {
            Vector3 extent = DomainMax - DomainMin;
            int nx = Mathf.CeilToInt(extent.x / cellSize) + 1;
            int ny = Mathf.CeilToInt(extent.y / cellSize) + 1;
            int nz = Mathf.CeilToInt(extent.z / cellSize) + 1;
            Vector3 step = new Vector3(extent.x / (nx - 1), extent.y / (ny - 1), extent.z / (nz - 1));
            var points = new Vector3[nx * ny * nz];
            var values = new float[points.Length];
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int id = (z * ny + y) * nx + x;
                points[id] = DomainMin + Vector3.Scale(new Vector3(x, y, z), step);
                values[id] = field.Evaluate(points[id]);
                if ((x == 0 || y == 0 || z == 0 || x == nx - 1 || y == ny - 1 || z == nz - 1) && values[id] <= 0f)
                    throw new InvalidOperationException("Cloud shape reached its extraction boundary; refusing an open mesh.");
            }
            var vertices = new List<Vector3>(18000);
            var normals = new List<Vector3>(18000);
            var indices = new List<int>(TriangleBudget * 3);
            var crossings = new Dictionary<ulong, int>(24000);
            var cube = new int[8];
            var inside = new int[4];
            var outside = new int[4];
            int layer = nx * ny;
            for (int z = 0; z < nz - 1; z++)
            for (int y = 0; y < ny - 1; y++)
            for (int x = 0; x < nx - 1; x++)
            {
                int id = (z * ny + y) * nx + x;
                cube[0] = id; cube[1] = id + 1; cube[2] = id + nx + 1; cube[3] = id + nx;
                cube[4] = id + layer; cube[5] = id + layer + 1;
                cube[6] = id + layer + nx + 1; cube[7] = id + layer + nx;
                int negative = 0;
                for (int corner = 0; corner < 8; corner++) if (values[cube[corner]] < 0f) negative++;
                if (negative == 0 || negative == 8) continue;
                for (int tet = 0; tet < 6; tet++)
                {
                    int nInside = 0, nOutside = 0;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        int pointId = cube[Tetrahedra[tet, corner]];
                        if (values[pointId] < 0f) inside[nInside++] = pointId;
                        else outside[nOutside++] = pointId;
                    }
                    if (nInside == 0 || nInside == 4) continue;
                    if (nInside == 1 || nInside == 3)
                    {
                        int lone = nInside == 1 ? inside[0] : outside[0];
                        int[] others = nInside == 1 ? outside : inside;
                        int a = Crossing(lone, others[0], points, values, field, crossings, vertices, normals);
                        int b = Crossing(lone, others[1], points, values, field, crossings, vertices, normals);
                        int c = Crossing(lone, others[2], points, values, field, crossings, vertices, normals);
                        AddTriangle(a, b, c, vertices, normals, indices);
                    }
                    else
                    {
                        int ac = Crossing(inside[0], outside[0], points, values, field, crossings, vertices, normals);
                        int ad = Crossing(inside[0], outside[1], points, values, field, crossings, vertices, normals);
                        int bc = Crossing(inside[1], outside[0], points, values, field, crossings, vertices, normals);
                        int bd = Crossing(inside[1], outside[1], points, values, field, crossings, vertices, normals);
                        AddTriangle(ac, ad, bc, vertices, normals, indices);
                        AddTriangle(ad, bd, bc, vertices, normals, indices);
                    }
                    if (indices.Count > TriangleBudget * 3) return null;
                }
            }
            if (indices.Count == 0) throw new InvalidOperationException("Cloud extraction returned an empty surface.");
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
            Vector3 scale = new Vector3(field.targetSize.x / bounds.size.x, field.targetSize.y / bounds.size.y, field.targetSize.z / bounds.size.z);
            var uv = new List<Vector2>(vertices.Count);
            var colors = new List<Color>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 original = vertices[i];
                colors.Add(new Color(field.Accessibility(original, normals[i]),
                    Mathf.Clamp01((original.y - bounds.min.y) / bounds.size.y), 0f, 1f));
                vertices[i] = Vector3.Scale(vertices[i] - bounds.center, scale) + new Vector3(0f, 2f, 0f);
                Vector3 n = normals[i];
                normals[i] = new Vector3(n.x / scale.x, n.y / scale.y, n.z / scale.z).normalized;
                uv.Add(new Vector2(vertices[i].x / field.targetSize.x + 0.5f, vertices[i].z / field.targetSize.z + 0.5f));
            }
            var mesh = new Mesh { indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static int Crossing(int a, int b, Vector3[] points, float[] values, CloudField field,
            Dictionary<ulong, int> cache, List<Vector3> vertices, List<Vector3> normals)
        {
            int low = Math.Min(a, b), high = Math.Max(a, b);
            ulong key = ((ulong)(uint)low << 32) | (uint)high;
            if (cache.TryGetValue(key, out int existing)) return existing;
            float t = Mathf.Clamp01(values[a] / (values[a] - values[b]));
            Vector3 position = Vector3.LerpUnclamped(points[a], points[b], t);
            int index = vertices.Count;
            vertices.Add(position);
            normals.Add(field.Gradient(position));
            cache.Add(key, index);
            return index;
        }

        static void AddTriangle(int a, int b, int c, List<Vector3> vertices, List<Vector3> normals, List<int> indices)
        {
            Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (cross.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(cross, normals[a] + normals[b] + normals[c]) < 0f)
            {
                int swap = b; b = c; c = swap;
            }
            indices.Add(a); indices.Add(b); indices.Add(c);
        }

        static float Range(System.Random random, float low, float high) => low + (high - low) * (float)random.NextDouble();

        static float Ellipsoid(Vector3 point, Lobe lobe)
        {
            Vector3 p = lobe.inverseRotation * (point - lobe.center);
            Vector3 q = Vector3.Scale(p, lobe.inverseRadii);
            float k0 = q.magnitude;
            if (k0 < 0.000001f) return -lobe.minRadius;
            Vector3 qq = Vector3.Scale(q, lobe.inverseRadii);
            return k0 * (k0 - 1f) / Mathf.Max(qq.magnitude, 0.000001f);
        }

        static float SmoothMin(float a, float b, float width)
        {
            float h = Mathf.Max(width - Mathf.Abs(a - b), 0f) / width;
            return Mathf.Min(a, b) - h * h * width * 0.25f;
        }

        static float Noise(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float lower = Mathf.Lerp(Mathf.Lerp(Hash(x, y, z), Hash(x + 1, y, z), fx),
                Mathf.Lerp(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), fx), fy);
            float upper = Mathf.Lerp(Mathf.Lerp(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), fx),
                Mathf.Lerp(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), fx), fy);
            return Mathf.Lerp(lower, upper, fz) * 2f - 1f;
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }
    }
}
