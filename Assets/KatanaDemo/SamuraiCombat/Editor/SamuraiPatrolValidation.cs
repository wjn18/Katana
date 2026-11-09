using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>
    /// Paused-Play validation with temporary actors and colliders 2km away from the level.
    /// Physics queries use the default physics scene, as does SamuraiEnemySensors.
    /// Never saves the scene, edits source assets, or writes player preferences.
    /// Normal Combatant construction may read its existing loadout preferences.
    /// </summary>
    public static class SamuraiPatrolValidation
    {
        static readonly Vector3 Origin = new Vector3(2000f, 100f, 2000f);
        const float Dt = 1f / 30f;

        [Serializable]
        sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string detail;
        }

        sealed class Checks
        {
            public readonly List<CheckResult> results = new List<CheckResult>();
            public void Add(bool passed, string name, string detail = null)
                => results.Add(new CheckResult { name = name, passed = passed, detail = detail });
            public string Save(string outputPath, string suite, object extra = null)
            {
                var failures = results.Where(r => !r.passed).Select(r => r.name).ToArray();
                string fullPath = Path.GetFullPath(outputPath);
                string folder = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(fullPath, Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    suite, passed = failures.Length == 0, assertions = results.Count,
                    failures, results, extra,
                    isolation = "Paused Play, temporary ordinary-enemy clones and colliders at +2km; no scene save or progress/preferences writes. Normal Combatant initialization may read the existing loadout."
                }, Newtonsoft.Json.Formatting.Indented));
                return Newtonsoft.Json.JsonConvert.SerializeObject(new
                { suite, passed = failures.Length == 0, assertions = results.Count, failures, outputPath = fullPath });
            }
        }

        sealed class ActorSnapshot
        {
            public SamuraiCombatant actor;
            public Vector3 position;
            public Quaternion rotation;
            public bool active;
        }

        sealed class Fixture : IDisposable
        {
            public readonly GameObject root;
            public readonly SamuraiCombatant actor;
            public readonly SamuraiPatrolAgent agent;
            public readonly SamuraiPatrolRoute route;
            public readonly SamuraiTrainingEnemy ai;
            public readonly Vector3 offset;
            public GameObject floor;

            public Fixture(SamuraiCombatant source, Vector3 spawn, bool createFloor)
            {
                root = new GameObject("Patrol validation temporary - never save");
                SceneManager.MoveGameObjectToScene(root, source.gameObject.scene);
                offset = spawn - source.transform.position;
                var clone = UnityEngine.Object.Instantiate(source.gameObject, root.transform);
                clone.name = "Patrol validation " + source.name;
                foreach (var script in clone.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(script is SamuraiCombatant) && !(script is SamuraiPatrolAgent)) script.enabled = false;
                foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                actor = clone.GetComponent<SamuraiCombatant>();
                ai = clone.GetComponent<SamuraiTrainingEnemy>();
                actor.Motor.enabled = false;
                clone.transform.SetPositionAndRotation(spawn, Quaternion.identity);
                actor.Motor.enabled = true;
                actor.Movement.CaptureSpawn();
                actor.ResetCombat();
                agent = clone.GetComponent<SamuraiPatrolAgent>();
                if (agent == null) agent = clone.AddComponent<SamuraiPatrolAgent>();
                agent.enabled = true;
                agent.patrolEnabled = true;
                agent.startPoint = 0;
                agent.speedMultiplier = .65f;
                agent.arrivalDistance = .15f;
                var routeObject = new GameObject("Patrol validation route");
                routeObject.transform.SetParent(root.transform);
                routeObject.transform.position = spawn - Vector3.up * .005f;
                route = routeObject.AddComponent<SamuraiPatrolRoute>();
                agent.route = route;
                if (createFloor)
                {
                    floor = Box("Patrol validation floor", routeObject.transform.position + Vector3.down * .5f,
                        new Vector3(30f, 1f, 30f));
                }
                Physics.SyncTransforms();
            }

            public GameObject Box(string name, Vector3 position, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform);
                go.transform.position = position;
                go.layer = FirstLayer(actor.Profile.obstructionMask);
                go.AddComponent<BoxCollider>().size = size;
                return go;
            }

            public void Points(SamuraiPatrolMode mode, params Vector3[] positions)
            {
                route.mode = mode;
                route.points.Clear();
                foreach (Vector3 position in positions)
                    route.points.Add(new SamuraiPatrolPoint { localPosition = position });
                agent.ResetPatrol();
                Settle();
            }

            public void Settle()
            {
                for (int i = 0; i < 8; i++) actor.Tick(default(SamuraiInputFrame), Dt);
                Physics.SyncTransforms();
            }

            public SamuraiInputFrame Step(float dt = Dt)
            {
                agent.TryBuildFrame(actor, dt, out SamuraiInputFrame frame);
                actor.AdvanceHitStop(dt);
                actor.Tick(frame, dt);
                Physics.SyncTransforms();
                return frame;
            }

            public void Drive(Vector3 direction, int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    agent.SetEngaged(actor, true);
                    actor.Tick(new SamuraiInputFrame { moveWorld = direction, facingWorld = direction }, Dt);
                    Physics.SyncTransforms();
                }
                agent.SetEngaged(actor, true);
            }

            public void Dispose()
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                Physics.SyncTransforms();
            }
        }

        static void RequirePausedPlay()
        {
            if (!Application.isPlaying || !EditorApplication.isPaused || Time.timeScale <= 0f)
                throw new InvalidOperationException("Enter Play and pause with timeScale > 0 before patrol validation.");
        }

        static SamuraiCombatant FindOrdinaryEnemy()
        {
            var ai = UnityEngine.Object.FindObjectsByType<SamuraiTrainingEnemy>()
                .FirstOrDefault(a => a.actor != null && a.actor.Profile != null && a.actor.Motor != null
                    && a.actor.gameObject.activeInHierarchy && a.actor.name.IndexOf("Elite", StringComparison.OrdinalIgnoreCase) < 0);
            if (ai == null) throw new InvalidOperationException("No active ordinary SamuraiTrainingEnemy in the loaded level.");
            return ai.actor;
        }

        static List<ActorSnapshot> CaptureActors()
        {
            return UnityEngine.Object.FindObjectsByType<SamuraiCombatant>()
                .Select(a => new ActorSnapshot { actor = a, position = a.transform.position,
                    rotation = a.transform.rotation, active = a.gameObject.activeSelf }).ToList();
        }

        static bool Preserved(List<ActorSnapshot> snapshots)
            => snapshots.All(s => s.actor != null && s.actor.transform.position == s.position
                && s.actor.transform.rotation == s.rotation && s.actor.gameObject.activeSelf == s.active);

        static int FirstLayer(LayerMask mask)
        {
            for (int i = 0; i < 32; i++) if ((mask.value & (1 << i)) != 0) return i;
            return 0;
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        { a.y = b.y; return Vector3.Distance(a, b); }

        /// <summary>Tests actual CharacterController movement and patrol state at 15/30/120Hz.</summary>
        public static string RunMatrix(string outputPath)
        {
            RequirePausedPlay();
            var source = FindOrdinaryEnemy();
            var originals = CaptureActors();
            var checks = new Checks();
            foreach (int hz in new[] { 15, 30, 120 })
            {
                float dt = 1f / hz;
                using (var f = new Fixture(source, Origin + Vector3.up * .005f, true))
                {
                    f.Points(SamuraiPatrolMode.Loop, Vector3.zero, Vector3.right * 2f, new Vector3(2f, 0f, 2f));
                    f.route.points[0].waitSeconds = .8f;
                    f.route.points[0].useFacing = true;
                    f.route.points[0].localYaw = 90f;
                    f.Step(dt);
                    Vector3 waitingPosition = f.actor.transform.position;
                    bool stationary = true;
                    for (int i = 0; i < Mathf.FloorToInt(hz * .6f); i++)
                        stationary &= f.Step(dt).moveWorld.sqrMagnitude < .000001f;
                    checks.Add(stationary && PlanarDistance(waitingPosition, f.actor.transform.position) < .001f
                        && f.agent.WaitRemaining > 0f, hz + "Hz node wait holds movement");
                    checks.Add(Vector3.Dot(f.actor.transform.forward, Vector3.right) > .95f,
                        hz + "Hz node facing uses normal actor turn");

                    var sequence = new List<int>();
                    var arrivedNodes = new HashSet<int> { 0 };
                    bool allArrivalsReal = true;
                    int previous = f.agent.CurrentPointIndex;
                    sequence.Add(previous);
                    for (int i = 0; i < hz * 16; i++)
                    {
                        f.Step(dt);
                        if (f.agent.IsWaiting)
                        {
                            int index = f.agent.CurrentPointIndex;
                            arrivedNodes.Add(index);
                            Vector3 destination = f.route.GetWorldPosition(index);
                            allArrivalsReal &= PlanarDistance(f.actor.transform.position, destination) <= f.agent.arrivalDistance + .002f
                                && Mathf.Abs(f.actor.transform.position.y - destination.y) <= .35f;
                        }
                        if (f.agent.CurrentPointIndex == previous) continue;
                        previous = f.agent.CurrentPointIndex;
                        sequence.Add(previous);
                        if (sequence.Count >= 5) break;
                    }
                    checks.Add(sequence.Count >= 5 && sequence.Take(5).SequenceEqual(new[] { 0, 1, 2, 0, 1 }),
                        hz + "Hz loop visits 0-1-2-0-1", string.Join(",", sequence));
                    checks.Add(allArrivalsReal && arrivedNodes.Count == 3,
                        hz + "Hz loop arrivals match real actor positions", "arrived=" + string.Join(",", arrivedNodes));
                    checks.Add(Mathf.Abs(f.actor.transform.position.y - Origin.y) < .15f,
                        hz + "Hz loop stays grounded", f.actor.transform.position.ToString("F3"));

                    f.actor.ResetCombat();
                    f.Points(SamuraiPatrolMode.PingPong, Vector3.zero, Vector3.right * 1.5f, Vector3.right * 3f);
                    sequence.Clear(); sequence.Add(0); previous = 0;
                    arrivedNodes.Clear(); allArrivalsReal = true;
                    for (int i = 0; i < hz * 15; i++)
                    {
                        f.Step(dt);
                        if (f.agent.IsWaiting)
                        {
                            int index = f.agent.CurrentPointIndex;
                            arrivedNodes.Add(index);
                            Vector3 destination = f.route.GetWorldPosition(index);
                            allArrivalsReal &= PlanarDistance(f.actor.transform.position, destination) <= f.agent.arrivalDistance + .002f
                                && Mathf.Abs(f.actor.transform.position.y - destination.y) <= .35f;
                        }
                        if (f.agent.CurrentPointIndex == previous) continue;
                        previous = f.agent.CurrentPointIndex; sequence.Add(previous);
                        if (sequence.Count >= 6) break;
                    }
                    checks.Add(sequence.Count >= 6 && sequence.Take(6).SequenceEqual(new[] { 0, 1, 2, 1, 0, 1 }),
                        hz + "Hz ping-pong reverses at endpoints", string.Join(",", sequence));
                    checks.Add(allArrivalsReal && arrivedNodes.Count == 3,
                        hz + "Hz ping-pong arrivals match real actor positions", "arrived=" + string.Join(",", arrivedNodes));
                    int frozenIndex = f.agent.CurrentPointIndex;
                    float frozenWait = f.agent.WaitRemaining;
                    Vector3 frozenPosition = f.actor.transform.position;
                    for (int i = 0; i < 20; i++) f.Step(0f);
                    checks.Add(frozenIndex == f.agent.CurrentPointIndex && frozenWait == f.agent.WaitRemaining
                        && frozenPosition == f.actor.transform.position, hz + "Hz zero delta freezes progress");
                    f.actor.ResetCombat();
                    checks.Add(f.agent.CurrentPointIndex == -1 && !f.agent.IsReturning && !f.agent.IsEngaged
                        && f.agent.BreadcrumbCount == 0 && f.agent.WaitRemaining == 0f,
                        hz + "Hz CombatReset clears all patrol progress");
                    checks.Add(Vector3.Distance(f.actor.transform.position, Origin + Vector3.up * .005f) < .002f,
                        hz + "Hz CombatReset restores captured spawn position", f.actor.transform.position.ToString("F3"));
                }
            }

            using (var f = new Fixture(source, Origin + Vector3.up * .005f, true))
            {
                f.Points(SamuraiPatrolMode.PingPong, Vector3.zero, Vector3.forward * 3f);
                f.Step();
                Vector3 departure = f.actor.transform.position;
                f.agent.SetEngaged(f.actor, true);
                int legFrames = Mathf.CeilToInt(3f / (f.actor.Profile.walkSpeed * Dt));
                f.Drive(Vector3.right, legFrames);
                Vector3 corner = f.actor.transform.position;
                f.Drive(Vector3.forward, legFrames);
                f.agent.SetEngaged(f.actor, false);
                checks.Add(f.agent.IsReturning && f.agent.BreadcrumbCount > 8,
                    "combat stores L-shaped actual movement trace", "breadcrumbs=" + f.agent.BreadcrumbCount);
                var wall = f.Box("Patrol validation closed gate", corner + Vector3.forward * 1.5f + Vector3.up,
                    new Vector3(2f, 2f, .2f));
                Physics.SyncTransforms();
                bool stationaryAtGate = false;
                for (int i = 0; i < 30 * 5; i++)
                {
                    SamuraiInputFrame frame = f.Step();
                    if (i > 60 && frame.moveWorld.sqrMagnitude < .000001f) stationaryAtGate = true;
                }
                checks.Add(f.agent.IsReturning && stationaryAtGate && f.actor.transform.position.z > wall.transform.position.z,
                    "closed gate blocks return without teleporting", f.agent.Status + "; " + f.actor.transform.position.ToString("F3"));
                wall.SetActive(false);
                Physics.SyncTransforms();
                bool passedCorner = false, returned = false;
                for (int i = 0; i < 30 * 15; i++)
                {
                    f.Step();
                    if (PlanarDistance(f.actor.transform.position, corner) < .3f) passedCorner = true;
                    if (!f.agent.IsReturning) { returned = true; break; }
                }
                checks.Add(returned && passedCorner && PlanarDistance(f.actor.transform.position, departure) <= .12f,
                    "gate reopening resumes L-shaped return to departure",
                    "distance=" + PlanarDistance(f.actor.transform.position, departure).ToString("F3") + "; corner=" + passedCorner);

                f.actor.ResetCombat(); f.agent.ResetPatrol(); f.Step();
                f.agent.SetEngaged(f.actor, true);
                var committedAction = f.actor.Profile.combo != null && f.actor.Profile.combo.Length > 0
                    ? f.actor.Profile.combo[0] : null;
                bool attackStarted = committedAction != null
                    && f.actor.Actions.TryStartAttack(committedAction, 0, Vector3.forward);
                var rootMotion = f.actor.Animator.GetComponent<SamuraiRootMotionDriver>();
                if (rootMotion != null) { rootMotion.Configure(f.actor); rootMotion.enabled = true; }
                var oldCullingMode = f.actor.Animator.cullingMode;
                f.actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                f.actor.Animator.Update(0f);
                bool deferredDuringAttack = false;
                int committedFrames = 0;
                for (int i = 0; attackStarted && i < 30 * 8; i++)
                {
                    f.agent.SetEngaged(f.actor, false);
                    f.agent.TryBuildFrame(f.actor, Dt, out SamuraiInputFrame frame);
                    if (f.actor.IsAttacking) deferredDuringAttack |= f.agent.IsEngaged && !f.agent.IsReturning
                        && frame.moveWorld.sqrMagnitude < .000001f;
                    f.actor.Tick(frame, Dt);
                    // Attack progress is sourced from the Animator, unlike ordinary locomotion.
                    // The real root-motion bridge transfers its evaluated displacement through the motor.
                    f.actor.Animator.Update(Dt);
                    Physics.SyncTransforms(); committedFrames++;
                    if (!f.actor.IsAttacking && f.actor.CanAct) break;
                }
                f.agent.SetEngaged(f.actor, false);
                checks.Add(attackStarted && deferredDuringAttack && !f.agent.IsEngaged && f.agent.IsReturning,
                    "disengagement finishes committed attack before sealing return trace",
                    "started=" + attackStarted + "; deferred=" + deferredDuringAttack + "; frames=" + committedFrames
                    + "; state=" + f.actor.StateName + "; phase=" + f.actor.StateNormalizedTime.ToString("F3")
                    + "; canAct=" + f.actor.CanAct + "; engaged=" + f.agent.IsEngaged + "; returning=" + f.agent.IsReturning
                    + "; breadcrumbs=" + f.agent.BreadcrumbCount + "; action=" + (committedAction != null ? committedAction.name : "null")
                    + "; rootMotion=" + (committedAction != null && committedAction.useRootMotion)
                    + "; duration=" + (committedAction != null ? committedAction.Duration.ToString("F3") : "null"));
                f.actor.Animator.cullingMode = oldCullingMode;
                if (rootMotion != null) rootMotion.enabled = false;

                f.actor.ResetCombat(); f.agent.ResetPatrol(); f.Step();
                f.actor.ReceiveDeflect(.6f);
                bool reactionHeld = !f.actor.CanAct;
                for (int i = 0; i < 4; i++) reactionHeld &= f.Step().moveWorld.sqrMagnitude < .000001f;
                checks.Add(reactionHeld, "deflected reaction suppresses patrol movement");
                f.actor.ResetCombat(); f.Step();
                var result = f.actor.ReceiveHit(new SamuraiDamageHit { Damage = f.actor.MaxHealth + 10f,
                    Unblockable = true, SourcePosition = f.actor.transform.position - Vector3.forward,
                    Point = f.actor.transform.position + Vector3.up });
                checks.Add(result == SamuraiHitResult.Killed && f.actor.IsDead
                    && f.Step().moveWorld.sqrMagnitude < .000001f, "death suppresses patrol movement");
                f.actor.ResetCombat();
                checks.Add(!f.actor.IsDead && f.agent.CurrentPointIndex == -1 && f.agent.BreadcrumbCount == 0,
                    "reset after death restores original spawn and clears route state");
            }

            using (var f = new Fixture(source, Origin + new Vector3(13f, .005f, 0f), true))
            {
                // Keep the floor centered on Origin: the route deliberately goes beyond its right edge.
                f.floor.transform.position = Origin + Vector3.down * .5f;
                f.Points(SamuraiPatrolMode.PingPong, Vector3.zero, Vector3.right * 7f);
                for (int i = 0; i < 30 * 9; i++) f.Step();
                checks.Add(f.actor.transform.position.x < Origin.x + 15f && f.actor.transform.position.y > Origin.y - .2f,
                    "ground probe prevents active walking off platform", f.actor.transform.position.ToString("F3"));
            }

            using (var f = new Fixture(source, Origin + Vector3.up * .005f, true))
            {
                f.Points(SamuraiPatrolMode.Loop, Vector3.up * 4f, Vector3.right * 2f + Vector3.up * 4f);
                bool held = true;
                for (int i = 0; i < 60; i++) held &= f.Step().moveWorld.sqrMagnitude < .000001f;
                checks.Add(held && f.agent.CurrentPointIndex == 0 && !f.agent.IsWaiting,
                    "same XZ on another floor is not treated as arrival");
            }
            ValidateAIIntegration(source, checks);
            checks.Add(Preserved(originals), "all original actors retain transforms and active states");
            return checks.Save(outputPath, "patrol-runtime-matrix");
        }

        static void ValidateAIIntegration(SamuraiCombatant source, Checks checks)
        {
            var opponentSource = UnityEngine.Object.FindObjectsByType<SamuraiPlayerInput>()
                .Select(p => p.actor != null ? p.actor : p.GetComponent<SamuraiCombatant>())
                .FirstOrDefault(a => a != null && a.Team != source.Team);
            if (opponentSource == null)
            { checks.Add(false, "AI integration has an opposing player actor"); return; }
            using (var f = new Fixture(source, Origin + Vector3.up * .005f, true))
            using (var opponent = new Fixture(opponentSource, Origin + new Vector3(100f, .005f, 0f), false))
            {
                if (f.ai == null) { checks.Add(false, "AI integration has SamuraiTrainingEnemy"); return; }
                f.Points(SamuraiPatrolMode.PingPong, Vector3.zero, Vector3.right * 3f);
                f.ai.enabled = true; // Editor remains paused; BuildFrame and Tick are still driven once below.
                f.ai.waitForPlayerCapture = false;
                f.ai.trainingMode = SamuraiTrainingMode.Normal;
                f.ai.activeTraining = true;
                f.ai.Configure(f.actor, opponent.actor);
                bool sawPatrolInput = false;
                for (int i = 0; i < 12; i++)
                {
                    SamuraiInputFrame frame = f.ai.BuildFrame(Dt);
                    sawPatrolInput |= frame.moveWorld.sqrMagnitude > .001f;
                    f.actor.Tick(frame, Dt); Physics.SyncTransforms();
                }
                checks.Add(sawPatrolInput && !f.ai.HasTrackedTarget && !f.agent.IsEngaged,
                    "AI without tracked target produces patrol input");
                opponent.actor.Motor.enabled = false;
                opponent.actor.transform.position = f.actor.transform.position + f.actor.transform.forward * 4f;
                opponent.actor.Motor.enabled = true;
                Physics.SyncTransforms();
                for (int i = 0; i < 16; i++)
                {
                    f.actor.Tick(f.ai.BuildFrame(Dt), Dt); Physics.SyncTransforms();
                }
                checks.Add(f.ai.HasTrackedTarget && f.agent.IsEngaged && f.ai.DecisionBranch != "Patrol",
                    "AI perception interrupts patrol and uses combat branch", f.ai.DecisionBranch);
                opponent.actor.Motor.enabled = false;
                opponent.actor.transform.position = Origin + Vector3.right * 100f;
                opponent.actor.Motor.enabled = true;
                Physics.SyncTransforms();
                f.actor.Tick(f.ai.BuildFrame(Dt), Dt);
                checks.Add(!f.ai.HasTrackedTarget && !f.agent.IsEngaged && f.agent.IsReturning,
                    "AI range disengagement enters recorded return", f.ai.DecisionBranch);
                bool returned = false;
                for (int i = 0; i < 30 * 12; i++)
                {
                    f.actor.Tick(f.ai.BuildFrame(Dt), Dt); Physics.SyncTransforms();
                    if (!f.agent.IsReturning) { returned = true; break; }
                }
                checks.Add(returned && f.ai.DecisionBranch == "Patrol",
                    "AI return hands control back to patrol", f.ai.DecisionBranch);
            }
        }

        /// <summary>
        /// Copies only enabled solid colliders in each route's neighborhood to the isolated test area,
        /// then walks the actual authored route through the source enemy's real motor.
        /// </summary>
        public static string RunSceneRoutes(string outputPath, string onlyName = null)
        {
            RequirePausedPlay();
            var originals = CaptureActors();
            var sources = UnityEngine.Object.FindObjectsByType<SamuraiPatrolAgent>()
                .Where(a => a.patrolEnabled && a.route != null && a.route.points.Count > 0
                    && a.GetComponent<SamuraiCombatant>() != null
                    && (string.IsNullOrEmpty(onlyName) || a.name.IndexOf(onlyName, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(a => a.name).ToArray();
            var checks = new Checks();
            var routes = new List<object>();
            checks.Add(sources.Length > 0, "authored patrol routes found", "count=" + sources.Length);
            foreach (var source in sources)
            {
                var originalActor = source.GetComponent<SamuraiCombatant>();
                using (var f = new Fixture(originalActor, originalActor.transform.position + Origin, false))
                {
                    f.route.transform.SetPositionAndRotation(source.route.transform.position + Origin, source.route.transform.rotation);
                    f.route.transform.localScale = source.route.transform.lossyScale;
                    f.route.mode = source.route.mode;
                    foreach (var p in source.route.points) f.route.points.Add(new SamuraiPatrolPoint
                    { localPosition = p.localPosition, waitSeconds = p.waitSeconds, useFacing = p.useFacing, localYaw = p.localYaw });
                    f.agent.speedMultiplier = source.speedMultiplier;
                    f.agent.arrivalDistance = source.arrivalDistance;
                    f.agent.startPoint = source.startPoint;
                    int colliders = CopyRouteColliders(source, f.root.transform, Origin);
                    f.agent.ResetPatrol(); f.Settle();
                    float distance = 0f, waits = 0f;
                    for (int i = 0; i < source.route.points.Count; i++)
                    {
                        waits += Mathf.Max(0f, source.route.points[i].waitSeconds);
                        if (i > 0) distance += Vector3.Distance(source.route.GetWorldPosition(i - 1), source.route.GetWorldPosition(i));
                    }
                    if (source.route.mode == SamuraiPatrolMode.Loop && source.route.points.Count > 1)
                        distance += Vector3.Distance(source.route.GetWorldPosition(source.route.points.Count - 1), source.route.GetWorldPosition(0));
                    else { distance *= 2f; waits *= 2f; }
                    float budget = Mathf.Clamp(distance / Mathf.Max(.05f, originalActor.Profile.walkSpeed * source.speedMultiplier)
                        + waits + 20f, 25f, 300f);
                    var visited = new List<int>();
                    var arrivedNodes = new HashSet<int>();
                    bool allArrivalsReal = true;
                    int previous = -1;
                    int expectedTransitions = source.route.points.Count < 2 ? 0 : source.route.mode == SamuraiPatrolMode.Loop
                        ? source.route.points.Count : 2 * (source.route.points.Count - 1);
                    bool completed = false;
                    float elapsed = 0f, stalled = 0f, maximumStall = 0f;
                    Vector3 before = f.actor.transform.position;
                    for (int frameIndex = 0; frameIndex < Mathf.CeilToInt(budget / Dt); frameIndex++)
                    {
                        f.Step(); elapsed += Dt;
                        if (f.agent.IsWaiting)
                        {
                            int index = f.agent.CurrentPointIndex;
                            arrivedNodes.Add(index);
                            Vector3 destination = f.route.GetWorldPosition(index);
                            allArrivalsReal &= PlanarDistance(f.actor.transform.position, destination) <= f.agent.arrivalDistance + .002f
                                && Mathf.Abs(f.actor.transform.position.y - destination.y) <= .35f;
                        }
                        if (f.agent.CurrentPointIndex != previous)
                        { previous = f.agent.CurrentPointIndex; visited.Add(previous); }
                        if (!f.agent.IsWaiting && PlanarDistance(before, f.actor.transform.position) < .0001f
                            && !f.agent.IsReturning) stalled += Dt;
                        else stalled = 0f;
                        maximumStall = Mathf.Max(maximumStall, stalled);
                        before = f.actor.transform.position;
                        if (visited.Count >= expectedTransitions + 1 && f.agent.IsWaiting
                            && f.agent.CurrentPointIndex == Mathf.Clamp(source.startPoint, 0, source.route.points.Count - 1))
                        { completed = true; break; }
                        if (stalled > 4f) break;
                    }
                    checks.Add(completed, source.name + " completes " + source.route.mode + " route",
                        "visited=" + string.Join(",", visited) + "; " + f.agent.Status);
                    checks.Add(maximumStall < 4f, source.name + " has no persistent blocked segment",
                        "maximumStall=" + maximumStall.ToString("F2"));
                    checks.Add(allArrivalsReal && arrivedNodes.Count == source.route.points.Count,
                        source.name + " arrives physically at every authored node", "arrived=" + string.Join(",", arrivedNodes));
                    routes.Add(new { enemy = source.name, route = source.route.name, mode = source.route.mode.ToString(),
                        points = source.route.points.Count, colliders, completed, elapsed,
                        visited, position = (f.actor.transform.position - Origin).ToString("F3"), status = f.agent.Status });
                }
            }
            checks.Add(Preserved(originals), "all original actors retain transforms and active states");
            return checks.Save(outputPath, "authored-scene-routes", routes);
        }

        static int CopyRouteColliders(SamuraiPatrolAgent source, Transform root, Vector3 offset)
        {
            var bounds = new Bounds(source.transform.position, Vector3.one);
            for (int i = 0; i < source.route.points.Count; i++) bounds.Encapsulate(source.route.GetWorldPosition(i));
            bounds.Expand(new Vector3(5f, 8f, 5f));
            int copied = 0;
            foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider>())
            {
                if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy
                    || collider.gameObject.scene != source.gameObject.scene
                    || collider is CharacterController || collider.GetComponentInParent<SamuraiCombatant>() != null
                    || !bounds.Intersects(collider.bounds)) continue;
                var go = new GameObject("Patrol validation collision " + collider.name);
                go.transform.SetParent(root);
                go.transform.SetPositionAndRotation(collider.transform.position + offset, collider.transform.rotation);
                go.transform.localScale = collider.transform.lossyScale;
                go.layer = collider.gameObject.layer;
                if (collider is BoxCollider box)
                { var copy = go.AddComponent<BoxCollider>(); copy.center = box.center; copy.size = box.size; }
                else if (collider is SphereCollider sphere)
                { var copy = go.AddComponent<SphereCollider>(); copy.center = sphere.center; copy.radius = sphere.radius; }
                else if (collider is CapsuleCollider capsule)
                { var copy = go.AddComponent<CapsuleCollider>(); copy.center = capsule.center; copy.radius = capsule.radius; copy.height = capsule.height; copy.direction = capsule.direction; }
                else if (collider is MeshCollider mesh && mesh.sharedMesh != null)
                { var copy = go.AddComponent<MeshCollider>(); copy.sharedMesh = mesh.sharedMesh; copy.convex = mesh.convex; }
                else if (collider is TerrainCollider terrain && terrain.terrainData != null)
                { var copy = go.AddComponent<TerrainCollider>(); copy.terrainData = terrain.terrainData; }
                else { UnityEngine.Object.DestroyImmediate(go); continue; }
                copied++;
            }
            Physics.SyncTransforms();
            return copied;
        }
    }
}
