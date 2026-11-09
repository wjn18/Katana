using System;
using System.Collections.Generic;
using UnityEngine;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Closed roof and trim meshes in the ridge lodge's local coordinates.</summary>
    public static class SamuraiRidgeHallRoof
    {
        const float HalfLength = 7.9f;
        const float HalfDepth = 3.2f;
        const float EaveHeight = 5.1f;
        const float RidgeHeight = 7.55f;
        const float RoofThickness = .24f;

        // Extra samples at the eave and the ends describe the upturned rim.
        static readonly float[] LengthSections = { -7.9f, -7.58f, -6.9f, -5.5f, -3f, 0, 3f, 5.5f, 6.9f, 7.58f, 7.9f };
        static readonly float[] PitchSections = { 0, .16f, .32f, .50f, .68f, .82f, .94f, 1f };
        static readonly float[] PitchHeights = { 1f, .83f, .62f, .39f, .20f, .06f, 0, .035f };

        public static void Build(Action<string, Mesh, string> emit)
        {
            if (emit == null) throw new ArgumentNullException(nameof(emit));

            float[] crossSections = RoofCrossSections();
            var roof = new ClosedSurfaces();
            roof.AddShell(LengthSections, crossSections, RoofHeight, RoofThickness);
            Emit(emit, roof, "RidgeHall_CurvedGableRoof", "Roof");

            var soffit = new ClosedSurfaces();
            // Side rails stay outside the walking span. Gable boards follow the
            // pitch instead of running a low crossbeam across either entrance.
            soffit.AddShell(LengthSections, new[] { -3.13f, -2.89f }, EaveRailHeight, .16f);
            soffit.AddShell(LengthSections, new[] { 2.89f, 3.13f }, EaveRailHeight, .16f);
            soffit.AddShell(new[] { -7.88f, -7.72f }, crossSections, BargeboardHeight, .21f);
            soffit.AddShell(new[] { 7.72f, 7.88f }, crossSections, BargeboardHeight, .21f);
            Emit(emit, soffit, "RidgeHall_TimberSoffitAndBargeboards", "Timber");

            var rafters = new ClosedSurfaces();
            float[] rafterPositions = { -6.8f, -5.4f, -4f, -2.6f, -1.2f, 1.2f, 2.6f, 4f, 5.4f, 6.8f };
            foreach (float x in rafterPositions)
            {
                float[] span = { x - .055f, x + .055f };
                rafters.AddShell(span, new[] { -3.14f, -2.52f }, RafterHeight, .14f);
                rafters.AddShell(span, new[] { 2.52f, 3.14f }, RafterHeight, .14f);
            }
            Emit(emit, rafters, "RidgeHall_ExposedRafterTails", "Timber");

            var ridge = new ClosedSurfaces();
            float[] ridgeSections = { -7.78f, -7.46f, -6.7f, -5.6f, 0, 5.6f, 6.7f, 7.46f, 7.78f };
            ridge.AddShell(ridgeSections, new[] { -.23f, -.12f, 0, .12f, .23f }, RidgeCapHeight, .18f);
            Emit(emit, ridge, "RidgeHall_RaisedRidgeCap", "Roof");

            var fittings = new ClosedSurfaces();
            float[] trimSections = { -7.46f, -6.7f, -5.6f, 0, 5.6f, 6.7f, 7.46f };
            fittings.AddShell(trimSections, new[] { -.031f, .031f }, RidgeTrimHeight, .046f);
            fittings.AddShell(new[] { -7.67f, -7.47f }, new[] { -.09f, .09f }, RidgeEndHeight, .105f);
            fittings.AddShell(new[] { 7.47f, 7.67f }, new[] { -.09f, .09f }, RidgeEndHeight, .105f);
            Emit(emit, fittings, "RidgeHall_RidgeMetalFittings", "Gold");
        }

        static float[] RoofCrossSections()
        {
            int center = PitchSections.Length - 1;
            var coordinates = new float[center * 2 + 1];
            for (int i = 1; i < PitchSections.Length; i++)
            {
                float z = PitchSections[i] * HalfDepth;
                coordinates[center - i] = -z;
                coordinates[center + i] = z;
            }
            return coordinates;
        }

        static float RoofHeight(float x, float z)
        {
            float pitch = Mathf.Clamp01(Mathf.Abs(z) / HalfDepth);
            float height = PitchHeights[PitchHeights.Length - 1];
            for (int i = 1; i < PitchSections.Length; i++)
            {
                if (pitch > PitchSections[i]) continue;
                float t = Mathf.InverseLerp(PitchSections[i - 1], PitchSections[i], pitch);
                height = Mathf.Lerp(PitchHeights[i - 1], PitchHeights[i], t);
                break;
            }
            float corner = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(5.5f, HalfLength, Mathf.Abs(x)));
            float upsweep = .16f * corner * pitch * pitch * pitch;
            return EaveHeight + (RidgeHeight - EaveHeight) * height + upsweep;
        }

        static float EaveRailHeight(float x, float z) => RoofHeight(x, z) - .22f;
        internal static float UndersideHeight(float x, float z) => RoofHeight(x, z) - RoofThickness;
        static float BargeboardHeight(float x, float z) => RoofHeight(x, z) - .16f;
        static float RafterHeight(float x, float z) => RoofHeight(x, z) - .26f;

        static float RidgeCapHeight(float x, float z)
        {
            float tip = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(5.6f, 7.78f, Mathf.Abs(x)));
            return RidgeHeight + .10f + .027f * tip - .12f * Mathf.Abs(z) / .23f;
        }

        static float RidgeTrimHeight(float x, float z) => RidgeCapHeight(x, 0) + .024f;
        static float RidgeEndHeight(float x, float z) => RidgeCapHeight(x, 0) + .065f;

        static void Emit(Action<string, Mesh, string> emit, ClosedSurfaces surfaces, string name, string material)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(surfaces.Vertices);
            mesh.SetTriangles(surfaces.Triangles, 0);
            mesh.SetUVs(0, surfaces.Uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            emit(name, mesh, material);
        }

        sealed class ClosedSurfaces
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
            public readonly List<Vector2> Uv = new List<Vector2>();

            // A shared grid on each skin and perimeter-only walls form a closed
            // shell. Winding is upward on top and outward on each edge.
            public void AddShell(float[] xs, float[] zs, Func<float, float, float> top, float thickness)
            {
                int start = Vertices.Count;
                int row = zs.Length;
                int skin = xs.Length * row;
                for (int layer = 0; layer < 2; layer++)
                    foreach (float x in xs)
                        foreach (float z in zs)
                        {
                            Vertices.Add(new Vector3(x, top(x, z) - layer * thickness, z));
                            Uv.Add(new Vector2(x, z));
                        }

                for (int x = 0; x < xs.Length - 1; x++)
                    for (int z = 0; z < row - 1; z++)
                    {
                        int a = start + x * row + z;
                        int b = a + row;
                        int c = b + 1;
                        int d = a + 1;
                        Quad(a, d, c, b);
                        Quad(a + skin, b + skin, c + skin, d + skin);
                        if (x == 0) Quad(a, a + skin, d + skin, d);
                        if (x == xs.Length - 2) Quad(b, c, c + skin, b + skin);
                        if (z == 0) Quad(a, b, b + skin, a + skin);
                        if (z == row - 2) Quad(d, d + skin, c + skin, c);
                    }
            }

            void Quad(int a, int b, int c, int d)
            {
                Triangles.Add(a);
                Triangles.Add(b);
                Triangles.Add(c);
                Triangles.Add(a);
                Triangles.Add(c);
                Triangles.Add(d);
            }
        }
    }
}
