// Stamp shader for the pen / paint system.
//
// Deliberately a plain built-in-pipeline shader rather than a URP one: brush marks are
// drawn with immediate-mode Graphics.DrawTexture straight into the wood's paint
// RenderTexture, outside of any SRP render pass. URP shaders read per-frame constants
// that the SRP sets up during its own passes, so driving one this way produces
// undefined output - in a headset (stereo rendering) that showed up as wrong-coloured
// marks. This shader depends on nothing but the MVP that DrawTexture sets.
//
// The wood surface shader composites as lerp(woodColour, paintColour, paintAlpha), so
// RGB carries the pen colour and A is the coverage mask. The alpha channel therefore
// needs its own blend factors (One, OneMinusSrcAlpha) - with the colour factors the
// mask would square itself on every stamp and never reach full opacity.
Shader "Hidden/Dumyat/PaintStamp"
{
    Properties
    {
        _MainTex ("Brush", 2D) = "white" {}
        _Color ("Tint", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "PreviewType" = "Plane" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off
            Lighting Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 brush = tex2D(_MainTex, i.uv);
                return fixed4(_Color.rgb, brush.a * _Color.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
