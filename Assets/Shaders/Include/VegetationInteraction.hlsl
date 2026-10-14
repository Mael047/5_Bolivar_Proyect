// VegetationInteraction.hlsl
// Funciones comunes de la vegetacion reactiva (pasto, flores...).
// Las posiciones de quien aparta la vegetacion (jugador, objeto sostenido con
// la mano...) las escribe VegetationInteractor.cs como variables globales.

#ifndef VEGETATION_INTERACTION_INCLUDED
#define VEGETATION_INTERACTION_INCLUDED

#define VEG_MAX_INTERACTORS 8

// xyz = posicion en mundo, w = radio de influencia (m). Globales: NO van en
// el CBUFFER del material (asi el SRP Batcher sigue funcionando).
float4 _VegInteractors[VEG_MAX_INTERACTORS];
float  _VegInteractorCount;

// Desplazamiento de un vertice por los interactores.
//   pivotWS : base de la planta (origen del objeto) en mundo
//   tip     : 0 en la base, 1 en la punta
//   height  : alto de la planta en metros
// La planta entera se inclina alejandose del interactor (curvada: la punta se
// mueve mas que la base) y se aplasta un poco hacia abajo.
float3 VegetationInteractionOffset(float3 pivotWS, float tip, float height,
                                   float radiusScale, float bend, float squash)
{
    float2 push = 0;
    float  press = 0;
    int count = (int)_VegInteractorCount;

    [unroll]
    for (int i = 0; i < VEG_MAX_INTERACTORS; i++)
    {
        if (i < count)
        {
            float4 it = _VegInteractors[i];
            float  r = max(it.w * radiusScale, 1e-3);
            float2 d = pivotWS.xz - it.xz;
            float  dist = length(d);

            // Solo si el interactor esta a la altura de la planta.
            float vertical = saturate(1.0 - abs(pivotWS.y - it.y) / (r + height + 1.0));
            float f = (1.0 - smoothstep(0.0, r, dist)) * vertical;

            float2 dir = dist > 1e-4 ? d / dist : float2(0.0, 1.0);
            push += dir * f;
            press = max(press, f);
        }
    }

    float len = length(push);
    if (len > 1.0) push /= len;

    float curve = tip * tip;
    float3 offset;
    offset.xz = push * (bend * height * curve);
    offset.y  = -press * squash * height * tip;
    return offset;
}

// Viento suave: oscilacion en XZ que depende de la posicion (no todas iguales).
float3 VegetationWind(float3 pivotWS, float tip, float height, float strength, float speed, float scale)
{
    float t = _Time.y * speed;
    float phase = dot(pivotWS.xz, float2(1.0, 0.7)) * scale;
    float gust = 0.6 + 0.4 * sin(t * 0.37 + phase * 0.5);
    float wx = sin(t + phase) * gust;
    float wz = cos(t * 0.83 + phase * 1.3) * gust * 0.6;
    return float3(wx, 0.0, wz) * (strength * height * tip * tip);
}

#endif
