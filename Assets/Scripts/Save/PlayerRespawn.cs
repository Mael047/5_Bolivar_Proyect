// PlayerRespawn.cs
// Muerte de la nutria (va en el Player, junto a Health):
//   1. Al llegar a 0 de vida se queda quieta un momento (sin control).
//   2. Todo lo que lleva (espada, escudo, llaves sin usar, coleccionables)
//      queda en el punto donde murio, como "restos". Si ya habia restos sin
//      recoger, se juntan con los nuevos (nunca se pierde nada).
//   3. Fundido a negro y reaparece junto al ultimo banco con la vida llena.
//      El mundo vuelve a como estaba (cajas normales y enemigos), pero las
//      puertas abiertas, puzzles resueltos, etc. siguen igual.
// Sin banco usado todavia, reaparece al inicio de la escena.

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerRespawn : MonoBehaviour
{
  [SerializeField, Min(0f), Tooltip("Segundos tras morir antes del fundido.")]
  private float _deathDelay = 1.2f;
  [SerializeField, Min(0f)] private float _fadeTime = 0.8f;
  [SerializeField] private Color _fadeColor = Color.black;
  [SerializeField, Tooltip("Trigger del Animator al morir (vacio = ninguno).")]
  private string _deathTrigger = "";
  [SerializeField] private AudioClip _deathSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;

  private Health _health;
  private bool _dying;

  private void Awake()
  {
    _health = GetComponentInParent<Health>();
    if (_health == null) _health = GetComponentInChildren<Health>();

    if (_health != null)
    {
      // La nutria no se destruye al morir: reaparece.
      _health.DestroyOnDeath = false;
      _health.Died += OnDied;
    }
  }

  private void OnDestroy()
  {
    if (_health != null) _health.Died -= OnDied;
  }

  private void OnDied()
  {
    if (!_dying)
    {
      StartCoroutine(DeathRoutine());
    }
  }

  private IEnumerator DeathRoutine()
  {
    _dying = true;

    var input = GetComponent<PlayerInput>();
    if (input != null) input.DeactivateInput();

    if (!string.IsNullOrEmpty(_deathTrigger))
    {
      var animator = GetComponentInChildren<Animator>();
      if (animator != null) animator.SetTrigger(_deathTrigger);
    }

    if (_deathSound != null)
    {
      AudioSource.PlayClipAtPoint(_deathSound, transform.position, _volume);
    }

    var body = GetComponent<Rigidbody>();
    if (body != null) body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);

    yield return new WaitForSeconds(_deathDelay);

    var scene = GameState.PrepareDeath(transform.position);
    SceneDoor.LoadSceneWithFade(scene, _fadeTime, _fadeColor);
  }
}
