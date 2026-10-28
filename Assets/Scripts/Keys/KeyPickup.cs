// KeyPickup.cs
// Llave que se recoge al tocarla. Ponla en la raiz del prefab de tu llave
// junto a FloatingPickup (para que flote y brille). Al acercarse el jugador,
// vuela hacia el, se guarda en el llavero (KeyRing) y desaparece.
//
// La puede soltar un EnemyEncounter al derrotar a todos los enemigos, salir
// de una caja (Contained Pickup) o estar colocada en el nivel.

using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class KeyPickup : MonoBehaviour
{
  [SerializeField, Tooltip("Tipo de llave. Vacio = llave pequeña generica (abre cualquier puerta generica).")]
  private string _keyId = "";

  [Header("Recoger")]
  [SerializeField, Min(0.1f)] private float _pickupRadius = 0.9f;
  [SerializeField, Min(0f), Tooltip("Distancia a la que vuela hacia el jugador (0 = no se atrae).")]
  private float _magnetRadius = 2.5f;
  [SerializeField, Min(0.1f)] private float _magnetSpeed = 9f;
  [SerializeField] private float _playerCenterHeight = 0.9f;

  [Header("Efectos")]
  [SerializeField] private AudioClip _pickupSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;
  [SerializeField, Tooltip("Prefab opcional al recogerla (destello, particulas...).")]
  private GameObject _pickupEffect;

  [Header("Eventos")]
  public UnityEvent OnPickedUp = new UnityEvent();

  private Transform _player;
  private FloatingPickup _floating;
  private bool _collected;
  private bool _attracted;
  private float _speed;

  public string KeyId => _keyId;

  /// <summary>Cambia el tipo de llave (lo usa EnemyEncounter al soltarla).</summary>
  public void SetKeyId(string id) => _keyId = id;

  private void Start()
  {
    _floating = GetComponent<FloatingPickup>();
  }

  private void Update()
  {
    if (_collected)
    {
      return;
    }

    if (_player == null)
    {
      var go = GameObject.FindWithTag("Player");
      _player = go != null ? go.transform : null;

      if (_player == null)
      {
        return;
      }
    }

    if (_floating != null && _floating.IsPopping)
    {
      return;
    }

    var target = _player.position + Vector3.up * _playerCenterHeight;
    var distance = Vector3.Distance(transform.position, target);

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

        if (_floating != null)
        {
          _floating.ExternallyMoved = true;
        }
      }

      _speed = Mathf.MoveTowards(_speed, _magnetSpeed, _magnetSpeed * 3f * Time.deltaTime);
      transform.position = Vector3.MoveTowards(transform.position, target, _speed * Time.deltaTime);
    }
    else if (_attracted)
    {
      _attracted = false;

      if (_floating != null)
      {
        _floating.ExternallyMoved = false;
      }
    }
  }

  private void Collect()
  {
    _collected = true;
    KeyRing.Add(_keyId);

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
    Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
    Gizmos.DrawWireSphere(transform.position, _pickupRadius);
  }
}
