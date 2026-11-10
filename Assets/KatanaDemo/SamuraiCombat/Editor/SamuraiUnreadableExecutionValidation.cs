using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>Forces genuinely non-readable runtime mesh clones; never changes an importer or source mesh.</summary>
    public static class SamuraiUnreadableExecutionValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const string OutlinePath = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic/Meshes/OutlineHulls/201231762adb58b4d838f6a02f1a4812_2599488565566332574.asset";
        static readonly FieldInfo GroundSkins = typeof(SamuraiCombatant).GetField("executionGroundSkins", Private);
        static readonly List<Mesh> owned = new List<Mesh>(), transient = new List<Mesh>();
        static readonly List<object> cachedEntries = new List<object>();
        static readonly List<SourceMesh> sources = new List<SourceMesh>();
        static readonly List<Log> logs = new List<Log>();
        static Mesh probe;
        static readonly List<Vector3> probeVertices = new List<Vector3>();
        static bool listening, ownsFixture;

        sealed class SourceMesh
        {
            public SkinnedMeshRenderer renderer;
            public Mesh original, clone;
            public bool originalReadable;
        }
        sealed class Log { public string type, message, stack; }
        sealed class Check { public string name; public bool passed; public object observed; }
        sealed class Case
        {
            public string name, exception;
            public List<Check> checks = new List<Check>();
            public bool passed => exception == null && checks.All(c => c.passed);
            public void Is(bool ok, string name, object observed = null)
                => checks.Add(new Check { passed = ok, name = name, observed = observed });
        }
        static SamuraiCombatant Enemy => SamuraiBackstabValidation.Enemy;
        static SamuraiCombatant Player => SamuraiBackstabValidation.Player;
        static Mesh Baked(object entry) => (Mesh)entry.GetType().GetField("bakedMesh", Private).GetValue(entry);
        static object[] Entries()
        {
            var array = GroundSkins.GetValue(Enemy) as IEnumerable;
            return array == null ? new object[0] : array.Cast<object>().ToArray();
        }
        static void RecordLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning)
                logs.Add(new Log { type = type.ToString(), message = message, stack = stack });
        }

        public static string Setup()
        {
            if (!Application.isPlaying || !EditorApplication.isPaused)
                throw new InvalidOperationException("Use paused Play with positive TimeScale.");
            Cleanup(); logs.Clear(); cachedEntries.Clear(); transient.Clear();
            Application.logMessageReceived += RecordLog; listening = true;
            SamuraiBackstabValidation.Setup(); ownsFixture = true;
            AddActualOutline();
            foreach (var renderer in Enemy.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var original = renderer.sharedMesh;
                if (original == null || original.vertexCount == 0 || renderer.bones.Length == 0) continue;
                var clone = UnityEngine.Object.Instantiate(original);
                clone.name = original.name + " (forced unreadable validation clone)";
                clone.UploadMeshData(true); // Actually discard this temporary runtime mesh's CPU data.
                owned.Add(clone); sources.Add(new SourceMesh { renderer = renderer, original = original,
                    clone = clone, originalReadable = original.isReadable });
                renderer.sharedMesh = clone;
            }
            AddReadableControl();
            probe = new Mesh { name = "Unreadable execution independent ground probe" };
            return "Forced " + sources.Count + " actual enemy meshes non-readable; one readable three-vertex control skin. No importer/source modification.";
        }

        // Comic presentation adds this actual outline skin to scene actors, rather than the Enemy1 prefab.
        static void AddActualOutline()
        {
            var body = Enemy.Animator.GetComponentsInChildren<SkinnedMeshRenderer>().First(s => s.sharedMesh != null && s.bones.Length > 0);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(OutlinePath);
            if (mesh == null) throw new InvalidOperationException("Missing actual Enemy1 outline mesh: " + OutlinePath);
            var go = new GameObject("Actual comic outline ground-sampling control"); go.transform.SetParent(body.transform, false);
            var outline = go.AddComponent<SkinnedMeshRenderer>();
            outline.bones = body.bones; outline.rootBone = body.rootBone; outline.sharedMesh = mesh;
            outline.sharedMaterials = body.sharedMaterials; outline.localBounds = body.localBounds;
            outline.enabled = false; // Geometry sampler includes active skins regardless of renderer visibility.
        }

        // An active but invisible control exercises the original readable path beside the actual fallback skins.
        static void AddReadableControl()
        {
            var go = new GameObject("Readable ground-skin control"); go.transform.SetParent(Enemy.Animator.transform, false);
            var bone = Enemy.Animator.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Hips") ?? Enemy.Animator.transform;
            var renderer = go.AddComponent<SkinnedMeshRenderer>(); renderer.enabled = false;
            Vector3 p = go.transform.InverseTransformPoint(Enemy.transform.position + Vector3.up);
            var mesh = new Mesh { name = "Readable execution control mesh" };
            mesh.vertices = new[] { p, p + Vector3.right * .01f, p + Vector3.forward * .01f };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { bone.worldToLocalMatrix * go.transform.localToWorldMatrix };
            mesh.boneWeights = Enumerable.Range(0, 3).Select(_ => new BoneWeight { boneIndex0 = 0, weight0 = 1 }).ToArray();
            mesh.RecalculateBounds(); owned.Add(mesh); renderer.bones = new[] { bone }; renderer.rootBone = bone; renderer.sharedMesh = mesh;
        }

        static void Prepare()
        {
            Enemy.enabled = true; Player.enabled = true;
            SamuraiBackstabValidation.Prepare();
        }
        static object[] EnterPreparation(float dt)
        {
            SamuraiBackstabValidation.PressE(dt);
            for (int i = 0; i < 100 && Player.IsBackstabbing; i++)
            {
                if (Player.ExecutionElapsed > 0 && Player.ExecutionElapsed < Enemy.Profile.backstabPair.receiverDelay)
                {
                    var entries = Entries();
                    foreach (var entry in entries)
                    {
                        if (!cachedEntries.Contains(entry)) cachedEntries.Add(entry);
                        var mesh = Baked(entry); if (mesh != null && !transient.Contains(mesh)) transient.Add(mesh);
                    }
                    return entries;
                }
                SamuraiBackstabValidation.Step(dt);
            }
            return new object[0];
        }
        static float MinimumWorldY()
        {
            float minimum = float.PositiveInfinity;
            foreach (var renderer in Enemy.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (renderer.sharedMesh == null) continue;
                renderer.BakeMesh(probe, false); probe.GetVertices(probeVertices);
                foreach (var vertex in probeVertices) minimum = Mathf.Min(minimum, renderer.transform.TransformPoint(vertex).y);
            }
            return minimum;
        }
        static void Released(Case c, object[] previous, string reason)
        {
            c.Is(GroundSkins.GetValue(Enemy) == null, reason + ": actor releases its skin cache");
            c.Is(previous.Length > 0 && previous.All(entry => Baked(entry) == null),
                reason + ": every fallback disposes its mesh reference before deferred Destroy", previous.Length);
        }

        public static string RunMatrix(string outputPath)
        {
            if (!ownsFixture || Enemy == null) throw new InvalidOperationException("Call Setup in paused Play first.");
            var cases = new List<Case>();
            Action<string, Action<Case>> run = (name, test) =>
            {
                var item = new Case { name = name }; cases.Add(item); int firstLog = logs.Count;
                try { test(item); } catch (Exception e) { item.exception = e.ToString(); }
                var errors = logs.Skip(firstLog).Where(l => l.type != "Warning").ToArray();
                item.Is(errors.Length == 0, "no Error/Exception/Assert emitted", errors);
            };
            foreach (int hz in new[] { 15, 30, 120 })
            {
                int rate = hz;
                run(rate + "Hz real E / preparation / full release", c =>
                {
                    Prepare(); float dt = 1f / rate;
                    c.Is(sources.Count >= 2 && sources.All(s => !s.clone.isReadable && s.renderer.sharedMesh == s.clone),
                        "actual body and outline clones are genuinely non-readable", sources.Select(s => new { mesh = s.clone.name, s.clone.isReadable, s.clone.vertexCount }).ToArray());
                    int blood = SamuraiExecutionBlood.BurstCount, shake = SamuraiBackstabValidation.Feedback.ExecutionShakeCount;
                    var entries = EnterPreparation(dt); var meshes = entries.Select(Baked).Where(m => m != null).ToArray();
                    c.Is(Player.IsBackstabbing && Player.ExecutionElapsed > 0 && Player.ExecutionElapsed < .10f,
                        "real E reaches the first .10s receiver-preparation slice", Player.ExecutionElapsed);
                    c.Is(entries.Length == sources.Count + 1 && meshes.Length == sources.Count,
                        "unreadable skins use fallback; readable control retains original CPU path", new { caches = entries.Length, fallbackMeshes = meshes.Length });
                    c.Is(meshes.Length > 0 && meshes.All(m => m.isReadable && m.vertexCount > 0),
                        "actual BakeMesh fallback outputs are readable and nonempty", meshes.Select(m => new { m.name, m.isReadable, m.vertexCount }).ToArray());
                    float minimum = MinimumWorldY();
                    c.Is(!float.IsNaN(minimum) && !float.IsInfinity(minimum) && minimum >= SamuraiBackstabValidation.Origin.y - .005f,
                        "independent BakeMesh world minimum stays above the floor", minimum);
                    SamuraiBackstabValidation.Step(Mathf.Min(dt, .005f));
                    c.Is(Entries().Select(Baked).Where(m => m != null).SequenceEqual(meshes),
                        "preparation samples reuse the same fallback meshes");
                    for (int i = 0; i < rate * 4 && Player.IsExecuting; i++) SamuraiBackstabValidation.Step(dt);
                    c.Is(!Player.IsExecuting && !Enemy.IsBeingExecuted && Enemy.IsDead && Player.CanAct,
                        "full B2 releases both actors and leaves the player actionable");
                    c.Is(SamuraiExecutionBlood.BurstCount - blood == 1 && SamuraiBackstabValidation.Feedback.ExecutionShakeCount - shake == 1,
                        "unreadable fallback retains the single impact feedback");
                    Released(c, entries, "completed preparation/pair");
                    c.Is(sources.All(s => s.original.isReadable == s.originalReadable && !s.clone.isReadable),
                        "source readability is preserved and temporary source clones stay unreadable");
                });
            }
            foreach (string mode in new[] { "owner reset", "receiver reset", "receiver disable", "owner disable" })
            {
                string operation = mode;
                run(operation + " during preparation", c =>
                {
                    Prepare(); var entries = EnterPreparation(1f / 120f);
                    c.Is(entries.Any(e => Baked(e) != null), "fallback exists before interruption");
                    if (operation == "owner reset") Player.ResetCombat();
                    else if (operation == "receiver reset") Enemy.ResetCombat();
                    else if (operation == "receiver disable") Enemy.enabled = false;
                    else Player.enabled = false;
                    Released(c, entries, operation);
                    c.Is(!Player.IsExecuting && !Enemy.IsBeingExecuted, "interruption releases both pair owners");
                });
            }
            run("repeated reset reuses no stale cache", c =>
            {
                for (int cycle = 0; cycle < 4; cycle++)
                {
                    Prepare(); var entries = EnterPreparation(1f / 120f);
                    c.Is(entries.Count(e => Baked(e) != null) == sources.Count, "cycle " + cycle + " creates the required fallback set");
                    Enemy.ResetCombat(); Released(c, entries, "cycle " + cycle + " reset");
                }
            });
            run("destroy releases final fallback set", c =>
            {
                Prepare(); var entries = EnterPreparation(1f / 120f);
                c.Is(entries.Any(e => Baked(e) != null), "fallback exists before destroy");
                UnityEngine.Object.DestroyImmediate(Enemy.gameObject);
                c.Is(entries.All(e => Baked(e) == null), "OnDisable/OnDestroy disposes all captured fallback references");
                c.Is(!Player.IsExecuting, "destroyed receiver releases its live owner");
            });
            var result = new
            {
                kind = "Forced non-readable execution meshes", utc = DateTime.UtcNow.ToString("o"),
                assertions = cases.Sum(c => c.checks.Count), passed = cases.All(c => c.passed) && logs.All(l => l.type == "Warning"), cases,
                warnings = logs.Where(l => l.type == "Warning").ToArray(),
                allErrors = logs.Where(l => l.type != "Warning").ToArray(),
                observedFallbackMeshes = transient.Count,
                allFallbackReferencesDisposed = cachedEntries.All(e => Baked(e) == null),
                deferredNativeMeshesPendingEndOfFrame = transient.Count(m => m != null),
                note = "Paused Play does not execute deferred Object.Destroy until frame end. Cache ownership and per-skin mesh-reference disposal are asserted synchronously; call AuditDeferredDisposal after the parent advances a real Play frame."
            };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))); File.WriteAllText(outputPath, json); return json;
        }

        public static string AuditDeferredDisposal()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(new { observed = transient.Count,
                stillAlive = transient.Count(m => m != null), allDestroyed = transient.All(m => m == null),
                allFallbackReferencesDisposed = cachedEntries.All(e => Baked(e) == null) });
        }

        public static string Cleanup()
        {
            foreach (var source in sources) if (source.renderer != null) source.renderer.sharedMesh = source.original;
            if (ownsFixture) SamuraiBackstabValidation.Cleanup(); ownsFixture = false;
            foreach (var mesh in owned) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            owned.Clear(); sources.Clear();
            if (probe != null) UnityEngine.Object.DestroyImmediate(probe); probe = null;
            if (listening) Application.logMessageReceived -= RecordLog; listening = false;
            return "Unreadable-mesh fixture removed; source meshes/importers unchanged. Deferred runtime mesh audit remains available.";
        }
    }
}
