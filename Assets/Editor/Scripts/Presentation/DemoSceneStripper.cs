#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatEditor
{
    /// <summary>
    /// Turns a copied gameplay scene into an inert presentation stage, driven by
    /// <see cref="DemoStripRulesSO"/>.
    ///
    /// ALWAYS DRY-RUN FIRST. The dry run reports exactly what would go and touches nothing, which is
    /// the only cheap way to notice that a rule is about to delete something that is actually scenery.
    /// The scene is left dirty and UNSAVED even on a real run, so a bad strip is abandoned by closing
    /// the scene without saving.
    ///
    /// Order is deliberate: whole objects first, then components on what survives. Doing it the other
    /// way strips components off objects that were about to be deleted anyway, which makes the report
    /// misleading about what actually mattered.
    /// </summary>
    public static class DemoSceneStripper
    {
        public sealed class StripResult
        {
            public bool DryRun;
            public string SceneName;
            public int ObjectsDeleted;
            public int MonoBehavioursRemoved;
            public int TypedComponentsRemoved;
            public int CollidersRemoved;
            public int RigidbodiesRemoved;
            public int CamerasRemoved;
            public int MissingScriptsRemoved;
            public int Failures;
            public int Passes;
            /// <summary>Renderers that went with a deleted object — XR controller meshes and the like.</summary>
            public int RenderersRemovedWithObjects;
            public readonly List<string> DeletedObjectNames = new List<string>();
            public readonly List<string> KeptBecauseProtected = new List<string>();
            public readonly List<string> FailureNotes = new List<string>();

            public int TotalComponentsRemoved =>
                MonoBehavioursRemoved + TypedComponentsRemoved + CollidersRemoved +
                RigidbodiesRemoved + CamerasRemoved + MissingScriptsRemoved;
        }

        public static StripResult Strip(Scene scene, DemoStripRulesSO rules, bool dryRun)
        {
            var res = new StripResult { DryRun = dryRun, SceneName = scene.name };

            if (rules == null)
            {
                // Worth naming the non-obvious cause: opening a scene in Single mode unloads assets
                // nothing holds a reference to, so a rules asset loaded BEFORE the OpenScene call is
                // already destroyed by the time it gets here — and a destroyed UnityEngine.Object
                // compares equal to null while still looking fine in a log written earlier.
                // Load the rules asset AFTER opening the scene.
                res.FailureNotes.Add("No DemoStripRulesSO supplied (or it was unloaded — load the " +
                                     "rules asset AFTER opening the scene, not before).");
                res.Failures++;
                return res;
            }
            if (!scene.isLoaded)
            {
                res.FailureNotes.Add($"Scene '{scene.path}' is not loaded.");
                res.Failures++;
                return res;
            }

            // ---- pass 1: whole objects ----
            // Collected before deleting so the hierarchy is not mutated mid-walk.
            var toDelete = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
                CollectDeletions(root.transform, rules, toDelete, res);

            foreach (GameObject go in toDelete)
            {
                if (go == null) continue;
                res.DeletedObjectNames.Add(go.name);
                res.ObjectsDeleted++;

                // Counted separately so the renderer total adds up afterwards. An XR rig legitimately
                // contains renderers (controller meshes, and a LineRenderer for the ray), and without
                // this the audit looks like the strip ate scenery.
                //
                // Counts EVERY renderer type, not just mesh ones, to match how SceneAuditReport
                // totals them — an earlier version counted only MeshRenderer and the two figures then
                // disagreed by exactly the XR ray's LineRenderers, which read as unexplained loss.
                res.RenderersRemovedWithObjects += go.GetComponentsInChildren<Renderer>(true).Length;

                if (!dryRun) Object.DestroyImmediate(go);
            }

            // ---- pass 2: components on what survived ----
            // Repeated until a pass changes nothing. [RequireComponent] means a dependency cannot be
            // removed before its dependents, and those form chains — ShipPartSpawnPoint requires
            // XRGrabInteractable, which requires Rigidbody. One pass in dependency-blind order leaves
            // a tail of refusals behind (and Unity LOGS those rather than throwing, so they cannot be
            // caught). Each pass peels one layer off the graph.
            const int maxPasses = 8;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                int removedThisPass = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                    removedThisPass += StripPass(root.transform, rules, res, dryRun);

                res.Passes = pass + 1;
                if (dryRun) break;              // nothing was removed, so a second pass would double-count
                if (removedThisPass == 0) break;
            }

            // Anything still present that the rules wanted gone is a genuine failure worth naming.
            if (!dryRun) RecordSurvivors(scene, rules, res);

            // ---- pass 3: baked data that the offset invalidated ----
            if (rules.ClearOcclusionData && !dryRun && StaticOcclusionCulling.umbraDataSize > 0)
            {
                SceneManager.SetActiveScene(scene);
                StaticOcclusionCulling.Clear();
            }

            if (!dryRun) EditorSceneManager.MarkSceneDirty(scene);
            return res;
        }

        private static void CollectDeletions(Transform t, DemoStripRulesSO rules,
                                            List<GameObject> toDelete, StripResult res)
        {
            if (rules.IsKept(t.name))
            {
                res.KeptBecauseProtected.Add(t.name);
                return;   // protected subtrees are not descended into either
            }

            if (rules.ShouldDeleteObject(t.name))
            {
                toDelete.Add(t.gameObject);
                return;   // no point walking children of something being deleted
            }

            for (int i = 0; i < t.childCount; i++)
                CollectDeletions(t.GetChild(i), rules, toDelete, res);
        }

        /// <summary>
        /// One removal pass over a subtree. Returns how many components actually went, which is what
        /// tells the caller whether another pass is worth running.
        /// </summary>
        private static int StripPass(Transform t, DemoStripRulesSO rules, StripResult res, bool dryRun)
        {
            if (t == null) return 0;

            // Protected: leave this object and everything under it completely alone.
            if (rules.IsKept(t.name)) return 0;

            int removed = 0;

            if (rules.RemoveMissingScripts && !dryRun)
                res.MissingScriptsRemoved += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            // MonoBehaviours first: [RequireComponent] almost always points from a script AT a
            // Rigidbody or Collider, so taking scripts out first unblocks the physics components in
            // the same pass instead of the next one.
            Component[] components = t.GetComponents<Component>();
            System.Array.Sort(components, (a, b) =>
            {
                int ra = a is MonoBehaviour ? 0 : 1;
                int rb = b is MonoBehaviour ? 0 : 1;
                return ra.CompareTo(rb);
            });

            foreach (Component c in components)
            {
                if (c == null) continue;                 // already gone, or a missing script slot
                if (c is Transform) continue;

                string typeName = c.GetType().Name;
                if (!WantsRemoval(c, typeName, rules)) continue;

                // Dry run tallies everything it wants gone, including things a later pass would get,
                // so the preview matches the eventual outcome.
                if (dryRun) { Tally(c, typeName, rules, res); continue; }

                // Skip anything another live component still declares [RequireComponent] on. Unity
                // would refuse and LOG AN ERROR rather than throw, which reads as a tool failure to
                // anyone watching the console. A later pass picks it up once the dependent is gone.
                if (IsRequiredByAnother(c)) continue;

                Object.DestroyImmediate(c);

                // Unity LOGS a refusal instead of throwing, and leaves the component in place. The
                // reference going null is the only reliable signal that it actually went.
                if (c == null)
                {
                    Tally(c, typeName, rules, res);
                    removed++;
                }
            }

            for (int i = t.childCount - 1; i >= 0; i--)
                removed += StripPass(t.GetChild(i), rules, res, dryRun);

            return removed;
        }

        /// <summary>
        /// True when some other component on the same GameObject declares
        /// <c>[RequireComponent(typeof(T))]</c> where T accepts this component. Attempting the removal
        /// anyway is harmless to the scene but produces a red console error, and 22 of those per scene
        /// makes a working tool look broken.
        ///
        /// Base types count: a script requiring <c>Collider</c> pins a <c>BoxCollider</c> too.
        /// </summary>
        private static bool IsRequiredByAnother(Component c)
        {
            System.Type mine = c.GetType();

            foreach (Component other in c.gameObject.GetComponents<Component>())
            {
                if (other == null || ReferenceEquals(other, c)) continue;

                object[] attrs = other.GetType().GetCustomAttributes(typeof(RequireComponent), true);
                foreach (object a in attrs)
                {
                    var rc = (RequireComponent)a;
                    if (Accepts(rc.m_Type0, mine) || Accepts(rc.m_Type1, mine) || Accepts(rc.m_Type2, mine))
                        return true;
                }
            }
            return false;
        }

        private static bool Accepts(System.Type required, System.Type mine) =>
            required != null && required.IsAssignableFrom(mine);

        private static bool WantsRemoval(Component c, string typeName, DemoStripRulesSO rules)
        {
            if (rules.IsComponentKept(typeName)) return false;
            if (rules.ShouldRemoveComponentType(typeName)) return true;
            if (c is Collider && rules.RemoveColliders) return true;
            if (c is Rigidbody && rules.RemoveRigidbodies) return true;
            if (c is ParticleSystem && rules.RemoveParticleSystems) return true;
            if (c is Animator && rules.RemoveAnimators) return true;
            if (c is Light && rules.RemoveLights) return true;
            if (c is Camera && rules.RemoveCameras && !IsCinemachine(c.gameObject)) return true;
            if (rules.RemoveAllMonoBehaviours && c is MonoBehaviour &&
                !IsCinemachine(c) && !IsRenderingInfrastructure(c)) return true;
            return false;
        }

        /// <summary>
        /// Categorises a removal. Takes the type name separately because a destroyed component's
        /// reference is already null by the time this is called on the real path.
        /// </summary>
        private static void Tally(Component c, string typeName, DemoStripRulesSO rules, StripResult res)
        {
            if (rules.ShouldRemoveComponentType(typeName)) { res.TypedComponentsRemoved++; return; }

            switch (typeName)
            {
                case "Rigidbody": res.RigidbodiesRemoved++; return;
                case "Camera": res.CamerasRemoved++; return;
            }

            if (typeName.EndsWith("Collider")) { res.CollidersRemoved++; return; }
            if (typeName == "ParticleSystem" || typeName == "Animator" || typeName == "Light")
            {
                res.TypedComponentsRemoved++;
                return;
            }
            res.MonoBehavioursRemoved++;
        }

        /// <summary>
        /// Names anything the rules wanted gone that is still there after every pass. These are real
        /// dependency knots — usually a script pair that each require the other — and the honest answer
        /// is to add them to the rules' keep-list or delete the object outright, not to force it.
        /// </summary>
        private static void RecordSurvivors(Scene scene, DemoStripRulesSO rules, StripResult res)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform) continue;
                    if (rules.IsKept(c.gameObject.name)) continue;

                    string typeName = c.GetType().Name;
                    if (!WantsRemoval(c, typeName, rules)) continue;

                    res.Failures++;
                    if (res.FailureNotes.Count < 40)
                        res.FailureNotes.Add($"{c.gameObject.name}.{typeName} survived — dependency knot");
                }
            }
        }

        /// <summary>
        /// Render-pipeline components survive "remove all MonoBehaviours", because several of the
        /// things that make a scene LOOK like itself happen to be MonoBehaviours:
        /// <c>Volume</c> carries the post-process grade and the fog, and <c>ProbeVolume</c> carries the
        /// APV lighting data (Shipyard has one of each). Stripping those would silently change the art
        /// this deck exists to show.
        ///
        /// Checked by namespace rather than by a list of names on purpose — a name list would have to
        /// be extended every time the pipeline adds a component, and the failure mode is invisible.
        /// </summary>
        private static bool IsRenderingInfrastructure(Component c)
        {
            System.Type t = c.GetType();
            while (t != null)
            {
                if (t.Namespace != null && t.Namespace.StartsWith("UnityEngine.Rendering")) return true;
                t = t.BaseType;
            }
            return false;
        }

        /// <summary>
        /// Cinemachine survives every rule — the angles authored by AnglePlacementTool are the one
        /// piece of "logic" a demo stage is supposed to keep.
        /// </summary>
        private static bool IsCinemachine(Object o)
        {
            if (o == null) return false;
            System.Type t = o.GetType();
            while (t != null)
            {
                if (t.Namespace != null && t.Namespace.StartsWith("Unity.Cinemachine")) return true;
                t = t.BaseType;
            }
            if (o is GameObject go)
            {
                foreach (Component c in go.GetComponents<Component>())
                {
                    if (c == null) continue;
                    if (c.GetType().Namespace != null &&
                        c.GetType().Namespace.StartsWith("Unity.Cinemachine")) return true;
                }
            }
            return false;
        }

        public static string Format(StripResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {(r.DryRun ? "DRY RUN" : "STRIPPED")}  {r.SceneName} ===");
            sb.AppendLine($"  objects deleted        {r.ObjectsDeleted}");
            sb.AppendLine($"  monoBehaviours removed {r.MonoBehavioursRemoved}");
            sb.AppendLine($"  typed components       {r.TypedComponentsRemoved}");
            sb.AppendLine($"  colliders              {r.CollidersRemoved}");
            sb.AppendLine($"  rigidbodies            {r.RigidbodiesRemoved}");
            sb.AppendLine($"  cameras                {r.CamerasRemoved}");
            sb.AppendLine($"  missing scripts        {r.MissingScriptsRemoved}");
            sb.AppendLine($"  TOTAL components       {r.TotalComponentsRemoved}");
            sb.AppendLine($"  renderers gone with deleted objects  {r.RenderersRemovedWithObjects}" +
                          "   (XR controller meshes and similar — expected, not scenery)");
            sb.AppendLine($"  removal passes         {r.Passes}");

            if (r.DeletedObjectNames.Count > 0)
            {
                sb.AppendLine("  deleted objects:");
                for (int i = 0; i < r.DeletedObjectNames.Count && i < 40; i++)
                    sb.AppendLine("    - " + r.DeletedObjectNames[i]);
                if (r.DeletedObjectNames.Count > 40)
                    sb.AppendLine($"    … and {r.DeletedObjectNames.Count - 40} more");
            }

            if (r.KeptBecauseProtected.Count > 0)
            {
                sb.AppendLine("  protected by the keep-list (untouched, children included):");
                var seen = new HashSet<string>();
                foreach (string k in r.KeptBecauseProtected)
                    if (seen.Add(k)) sb.AppendLine("    ✓ " + k);
            }

            if (r.Failures > 0)
            {
                sb.AppendLine($"  {r.Failures} FAILURE(S) — usually a [RequireComponent] dependency:");
                for (int i = 0; i < r.FailureNotes.Count && i < 20; i++)
                    sb.AppendLine("    ! " + r.FailureNotes[i]);
            }

            if (r.DryRun) sb.AppendLine("  nothing was changed.");
            else sb.AppendLine("  scene is dirty and NOT saved — review it, then save deliberately.");
            return sb.ToString();
        }

        // -------------------------------------------------------------------------
        // Menu entry points
        // -------------------------------------------------------------------------

        [MenuItem("Dumyat/Presentation/Strip Active Scene (Dry Run)", priority = 20)]
        private static void DryRunActive() => Run(true);

        [MenuItem("Dumyat/Presentation/Strip Active Scene (APPLY)", priority = 21)]
        private static void ApplyActive() => Run(false);

        private static void Run(bool dryRun)
        {
            DemoStripRulesSO rules = ResolveRules();
            if (rules == null)
            {
                Debug.LogError("[DemoSceneStripper] No DemoStripRulesSO found. Create one via " +
                               "Assets ▸ Create ▸ Dumyat ▸ Presentation ▸ Demo Strip Rules, or select " +
                               "the one you want before running.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            SceneAuditReport.Audit before = SceneAuditReport.Audit_(scene);
            StripResult result = Strip(scene, rules, dryRun);
            var sb = new StringBuilder(Format(result));

            if (!dryRun)
            {
                sb.AppendLine();
                sb.AppendLine("BEFORE:");
                sb.Append(SceneAuditReport.Format(before));
                sb.AppendLine("AFTER:");
                sb.Append(SceneAuditReport.Format(SceneAuditReport.Audit_(scene)));
            }

            Debug.Log(sb.ToString(), rules);
        }

        /// <summary>Selected rules asset if there is one, otherwise the only one in the project.</summary>
        private static DemoStripRulesSO ResolveRules()
        {
            if (Selection.activeObject is DemoStripRulesSO selected) return selected;

            string[] guids = AssetDatabase.FindAssets("t:DemoStripRulesSO");
            if (guids.Length == 0) return null;
            if (guids.Length > 1)
                Debug.LogWarning($"[DemoSceneStripper] {guids.Length} rules assets exist; using the " +
                                 "first. Select the one you want to be explicit.");
            return AssetDatabase.LoadAssetAtPath<DemoStripRulesSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
#endif
