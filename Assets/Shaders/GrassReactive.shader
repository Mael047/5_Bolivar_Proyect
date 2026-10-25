// Pasto reactivo (hojas de pasto de malla) para URP.
// Color en degradado de la base a la punta (segun UV.y), viento y reaccion al
// jugador / objetos con las mismas funciones que las flores
// (VegetationInteraction.hlsl). Sin texturas: pensado para hojas simples.
Shader "Custom/GrassReactive"
{
    Properties
    {
        _BaseColor ("Color de la base", Color) = (0.32, 0.56, 0.24, 1)
        _TipColor  ("Color de la punta", Color) = (0.58, 0.78, 0.35, 1)

        [Header(Reaccion al pasar)]
        _PlantHeight ("Altura del pasto (m)", Float) = 0.4
        _InteractionRadius ("Multiplicador de radio", Range(0.2, 3)) = 1.2
        _BendStrength ("Doblado", Range(0, 2)) = 1
        _SquashStrength ("Aplastado", Range(0, 1)) = 0.4

        [Header(Viento)]
        _WindStrength ("Fuerza", Range(0, 0.5)) = 0.15
        _WindSpeed ("Velocidad", Range(0, 6)) = 2.5
        _WindScale ("Variacion", Range(0, 4)) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Include/VegetationInteraction.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TipColor;
                float _PlantHeight;
                float _InteractionRadius;
                float _BendStrength;
                float _SquashStrength;
                float _WindStrength;
                float _WindSpeed;
                float _WindScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  color      : COLOR;
                half   fogFactor  : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 pivotWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float  height = max(_PlantHeight, 0.01);
                float  tip = saturate(input.uv.y); // 0 base -> 1 punta

                positionWS += VegetationWind(pivotWS, tip, height, _WindStrength, _WindSpeed, _WindScale);
                positionWS += VegetationInteractionOffset(pivotWS, tip, height, _InteractionRadius, _BendStrength, _SquashStrength);

                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.color = input.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 normalWS = normalize(i.normalWS);
                normalWS = IS_FRONT_VFACE(face, normalWS, -normalWS);

                half3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, saturate(i.uv.y)) * i.color.rgb;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse = saturate(dot(normalWS, mainLight.direction) * 0.5h + 0.5h);
                half3 color = albedo * mainLight.color * (diffuse * mainLight.shadowAttenuation);
                color += albedo * SampleSH(normalWS) * 0.6h;

                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
