// ZonaCorrupta.shader (URP)
// Va en un cubo (CorruptionZone). Todo lo que esta DENTRO del cubo se ve
// distorsionado y morado; lo de fuera no. La nutria y los enemigos no se
// afectan (se vuelven a dibujar encima con el Renderer Feature que prepara
// Tools > Zona corrupta > Preparar proyecto).
//
// Como funciona: dibuja las caras traseras del cubo sobre toda la pantalla
// que ocupa, lee la profundidad de cada pixel para saber su posicion en el
// mundo y, si esa posicion cae dentro del cubo, mezcla el color de la escena
// (Opaque Texture) distorsionado y teñido.
// Necesita en el URP Asset: Depth Texture y Opaque Texture activados.

Shader "ReQuinta/ZonaCorrupta"
{
  Properties
  {
    [Header(Color)]
    _Tint ("Color de la corrupcion", Color) = (0.55, 0.18, 0.85, 1)
    _Strength ("Fuerza del morado", Range(0, 1)) = 0.75
    _Darken ("Oscurecer", Range(0, 1)) = 0.25
    _Saturation ("Color original que queda", Range(0, 1)) = 0.2

    [Header(Distorsion)]
    _Distortion ("Distorsion", Range(0, 0.05)) = 0.012
    _DistortionScale ("Tamano de las ondas", Range(0.1, 10)) = 1.5
    _Speed ("Velocidad", Range(0, 3)) = 0.6
    _Chroma ("Separacion de color", Range(0, 1)) = 0.5

    [Header(Venas)]
    [HDR] _VeinColor ("Color de las venas", Color) = (1.2, 0.35, 1.6, 1)
    _Veins ("Venas", Range(0, 2)) = 0.6
    _VeinScale ("Tamano de las venas", Range(0.05, 5)) = 0.6
    _VeinWidth ("Grosor de las venas", Range(0.005, 0.1)) = 0.025

    [Header(Bordes)]
    _EdgeSoftness ("Borde suave (metros)", Range(0.01, 5)) = 1
    _Intensity ("Intensidad", Range(0, 1)) = 1
  }

  SubShader
  {
    Tags
    {
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "Transparent-499"
      "RenderType" = "Transparent"
      "IgnoreProjector" = "True"
    }

    Pass
    {
      Name "ZonaCorrupta"
      Tags { "LightMode" = "UniversalForward" }

      Cull Front
      ZWrite Off
      ZTest Always
      Blend Off

      HLSLPROGRAM
      #pragma vertex Vert
      #pragma fragment Frag

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

      CBUFFER_START(UnityPerMaterial)
        half4 _Tint;
        half _Strength;
        half _Darken;
        half _Saturation;
        float _Distortion;
        float _DistortionScale;
        float _Speed;
        half _Chroma;
        half4 _VeinColor;
        half _Veins;
        float _VeinScale;
        float _VeinWidth;
        float _EdgeSoftness;
        half _Intensity;
      CBUFFER_END

      struct Attributes
      {
        float4 positionOS : POSITION;
        UNITY_VERTEX_INPUT_INSTANCE_ID
      };

      struct Varyings
      {
        float4 positionCS : SV_POSITION;
        float4 screenPos : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
      };

      Varyings Vert(Attributes input)
      {
        Varyings o;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        o.screenPos = ComputeScreenPos(o.positionCS);
        return o;
      }

      // Ruido suave 3D (sin texturas).
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
        return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                         lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                         lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
      }

      half4 Frag(Varyings input) : SV_Target
      {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.screenPos.xy / input.screenPos.w;

        // Posicion en el mundo de lo que se ve en este pixel.
        float depth = SampleSceneDepth(uv);
        #if !UNITY_REVERSED_Z
          depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
        #endif
        float3 posWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

        // Dentro del cubo? (el cubo de Unity va de -0.5 a 0.5)
        float3 posOS = mul(GetWorldToObjectMatrix(), float4(posWS, 1.0)).xyz;
        float3 inside = 0.5 - abs(posOS);
        clip(min(inside.x, min(inside.y, inside.z)));

        // Borde suave en metros (independiente del tamano del cubo).
        float4x4 m = GetObjectToWorldMatrix();
        float3 scale = float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22));
        float3 edgeMeters = inside * scale;
        float edge = min(edgeMeters.x, min(edgeMeters.y, edgeMeters.z));
        float fade = saturate(edge / _EdgeSoftness) * _Intensity;

        // Distorsion ondulante
        float t = _Time.y * _Speed;
        float3 p = posWS * _DistortionScale;
        float2 wobble = float2(Noise(p + float3(t, 0, 0)), Noise(p + float3(17.3, -t, 5.1))) - 0.5;
        float2 duv = uv + wobble * (_Distortion * 2.0 * fade);

        half3 col = SampleSceneColor(duv);
        half shiftR = SampleSceneColor(duv + float2(_Distortion * _Chroma * fade, 0)).r;
        half shiftB = SampleSceneColor(duv - float2(_Distortion * _Chroma * fade, 0)).b;
        col.r = lerp(col.r, shiftR, 0.7);
        col.b = lerp(col.b, shiftB, 0.7);

        // Morado: luminancia teñida + un poco del color original.
        half lum = dot(col, half3(0.299, 0.587, 0.114));
        half3 tinted = lerp(lum.xxx, col, _Saturation) * _Tint.rgb * 1.6;
        half3 corrupted = lerp(col, tinted, _Strength) * (1.0 - _Darken);

        // Venas brillantes que se mueven por el suelo
        float v = Noise(posWS * _VeinScale + float3(0, t * 0.4, t * 0.2));
        float vein = 1.0 - smoothstep(0.0, _VeinWidth, abs(v - 0.5));
        corrupted += _VeinColor.rgb * vein * _Veins * (0.65 + 0.35 * sin(t * 3.0));

        half3 original = SampleSceneColor(uv);
        return half4(lerp(original, corrupted, fade), 1.0);
      }
      ENDHLSL
    }
  }

  FallBack Off
}
