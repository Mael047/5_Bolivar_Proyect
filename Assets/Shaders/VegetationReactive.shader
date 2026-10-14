// Vegetacion reactiva (flores, plantas) para URP:
//   - Textura + color, recorte de transparencia opcional (petalos).
//   - Luz principal con sombras, luces adicionales, luz ambiental y niebla.
//   - Viento y reaccion al jugador / objetos (VegetationInteraction.hlsl):
//     se doblan alejandose y se aplastan un poco al pasar por encima.
//   - Compatible con GPU Instancing (detalles del Terrain con "Use GPU Instancing").
// 'Plant Height' debe aproximarse al alto real del modelo (la herramienta lo calcula).
Shader "Custom/VegetationReactive"
{
    Properties
    {
        [MainTexture] _BaseMap ("Textura", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Recorte de transparencia", Float) = 0
        _Cutoff ("Umbral de recorte", Range(0, 1)) = 0.5

        [Header(Planta)]
        _PlantHeight ("Altura de la planta (m)", Float) = 0.5
        _Translucency ("Luz a traves de las hojas", Range(0, 1)) = 0.35

        [Header(Reaccion al pasar)]
        _InteractionRadius ("Multiplicador de radio", Range(0.2, 3)) = 1
        _BendStrength ("Doblado", Range(0, 2)) = 0.7
        _SquashStrength ("Aplastado", Range(0, 1)) = 0.35

        [Header(Viento)]
        _WindStrength ("Fuerza", Range(0, 0.5)) = 0.08
        _WindSpeed ("Velocidad", Range(0, 6)) = 2
        _WindScale ("Variacion entre plantas", Range(0, 4)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Include/VegetationInteraction.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _Cutoff;
            float  _PlantHeight;
            half   _Translucency;
            float  _InteractionRadius;
            float  _BendStrength;
            float  _SquashStrength;
            float  _WindStrength;
            float  _WindSpeed;
            float  _WindScale;
        CBUFFER_END

        // Posicion en mundo del vertice ya doblado por viento e interaccion.
        float3 DisplacedPositionWS(float3 positionOS)
        {
            float3 pivotWS = TransformObjectToWorld(float3(0, 0, 0));
            float3 positionWS = TransformObjectToWorld(positionOS);
            float  height = max(_PlantHeight, 0.01);
            float  tip = saturate((positionWS.y - pivotWS.y) / height);

            positionWS += VegetationWind(pivotWS, tip, height, _WindStrength, _WindSpeed, _WindScale);
            positionWS += VegetationInteractionOffset(pivotWS, tip, height, _InteractionRadius, _BendStrength, _SquashStrength);
            return positionWS;
        }

        half4 SampleAlbedo(float2 uv)
        {
            half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            #if defined(_ALPHATEST_ON)
                clip(albedo.a - _Cutoff);
            #endif
            return albedo;
        }
        ENDHLSL

        // ---------- Color ----------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                half   fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = DisplacedPositionWS(input.positionOS.xyz);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half3 Shade(Light light, half3 albedo, half3 normalWS)
            {
                half ndl = dot(normalWS, light.direction);
                // Difusa envuelta + algo de luz que atraviesa las hojas por detras.
                half diffuse = saturate(ndl * 0.6h + 0.4h) + saturate(-ndl) * _Translucency;
                return albedo * light.color * (diffuse * light.distanceAttenuation * light.shadowAttenuation);
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 albedo = SampleAlbedo(i.uv);

                half3 normalWS = normalize(i.normalWS);
                normalWS = IS_FRONT_VFACE(face, normalWS, -normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 color = Shade(mainLight, albedo.rgb, normalWS);
                color += albedo.rgb * SampleSH(normalWS);

                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalWS = normalWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);

                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                        color += Shade(light, albedo.rgb, normalWS);
                    LIGHT_LOOP_END
                #endif

                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // ---------- Sombras que proyecta ----------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = DisplacedPositionWS(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                o.positionCS = positionCS;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                SampleAlbedo(i.uv);
                return 0;
            }
            ENDHLSL
        }

        // ---------- Profundidad (SSAO, efectos de profundidad) ----------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformWorldToHClip(DisplacedPositionWS(input.positionOS.xyz));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half frag(Varyings i) : SV_Target
            {
                SampleAlbedo(i.uv);
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
