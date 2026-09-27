// FragmentMeshFactory.cs
// Construye la malla de un pedazo procedural: un cubo cuyas UV se muestrean de
// la malla original, de modo que el pedazo pinta la misma zona del atlas que la
// superficie de la que salio. Un cubo primitivo de Unity trae UV 0..1 por cara,
// que con un atlas compartido muestra texturas de otros modelos.

using UnityEngine;

namespace NuiGrab
{
  public static class FragmentMeshFactory
  {
    // Las 6 caras del cubo, cada una con sus 4 vertices propios (normales duras).
    private static readonly Vector3[] FaceNormals =
    {
      Vector3.forward, Vector3.back, Vector3.up,
      Vector3.down, Vector3.right, Vector3.left
    };

    /// <summary>
    /// Cubo centrado en el origen, de tamano <paramref name="size"/> en espacio
    /// local del fragmento. Las UV salen de <paramref name="sampler"/>: cada
    /// vertice se lleva al espacio local de la malla original y se muestrea la
    /// superficie mas cercana. <paramref name="uvBlend"/> mezcla entre color
    /// plano (0: toda la cara usa la UV del centro del pedazo, sin riesgo de
    /// sangrado del atlas) y UV completas de la superficie (1).
    /// </summary>
    public static Mesh CreateFragmentCube(
        Vector3 size,
        Transform fragmentTransform,
        Transform sourceTransform,
        MeshUvSampler sampler,
        float uvBlend)
    {
      var half = size * 0.5f;
      var vertices = new Vector3[24];
      var normals = new Vector3[24];
      var uv = new Vector2[24];
      var triangles = new int[36];

      var useSampler = sampler != null && sampler.IsValid
          && fragmentTransform != null && sourceTransform != null;

      var centerUv = Vector2.zero;
      if (useSampler)
      {
        var worldCenter = fragmentTransform.position;
        centerUv = sampler.SampleUv(sourceTransform.InverseTransformPoint(worldCenter));
      }

      var blend = Mathf.Clamp01(uvBlend);
      var v = 0;
      var t = 0;

      for (int f = 0; f < FaceNormals.Length; f++)
      {
        var normal = FaceNormals[f];
        var tangent = Vector3.Cross(normal, PickAxis(normal)).normalized;
        var bitangent = Vector3.Cross(normal, tangent).normalized;

        var faceCenter = Vector3.Scale(normal, half);
        var du = Vector3.Scale(tangent, half);
        var dv = Vector3.Scale(bitangent, half);

        var corners = new[]
        {
          faceCenter - du - dv,
          faceCenter + du - dv,
          faceCenter + du + dv,
          faceCenter - du + dv
        };

        for (int c = 0; c < 4; c++)
        {
          vertices[v + c] = corners[c];
          normals[v + c] = normal;

          if (useSampler)
          {
            var world = fragmentTransform.TransformPoint(corners[c]);
            var sampled = sampler.SampleUv(sourceTransform.InverseTransformPoint(world));
            uv[v + c] = Vector2.Lerp(centerUv, sampled, blend);
          }
          else
          {
            uv[v + c] = centerUv;
          }
        }

        triangles[t + 0] = v + 0;
        triangles[t + 1] = v + 1;
        triangles[t + 2] = v + 2;
        triangles[t + 3] = v + 0;
        triangles[t + 4] = v + 2;
        triangles[t + 5] = v + 3;

        v += 4;
        t += 6;
      }

      var mesh = new Mesh { name = "FragmentCube" };
      mesh.vertices = vertices;
      mesh.normals = normals;
      mesh.uv = uv;
      mesh.triangles = triangles;
      mesh.RecalculateBounds();
      mesh.RecalculateTangents();

      return mesh;
    }

    private static Vector3 PickAxis(Vector3 normal)
    {
      return Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up;
    }
  }
}
