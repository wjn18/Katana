using System;
using UnityEngine;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Occupied hall floors, framed openings and the west drop veranda.</summary>
    public static class SamuraiEliteGrandHallBody
    {
        const float Ground = 1.3671f;
        const float Lower = 1.58f;
        const float Upper = 5.30f;
        const float Front = 14.82f;
        const float Back = 22.40f;
        const float East = -17.25f;
        const float EntryX = -25.40f;
        const float Lip = 11.65f;
        internal const float WestFace = -33.40f;
        internal const float WestDropZ = 18.61f;
        internal static Vector3 WestDropLip => new Vector3(WestFace + Lip - Front, Upper, WestDropZ);

        public static void Build(Action<string, string, Vector3, Vector3, Quaternion, bool> box,
            Func<float, float, float> balconyGround, Action<string, Vector3, Vector3, Quaternion> collisionBox = null)
        {
            if (box == null) throw new ArgumentNullException(nameof(box));
            bool veranda = false;
            var westFacing = Quaternion.AngleAxis(90f, Vector3.up);
            var westAnchor = new Vector3(WestFace, 0f, WestDropZ);
            var originalAnchor = new Vector3(EntryX, 0f, Front);

            void Emit(string name, string palette, Vector3 position, Vector3 size, Quaternion rotation, bool collision,
                Vector3 visualOffset = default, Vector3 visualExpansion = default)
            {
                if (veranda)
                {
                    position = westAnchor + westFacing * (position - originalAnchor);
                    rotation = westFacing * rotation;
                    visualOffset = westFacing * visualOffset;
                }
                if (collision && (visualOffset != Vector3.zero || visualExpansion != Vector3.zero))
                {
                    if (collisionBox == null)
                    {
                        box(name, palette, position, size, rotation, true);
                        return;
                    }
                    collisionBox(name, position, size, rotation);
                    collision = false;
                }
                box(name, palette, position + visualOffset, size + visualExpansion, rotation, collision);
            }

            void Add(string name, string palette, float x, float y, float z,
                float width, float height, float depth, bool collision = false,
                Vector3 visualOffset = default, Vector3 visualExpansion = default)
            {
                Emit(name, palette, new Vector3(x, y, z), new Vector3(width, height, depth), Quaternion.identity,
                    collision, visualOffset, visualExpansion);
            }

            void Band(string name, string palette, bool alongX, float axis, float start, float end,
                float bottom, float top, float thickness, bool collision = false)
            {
                float center = (start + end) * .5f;
                float height = top - bottom;
                float length = end - start;
                if (alongX) Add(name, palette, center, bottom + height * .5f, axis, length, height, thickness, collision);
                else Add(name, palette, axis, bottom + height * .5f, center, thickness, height, length, collision);
            }

            void Wall(string name, bool alongX, float axis, float start, float end, float bottom, float top)
            {
                Band(name + " plaster wall", "Plaster", alongX, axis, start, end, bottom, top, .22f, true);
                Band(name + " slate plinth", "Slate", alongX, axis, start, end, bottom, bottom + .47f, .252f);
                Band(name + " lower timber sill", "Timber", alongX, axis, start, end, bottom + .47f, bottom + .57f, .26f);
                Band(name + " dark crown", "Dark", alongX, axis, start, end, top - .16f, top + .02f, .28f);
                int count = Mathf.Max(1, Mathf.CeilToInt((end - start) / 2.62f));
                float span = (end - start) / count;
                for (int i = 0; i < count; i++)
                {
                    float center = start + span * (i + .5f);
                    string bay = name + " bay " + (i + 1);
                    if (i > 0)
                        Band(bay + " dividing timber", "Timber", alongX, axis, start + i * span - .065f,
                            start + i * span + .065f, bottom + .56f, top - .1f, .273f);
                    if (span < 1.1f) continue;
                    float half = Mathf.Min(.82f, span * .5f - .26f);
                    float sill = bottom + 1.15f;
                    float head = bottom + 2.25f;
                    if ((i & 1) != 0)
                    {
                        Band(bay + " oak panel", "Timber", alongX, axis, center - half, center + half, sill, head, .237f);
                        Band(bay + " raised upper rib", "Dark", alongX, axis, center - half - .012f,
                            center + half + .012f, head - .1f, head + .012f, .266f);
                        continue;
                    }
                    foreach (int side in new[] { -1, 1 })
                    {
                        float face = axis + side * .122f;
                        string faceName = bay + (side < 0 ? " face A" : " face B");
                        Band(faceName + " paper", "Paper", alongX, face, center - half, center + half, sill, head, .026f);
                        float frameFace = axis + side * .149f;
                        Band(faceName + " window sill", "Dark", alongX, frameFace, center - half - .07f,
                            center + half + .07f, sill - .065f, sill + .035f, .044f);
                        Band(faceName + " window head", "Timber", alongX, frameFace, center - half - .055f,
                            center + half + .055f, head - .045f, head + .045f, .040f);
                        const float uprightHalfWidth = .023f;
                        for (int j = -2; j <= 2; j++)
                        {
                            float line = center + j * half * .5f;
                            Band(faceName + " lattice upright " + (j + 3), "Timber", alongX, frameFace, line - uprightHalfWidth,
                                line + uprightHalfWidth, sill + .035f, head - .045f, .029f);
                            if (j < 2)
                                Band(faceName + " middle lattice " + (j + 3), "Timber", alongX, frameFace,
                                    line + uprightHalfWidth, line + half * .5f - uprightHalfWidth,
                                    sill + .49f, sill + .54f, .029f);
                        }
                    }
                }
            }

            void Column(string name, float x, float z, float bottom, float top, float size, bool collision,
                bool doorway = false)
            {
                // The visible jamb overlaps the opening edge. The passage keeps its original collision width.
                var reveal = doorway ? new Vector3(.04f, 0f, .04f) : Vector3.zero;
                Add(name + " red shaft", "Red", x, (bottom + top) * .5f, z, size, top - bottom, size,
                    collision, visualExpansion: reveal);
                Add(name + " sandstone shoe", "Stone", x, bottom + .105f, z, size + .13f, .21f, size + .13f);
                Add(name + " dark capital", "Dark", x, top - .105f, z, size + .17f, .21f, size + .17f);
                float neck = size + .034f + reveal.x;
                Add(name + " brass neck", "Gold", x, top - .235f, z, neck, .045f, neck);
            }

            void Lantern(string name, float x, float y, float z, Vector3 towardWall)
            {
                Add(name + " paper chamber", "Paper", x, y, z, .27f, .40f, .27f);
                Add(name + " dark cap", "Dark", x, y + .21f, z, .34f, .07f, .34f);
                Add(name + " timber foot", "Timber", x, y - .21f, z, .31f, .07f, .31f);
                Add(name + " warm band", "Gold", x, y + .148f, z, .285f, .025f, .285f);
                Add(name + " hanging stem", "Dark", x, y + .325f, z, .036f, .19f, .036f);
                var bracket = new Vector3(x, y + .387f, z) + towardWall * .162f;
                Add(name + " wall bracket", "Dark", bracket.x, bracket.y, bracket.z,
                    .052f + towardWall.x * .248f, .052f, .052f + towardWall.z * .248f);
                foreach (int dx in new[] { -1, 1 })
                    foreach (int dz in new[] { -1, 1 })
                        Add(name + " cage " + dx + " " + dz, "Timber", x + dx * .132f, y, z + dz * .132f, .033f, .41f, .033f);
            }

            // 1F. The stone ramp touches both planes along its sloped top face.
            Add("Great hall lower stone floor", "Stone", -25.40f, Lower - .12f, 18.76f, 16.30f, .24f, 7.28f, true);
            const float rampStart = 14.74f;
            const float rampEnd = 15.12f;
            float rise = Lower - Ground;
            float run = rampEnd - rampStart;
            var grade = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0f, 0f);
            Vector3 rampTop = new Vector3(EntryX, (Ground + Lower) * .5f, (rampStart + rampEnd) * .5f);
            box("Lower south entrance graded stone threshold", "Stone", rampTop - grade * Vector3.up * .06f,
                new Vector3(3.65f, .12f, Mathf.Sqrt(run * run + rise * rise)), grade, true);
            Add("Lower entry slate runner", "Slate", EntryX, Lower + .009f, 18.53f, 2.76f, .018f, 6.70f);
            foreach (float x in new[] { -29.85f, -20.95f })
                Add("Lower side aisle inset floor " + (x < EntryX ? "west" : "east"), "Timber", x, Lower + .010f, 18.76f,
                    5.26f, .020f, 6.66f);
            Wall("Lower west", false, -33.44f, 15.12f, Back, Lower, 5.08f);
            Wall("Lower east", false, -17.36f, 15.12f, Back, Lower, 5.08f);
            Wall("Lower north", true, 22.29f, -33.55f, East, Lower, 5.08f);
            Wall("Lower south west wing", true, 15.23f, -33.55f, -27.24f, Lower, 5.08f);
            Wall("Lower south east wing", true, 15.23f, -23.56f, East, Lower, 5.08f);
            Band("Lower south entry lintel", "Dark", true, 15.23f, -27.24f, -23.56f, 4.51f, 5.08f, .28f, true);
            Band("Lower south warm entry frieze", "Paper", true, 15.074f, -27.01f, -23.79f, 4.63f, 4.86f, .045f);
            foreach (float x in new[] { -27.37f, -23.43f })
                Column("Lower south doorway " + (x < EntryX ? "west" : "east"), x, 15.18f, Lower, 4.98f, .26f, true, doorway: true);
            Band("Lower hall fascia beam", "Timber", true, 15.184f, -33.55f, East, 4.81f, 5.00f, .22f);
            foreach (float x in new[] { -29.48f, -21.32f })
                foreach (float z in new[] { 16.82f, 20.92f })
                    Column("Lower interior column " + (x < EntryX ? "west " : "east ") + (z < 18f ? "south" : "north"),
                        x, z, Lower, 5.08f, .30f, true);
            Lantern("Lower west entrance lantern", -27.97f, 4.03f, 14.88f, Vector3.forward);
            Lantern("Lower east entrance lantern", -22.83f, 4.03f, 14.88f, Vector3.forward);

            // 2F. The east gallery and west veranda have real openings in the wall shell.
            Add("Great hall upper timber floor", "Timber", -25.325f, Upper - .11f, 18.61f, 16.15f, .22f, 7.58f, true);
            const float westDoorSouth = WestDropZ - 1.85f, westDoorNorth = WestDropZ + 1.85f;
            Wall("Upper west south of veranda", false, -33.29f, Front, westDoorSouth, Upper, 8.78f);
            Wall("Upper west north of veranda", false, -33.29f, westDoorNorth, Back, Upper, 8.78f);
            Band("West veranda door lintel", "Dark", false, -33.29f, westDoorSouth, westDoorNorth, 8.02f, 8.78f, .30f, true);
            Band("West veranda warm transom", "Paper", false, -33.45f, westDoorSouth + .21f, westDoorNorth - .21f, 8.15f, 8.47f, .036f);
            foreach (float z in new[] { westDoorSouth - .13f, westDoorNorth + .13f })
                Column(z < WestDropZ ? "West veranda south jamb" : "West veranda north jamb",
                    -33.29f, z, Upper, 8.66f, .26f, true, doorway: true);
            Lantern("West veranda south lantern", -33.64f, 7.51f, WestDropZ - 2.62f, Vector3.right);
            Lantern("West veranda north lantern", -33.64f, 7.51f, WestDropZ + 2.62f, Vector3.right);
            Wall("Upper north", true, 22.29f, -33.40f, East, Upper, 8.78f);
            Wall("Upper east south of gallery", false, -17.36f, Front, 19.43f, Upper, 8.78f);
            Wall("Upper east north of gallery", false, -17.36f, 21.47f, Back, Upper, 8.78f);
            Band("Gallery doorway lintel", "Dark", false, -17.36f, 19.43f, 21.47f, 8.03f, 8.78f, .28f, true);
            Band("Gallery brass transom trim", "Gold", false, -17.205f, 19.56f, 21.34f, 8.17f, 8.20f, .024f);
            Add("Gallery flush joint sill", "Slate", -17.265f, Upper - .052f, 20.45f, .13f, .104f, 2.04f, true,
                visualOffset: Vector3.up * .007f, visualExpansion: Vector3.up * .014f);
            foreach (float z in new[] { 19.30f, 21.60f })
                Column("Upper gallery doorway " + (z < 20.45f ? "south" : "north"), -17.38f, z, Upper, 8.65f, .26f, true, doorway: true);
            Wall("Upper south west wing", true, 14.93f, -33.40f, -27.25f, Upper, 8.78f);
            Wall("Upper south east wing", true, 14.93f, -23.55f, East, Upper, 8.78f);
            Band("Upper veranda door lintel", "Dark", true, 14.93f, -27.25f, -23.55f, 8.02f, 8.78f, .30f, true);
            Band("Upper veranda warm transom", "Paper", true, 14.761f, -27.04f, -23.76f, 8.15f, 8.47f, .036f);
            foreach (float x in new[] { -27.38f, -23.42f })
                Column("Upper veranda doorway " + (x < EntryX ? "west" : "east"), x, 14.93f, Upper, 8.66f, .26f, true, doorway: true);
            Lantern("Upper west veranda lantern", -28.02f, 7.51f, 14.58f, Vector3.forward);
            Lantern("Upper east veranda lantern", -22.78f, 7.51f, 14.58f, Vector3.forward);
            foreach (float x in new[] { -30.39f, -20.41f })
                foreach (float z in new[] { 15.68f, 21.66f })
                    Column("Upper interior column " + (x < EntryX ? "west " : "east ") + (z < 18f ? "south" : "north"),
                        x, z, Upper, 8.68f, .27f, true);
            foreach (float z in new[] { 15.68f, 21.66f })
                Band("Upper ceiling tie " + (z < 18f ? "south" : "north"), "Timber", true, z, -33.3f,
                    -17.35f, 8.53f, 8.76f, .31f);

            // Veranda supports stop behind the edge. Only the middle of the rail is open.
            veranda = true;
            Add("West drop veranda deck", "Timber", EntryX, Upper - .11f, (Lip + Front) * .5f, 7f, .22f, Front - Lip, true);
            Add("Veranda west edge fascia", "Dark", EntryX, Upper - .165f, Lip + .024f, 6.94f, .22f, .092f);
            Add("Veranda fascia copper band", "Gold", EntryX, Upper - .117f, Lip - .027f, 6.90f, .019f, .014f);
            foreach (float x in new[] { -28.43f, -22.37f })
            {
                string side = x < EntryX ? "North" : "South";
                var foot = westAnchor + westFacing * (new Vector3(x, 0f, 13.48f) - originalAnchor);
                float ground = balconyGround(foot.x, foot.z) - .07f;
                Column(side + " veranda ground support", x, 13.48f, ground, 5.08f, .37f, true);
                Add(side + " veranda support crosshead", "Timber", x, 4.97f, 13.48f, .78f, .22f, .76f);
            }

            void Rail(string name, bool alongX, float axis, float start, float end)
            {
                Band(name + " top rail", "Dark", alongX, axis, start, end, Upper + .77f, Upper + .92f, .16f, true);
                Band(name + " middle rail", "Timber", alongX, axis, start, end, Upper + .38f, Upper + .49f, .12f, true);
                Band(name + " stone curb", "Slate", alongX, axis, start, end, Upper, Upper + .12f, .14f);
                int bays = Mathf.Max(1, Mathf.CeilToInt((end - start) / .55f));
                for (int i = 0; i < bays; i++)
                {
                    float center = Mathf.Lerp(start, end, (i + .5f) / bays);
                    Band(name + " baluster " + (i + 1), "Red", alongX, axis, center - .039f, center + .039f,
                        Upper + .12f, Upper + .78f, .09f);
                }
            }

            Rail("North veranda flank", false, -28.82f, Lip, Front);
            Rail("South veranda flank", false, -21.98f, Lip, Front);
            Rail("North west drop guard", true, Lip + .08f, -28.90f, -26.35f);
            Rail("South west drop guard", true, Lip + .08f, -24.45f, -21.90f);
            foreach (float x in new[] { -28.79f, -26.47f, -24.33f, -22.01f })
                Column("West veranda rail post " + (x < -27f ? "north corner" : x < EntryX ? "north drop" : x < -23f ? "south drop" : "south corner"),
                    x, Lip + .16f, Upper, Upper + .96f, .16f, true);

            for (int i = 0; i < 2; i++)
            {
                float z = Lip + 1.00f - i * .32f;
                var tip = new Vector3(EntryX, Upper + .006f, z - .21f);
                foreach (int side in new[] { -1, 1 })
                {
                    var heel = new Vector3(EntryX + side * .22f, tip.y, z);
                    Vector3 line = tip - heel;
                    Emit("West drop brass floor cue " + i + " " + side, "Gold", (heel + tip) * .5f,
                        new Vector3(.029f, .010f, line.magnitude), Quaternion.LookRotation(line, Vector3.up), false);
                }
            }
        }
    }
}
