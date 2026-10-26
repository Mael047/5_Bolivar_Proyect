// Recorte circular para la vista del minimapa (RawImage).
// Corta la imagen en circulo con borde suave, y permite retocar el color
// (saturacion, brillo) y oscurecer un poco el borde para dar profundidad.
// Compatible con Mask / RectMask2D como cualquier shader de UI.
Shader "UI/MinimapCircle"
{
    Properties
    {
        [PerRendererData] _MainTex ("Textura", 2D) = "white" {}
        _Color ("Tinte", Color) = (1, 1, 1, 1)

        [Header(Circulo)]
        _Radius ("Radio", Range(0.1, 0.5)) = 0.5
        _Softness ("Borde suave", Range(0, 0.2)) = 0.02

        [Header(Color)]
        _Saturation ("Saturacion", Range(0, 2)) = 1.15
        _Brightness ("Brillo", Range(0, 2)) = 1.05
        _EdgeShade ("Oscurecer borde", Range(0, 1)) = 0.25
        _EdgeShadeColor ("Color del borde", Color) = (0.05, 0.1, 0.2, 1)

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipRect;
            float _Radius;
            float _Softness;
            float _Saturation;
            float _Brightness;
            float _EdgeShade;
            fixed4 _EdgeShadeColor;

            v2f vert (appdata v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                c.a = 1;

                // Color
                float gray = dot(c.rgb, float3(0.299, 0.587, 0.114));
                c.rgb = lerp(gray.xxx, c.rgb, _Saturation) * _Brightness;

                // Circulo (en UV de la vista, centro 0.5)
                float d = distance(i.uv, float2(0.5, 0.5));
                float mask = 1 - smoothstep(_Radius - _Softness, _Radius, d);

                // Borde oscurecido
                float edge = smoothstep(_Radius * 0.55, _Radius, d);
                c.rgb = lerp(c.rgb, _EdgeShadeColor.rgb, edge * _EdgeShade);

                c *= i.color;
                c.a *= mask;

                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif

                return c;
            }
            ENDCG
        }
    }
}
