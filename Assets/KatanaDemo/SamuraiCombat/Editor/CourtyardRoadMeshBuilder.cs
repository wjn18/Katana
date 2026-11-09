using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Samurai.Editor
{
    /// <summary>
    /// Pure geometry helpers. Coordinates are used as supplied; callers own asset
    /// persistence, scene placement, materials and collision components.
    /// </summary>
    public static class CourtyardRoadMeshBuilder
    {
        const float CoordinateEpsilon = .00001f;
        const float AxisEpsilon = .0001f;

        /// <summary>
        /// Closed union of orthogonal XZ road strips. Route ends have flat caps at
        /// the specified endpoints. Internal nodes get a same-width square join.
        /// A shared rectangular grid retains holes and eliminates internal walls.
        /// Input Y is ignored in favour of the supplied top and bottom planes.
        /// </summary>
        public static Mesh FlatNetwork(string name, Vector3[][] routes, float[] widths, float top, float bottom)
        {
            if (routes == null || widths == null || routes.Length == 0 || routes.Length != widths.Length)
                throw new ArgumentException("Supply one positive width for each nonempty road route.");
            RequireFinite(top, "top"); RequireFinite(bottom, "bottom");
            if (bottom >= top - CoordinateEpsilon)
                throw new ArgumentException("Road bottom must be below its top.");
            var rectangles = new List<Rectangle>();
            for (int r = 0; r < routes.Length; r++)
            {
                var points = routes[r]; float width = widths[r];
                RequireWidth(width);
                if (points == null || points.Length < 2)
                    throw new ArgumentException("Every route must contain at least two points.");
                for (int i = 0; i < points.Length; i++) RequirePoint(points[i]);
                float half = width * .5f;
                for (int i = 1; i < points.Length; i++)
                {
                    Vector3 a = points[i - 1], b = points[i];
                    float dx = Mathf.Abs(b.x - a.x), dz = Mathf.Abs(b.z - a.z);
                    if (dx <= AxisEpsilon && dz <= AxisEpsilon) continue;
                    if (dx > AxisEpsilon && dz > AxisEpsilon)
                        throw new ArgumentException("FlatNetwork routes must be orthogonal in XZ.");
                    if (dx > dz)
                    {
                        float z = (a.z + b.z) * .5f;
                        rectangles.Add(new Rectangle(Mathf.Min(a.x, b.x), Mathf.Max(a.x, b.x), z - half, z + half));
                    }
                    else
                    {
                        float x = (a.x + b.x) * .5f;
                        rectangles.Add(new Rectangle(x - half, x + half, Mathf.Min(a.z, b.z), Mathf.Max(a.z, b.z)));
                    }
                }
                // This fills the outside quarter of a right-angle join as well
                // as intersections, without extending the ends of the route.
                for (int i = 1; i < points.Length - 1; i++)
                    rectangles.Add(new Rectangle(points[i].x - half, points[i].x + half,
                        points[i].z - half, points[i].z + half));
            }
            if (rectangles.Count == 0) throw new ArgumentException("Road routes contain no nonzero segments.");
            var xValues = new List<float>(); var zValues = new List<float>();
            foreach (var rect in rectangles)
            {
                xValues.Add(rect.x0); xValues.Add(rect.x1);
                zValues.Add(rect.z0); zValues.Add(rect.z1);
            }
            float[] xs = Coordinates(xValues), zs = Coordinates(zValues);
            int nx = xs.Length - 1, nz = zs.Length - 1;
            if ((long)nx * nz > 1000000L)
                throw new ArgumentException("Road grid exceeds one million cells; split the installation into separate networks.");
            var occupied = new bool[nx, nz]; int cellCount = 0;
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                {
                    float cx = (xs[x] + xs[x + 1]) * .5f, cz = (zs[z] + zs[z + 1]) * .5f;
                    foreach (var rect in rectangles)
                        if (cx > rect.x0 && cx < rect.x1 && cz > rect.z0 && cz < rect.z1)
                        { occupied[x, z] = true; cellCount++; break; }
                }
            ValidateCells(occupied, cellCount);
            var geometry = new Geometry();
            var grid = new GridVertices(xs, zs, top, bottom, geometry);
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                {
                    if (!occupied[x, z]) continue;
                    int t00 = grid.Vertex(x, z, true), t10 = grid.Vertex(x + 1, z, true);
                    int t01 = grid.Vertex(x, z + 1, true), t11 = grid.Vertex(x + 1, z + 1, true);
                    int b00 = grid.Vertex(x, z, false), b10 = grid.Vertex(x + 1, z, false);
                    int b01 = grid.Vertex(x, z + 1, false), b11 = grid.Vertex(x + 1, z + 1, false);
                    geometry.Quad(t00, t01, t11, t10); // +Y
                    geometry.Quad(b00, b10, b11, b01); // -Y
                    if (x == 0 || !occupied[x - 1, z]) geometry.Quad(t00, b00, b01, t01);
                    if (x == nx - 1 || !occupied[x + 1, z]) geometry.Quad(t10, t11, b11, b10);
                    if (z == 0 || !occupied[x, z - 1]) geometry.Quad(t00, t10, b10, b00);
                    if (z == nz - 1 || !occupied[x, z + 1]) geometry.Quad(t01, b01, b11, t11);
                }
            return Finish(name, geometry);
        }

        /// <summary>
        /// Continuous closed terrain-height ribbon. Every input point owns one
        /// shared left/center/right section with its exact Y; the bottom is one plane.
        /// A real centerline edge preserves the original piecewise-linear height
        /// between samples, including the segments immediately next to a bend.
        /// XZ joins use an angle bisector with a three-half-width miter limit.
        /// Sections that fold or a boundary that crosses itself are rejected.
        /// </summary>
        public static Mesh Ribbon(string name, Vector3[] centers, float width, float bottom)
        {
            if (centers == null || centers.Length < 2)
                throw new ArgumentException("Ribbon needs at least two center points.");
            RequireWidth(width); RequireFinite(bottom, "bottom");
            int n = centers.Length; float half = width * .5f;
            var directions = new Vector2[n - 1];
            for (int i = 0; i < n; i++)
            {
                RequirePoint(centers[i]);
                if (bottom >= centers[i].y - CoordinateEpsilon)
                    throw new ArgumentException("Ribbon bottom must be below every supplied top height.");
                if (i == 0) continue;
                var planar = new Vector2(centers[i].x - centers[i - 1].x, centers[i].z - centers[i - 1].z);
                if (planar.magnitude <= AxisEpsilon)
                    throw new ArgumentException("Adjacent ribbon points must be more than 0.0001m apart in XZ.");
                directions[i - 1] = planar.normalized;
            }
            var left = new Vector3[n]; var right = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 offset;
                if (i == 0) offset = Left(directions[0]) * half;
                else if (i == n - 1) offset = Left(directions[n - 2]) * half;
                else
                {
                    Vector2 n0 = Left(directions[i - 1]), n1 = Left(directions[i]);
                    Vector2 sum = n0 + n1;
                    if (sum.sqrMagnitude < .000001f)
                        throw new ArgumentException("Ribbon cannot reverse direction at one center point.");
                    Vector2 miter = sum.normalized;
                    float denominator = Vector2.Dot(miter, n1);
                    offset = miter * Mathf.Min(half * 3f, half / Mathf.Max(.000001f, denominator));
                }
                left[i] = centers[i] + new Vector3(offset.x, 0, offset.y);
                right[i] = centers[i] - new Vector3(offset.x, 0, offset.y);
            }
            ValidateRibbonBoundary(left, right);
            var geometry = new Geometry();
            for (int i = 0; i < n; i++)
            {
                geometry.vertices.Add(left[i]); geometry.vertices.Add(centers[i]); geometry.vertices.Add(right[i]);
                geometry.vertices.Add(new Vector3(left[i].x, bottom, left[i].z));
                geometry.vertices.Add(new Vector3(centers[i].x, bottom, centers[i].z));
                geometry.vertices.Add(new Vector3(right[i].x, bottom, right[i].z));
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 6, b = a + 6;
                geometry.TopQuad(a, b, b + 1, a + 1);
                geometry.TopQuad(a + 1, b + 1, b + 2, a + 2);
                geometry.Quad(a + 3, a + 4, b + 4, b + 3);
                geometry.Quad(a + 4, a + 5, b + 5, b + 4);
                geometry.Quad(a, a + 3, b + 3, b);
                geometry.Quad(a + 2, b + 2, b + 5, a + 5);
            }
            geometry.Quad(0, 1, 4, 3); geometry.Quad(1, 2, 5, 4);
            int end = (n - 1) * 6;
            geometry.Quad(end, end + 3, end + 4, end + 1);
            geometry.Quad(end + 1, end + 4, end + 5, end + 2);
            return Finish(name, geometry);
        }

        [Serializable]
        public sealed class MeshAudit
        {
            public int vertexCount, triangleCount, componentCount;
            public int nonManifoldEdges, normalDirectionConflicts, duplicateTriangles, degenerateTriangles;
            public double signedVolume;
            public bool closedPositiveVolume
            {
                get { return componentCount == 1 && nonManifoldEdges == 0 && normalDirectionConflicts == 0
                    && duplicateTriangles == 0 && degenerateTriangles == 0 && signedVolume > 0; }
            }
        }

        /// <summary>Index-topology and signed-volume audit; no scene or asset changes.</summary>
        public static MeshAudit Audit(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException("mesh");
            var v = mesh.vertices; var t = mesh.triangles;
            var result = new MeshAudit { vertexCount = v.Length, triangleCount = t.Length / 3 };
            var edges = new Dictionary<Edge, EdgeUse>(); var faces = new HashSet<Triangle>();
            var sets = new DisjointSet(v.Length); var used = new bool[v.Length];
            Vector3 origin = v.Length == 0 ? Vector3.zero : v[0];
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                used[a] = used[b] = used[c] = true; sets.Join(a, b); sets.Join(a, c);
                if (!faces.Add(new Triangle(a, b, c))) result.duplicateTriangles++;
                Vector3 ab = v[b] - v[a], ac = v[c] - v[a];
                double cx = (double)ab.y * ac.z - (double)ab.z * ac.y;
                double cy = (double)ab.z * ac.x - (double)ab.x * ac.z;
                double cz = (double)ab.x * ac.y - (double)ab.y * ac.x;
                if (cx * cx + cy * cy + cz * cz <= 1e-24) result.degenerateTriangles++;
                Vector3 p = v[a] - origin, q = v[b] - origin, r = v[c] - origin;
                result.signedVolume += ((double)p.x * ((double)q.y * r.z - (double)q.z * r.y)
                    + (double)p.y * ((double)q.z * r.x - (double)q.x * r.z)
                    + (double)p.z * ((double)q.x * r.y - (double)q.y * r.x)) / 6.0;
                CountEdge(edges, a, b); CountEdge(edges, b, c); CountEdge(edges, c, a);
            }
            foreach (var entry in edges.Values)
            {
                if (entry.count != 2) result.nonManifoldEdges++;
                else if (entry.balance != 0) result.normalDirectionConflicts++;
            }
            var roots = new HashSet<int>();
            for (int i = 0; i < used.Length; i++) if (used[i]) roots.Add(sets.Root(i));
            result.componentCount = roots.Count;
            return result;
        }

        static Mesh Finish(string name, Geometry geometry)
        {
            var mesh = new Mesh { name = name, indexFormat = geometry.vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(geometry.vertices); mesh.SetTriangles(geometry.triangles, 0);
            var uv = new List<Vector2>(geometry.vertices.Count);
            foreach (var p in geometry.vertices) uv.Add(new Vector2(p.x, p.z));
            mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var audit = Audit(mesh);
            if (!audit.closedPositiveVolume)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw new InvalidOperationException("Road mesh failed its closed, oriented, nondegenerate single-component audit.");
            }
            return mesh;
        }

        static float[] Coordinates(List<float> values)
        {
            values.Sort(); var merged = new List<float>();
            foreach (float value in values)
                if (merged.Count == 0 || value - merged[merged.Count - 1] > CoordinateEpsilon) merged.Add(value);
            return merged.ToArray();
        }

        static void ValidateCells(bool[,] cells, int count)
        {
            int nx = cells.GetLength(0), nz = cells.GetLength(1);
            if (count == 0) throw new ArgumentException("Road rectangles have no nonzero occupied cells.");
            for (int x = 1; x < nx; x++)
                for (int z = 1; z < nz; z++)
                {
                    bool a = cells[x - 1, z - 1], b = cells[x, z - 1], c = cells[x, z], d = cells[x - 1, z];
                    if (a == c && b == d && a != b)
                        throw new ArgumentException("Road rectangles meet only at a corner; give their junction positive width.");
                }
            var seen = new bool[nx, nz]; var pending = new Queue<int>();
            for (int x = 0; x < nx && pending.Count == 0; x++)
                for (int z = 0; z < nz; z++) if (cells[x, z]) { pending.Enqueue(x * nz + z); seen[x, z] = true; break; }
            int reached = 0;
            int[] dx = { -1, 1, 0, 0 }, dz = { 0, 0, -1, 1 };
            while (pending.Count > 0)
            {
                int id = pending.Dequeue(), x = id / nz, z = id % nz; reached++;
                for (int j = 0; j < 4; j++)
                {
                    int xx = x + dx[j], zz = z + dz[j];
                    if (xx < 0 || zz < 0 || xx >= nx || zz >= nz || !cells[xx, zz] || seen[xx, zz]) continue;
                    seen[xx, zz] = true; pending.Enqueue(xx * nz + zz);
                }
            }
            if (reached != count) throw new ArgumentException("FlatNetwork routes must form one connected road area.");
        }

        static void ValidateRibbonBoundary(Vector3[] left, Vector3[] right)
        {
            int n = left.Length; var border = new Vector2[n * 2];
            for (int i = 0; i < n; i++)
            {
                border[i] = new Vector2(left[i].x, left[i].z);
                border[n + i] = new Vector2(right[n - 1 - i].x, right[n - 1 - i].z);
            }
            for (int i = 0; i < border.Length; i++)
                for (int j = i + 1; j < border.Length; j++)
                {
                    int ii = (i + 1) % border.Length, jj = (j + 1) % border.Length;
                    if (ii == j || jj == i) continue;
                    if (SegmentsIntersect(border[i], border[ii], border[j], border[jj]))
                        throw new ArgumentException("Ribbon boundary intersects itself; its width or nearby bend samples must be adjusted.");
                }
        }

        static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            double abC = Cross(b - a, c - a), abD = Cross(b - a, d - a);
            double cdA = Cross(d - c, a - c), cdB = Cross(d - c, b - c);
            const double epsilon = 1e-10;
            if (((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon))
                && ((cdA > epsilon && cdB < -epsilon) || (cdA < -epsilon && cdB > epsilon))) return true;
            return (Math.Abs(abC) <= epsilon && OnSegment(a, b, c))
                || (Math.Abs(abD) <= epsilon && OnSegment(a, b, d))
                || (Math.Abs(cdA) <= epsilon && OnSegment(c, d, a))
                || (Math.Abs(cdB) <= epsilon && OnSegment(c, d, b));
        }
        static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            return p.x >= Mathf.Min(a.x, b.x) - CoordinateEpsilon && p.x <= Mathf.Max(a.x, b.x) + CoordinateEpsilon
                && p.y >= Mathf.Min(a.y, b.y) - CoordinateEpsilon && p.y <= Mathf.Max(a.y, b.y) + CoordinateEpsilon;
        }
        static double Cross(Vector2 a, Vector2 b) { return (double)a.x * b.y - (double)a.y * b.x; }
        static Vector2 Left(Vector2 direction) { return new Vector2(-direction.y, direction.x); }
        static void RequireWidth(float width)
        {
            RequireFinite(width, "width");
            if (width <= AxisEpsilon) throw new ArgumentException("Road width must exceed 0.0001m.");
        }
        static void RequirePoint(Vector3 p) { RequireFinite(p.x, "point.x"); RequireFinite(p.y, "point.y"); RequireFinite(p.z, "point.z"); }
        static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException(name + " must be finite.");
        }

        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
            public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }
            void Tri(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
            public void TopQuad(int a, int b, int c, int d)
            {
                if (Up(a, b, c) && Up(a, c, d)) Quad(a, b, c, d);
                else if (Up(a, b, d) && Up(b, c, d)) { Tri(a, b, d); Tri(b, c, d); }
                else throw new ArgumentException("Ribbon offset sections fold at a bend; reduce width or remove overly close bend samples.");
            }
            bool Up(int a, int b, int c)
            {
                Vector3 ab = vertices[b] - vertices[a], ac = vertices[c] - vertices[a];
                return (double)ab.z * ac.x - (double)ab.x * ac.z > 1e-12;
            }
        }

        sealed class GridVertices
        {
            readonly float[] xs, zs; readonly float top, bottom; readonly Geometry geometry;
            readonly Dictionary<int, int> lookup = new Dictionary<int, int>();
            public GridVertices(float[] x, float[] z, float t, float b, Geometry g) { xs = x; zs = z; top = t; bottom = b; geometry = g; }
            public int Vertex(int x, int z, bool upper)
            {
                int key = ((x * zs.Length + z) * 2) + (upper ? 1 : 0), index;
                if (lookup.TryGetValue(key, out index)) return index;
                index = geometry.vertices.Count; lookup.Add(key, index);
                geometry.vertices.Add(new Vector3(xs[x], upper ? top : bottom, zs[z])); return index;
            }
        }

        struct Rectangle
        {
            public readonly float x0, x1, z0, z1;
            public Rectangle(float a, float b, float c, float d) { x0 = a; x1 = b; z0 = c; z1 = d; }
        }
        struct Edge : IEquatable<Edge>
        {
            readonly int a, b;
            public Edge(int x, int y) { a = Math.Min(x, y); b = Math.Max(x, y); }
            public bool Equals(Edge other) { return a == other.a && b == other.b; }
            public override bool Equals(object other) { return other is Edge && Equals((Edge)other); }
            public override int GetHashCode() { unchecked { return a * 397 ^ b; } }
        }
        sealed class EdgeUse { public int count, balance; }
        static void CountEdge(Dictionary<Edge, EdgeUse> edges, int a, int b)
        {
            var key = new Edge(a, b); EdgeUse use;
            if (!edges.TryGetValue(key, out use)) { use = new EdgeUse(); edges.Add(key, use); }
            use.count++; use.balance += a < b ? 1 : -1;
        }
        struct Triangle : IEquatable<Triangle>
        {
            readonly int a, b, c;
            public Triangle(int x, int y, int z)
            {
                int swap; if (x > y) { swap = x; x = y; y = swap; }
                if (y > z) { swap = y; y = z; z = swap; }
                if (x > y) { swap = x; x = y; y = swap; }
                a = x; b = y; c = z;
            }
            public bool Equals(Triangle other) { return a == other.a && b == other.b && c == other.c; }
            public override bool Equals(object other) { return other is Triangle && Equals((Triangle)other); }
            public override int GetHashCode() { unchecked { return (a * 397 ^ b) * 397 ^ c; } }
        }
        sealed class DisjointSet
        {
            readonly int[] parents;
            public DisjointSet(int size) { parents = new int[size]; for (int i = 0; i < size; i++) parents[i] = i; }
            public int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
            public void Join(int a, int b) { a = Root(a); b = Root(b); if (a != b) parents[b] = a; }
        }
    }
}
