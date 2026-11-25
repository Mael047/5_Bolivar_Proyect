// KeyIndicatorHUD.cs
// Llave del HUD, al lado de la barra de vida (hojas). Sin llave se ve con su
// color original (gris); al recoger una se cambia a la version dorada con un
// pequeño salto y destello. Al gastarla (abrir una puerta) vuelve a gris.
//
// Va en el objeto "Llave" del prefab PlayerHUD (Image). Si "Colocar despues
// de" apunta al contenedor de hojas, se coloca sola a la derecha de la ultima
// hoja (asi funciona aunque cambie el numero de hojas).

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class KeyIndicatorHUD : MonoBehaviour
{
  [Header("Imagen")]
  [SerializeField, Tooltip("Imagen de la llave. Vacio = la de este objeto.")]
  private Image _image;

  [SerializeField, Tooltip("Llave sin conseguir (color original).")]
  private Sprite _emptySprite;

  [SerializeField, Tooltip("Llave conseguida (dorada).")]
  private Sprite _goldSprite;

  [SerializeField, Tooltip("Color de la imagen sin llave (blanco = tal cual viene).")]
  private Color _emptyColor = Color.white;

  [SerializeField, Tooltip("Color de la imagen con llave (blanco = tal cual viene la dorada).")]
  private Color _goldColor = Color.white;

  [Header("Que llaves cuentan")]
  [SerializeField, Tooltip("Activo: cualquier llave la pone dorada. Inactivo: solo la del Key Id.")]
  private bool _anyKey = true;

  [SerializeField, Tooltip("Tipo de llave (vacio = llave pequeña generica). Solo si 'Any Key' esta apagado.")]
  private string _keyId = "";

  [SerializeField, Tooltip("Texto opcional con la cantidad (x2, x3...). Se oculta con 0 o 1 llave.")]
  private Text _countLabel;

  [Header("Posicion")]
  [SerializeField, Tooltip("Contenedor de las hojas de vida. La llave se coloca a la derecha de la ultima hoja. Vacio = se queda donde esta en el prefab.")]
  private RectTransform _placeAfter;

  [SerializeField] private float _gap = 125f;

  [Header("Animacion")]
  [SerializeField, Tooltip("Escala del salto al conseguir la llave.")]
  private float _popScale = 1.45f;

  [SerializeField, Min(0.05f)] private float _popDuration = 0.45f;

  private RectTransform _rect;
  private Vector3 _baseScale;
  private Coroutine _pop;
  private int _lastCount;

  private void Awake()
  {
    _rect = (RectTransform)transform;
    _baseScale = _rect.localScale;

    if (_image == null)
    {
      _image = GetComponent<Image>();
    }

    if (_emptySprite == null && _image != null)
    {
      _emptySprite = _image.sprite;
    }
  }

  private void OnEnable()
  {
    KeyRing.Changed += OnKeysChanged;
    _lastCount = CountKeys();
    Refresh();
    StartCoroutine(PlaceNextFrame());
  }

  private void OnDisable()
  {
    KeyRing.Changed -= OnKeysChanged;
    _rect.localScale = _baseScale;
    _pop = null;
  }

  private bool HasKey()
  {
    return CountKeys() > 0;
  }

  private int CountKeys()
  {
    if (!_anyKey)
    {
      return KeyRing.Count(_keyId);
    }

    var total = 0;

    foreach (var pair in KeyRing.All)
    {
      total += pair.Value;
    }

    return total;
  }

  private void OnKeysChanged(string id, int count)
  {
    var total = CountKeys();
    Refresh();

    // Salto al ganar una llave (no al gastarla).
    if (total > _lastCount && isActiveAndEnabled)
    {
      if (_pop != null) StopCoroutine(_pop);
      _pop = StartCoroutine(Pop());
    }

    _lastCount = total;
  }

  private void Refresh()
  {
    if (_image == null)
    {
      return;
    }

    var has = HasKey();

    if (has && _goldSprite != null)
    {
      _image.sprite = _goldSprite;
    }
    else if (_emptySprite != null)
    {
      _image.sprite = _emptySprite;
    }

    _image.color = has ? _goldColor : _emptyColor;

    if (_countLabel != null)
    {
      var n = CountKeys();
      _countLabel.gameObject.SetActive(n > 1);
      _countLabel.text = "x" + n;
    }
  }

  private IEnumerator Pop()
  {
    var t = 0f;

    while (t < _popDuration)
    {
      t += Time.unscaledDeltaTime;
      var k = Mathf.Clamp01(t / _popDuration);
      // Sube rapido y vuelve con un pequeño rebote.
      var s = 1f + (_popScale - 1f) * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.5f);
      _rect.localScale = _baseScale * s;
      _rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 3f) * 12f * (1f - k));

      if (_image != null)
      {
        // Destello: un instante mas brillante.
        var flash = Mathf.Clamp01(1f - k * 3f);
        _image.color = Color.Lerp(_goldColor, Color.white, flash);
      }

      yield return null;
    }

    _rect.localScale = _baseScale;
    _rect.localRotation = Quaternion.identity;
    Refresh();
    _pop = null;
  }

  // Las hojas se crean en Awake del HUD; se espera a que el layout las coloque.
  private IEnumerator PlaceNextFrame()
  {
    if (_placeAfter == null)
    {
      yield break;
    }

    yield return null;
    LayoutRebuilder.ForceRebuildLayoutImmediate(_placeAfter);

    RectTransform last = null;

    for (var i = _placeAfter.childCount - 1; i >= 0; i--)
    {
      var child = _placeAfter.GetChild(i) as RectTransform;

      if (child != null && child.gameObject.activeSelf)
      {
        last = child;
        break;
      }
    }

    if (last == null || _rect.parent == null)
    {
      yield break;
    }

    // Borde derecho de la ultima hoja, en el espacio del padre de la llave.
    var corners = new Vector3[4];
    last.GetWorldCorners(corners);
    var parent = (RectTransform)_rect.parent;
    var right = parent.InverseTransformPoint((corners[2] + corners[3]) * 0.5f);
    var middleY = parent.InverseTransformPoint((corners[0] + corners[2]) * 0.5f).y;

    // Se pone la llave con su borde izquierdo justo despues (respetando su pivote).
    var width = _rect.rect.width * _rect.localScale.x;
    var localPos = new Vector3(right.x + _gap + width * _rect.pivot.x, middleY, _rect.localPosition.z);
    _rect.localPosition = localPos;
  }
}
