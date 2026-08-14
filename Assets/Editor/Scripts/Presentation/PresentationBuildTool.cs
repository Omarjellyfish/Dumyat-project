#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DumyatEditor
{
    /// <summary>
    /// Builds the presentation as a standalone Windows player.
    ///
    /// WHY THIS RATHER THAN A BUILD PROFILE OR THE SHARED SCENE LIST:
    /// <see cref="BuildPipeline.BuildPlayer(BuildPlayerOptions)"/> takes the scene list as an argument, so
    /// the presentation build never touches <c>EditorBuildSettings</c> — the list the GAME builds from.
    /// Registering six demo scenes there would bloat every game build and shift its scene indices;
    /// passing them here costs nothing and cannot affect the game at all.
    ///
    /// Player settings (resolution, fullscreen) ARE global, so those are applied before the build and
    /// restored afterwards. That is stated in the log rather than done quietly.
    /// </summary>
    public static class PresentationBuildTool
    {
        private const string Root = "Assets/Runtime/Presentation";
        private const string OutputDir = "Builds/Presentation-Win64";
        private const string ExeName = "Dumyat Presentation.exe";

        /// <summary>Presentation.unity MUST be first — index 0 is what the player boots into.</summary>
        private static readonly string[] Scenes =
        {
            Root + "/Scenes/Presentation.unity",
            Root + "/Scenes/Demo/Home_Demo.unity",
            Root + "/Scenes/Demo/CarpentryQuarter_Demo.unity",
            Root + "/Scenes/Demo/RasElBar_Demo.unity",
            Root + "/Scenes/Demo/Shipyard_Demo.unity",
            Root + "/Scenes/Demo/EzbetElBorg_Demo.unity",
            Root + "/Scenes/Demo/Turntable_Demo.unity",
        };

        [MenuItem("Dumyat/Presentation/Build/Verify Build Readiness", priority = 80)]
        public static void Verify() => Debug.Log(BuildReadinessReport());

        public static string BuildReadinessReport()
        {
            var sb = new StringBuilder("=== presentation build readiness ===\n");
            int problems = 0;

            foreach (string p in Scenes)
            {
                bool ok = AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null;
                sb.AppendLine((ok ? "  ok    " : "  MISS  ") + p);
                if (!ok) problems++;
            }

            sb.AppendLine();
            sb.AppendLine("  scripting backend : " +
                PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone) +
                "  (Mono builds faster and avoids the IL2CPP stripping risk to reflection-loaded SOs)");
            sb.AppendLine("  fullscreen mode   : " + PlayerSettings.fullScreenMode);
            sb.AppendLine("  default resolution: " + PlayerSettings.defaultScreenWidth + " x " +
                PlayerSettings.defaultScreenHeight);
            sb.AppendLine("  graphics APIs     : " + string.Join(", ", System.Array.ConvertAll(
                PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64), a => a.ToString())));
            sb.AppendLine("  colour space      : " + PlayerSettings.colorSpace);

            // The deck opts out of XR at runtime rather than by unchecking OpenXR, because the GAME
            // needs OpenXR on this build target. Confirm the component is actually in the scene.
            bool optOut = SceneHasComponent(Root + "/Scenes/Presentation.unity", "HeadsetOptOut");
            sb.AppendLine("  HeadsetOptOut in Presentation.unity : " + (optOut ? "yes" : "NO — XR will initialise"));
            if (!optOut) problems++;

            sb.AppendLine();
            sb.AppendLine(problems == 0
                ? "  READY — nothing blocking a build."
                : "  " + problems + " problem(s) above must be fixed first.");
            return sb.ToString();
        }

        /// <summary>
        /// Text search of the scene file rather than opening it: this runs from a menu item and must not
        /// discard whatever the user currently has open and unsaved.
        /// </summary>
        private static bool SceneHasComponent(string scenePath, string componentTypeName)
        {
            var script = FindScript(componentTypeName);
            if (script == null) return false;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
            if (string.IsNullOrEmpty(guid) || !System.IO.File.Exists(scenePath)) return false;
            return System.IO.File.ReadAllText(scenePath).Contains(guid);
        }

        private static MonoScript FindScript(string typeName)
        {
            foreach (string g in AssetDatabase.FindAssets(typeName + " t:MonoScript"))
            {
                var s = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g));
                if (s != null && s.name == typeName) return s;
            }
            return null;
        }

        [MenuItem("Dumyat/Presentation/Build/Build Presentation (Win64)", priority = 81)]
        public static void Build()
        {
            string report = BuildReadinessReport();
            Debug.Log(report);
            if (report.Contains("must be fixed first"))
            {
                Debug.LogError("[PresentationBuild] not ready — see the report above.");
                return;
            }

            // Global player settings, changed for the build and put back afterwards.
            FullScreenMode prevMode = PlayerSettings.fullScreenMode;
            int prevW = PlayerSettings.defaultScreenWidth;
            int prevH = PlayerSettings.defaultScreenHeight;
            bool prevResizable = PlayerSettings.resizableWindow;
            string prevProduct = PlayerSettings.productName;
            bool prevAutoApi = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64);
            UnityEngine.Rendering.GraphicsDeviceType[] prevApis =
                PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);

            try
            {
                PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
                PlayerSettings.defaultScreenWidth = 1920;
                PlayerSettings.defaultScreenHeight = 1080;
                PlayerSettings.resizableWindow = false;
                PlayerSettings.productName = "Dumyat Presentation";

                // ONE graphics API, and it roughly halves the build.
                //
                // The project ships D3D12 and D3D11 with auto-API on, so every shader variant compiles
                // TWICE — once per backend. The first presentation build spent most of its time on
                // 10240 variants and then began the whole set again for the second API. A deck that runs
                // on one known machine needs one backend, and D3D11 is the safer choice on an unknown
                // venue machine anyway.
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                    new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });

                System.IO.Directory.CreateDirectory(OutputDir);

                var options = new BuildPlayerOptions
                {
                    scenes = Scenes,
                    locationPathName = System.IO.Path.Combine(OutputDir, ExeName),
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                };

                Debug.Log($"[PresentationBuild] building {Scenes.Length} scenes to {options.locationPathName} …");
                BuildReport buildReport = BuildPipeline.BuildPlayer(options);
                BuildSummary s = buildReport.summary;

                Debug.Log($"[PresentationBuild] {s.result} — {s.totalSize / 1048576} MB, " +
                          $"{s.totalTime.TotalSeconds:F0}s, {s.totalErrors} error(s), {s.totalWarnings} warning(s)");

                if (s.result != BuildResult.Succeeded)
                    Debug.LogError("[PresentationBuild] build did not succeed — see the Console.");
            }
            finally
            {
                PlayerSettings.fullScreenMode = prevMode;
                PlayerSettings.defaultScreenWidth = prevW;
                PlayerSettings.defaultScreenHeight = prevH;
                PlayerSettings.resizableWindow = prevResizable;
                PlayerSettings.productName = prevProduct;
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, prevApis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, prevAutoApi);
                AssetDatabase.SaveAssets();
                Debug.Log("[PresentationBuild] player settings restored to their pre-build values " +
                          "(resolution, fullscreen, product name, graphics APIs).");
            }
        }

        [MenuItem("Dumyat/Presentation/Build/Reveal Build Folder", priority = 82)]
        private static void Reveal()
        {
            string full = System.IO.Path.GetFullPath(OutputDir);
            if (System.IO.Directory.Exists(full)) EditorUtility.RevealInFinder(full);
            else Debug.Log("[PresentationBuild] nothing built yet at " + full);
        }
    }
}
#endif
