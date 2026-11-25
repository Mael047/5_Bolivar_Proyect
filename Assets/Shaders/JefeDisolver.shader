// JefeDisolver.shader (URP)
// Desvanecido "de polvo" para el jefe al morir: la malla se va deshaciendo
// en manchas desde arriba hacia abajo, con un borde brillante. Lo usa
// BossFight al final de la Derrota (copia la textura y el color de cada
// material y anima _Progress de 0 a 1).

Shader "ReQuinta/JefeDisolver"
{
  Properties
  {
    _BaseMap ("Textura", 2D) = "white" {}
    _BaseColor ("Color", Color) = (1, 1, 1, 1)
    _Progress ("Progreso", Range(0, 1)) = 0
    _NoiseScale ("Tamano de las manchas", Range(1, 60)) = 18
    _EdgeWidth ("Grosor del borde", Range(0.005, 0.3)) = 0.07
    [HDR] _EdgeColor ("Color del borde", Color) = (2.2, 1.1, 3.0, 1)
    _HeightMin ("Altura minima (la pone el script)", Float) = 0
    _HeightMax ("Altura maxima (la pone el script)", Float) = 2
  }

  SubShader
  {
    Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

    Pass
    {
      Name "Disolver"
      Tags { "LightMode" = "UniversalForward" }
      Cull Off
      ZWrite On

      HLSLPROGRAM
      #pragma vertex Vert
      #pragma fragment Frag
      #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

      TEXTURE2D(_BaseMap);
      SAMPLER(sampler_BaseMap);

      CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _Progress;
        float _NoiseScale;
        half _EdgeWidth;
        half4 _EdgeColor;
        float _HeightMin;
        float _HeightMax;
      CBUFFER_END

      struct Attributes
      {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 uv : TEXCOORD0;
      };

      struct Varyings
      {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 positionWS : TEXCOORD1;
        float3 normalWS : TEXCOORD2;
        float3 positionOS : TEXCOORD3;
      };

      Varyings Vert(Attributes input)
      {
        Varyings o;
        VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
        o.positionCS = p.positionCS;
        o.positionWS = p.positionWS;
        o.normalWS = TransformObjectToWorldNormal(input.normalOS);
        o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
        o.positionOS = input.positionOS.xyz;
        return o;
      }

      float Hash(float3 p)
      {
        p = frac(p * 0.3183099 + 0.1);
        p *= 17.0;
        return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
      }

      float Noise(float3 x)
      {
        float3 i = floor(x);
        float3 f = frac(x);
        f = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                         lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                         lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
      }

      half4 Frag(Varyings input) : SV_Target
      {
        // Manchas (dos escalas) + altura: lo de arriba se va primero.
        float3 q = input.positionWS * _NoiseScale;
        float n = Noise(q) * 0.65 + Noise(q * 2.7) * 0.35;
        float h = saturate((input.positionWS.y - _HeightMin) / max(0.001, _HeightMax - _HeightMin));
        float v = n * 0.55 + (1.0 - h) * 0.45;

        float progress = _Progress * 1.1;
        clip(v - progress);

        half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

        float3 normal = normalize(input.normalWS);
        Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
        half3 diffuse = saturate(dot(normal, mainLight.direction)) * mainLight.color * mainLight.shadowAttenuation;
        half3 ambient = SampleSH(normal);
        half3 color = tex.rgb * (diffuse + ambient);

        // Borde brillante donde se deshace
        half edge = 1.0 - saturate((v - progress) / _EdgeWidth);
        edge *= step(0.001, _Progress);
        color = lerp(color, _EdgeColor.rgb, edge);

        return half4(color, 1.0);
      }
      ENDHLSL
    }
  }

  FallBack Off
}
