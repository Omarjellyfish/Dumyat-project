#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatEditor
{
    /// <summary>
    /// Read-only audit of a single scene: how big it is in world space, what it costs, and
    /// which baked data it carries. Two jobs use this, which is why it is one shared helper:
    ///
    ///  1. Choosing the presentation world-offset grid — the spacing has to exceed the largest
    ///     scene's extent or two stages will intersect.
    ///  2. Planning the demo-scene strip — the component tallies below are the strip list.
    ///
    /// Exotic component types (Volume, ProbeVolume) are counted by type NAME rather than by a
    /// compile-time reference, so this file does not force Project.Editor to reference the
    /// render-pipeline assemblies. This is a diagnostic tally, not a behavioural decision.
    /// </summary>
    public static class SceneAuditReport
    {
        public sealed class Audit
        {
            public string ScenePath;
            public bool HasRendererBounds;
            public Bounds RendererBounds;
            public int RendererCount;
            public int StaticRendererCount;
            public int LightmappedRendererCount;
            public int TerrainCount;
            public int DirectionalLights;
            public int OtherLights;
            public int RealtimeLights;
            public int ReflectionProbes;
            public int LightProbeGroups;
            public int ProbeVolumes;
            public int Volumes;
            public int AudioSources;
            public int Canvases;
            public int UiDocuments;
            public int ParticleSystems;
            public int Colliders;
            public int MonoBehaviours;
            public bool HasLightingDataAsset;
            public string LightingDataAssetPath;
            public bool HasOcclusionData;
            public int RootCount;
            public List<string> LargestRenderers = new List<string>();

            /// <summary>Half-extent along the largest horizontal axis — the number the offset grid must clear.</summary>
            public float HorizontalRadius =>
                HasRendererBounds ? Mathf.Max(RendererBounds.extents.x, RendererBounds.extents.z) : 0f;
        }

        public static Audit Audit_(Scene scene)
        {
            var a = new Audit { ScenePath = scene.path, RootCount = scene.rootCount };

            // Lightmapping.lightingDataAsset and the occlusion data size are both properties of the
            // ACTIVE scene, not of an arbitrary loaded one — there is no per-scene public accessor.
            // Reporting them for a non-active scene would be quietly wrong, so we say so instead.
            bool isActive = SceneManager.GetActiveScene() == scene;
            if (isActive)
            {
                var lightingData = Lightmapping.lightingDataAsset;
                a.HasLightingDataAsset = lightingData != null;
                a.LightingDataAssetPath = lightingData != null
                    ? AssetDatabase.GetAssetPath(lightingData)
                    : "<none>";
                a.HasOcclusionData = StaticOcclusionCulling.umbraDataSize > 0;
            }
            else
            {
                a.LightingDataAssetPath = "<not the active scene — re-audit with it active>";
            }

            var biggest = new List<(float size, string name)>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    // Particle and trail renderers have meaningless authoring-time bounds and would
                    // inflate the scene extent, so they are tallied but never folded into bounds.
                    bool isMesh = r is MeshRenderer || r is SkinnedMeshRenderer;
                    a.RendererCount++;
                    if (r.gameObject.isStatic) a.StaticRendererCount++;
                    if (r.lightmapIndex >= 0 && r.lightmapIndex < 65534) a.LightmappedRendererCount++;

                    if (!isMesh) continue;

                    Bounds b = r.bounds;
                    if (b.size.sqrMagnitude <= 0f) continue;

                    if (!a.HasRendererBounds) { a.RendererBounds = b; a.HasRendererBounds = true; }
                    else a.RendererBounds.Encapsulate(b);

                    biggest.Add((b.size.magnitude, r.gameObject.name));
                }

                foreach (Terrain t in root.GetComponentsInChildren<Terrain>(true))
                {
                    a.TerrainCount++;
                    if (t.terrainData == null) continue;
                    var tb = new Bounds(
                        t.transform.position + t.terrainData.size * 0.5f,
                        t.terrainData.size);
                    if (!a.HasRendererBounds) { a.RendererBounds = tb; a.HasRendererBounds = true; }
                    else a.RendererBounds.Encapsulate(tb);
                }

                foreach (Light l in root.GetComponentsInChildren<Light>(true))
                {
                    if (l.type == LightType.Directional) a.DirectionalLights++; else a.OtherLights++;
                    if (l.lightmapBakeType == LightmapBakeType.Realtime) a.RealtimeLights++;
                }

                a.ReflectionProbes  += root.GetComponentsInChildren<ReflectionProbe>(true).Length;
                a.LightProbeGroups  += root.GetComponentsInChildren<LightProbeGroup>(true).Length;
                a.AudioSources      += root.GetComponentsInChildren<AudioSource>(true).Length;
                a.Canvases          += root.GetComponentsInChildren<Canvas>(true).Length;
                a.ParticleSystems   += root.GetComponentsInChildren<ParticleSystem>(true).Length;
                a.Colliders         += root.GetComponentsInChildren<Collider>(true).Length;

                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    switch (c.GetType().Name)
                    {
                        case "Volume":      a.Volumes++;      break;
                        case "ProbeVolume": a.ProbeVolumes++; break;
                        case "UIDocument":  a.UiDocuments++;  break;
                    }
                    if (c is MonoBehaviour) a.MonoBehaviours++;
                }
            }

            a.LargestRenderers = biggest
                .OrderByDescending(x => x.size)
                .Take(5)
                .Select(x => $"{x.name} ({x.size:F0} u)")
                .ToList();

            return a;
        }

        public static string Format(Audit a)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {a.ScenePath} ===");
            sb.AppendLine($"  roots {a.RootCount} | renderers {a.RendererCount} (static {a.StaticRendererCount}, lightmapped {a.LightmappedRendererCount}) | terrains {a.TerrainCount}");
            if (a.HasRendererBounds)
            {
                Bounds b = a.RendererBounds;
                sb.AppendLine($"  bounds center ({b.center.x:F0}, {b.center.y:F0}, {b.center.z:F0})  size ({b.size.x:F0} x {b.size.y:F0} x {b.size.z:F0})");
                sb.AppendLine($"  horizontal radius {a.HorizontalRadius:F0} u   -> offset grid spacing must exceed {a.HorizontalRadius * 2f:F0} u");
                sb.AppendLine($"  largest: {string.Join(", ", a.LargestRenderers)}");
            }
            else sb.AppendLine("  bounds: NO MESH RENDERERS FOUND");

            sb.AppendLine($"  lights: {a.DirectionalLights} directional, {a.OtherLights} other ({a.RealtimeLights} realtime)");
            sb.AppendLine($"  baked data: reflectionProbes {a.ReflectionProbes} | lightProbeGroups {a.LightProbeGroups} | APV probeVolumes {a.ProbeVolumes} | occlusion {(a.HasOcclusionData ? "YES" : "no")}");
            sb.AppendLine($"  lightingData: {a.LightingDataAssetPath}");
            sb.AppendLine($"  volumes {a.Volumes}");
            sb.AppendLine($"  TO STRIP -> audioSources {a.AudioSources} | canvases {a.Canvases} | uiDocuments {a.UiDocuments} | particles {a.ParticleSystems} | colliders {a.Colliders} | monoBehaviours {a.MonoBehaviours}");
            return sb.ToString();
        }

        [MenuItem("Dumyat/Presentation/Audit Open Scenes")]
        private static void AuditOpenScenes()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded) sb.AppendLine(Format(Audit_(s)));
            }
            Debug.Log(sb.ToString());
        }
    }
}
#endif
