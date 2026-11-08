// StepClimber.cs
// Deja que la nutria (Rigidbody) suba escalones y bordillos sin rampas.
// Cada paso fisico mira justo delante de los pies:
//   - Si hay un borde bajo (mas bajo que "Max Step Height") y encima hay
//     sitio libre, sube el cuerpo poco a poco hasta lo alto del escalon.
//   - Si el obstaculo es mas alto, es una pared: no hace nada.
// Va en el Player, junto al Rigidbody. Se ejecuta despues de Player para
// usar la velocidad que el jugador quiere llevar.

using UnityEngine;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class StepClimber : MonoBehaviour
{
  [SerializeField, Min(0.05f), Tooltip("Altura maxima de escalon que se sube (metros).")]
  private float _maxStepHeight = 0.45f;
  [SerializeField, Min(0.01f), Tooltip("Cuanto mira por delante del cuerpo.")]
  private float _lookAhead = 0.25f;
  [SerializeField, Min(0.1f), Tooltip("Velocidad de subida (metros por segundo). Mas alto = sube mas brusco.")]
  private float _climbSpeed = 5f;
  [SerializeField, Min(0f), Tooltip("Velocidad minima para intentar subir (evita subir quieto).")]
  private float _minSpeed = 0.1f;
  [SerializeField, Range(0f, 60f), Tooltip("Pendiente maxima de la parte de arriba del escalon.")]
  private float _maxTopSlope = 40f;
  [SerializeField, Tooltip("Capas que cuentan como escalon (por defecto todo menos Player, UI, Grabbable y Enemy).")]
  private LayerMask _stepLayers = ~((1 << 3) | (1 << 5) | (1 << 8) | (1 << 10));
  [SerializeField, Tooltip("Collider del cuerpo (vacio = el del propio objeto).")]
  private Collider _body;

  private Rigidbody _rb;

  private void Awake()
  {
    _rb = GetComponent<Rigidbody>();

    if (_body == null)
    {
      _body = GetComponent<Collider>();
    }
  }

  private void FixedUpdate()
  {
    if (_body == null || _rb.isKinematic)
    {
      return;
    }

    var v = _rb.linearVelocity;
    var flat = new Vector3(v.x, 0f, v.z);

    if (flat.magnitude < _minSpeed)
    {
      return;
    }

    var dir = flat.normalized;
    var b = _body.bounds;
    var feet = b.min.y;
    var reach = Mathf.Min(b.extents.x, b.extents.z) + _lookAhead;
    var low = new Vector3(b.center.x, feet + 0.04f, b.center.z);
    const QueryTriggerInteraction ignore = QueryTriggerInteraction.Ignore;

    // 1. Algo delante de los pies...
    if (!Physics.Raycast(low, dir, out var face, reach, _stepLayers, ignore))
    {
      return;
    }

    // ...que sea una cara vertical (las rampas ya se suben solas)...
    if (Vector3.Angle(face.normal, Vector3.up) < 50f)
    {
      return;
    }

    // ...y no un objeto suelto que se puede empujar.
    var body = face.collider.attachedRigidbody;

    if (body != null && !body.isKinematic)
    {
      return;
    }

    // 2. A la altura maxima de escalon no hay nada (si no, es pared).
    var high = low + Vector3.up * _maxStepHeight;

    if (Physics.Raycast(high, dir, reach + 0.05f, _stepLayers, ignore))
    {
      return;
    }

    // 3. Altura real de lo alto del escalon.
    var probe = high + dir * (face.distance + 0.06f);

    if (!Physics.Raycast(probe, Vector3.down, out var top, _maxStepHeight, _stepLayers, ignore)
        || Vector3.Angle(top.normal, Vector3.up) > _maxTopSlope)
    {
      return;
    }

    var rise = top.point.y - feet;

    if (rise <= 0.005f)
    {
      return;
    }

    // 4. Subir suave, sin que la gravedad tire hacia abajo mientras tanto.
    var step = Mathf.Min(rise + 0.01f, _climbSpeed * Time.fixedDeltaTime);
    _rb.position += Vector3.up * step;

    if (v.y < 0f)
    {
      _rb.linearVelocity = new Vector3(v.x, 0f, v.z);
    }
  }

  private void OnDrawGizmosSelected()
  {
    var c = _body != null ? _body : GetComponent<Collider>();

    if (c == null)
    {
      return;
    }

    var b = c.bounds;
    var feet = new Vector3(b.center.x, b.min.y, b.center.z);
    Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.8f);
    Gizmos.DrawLine(feet, feet + Vector3.up * _maxStepHeight);
    Gizmos.DrawWireCube(feet + Vector3.up * _maxStepHeight, new Vector3(0.2f, 0.01f, 0.2f));
  }
}
