// MeshUvSampler.cs
// Muestrea las UV de una malla en el punto de su superficie mas cercano a una
// posicion dada. Lo usa Breakable para que los pedazos procedurales tomen la
// region correcta de un atlas de textura compartido por varios modelos, en vez
// de repetir el atlas entero en cada cara (que es lo que hace un cubo primitivo
// de Unity con sus UV 0..1).
//
// Trabaja en espacio local de la malla y cachea vertices/UV/triangulos, asi que
// una instancia se crea una sola vez por rotura y se reutiliza para todos los
// vertices de todos los fragmentos.

using UnityEngine;

namespace NuiGrab
{
  public sealed class MeshUvSampler
  {
    private readonly Vector3[] _vertices;
    private readonly Vector2[] _uv;
    private readonly int[] _triangles;

    public bool IsValid { get; }

    public MeshUvSampler(Mesh mesh)
    {
      if (mesh == null || !mesh.isReadable)
      {
        IsValid = false;
        return;
      }

      _vertices = mesh.vertices;
      _uv = mesh.uv;
      _triangles = mesh.triangles;

      IsValid = _vertices != null && _vertices.Length > 0
          && _uv != null && _uv.Length == _vertices.Length
          && _triangles != null && _triangles.Length >= 3;
    }

    /// <summary>
    /// UV del punto de la superficie mas cercano a <paramref name="localPoint"/>
    /// (coordenadas locales de la malla).
    /// </summary>
    public Vector2 SampleUv(Vector3 localPoint)
    {
      if (!IsValid)
      {
        return Vector2.zero;
      }

      var bestDistance = float.MaxValue;
      var bestUv = _uv[0];

      for (int t = 0; t < _triangles.Length; t += 3)
      {
        var i0 = _triangles[t];
        var i1 = _triangles[t + 1];
        var i2 = _triangles[t + 2];

        var a = _vertices[i0];
        var b = _vertices[i1];
        var c = _vertices[i2];

        var closest = ClosestPointOnTriangle(localPoint, a, b, c, out var bary);
        var distance = (closest - localPoint).sqrMagnitude;

        if (distance < bestDistance)
        {
          bestDistance = distance;
          bestUv = _uv[i0] * bary.x + _uv[i1] * bary.y + _uv[i2] * bary.z;
        }
      }

      return bestUv;
    }

    /// <summary>
    /// Punto del triangulo abc mas cercano a p, con sus coordenadas
    /// baricentricas (Ericson, Real-Time Collision Detection).
    /// </summary>
    private static Vector3 ClosestPointOnTriangle(
        Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
    {
      var ab = b - a;
      var ac = c - a;
      var ap = p - a;

      var d1 = Vector3.Dot(ab, ap);
      var d2 = Vector3.Dot(ac, ap);

      if (d1 <= 0f && d2 <= 0f)
      {
        bary = new Vector3(1f, 0f, 0f);
        return a;
      }

      var bp = p - b;
      var d3 = Vector3.Dot(ab, bp);
      var d4 = Vector3.Dot(ac, bp);

      if (d3 >= 0f && d4 <= d3)
      {
        bary = new Vector3(0f, 1f, 0f);
        return b;
      }

      var vc = d1 * d4 - d3 * d2;
      if (vc <= 0f && d1 >= 0f && d3 <= 0f)
      {
        var t = d1 / (d1 - d3);
        bary = new Vector3(1f - t, t, 0f);
        return a + ab * t;
      }

      var cp = p - c;
      var d5 = Vector3.Dot(ab, cp);
      var d6 = Vector3.Dot(ac, cp);

      if (d6 >= 0f && d5 <= d6)
      {
        bary = new Vector3(0f, 0f, 1f);
        return c;
      }

      var vb = d5 * d2 - d1 * d6;
      if (vb <= 0f && d2 >= 0f && d6 <= 0f)
      {
        var t = d2 / (d2 - d6);
        bary = new Vector3(1f - t, 0f, t);
        return a + ac * t;
      }

      var va = d3 * d6 - d5 * d4;
      if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
      {
        var t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
        bary = new Vector3(0f, 1f - t, t);
        return b + (c - b) * t;
      }

      var denom = 1f / (va + vb + vc);
      var v = vb * denom;
      var w = vc * denom;

      bary = new Vector3(1f - v - w, v, w);
      return a + ab * v + ac * w;
    }
  }
}
