Shader "Custom/GrassReactive"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.32, 0.56, 0.24, 1)
        _TipColor  ("Tip Color", Color) = (0.58, 0.78, 0.35, 1)
        _Radius    ("Reaction Radius", Float) = 2.0
        _BendDistance ("Bend Distance", Float) = 0.6
        _WindStrength ("Wind Strength", Float) = 0.08
        _WindSpeed ("Wind Speed", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 color      : COLOR;
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TipColor;
                float  _Radius;
                float  _BendDistance;
                float  _WindStrength;
                float  _WindSpeed;
            CBUFFER_END

            // Globals que escribe GrassReaction via Shader.SetGlobal* (NO son
            // propiedades de material, por eso van fuera de UnityPerMaterial)
            CBUFFER_START(GrassGlobals)
                float4 _TrackerPosition;
                float  _TrackerActive;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 localPos = input.positionOS.xyz;
                float tip = saturate(input.uv.y); // 0 base -> 1 punta

                float3 worldBefore = TransformObjectToWorld(localPos);

                // Viento: oscilacion suave en XZ, mas fuerte en las puntas
                float tWind = _Time.y * _WindSpeed;
                float windX = sin(tWind + worldBefore.z * 3.0) * _WindStrength * tip;
                float windZ = cos(tWind * 0.8 + worldBefore.x * 3.0) * _WindStrength * tip;

                // Doblado lateral apartandose del agente mas cercano
                float3 toTracker = worldBefore - _TrackerPosition.xyz;
                float distXZ = length(toTracker.xz);
                float fall = _TrackerActive * (1.0 - smoothstep(0.0, _Radius, distXZ));
                float3 away = (distXZ > 1e-4) ? float3(toTracker.x, 0.0, toTracker.z) / distXZ : float3(0.0, 0.0, 0.0);
                float3 horizontal = away * (fall * _BendDistance * tip);

                float3 worldPos = worldBefore + horizontal + float3(windX, 0.0, windZ);

                o.positionCS = TransformWorldToHClip(worldPos);
                o.positionWS = worldPos;
                o.normalWS   = normalize(TransformObjectToWorldNormal(input.normalOS));
                o.uv         = input.uv;
                o.color      = input.color;
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 col = lerp(_BaseColor.rgb, _TipColor.rgb, saturate(i.uv.y));
                col *= i.color.rgb;

                Light main = GetMainLight();
                float nl = saturate(dot(normalize(i.normalWS), main.direction));
                col *= (0.55 + 0.45 * nl) * main.shadowAttenuation;
                col += SampleSH(normalize(i.normalWS)) * 0.35;

                half fogFactor = i.fogFactor;
                col = MixFog(col, fogFactor);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}