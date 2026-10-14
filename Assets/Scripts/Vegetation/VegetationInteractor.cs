// VegetationInteractor.cs
// Hace que la vegetacion reactiva (shaders Custom/VegetationReactive y
// Custom/GrassReactive) se aparte de este objeto: las flores y el pasto se
// doblan alejandose y se aplastan un poco al pasar por encima.
//
// Ponlo en el Player y, con 'Source = Objeto Sostenido', en el HandSystem para
// que el objeto agarrado con la mano tambien aparte las plantas.
// Todos los interactores activos se envian juntos al shader (maximo 8).

using System.Collections.Generic;
using NuiGrab;
using UnityEngine;

[DefaultExecutionOrder(500)]
public class VegetationInteractor : MonoBehaviour
{
  public enum Source
  {
    [Tooltip("La posicion de este objeto (jugador, enemigo...).")]
    EsteObjeto,
    [Tooltip("El objeto que la mano sostiene con telequinesis (si hay uno).")]
    ObjetoSostenido
  }

  private const int MaxInteractors = 8;
  private static readonly int InteractorsId = Shader.PropertyToID("_VegInteractors");
  private static readonly int CountId = Shader.PropertyToID("_VegInteractorCount");
  private static readonly List<VegetationInteractor> s_all = new List<VegetationInteractor>();
  private static readonly Vector4[] s_data = new Vector4[MaxInteractors];
  private static int s_uploadedFrame = -1;

  [SerializeField] private Source _source = Source.EsteObjeto;

  [SerializeField, Min(0.05f), Tooltip("Radio (m) en el que aparta la vegetacion.")]
  private float _radius = 0.8f;

  [SerializeField, Tooltip("Desplazamiento de la posicion (p. ej. para usar los pies en vez del centro).")]
  private Vector3 _offset = Vector3.zero;

  private HandGrabManager _grab;

  private void OnEnable()
  {
    if (!s_all.Contains(this)) s_all.Add(this);
  }

  private void OnDisable()
  {
    s_all.Remove(this);

    if (s_all.Count == 0)
    {
      Shader.SetGlobalFloat(CountId, 0f);
    }
  }

  private bool TryGetPoint(out Vector4 point)
  {
    point = default;

    if (_source == Source.EsteObjeto)
    {
      var p = transform.position + _offset;
      point = new Vector4(p.x, p.y, p.z, _radius);
      return true;
    }

    if (_grab == null) _grab = FindAnyObjectByType<HandGrabManager>();
    if (_grab == null || !_grab.IsHolding || _grab.Held == null) return false;

    var held = _grab.Held.position + _offset;
    point = new Vector4(held.x, held.y, held.z, _radius);
    return true;
  }

  // El primero en ejecutarse cada frame envia todos los puntos al shader.
  private void LateUpdate()
  {
    if (s_uploadedFrame == Time.frameCount) return;
    s_uploadedFrame = Time.frameCount;

    var count = 0;

    foreach (var interactor in s_all)
    {
      if (count >= MaxInteractors) break;
      if (interactor != null && interactor.TryGetPoint(out var point)) s_data[count++] = point;
    }

    for (var i = count; i < MaxInteractors; i++) s_data[i] = Vector4.zero;

    Shader.SetGlobalVectorArray(InteractorsId, s_data);
    Shader.SetGlobalFloat(CountId, count);
  }

  private void OnDrawGizmosSelected()
  {
    if (_source != Source.EsteObjeto) return;
    Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.6f);
    Gizmos.DrawWireSphere(transform.position + _offset, _radius);
  }
}
