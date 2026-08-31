// PlayerSwordAttack.cs
// Golpe arma cuerpo a cuerpo basado en la espada real del personaje, definida por
// DOS puntos que siguen la animacion: la base (empunadura) y la punta de la hoja.
// DAMAGE: el barrido de la linea base -> punta es lo que hace contacto. Se divide
// la hoja en _bladeSamples y, por cada punto, se lanza un rayo en la direccion del
// movimiento de la hoja; asi el filo completo corta a quien se atraviesa. Solo
// conecta cuando la hoja se mueve por encima de _swingSpeedThreshold (la fase
// activa del swing); con la espada quieta nunca dania.
// Al conectar, rompe el primer NuiGrab.Breakable que atraviese (cajas, etc.).

using UnityEngine;

public class PlayerSwordAttack : MonoBehaviour
{
  [Header("Espada")]
  [Tooltip("Punto de origen de la hoja (empunadura). Si no se asigna, se busca un hijo llamado 'SwordBase' y, si no, 'Sword'.")]
  [SerializeField] private Transform _swordBase;
  [Tooltip("Punto de la punta de la hoja. Si no se asigna, se busca un hijo llamado 'SwordTip'.")]
  [SerializeField] private Transform _swordTip;
  [Tooltip("Velocidad minima de la hoja (m/s) para que el golpe conecte. Filtra el momento del swing.")]
  [SerializeField, Min(0f)] private float _swingSpeedThreshold = 3f;
  [Tooltip("Numero de muestras en las que se divide la hoja (base -> punta) para el barrido de danio. Mas muestras = mayor precision, menor rendimiento.")]
  [SerializeField, Min(1)] private int _bladeSamples = 8;
  [Tooltip("Impulso de impacto que heredan los fragmentos al romper.")]
  [SerializeField, Min(0f)] private float _impactSpeed = 5f;

  [Header("Debug (live)")]
  [SerializeField] private float _debugBladeSpeed;

  [Header("Debug (gizmos)")]
  [Tooltip("Muestra la hoja de la espada (linea base -> punta) en la Scene View para calibrar.")]
  [SerializeField] private bool _drawGizmos = true;
  [SerializeField] private Color _bladeColor = new Color(1f, 0.9f, 0.4f, 0.9f);

  private Player _player;
  private Vector3 _prevCenter;
  private bool _hasPrev;

  private void Awake()
  {
    _player = GetComponent<Player>();

    if (_swordBase == null)
    {
      _swordBase = FindSword("SwordBase", "Sword");
    }

    if (_swordTip == null)
    {
      _swordTip = FindSword("SwordTip", null);
    }
  }

  private Transform FindSword(string primary, string fallback)
  {
    foreach (var child in GetComponentsInChildren<Transform>(true))
    {
      if (child.name == primary)
      {
        return child;
      }
    }

    if (fallback != null)
    {
      foreach (var child in GetComponentsInChildren<Transform>(true))
      {
        if (child.name == fallback)
        {
          return child;
        }
      }
    }

    return null;
  }

  private Vector3 BladeCenter()
  {
    return (_swordBase.position + _swordTip.position) * 0.5f;
  }

  private void LateUpdate()
  {
    if (_player == null || _swordBase == null || _swordTip == null || !_player.IsAttacking || _player.IsBlocking)
    {
      _hasPrev = false;
      return;
    }

    var center = BladeCenter();

    if (_hasPrev)
    {
      var displacement = center - _prevCenter;
      var delta = Mathf.Max(Time.deltaTime, 1e-4f);
      var speed = displacement.magnitude / delta;
      _debugBladeSpeed = speed;

      if (speed >= _swingSpeedThreshold)
      {
        // Barrido de la hoja: se muestrea la linea base -> punta y, por cada punto,
        // se lanza un rayo en la direccion del desplazamiento de la hoja. Asi el
        // filo completo corta a quien se atraviesa mientras barre.
        var direction = displacement.normalized;
        var deltaMagnitude = displacement.magnitude;
        var bladeStart = _swordBase.position;
        var bladeEnd = _swordTip.position;

        for (int i = 0; i < _bladeSamples; i++)
        {
          var t = _bladeSamples == 1 ? 0f : (float)i / (_bladeSamples - 1);
          var sample = Vector3.Lerp(bladeStart, bladeEnd, t);

          if (Physics.Raycast(sample, direction, out var hit, deltaMagnitude))
          {
            var breakable = hit.collider.GetComponentInParent<NuiGrab.Breakable>();

            if (breakable != null && !breakable.IsBroken)
            {
              breakable.Break(hit.point, direction * _impactSpeed);
              break;
            }
          }
        }
      }
    }

    _prevCenter = center;
    _hasPrev = true;
  }

  private void OnDrawGizmos()
  {
    if (_drawGizmos && _swordBase == null)
    {
      _swordBase = FindSword("SwordBase", "Sword");
    }

    if (_drawGizmos && _swordTip == null)
    {
      _swordTip = FindSword("SwordTip", null);
    }

    if (!_drawGizmos || _swordBase == null || _swordTip == null)
    {
      return;
    }

    // La hoja real (base -> punta), la unica cosa dibujada: sigue la animacion
    // y sirve para calibrar la posicion de los dos puntos.
    Gizmos.color = _bladeColor;
    Gizmos.DrawLine(_swordBase.position, _swordTip.position);
    Gizmos.DrawWireSphere(_swordBase.position, 0.06f);
    Gizmos.DrawWireSphere(_swordTip.position, 0.06f);
  }
}
