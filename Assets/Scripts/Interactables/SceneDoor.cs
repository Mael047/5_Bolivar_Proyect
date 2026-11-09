// SceneDoor.cs
// Zona invisible en una puerta: cuando la nutria entra, la pantalla se
// funde a negro y se carga otra escena (por ejemplo, del jardin a Nivel_CASA).
//
// Uso: ponlo en un objeto con BoxCollider (se marca solo como Trigger),
// colocalo en la puerta y escribe el nombre de la escena. La escena tiene
// que estar en File > Build Profiles > Scene List.

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class SceneDoor : MonoBehaviour
{
  [SerializeField, Tooltip("Nombre exacto de la escena a cargar.")]
  private string _sceneName = "Nivel_CASA";
  [SerializeField, Tooltip("Nombre del objeto de la otra escena donde aparece la nutria (vacio = donde la ponga esa escena).")]
  private string _arrivalPoint = "";
  [SerializeField, Min(0f), Tooltip("Segundos del fundido a negro (y de vuelta en la escena nueva).")]
  private float _fadeTime = 0.6f;
  [SerializeField] private Color _fadeColor = Color.black;
  [SerializeField, Min(0f), Tooltip("Al empezar la escena, segundos en los que la puerta no hace nada. Si la nutria aparece dentro, tiene que salir y volver a entrar.")]
  private float _armDelay = 1f;
  [SerializeField] private AudioClip _enterSound;
  [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;

  private static bool s_loading;
  private float _armedAt;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void ResetStatics() => s_loading = false;

  private void Reset()
  {
    var box = GetComponent<BoxCollider>();
    box.isTrigger = true;
    box.size = new Vector3(3f, 4f, 2.4f);
    box.center = new Vector3(0f, 2f, 0f);
  }

  private void Awake()
  {
    GetComponent<BoxCollider>().isTrigger = true;
    _armedAt = Time.time + _armDelay;
  }

  private void OnTriggerEnter(Collider other) => TryEnter(other);


  private void TryEnter(Collider other)
  {
    if (s_loading || Time.time < _armedAt || string.IsNullOrEmpty(_sceneName))
    {
      return;
    }

    var player = other.GetComponentInParent<Player>();

    if (player == null)
    {
      return;
    }

    if (!Application.CanStreamedLevelBeLoaded(_sceneName))
    {
      Debug.LogWarning($"SceneDoor: la escena '{_sceneName}' no esta en la lista de escenas de la build.", this);
      return;
    }

    s_loading = true;

    // Lo que lleva la nutria pasa a la escena nueva.
    GameState.PrepareSceneChange(_arrivalPoint);

    // La nutria deja de responder mientras se funde.
    var input = player.GetComponent<PlayerInput>();
    if (input != null) input.DeactivateInput();

    if (_enterSound != null)
    {
      AudioSource.PlayClipAtPoint(_enterSound, transform.position, _volume);
    }

    ScreenFader.LoadScene(_sceneName, _fadeTime, _fadeColor, () => s_loading = false);
  }

  /// <summary>Fundido a negro y carga de escena (lo usan tambien la muerte y Cargar partida).</summary>
  public static void LoadSceneWithFade(string scene, float fadeTime, Color color)
  {
    if (s_loading)
    {
      return;
    }

    s_loading = true;
    ScreenFader.LoadScene(scene, fadeTime, color, () => s_loading = false);
  }

  private void OnDrawGizmos()
  {
    var box = GetComponent<BoxCollider>();

    if (box == null)
    {
      return;
    }

    Gizmos.matrix = transform.localToWorldMatrix;
    Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.25f);
    Gizmos.DrawCube(box.center, box.size);
    Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
    Gizmos.DrawWireCube(box.center, box.size);
  }

  /// <summary>Fundido a pantalla completa que sobrevive al cambio de escena.</summary>
  private class ScreenFader : MonoBehaviour
  {
    private Image _image;

    public static void LoadScene(string scene, float fadeTime, Color color, System.Action done)
    {
      var go = new GameObject("ScreenFader");
      DontDestroyOnLoad(go);
      var canvas = go.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = 5000;

      var imageGo = new GameObject("Fade", typeof(RectTransform));
      imageGo.transform.SetParent(go.transform, false);
      var rt = (RectTransform)imageGo.transform;
      rt.anchorMin = Vector2.zero;
      rt.anchorMax = Vector2.one;
      rt.offsetMin = rt.offsetMax = Vector2.zero;

      var fader = go.AddComponent<ScreenFader>();
      fader._image = imageGo.AddComponent<Image>();
      fader._image.raycastTarget = true;
      color.a = 0f;
      fader._image.color = color;
      fader.StartCoroutine(fader.Run(scene, fadeTime, done));
    }

    private IEnumerator Run(string scene, float fadeTime, System.Action done)
    {
      yield return Fade(0f, 1f, fadeTime);

      var load = SceneManager.LoadSceneAsync(scene);
      while (load != null && !load.isDone) yield return null;

      yield return null; // un frame para que la escena nueva se coloque
      done?.Invoke();
      yield return Fade(1f, 0f, fadeTime);
      Destroy(gameObject);
    }

    private IEnumerator Fade(float from, float to, float time)
    {
      var c = _image.color;
      var t = 0f;

      while (t < 1f)
      {
        t = time > 0f ? Mathf.Min(1f, t + Time.unscaledDeltaTime / time) : 1f;
        c.a = Mathf.Lerp(from, to, t * t * (3f - 2f * t));
        _image.color = c;
        yield return null;
      }
    }
  }
}
