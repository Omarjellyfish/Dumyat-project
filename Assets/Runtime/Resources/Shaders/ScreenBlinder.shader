// Blinder shell for scene-load fades. Drawn on the inside of a small sphere that is hard-locked
// to the head, so it occludes the world without a screen-space canvas — those do not composite
// into the HMD eye textures at all.
//
// Lives under Resources/ deliberately: ScreenBlinder loads it with Resources.Load at runtime and
// nothing references it from a scene, so anywhere else it would be stripped from a player build.
Shader "Dumyat/ScreenBlinder"
{
    Properties
    {
        _BlinderColor ("Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            // Transparent-100. After every opaque object and the skybox, but before the standard
            // transparent queue — which is where the world-space loading panel draws, so the panel
            // still lands on top of the black.
            "Queue" = "Transparent-100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Blinder"
            Tags { "LightMode" = "UniversalForward" }

            // Seen from inside, so front faces are culled and exactly one layer of the interior
            // shades. Cull Off would composite the near and far walls and double the alpha.
            Cull Front

            // Depth-independent on purpose: a workbench 40 cm from the player's face must not poke
            // through the blinder. Nothing is written back, so later queues still draw over it.
            ZTest Always
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BlinderColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                // Required for single-pass instanced stereo — without these the blinder renders
                // to one eye only.
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return _BlinderColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
