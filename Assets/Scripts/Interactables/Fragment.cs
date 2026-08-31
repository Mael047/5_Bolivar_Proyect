// Fragment.cs
// Pedazo resultante de un objeto rompible (Breakable). Vuela con fisica
// dinamica y se autodestruye tras su tiempo de vida: primero flota/rueda unos
// segundos y luego encoge hasta desaparecer, evitando saturar la escena.

using System.Collections;
using UnityEngine;

namespace NuiGrab
{
  [RequireComponent(typeof(Rigidbody))]
  public class Fragment : MonoBehaviour
  {
    [SerializeField, Min(0.05f)] private float _shrinkDuration = 0.35f;

    private float _lifetime;
    private Vector3 _baseScale;

    /// <summary>Inicializa el pedazo tras ser instanciado (llamado por Breakable).</summary>
    public void Init(float lifetime)
    {
      _lifetime = Mathf.Max(lifetime, 0.05f);
      _baseScale = transform.localScale;
      StartCoroutine(LifetimeRoutine());
    }

    private IEnumerator LifetimeRoutine()
    {
      yield return new WaitForSeconds(_lifetime);

      var t = 0f;
      var duration = Mathf.Max(_shrinkDuration, 0.01f);

      while (t < 1f)
      {
        t += Time.deltaTime / duration;
        transform.localScale = _baseScale * Mathf.Clamp01(1f - t);
        yield return null;
      }

      Destroy(gameObject);
    }
  }
}
