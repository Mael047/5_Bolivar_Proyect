// HealthPickup.cs
// Objeto que cura al jugador al tocarlo (por ejemplo una hoja 3D).
// Ponlo en la raiz del prefab junto a FloatingPickup y metelo en
// "Contained Pickup" de una caja, o colocalo directamente en el nivel.
//
// Cuando el jugador se acerca, la hoja vuela hacia el (como los corazones de
// Zelda), lo cura, hace un destello y desaparece. Con la vida llena se queda
// flotando esperando (opcional).
// No necesita collider: detecta la distancia al jugador.

using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class HealthPickup : MonoBehaviour
{
  [Header("Curacion")]
  [SerializeField, Min(1), Tooltip("Vida que recupera. Con 100 de vida y 5 hojas, 20 = una hoja.")]
  private int _healAmount = 20;
  [SerializeField, Tooltip("Con la vida llena no se recoge, se queda esperando.")]
  private bool _onlyIfHurt = true;

  [Header("Recoger")]
  [SerializeField, Min(0.1f), Tooltip("Distancia a la que se recoge.")]
  private float _pickupRadius = 0.9f;
  [SerializeField, Min(0f), Tooltip("Distancia a la que empieza a volar hacia el jugador (0 = no se atrae).")]
  private float _magnetRadius = 3f;
  [SerializeField, Min(0.1f)] private float _magnetSpeed = 9f;
  [SerializeField, Tooltip("Altura del pecho del jugador (a donde vuela).")]
  private float _playerCenterHeight = 0.9f;

  [Header("Desaparecer solo (opcional)")]
  [SerializeField, Min(0f), Tooltip("Segundos hasta desaparecer si nadie lo recoge (0 = nunca).")]
  private float _lifetime;
  [SerializeField, Min(0f), Tooltip("Segundos finales en los que parpadea avisando.")]
  private float _blinkTime = 2.5f;

  [Header("Efectos")]
  [SerializeField] private AudioClip _pickupSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.8f;
  [SerializeField, Tooltip("Prefab opcional al recogerlo (particulas, destello...).")]
  private GameObject _pickupEffect;

  [Header("Eventos")]
  public UnityEvent OnPickedUp = new UnityEvent();

  private Transform _player;
  private Health _health;
  private FloatingPickup _floating;
  private Renderer[] _renderers;
  private float _age;
  private bool _collected;
  private bool _attracted;
  private float _speed;

  private void Start()
  {
    _floating = GetComponent<FloatingPickup>();
    _renderers = GetComponentsInChildren<Renderer>();
    FindPlayer();
  }

  private void FindPlayer()
  {
    var go = GameObject.FindWithTag("Player");

    if (go == null)
    {
      return;
    }

    _player = go.transform;
    _health = go.GetComponentInParent<Health>();

    if (_health == null)
    {
      _health = go.GetComponentInChildren<Health>();
    }
  }

  private void Update()
  {
    if (_collected)
    {
      return;
    }

    _age += Time.deltaTime;

    if (_lifetime > 0f)
    {
      var left = _lifetime - _age;

      if (left <= 0f)
      {
        Destroy(gameObject);
        return;
      }

      if (left < _blinkTime && !_attracted)
      {
        var visible = Mathf.Repeat(_age * Mathf.Lerp(16f, 6f, left / _blinkTime), 1f) > 0.35f;
        SetVisible(visible);
      }
    }

    if (_player == null || _health == null)
    {
      FindPlayer();
      return;
    }

    if (_health.IsDead || _floating != null && _floating.IsPopping)
    {
      return;
    }

    var wanted = !_onlyIfHurt || _health.CurrentHealth < _health.MaxHealth;
    var target = _player.position + Vector3.up * _playerCenterHeight;
    var distance = Vector3.Distance(transform.position, target);

    if (!wanted)
    {
      ReleaseMagnet();
      return;
    }

    if (distance <= _pickupRadius)
    {
      Collect();
      return;
    }

    if (_magnetRadius > 0f && distance <= _magnetRadius)
    {
      if (!_attracted)
      {
        _attracted = true;
        _speed = 0f;
        SetVisible(true);

        if (_floating != null)
        {
          _floating.ExternallyMoved = true;
        }
      }

      _speed = Mathf.MoveTowards(_speed, _magnetSpeed, _magnetSpeed * 3f * Time.deltaTime);
      transform.position = Vector3.MoveTowards(transform.position, target, _speed * Time.deltaTime);
      transform.Rotate(0f, 540f * Time.deltaTime, 0f, Space.World);
    }
    else
    {
      ReleaseMagnet();
    }
  }

  private void ReleaseMagnet()
  {
    if (!_attracted)
    {
      return;
    }

    _attracted = false;

    if (_floating != null)
    {
      _floating.ExternallyMoved = false;
    }
  }

  private void SetVisible(bool visible)
  {
    foreach (var r in _renderers)
    {
      if (r != null) r.enabled = visible;
    }
  }

  private void Collect()
  {
    _collected = true;
    _health.Heal(_healAmount);

    if (_pickupSound != null)
    {
      AudioSource.PlayClipAtPoint(_pickupSound, transform.position, _volume);
    }

    if (_pickupEffect != null)
    {
      Instantiate(_pickupEffect, transform.position, Quaternion.identity);
    }

    OnPickedUp.Invoke();
    Destroy(gameObject);
  }

  private void OnDrawGizmosSelected()
  {
    Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.8f);
    Gizmos.DrawWireSphere(transform.position, _pickupRadius);
    Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.3f);
    Gizmos.DrawWireSphere(transform.position, _magnetRadius);
  }
}
