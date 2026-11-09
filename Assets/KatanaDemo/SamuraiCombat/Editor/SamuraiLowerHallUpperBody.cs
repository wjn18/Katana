using System;
using UnityEngine;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Second-floor deck, stair opening and exterior walls of the lower great hall.</summary>
    public static class SamuraiLowerHallUpperBody
    {
        const float West = 3.42f, East = 22.57f, South = 12.08f, North = 22.10f;
        const float FloorY = 2.08f, WallTop = 5.35f;
        const float HoleWest = 9.52f, HoleEast = 16.80f, HoleSouth = 18.63f, HoleNorth = 20.57f;
        const float ExitSouth = 14.01f, ExitNorth = 15.99f, ExitHead = 4.62f;
        const float WallDepth = .22f;

        public static void Build(Action<string, string, Vector3, Vector3, Quaternion, bool> box)
        {
            if (box == null) throw new ArgumentNullException(nameof(box));
            FloorSlab(box, "Upper hall west landing slab", West, HoleWest, South, North);
            FloorSlab(box, "Upper hall east gallery slab", HoleEast, East, South, North);
            FloorSlab(box, "Upper hall south gallery slab", HoleWest, HoleEast, South, HoleSouth);
            FloorSlab(box, "Upper hall north gallery slab", HoleWest, HoleEast, HoleNorth, North);
            for (float x = West + .69f; x < East - .12f; x += .69f)
            {
                if (x <= HoleWest || x >= HoleEast) BoardJoint(box, x, South, North);
                else
                {
                    BoardJoint(box, x, South, HoleSouth);
                    BoardJoint(box, x, HoleNorth, North);
                }
            }

            // The west edge is the stair arrival. All rails sit on the deck side of the aperture.
            Guard(box, "Stair north guard", true, HoleNorth + .0675f, HoleWest, HoleEast);
            Guard(box, "Stair south guard", true, HoleSouth - .0675f, HoleWest, HoleEast);
            Guard(box, "Stair east guard", false, HoleEast + .0675f, HoleSouth, HoleNorth);

            Facade(box, "Upper hall south", true, South + WallDepth * .5f, -1, West, East, 5);
            Facade(box, "Upper hall north", true, North - WallDepth * .5f, 1, West, East, 5);
            Facade(box, "Upper hall east", false, East - WallDepth * .5f, 1, South, North, 3);
            Facade(box, "Upper hall west south wing", false, West + WallDepth * .5f, -1, South, ExitSouth, 1);
            Facade(box, "Upper hall west north wing", false, West + WallDepth * .5f, -1, ExitNorth, North, 2);
            FaceBox(box, "Mountain passage lintel", "Timber", false, West + WallDepth * .5f,
                (ExitSouth + ExitNorth) * .5f, (ExitHead + WallTop) * .5f,
                ExitNorth - ExitSouth, WallTop - ExitHead, WallDepth, true);
            FaceBox(box, "Mountain passage red transom", "Red", false, West + WallDepth * .5f,
                (ExitSouth + ExitNorth) * .5f, ExitHead + .10f, ExitNorth - ExitSouth, .20f, .278f, false);
            FaceBox(box, "Mountain passage gold transom line", "Gold", false, West - .008f,
                (ExitSouth + ExitNorth) * .5f, ExitHead + .226f, ExitNorth - ExitSouth - .18f, .033f, .065f, false);

            Lantern(box, "Upper hall southwest lantern", 5.67f, 13.84f);
            Lantern(box, "Upper hall southeast lantern", 20.28f, 13.84f);
            Lantern(box, "Upper hall northwest lantern", 5.68f, 21.18f);
            Lantern(box, "Upper hall northeast lantern", 20.23f, 21.18f);
        }

        static void FloorSlab(Action<string, string, Vector3, Vector3, Quaternion, bool> box,
            string name, float x0, float x1, float z0, float z1)
        {
            const float thickness = .18f;
            box(name, "Timber", new Vector3((x0 + x1) * .5f, FloorY - thickness * .5f, (z0 + z1) * .5f),
                new Vector3(x1 - x0, thickness, z1 - z0), Quaternion.identity, true);
        }

        static void BoardJoint(Action<string, string, Vector3, Vector3, Quaternion, bool> box, float x, float z0, float z1)
        {
            box("Upper hall floor board joint", "Dark", new Vector3(x, FloorY + .0015f, (z0 + z1) * .5f),
                new Vector3(.016f, .004f, z1 - z0 - .04f), Quaternion.identity, false);
        }

        static void Guard(Action<string, string, Vector3, Vector3, Quaternion, bool> box,
            string name, bool alongX, float face, float start, float end)
        {
            float span = end - start;
            float center = (start + end) * .5f;
            FaceBox(box, name + " kickboard", "Dark", alongX, face, center, FloorY + .10f, span, .20f, .135f, true);
            FaceBox(box, name + " middle rail", "Red", alongX, face, center, FloorY + .55f, span, .14f, .11f, true);
            FaceBox(box, name + " timber handrail", "Timber", alongX, face, center, FloorY + 1.09f, span, .14f, .135f, true);
            int segments = Mathf.CeilToInt(span / 1.40f);
            for (int i = 0; i <= segments; i++)
            {
                float u = Mathf.Lerp(start + .0675f, end - .0675f, i / (float)segments);
                FaceBox(box, name + " red post", "Red", alongX, face, u, FloorY + .59f, .135f, 1.18f, .135f, true);
                FaceBox(box, name + " gold cap", "Gold", alongX, face, u, FloorY + 1.182f, .153f, .048f, .153f, false);
            }
        }

        static void Facade(Action<string, string, Vector3, Vector3, Quaternion, bool> box, string name,
            bool alongX, float face, int outside, float start, float end, int bays)
        {
            const float sill = 2.88f, windowTop = 4.58f;
            float span = end - start;
            float center = (start + end) * .5f;
            FaceBox(box, name + " paneled knee wall", "Plaster", alongX, face, center,
                (FloorY + sill) * .5f, span, sill - FloorY, WallDepth, true);
            FaceBox(box, name + " clerestory solid", "Plaster", alongX, face, center,
                (windowTop + WallTop) * .5f, span, WallTop - windowTop, WallDepth, true);
            FaceBox(box, name + " base belt", "Dark", alongX, face + outside * .085f, center,
                FloorY + .14f, span, .18f, .092f, false);
            FaceBox(box, name + " upper red beam", "Red", alongX, face + outside * .065f, center,
                WallTop - .125f, span, .21f, .13f, false);

            float bayWidth = span / bays;
            float opening = bayWidth * .76f;
            float pier = (bayWidth - opening) * .5f;
            float midY = (sill + windowTop) * .5f;
            float height = windowTop - sill;
            for (int i = 0; i < bays; i++)
            {
                float mid = start + (i + .5f) * bayWidth;
                FaceBox(box, name + " pier west", "Plaster", alongX, face, mid - (opening + pier) * .5f,
                    midY, pier, height, WallDepth, true);
                FaceBox(box, name + " pier east", "Plaster", alongX, face, mid + (opening + pier) * .5f,
                    midY, pier, height, WallDepth, true);
                FaceBox(box, name + " recessed warm paper", "Paper", alongX, face, mid, midY,
                    opening, height, .058f, true);

                FaceBox(box, name + " paper window lower rail", "Timber", alongX, face, mid, sill + .045f,
                    opening, .09f, .16f, false);
                FaceBox(box, name + " paper window upper rail", "Timber", alongX, face, mid, windowTop - .045f,
                    opening, .09f, .16f, false);
                for (int j = -2; j <= 2; j++)
                    FaceBox(box, name + " paper lattice upright", "Timber", alongX, face,
                        mid + j * opening * .1667f, midY, .047f, height - .12f, .108f, false);
                for (int j = 1; j <= 3; j++)
                    FaceBox(box, name + " paper lattice crosspiece", "Timber", alongX, face,
                        mid, Mathf.Lerp(sill, windowTop, j * .25f), opening, .044f, .103f, false);
                FaceBox(box, name + " window gold line", "Gold", alongX, face + outside * .086f,
                    mid, windowTop - .045f, opening - .13f, .025f, .038f, false);
            }

            for (int i = 0; i <= bays; i++)
            {
                float u = Mathf.Clamp(start + i * bayWidth, start + .115f, end - .115f);
                // Let each finish stand proud of the plaster and its adjoining trim.
                FaceBox(box, name + " lacquered pillar", "Red", alongX, face, u,
                    (FloorY + WallTop) * .5f, .23f, WallTop - FloorY, .276f, false);
                FaceBox(box, name + " pillar stone foot", "Slate", alongX, face, u,
                    FloorY + .09f, .242f, .18f, .326f, false);
                FaceBox(box, name + " pillar bracket", "Timber", alongX, face, u,
                    WallTop - .31f, .246f, .16f, .318f, false);
            }
        }

        static void FaceBox(Action<string, string, Vector3, Vector3, Quaternion, bool> box, string name,
            string key, bool alongX, float face, float along, float y, float width, float height, float depth, bool collision)
        {
            Vector3 position = alongX ? new Vector3(along, y, face) : new Vector3(face, y, along);
            Vector3 size = alongX ? new Vector3(width, height, depth) : new Vector3(depth, height, width);
            box(name, key, position, size, Quaternion.identity, collision);
        }

        static void Lantern(Action<string, string, Vector3, Vector3, Quaternion, bool> box, string name, float x, float z)
        {
            box(name + " warm paper", "Paper", new Vector3(x, 4.58f, z), new Vector3(.30f, .41f, .30f), Quaternion.identity, false);
            box(name + " lower cap", "Red", new Vector3(x, 4.36f, z), new Vector3(.38f, .07f, .38f), Quaternion.identity, false);
            box(name + " upper cap", "Timber", new Vector3(x, 4.81f, z), new Vector3(.38f, .08f, .38f), Quaternion.identity, false);
            box(name + " gold collar", "Gold", new Vector3(x, 4.878f, z), new Vector3(.19f, .061f, .19f), Quaternion.identity, false);
            box(name + " suspension", "Dark", new Vector3(x, 5.126f, z), new Vector3(.041f, .45f, .041f), Quaternion.identity, false);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    box(name + " cage corner", "Timber", new Vector3(x + sx * .16f, 4.58f, z + sz * .16f),
                        new Vector3(.039f, .46f, .039f), Quaternion.identity, false);
        }
    }
}
