// PlayerHealthHUD.cs
// Vida del jugador estilo Hollow Knight con hojas: la vida maxima se reparte
// entre N hojas (5 por defecto) y cada hoja puede estar llena, a la mitad o
// vacia segun el daño recibido. Al perder una hoja (o media) la hoja tiembla
// y un "fantasma" de la hoja anterior cae girando y se desvanece; al curarse
// la hoja hace un pequeño salto.
//
// Va en la raiz del Canvas del HUD (prefab PlayerHUD). El diseño (posicion,
// tamaño, marco del retrato, fondo...) se hace en el prefab: aqui solo se
// asignan los sprites y la hoja plantilla, que se duplica hasta tener N hojas.
//
// Calculo: cada hoja vale MaxHealth / N. Se cuentan medias hojas redondeando
// hacia arriba: con 100 de vida y 5 hojas, cada hoja = 20 y cada mitad = 10;
// un golpe de 20 vacia una hoja, uno de 10 la deja a la mitad. Mientras quede
// algo de vida siempre se ve al menos media hoja.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
[DisallowMultipleComponent]
public class PlayerHealthHUD : MonoBehaviour
{
  private enum LeafState { Empty = 0, Half = 1, Full = 2 }

  [Header("Hojas")]
  [SerializeField, Min(1)] private int _leafCount = 5;

  [SerializeField, Tooltip("Hoja de ejemplo (Image). Se duplica hasta tener 'Leaf Count' hojas en su mismo contenedor.")]
  private Image _leafTemplate;

  [SerializeField] private Sprite _fullSprite;
  [SerializeField] private Sprite _halfSprite;
  [SerializeField] private Sprite _emptySprite;

  [Header("Jugador")]
  [SerializeField, Tooltip("Vida del jugador. Vacio = se busca el objeto con tag Player.")]
  private Health _health;

  [Header("Animaciones")]
  [SerializeField, Tooltip("Pixeles que tiembla una hoja al perder vida.")]
  private float _shakeAmount = 8f;
  [SerializeField, Min(0.01f)] private float _shakeDuration = 0.3f;
  [SerializeField, Tooltip("Escala del salto de una hoja al curarse.")]
  private float _healPopScale = 1.3f;
  [SerializeField, Tooltip("Una hoja 'fantasma' cae al perder vida.")]
  private bool _fallingLeaf = true;
  [SerializeField] private float _fallDistance = 70f;
  [SerializeField, Min(0.05f)] private float _fallDuration = 0.8f;
  [SerializeField, Range(0f, 1f), Tooltip("Con esta fraccion de vida o menos, la ultima hoja late.")]
  private float _lowHealthThreshold = 0.2f;

  [Header("Mostrar")]
  [SerializeField, Range(1f, 30f)] private float _fadeSpeed = 8f;

  private readonly List<Image> _leaves = new List<Image>();
  private readonly List<LeafState> _states = new List<LeafState>();
  private readonly List<Coroutine> _anims = new List<Coroutine>();
  private CanvasGroup _group;
  private int _current;
  private int _max;

  /// <summary>Se dispara al recibir daño (el retrato lo usa para reaccionar).</summary>
  public event System.Action<int> Damaged;

  private void Awake()
  {
    _group = GetComponent<CanvasGroup>();

    if (_group == null)
    {
      _group = gameObject.AddComponent<CanvasGroup>();
    }

    _group.blocksRaycasts = false;
    _group.interactable = false;
    _group.alpha = 0f;

    BuildLeaves();
  }

  private void Start()
  {
    if (_health == null)
    {
      var player = GameObject.FindWithTag("Player");
      _health = player != null ? player.GetComponentInParent<Health>() : null;

      if (_health == null && player != null)
      {
        _health = player.GetComponentInChildren<Health>();
      }
    }

    if (_health == null)
    {
      Debug.LogWarning("[PlayerHealthHUD] No encontre la Health del jugador.", this);
      return;
    }

    _health.Changed += OnHealthChanged;
    _current = _health.CurrentHealth;
    _max = _health.MaxHealth;
    Refresh(animate: false);
  }

  private void OnDestroy()
  {
    if (_health != null)
    {
      _health.Changed -= OnHealthChanged;
    }
  }

  private void Update()
  {
    // Oculto en el menu principal y mientras se examina un coleccionable.
    var visible = !Menus.MainMenu.IsOpen && !Collectibles.CollectibleInspector.IsOpen;
    var k = 1f - Mathf.Exp(-_fadeSpeed * Time.unscaledDeltaTime);
    _group.alpha = Mathf.Lerp(_group.alpha, visible ? 1f : 0f, k);

    LowHealthPulse();
  }

  private void OnHealthChanged(int current, int max, int delta)
  {
    _current = current;
    _max = max;
    Refresh(animate: true);

    if (delta < 0)
    {
      Damaged?.Invoke(-delta);
    }
  }

  // ---------- Hojas ----------

  private void BuildLeaves()
  {
    if (_leafTemplate == null)
    {
      Debug.LogError("[PlayerHealthHUD] Falta 'Leaf Template'.", this);
      return;
    }

    var parent = _leafTemplate.transform.parent;
    _leaves.Add(_leafTemplate);

    for (var i = 1; i < _leafCount; i++)
    {
      var copy = Instantiate(_leafTemplate, parent);
      copy.name = $"{_leafTemplate.name} ({i + 1})";
      _leaves.Add(copy);
    }

    foreach (var leaf in _leaves)
    {
      _states.Add(LeafState.Full);
      _anims.Add(null);
      leaf.sprite = _fullSprite;
    }
  }

  private void Refresh(bool animate)
  {
    if (_leaves.Count == 0 || _max <= 0)
    {
      return;
    }

    // Medias hojas que quedan, redondeando hacia arriba.
    var halves = Mathf.CeilToInt(_current * (_leaves.Count * 2f) / _max - 0.0001f);
    halves = Mathf.Clamp(halves, 0, _leaves.Count * 2);

    for (var i = 0; i < _leaves.Count; i++)
    {
      var remaining = halves - i * 2;
      var state = remaining >= 2 ? LeafState.Full : remaining == 1 ? LeafState.Half : LeafState.Empty;
      var previous = _states[i];

      if (state == previous && animate)
      {
        continue;
      }

      var oldSprite = SpriteFor(previous);
      _states[i] = state;
      _leaves[i].sprite = SpriteFor(state);

      if (!animate || state == previous)
      {
        continue;
      }

      if (_anims[i] != null)
      {
        StopCoroutine(_anims[i]);
        ResetLeaf(_leaves[i]);
      }

      _anims[i] = state < previous
        ? StartCoroutine(LoseRoutine(i, oldSprite))
        : StartCoroutine(GainRoutine(i));
    }
  }

  private Sprite SpriteFor(LeafState state)
  {
    switch (state)
    {
      case LeafState.Full: return _fullSprite;
      case LeafState.Half: return _halfSprite != null ? _halfSprite : _fullSprite;
      default: return _emptySprite;
    }
  }

  private IEnumerator LoseRoutine(int index, Sprite oldSprite)
  {
    var leaf = _leaves[index];
    var rect = leaf.rectTransform;

    if (_fallingLeaf && oldSprite != null)
    {
      StartCoroutine(FallingGhost(leaf, oldSprite));
    }

    var t = 0f;

    while (t < _shakeDuration)
    {
      t += Time.unscaledDeltaTime;
      var strength = 1f - t / _shakeDuration;
      var offset = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * (_shakeAmount * strength);
      rect.localPosition = LeafBase(rect) + (Vector3)offset;
      rect.localScale = Vector3.one * (1f + 0.15f * strength);
      yield return null;
    }

    ResetLeaf(leaf);
    _anims[index] = null;
  }

  private IEnumerator GainRoutine(int index)
  {
    var leaf = _leaves[index];
    var rect = leaf.rectTransform;
    var t = 0f;
    const float duration = 0.3f;

    while (t < duration)
    {
      t += Time.unscaledDeltaTime;
      var k = Mathf.Clamp01(t / duration);
      rect.localScale = Vector3.one * Mathf.Lerp(_healPopScale, 1f, 1f - (1f - k) * (1f - k));
      yield return null;
    }

    ResetLeaf(leaf);
    _anims[index] = null;
  }

  /// <summary>Copia de la hoja con el sprite anterior que cae girando y se desvanece.</summary>
  private IEnumerator FallingGhost(Image leaf, Sprite sprite)
  {
    var go = new GameObject("FallingLeaf", typeof(RectTransform));
    go.transform.SetParent(transform, false);
    var ghost = go.AddComponent<Image>();
    ghost.sprite = sprite;
    ghost.preserveAspect = leaf.preserveAspect;
    ghost.raycastTarget = false;

    var rect = ghost.rectTransform;
    var source = leaf.rectTransform;
    rect.position = source.position;
    rect.rotation = source.rotation;
    rect.sizeDelta = source.rect.size;
    rect.localScale = source.lossyScale.x > 0f ? Vector3.one * (source.lossyScale.x / transform.lossyScale.x) : Vector3.one;

    var start = rect.anchoredPosition;
    var drift = Random.Range(-1f, 1f) * _fallDistance * 0.5f;
    var spin = Random.Range(-1f, 1f) * 180f;
    var t = 0f;

    while (t < _fallDuration)
    {
      t += Time.unscaledDeltaTime;
      var k = Mathf.Clamp01(t / _fallDuration);

      // Caida tipo hoja: baja acelerando y se balancea de lado a lado.
      rect.anchoredPosition = start + new Vector2(drift * k + Mathf.Sin(k * Mathf.PI * 3f) * 10f, -_fallDistance * k * k);
      rect.localRotation = source.localRotation * Quaternion.Euler(0f, 0f, spin * k);
      ghost.color = new Color(1f, 1f, 1f, 1f - k);
      yield return null;
    }

    Destroy(go);
  }

  private void LowHealthPulse()
  {
    if (_max <= 0 || _leaves.Count == 0)
    {
      return;
    }

    var low = _current > 0 && _current <= _max * _lowHealthThreshold;

    // La ultima hoja con vida late.
    for (var i = _leaves.Count - 1; i >= 0; i--)
    {
      if (_states[i] == LeafState.Empty || _anims[i] != null)
      {
        continue;
      }

      var scale = low ? 1f + 0.08f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f)) : 1f;
      _leaves[i].rectTransform.localScale = Vector3.one * scale;
      break;
    }
  }

  // Posicion de reposo de cada hoja (la decide el LayoutGroup del contenedor).
  private readonly Dictionary<RectTransform, Vector3> _basePositions = new Dictionary<RectTransform, Vector3>();

  private Vector3 LeafBase(RectTransform rect)
  {
    if (!_basePositions.TryGetValue(rect, out var pos))
    {
      pos = rect.localPosition;
      _basePositions[rect] = pos;
    }

    return pos;
  }

  private void ResetLeaf(Image leaf)
  {
    var rect = leaf.rectTransform;

    if (_basePositions.TryGetValue(rect, out var pos))
    {
      rect.localPosition = pos;
    }

    rect.localScale = Vector3.one;
  }

  // ---------- Pruebas ----------

  [ContextMenu("Probar: recibir 10 de daño")]
  private void TestDamage10()
  {
    if (_health != null) _health.TakeDamage(10);
  }

  [ContextMenu("Probar: recibir 20 de daño")]
  private void TestDamage20()
  {
    if (_health != null) _health.TakeDamage(20);
  }

  [ContextMenu("Probar: curar 20")]
  private void TestHeal()
  {
    if (_health != null) _health.Heal(20);
  }
}
