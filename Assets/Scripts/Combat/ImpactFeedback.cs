// ImpactFeedback.cs
// Sensacion de impacto del LADO DEL ATACANTE: lo que el jugador siente al
// conectar un golpe, independiente de como reaccione el enemigo (eso es
// HitReaction). Cada ataque del combo trae su propio ImpactSettings, de modo
// que la estocada final puede sentirse mucho mas pesada que los cortes.
//
// Se reproduce una sola vez por golpe aunque la espada toque a varios enemigos.

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[System.Serializable]
public class ImpactSettings
{
  [Header("Tiempo")]
  [Tooltip("Segundos de congelado EXTRA al conectar (se suman al hit-stop del enemigo).")]
  [Range(0f, 0.3f)] public float extraHitStop = 0f;

  [Tooltip("Tras el congelado, segundos de camara lenta que vuelven poco a poco a velocidad normal. 0 = sin camara lenta.")]
  [Range(0f, 1f)] public float slowMotionDuration = 0f;

  [Tooltip("Velocidad del tiempo al empezar la camara lenta (0.3 = 30 %).")]
  [Range(0.05f, 1f)] public float slowMotionScale = 0.35f;

  [Header("Camara")]
  [Tooltip("Metros que la camara 'recibe' el golpe en la direccion del ataque y vuelve con un resorte.")]
  [Range(0f, 1f)] public float cameraKick = 0.05f;

  [Tooltip("Zoom de impacto: fraccion que se acerca la camara y vuelve (0.08 = 8 %).")]
  [Range(0f, 0.3f)] public float cameraZoomPunch = 0f;

  [Header("Vibracion del mando")]
  [Tooltip("Motor grave (izquierdo) 0..1: sensacion de peso.")]
  [Range(0f, 1f)] public float rumbleLow = 0.15f;
  [Tooltip("Motor agudo (derecho) 0..1: sensacion de golpe seco.")]
  [Range(0f, 1f)] public float rumbleHigh = 0.25f;
  [Range(0f, 1f)] public float rumbleDuration = 0.08f;

  [Header("Sonido")]
  [Tooltip("Sonido adicional del lado del jugador (p. ej. un golpe grave para la estocada).")]
  public AudioClip impactClip;
  [Range(0f, 1f)] public float impactVolume = 1f;
}

public static class ImpactFeedback
{
  private static FeedbackRunner _runner;

  public static void Play(ImpactSettings settings, Vector3 hitPoint, Vector3 hitDirection)
  {
    if (settings == null)
    {
      return;
    }

    // Tiempo: se suma al congelado que ya puso el HitReaction del enemigo y
    // luego entra la camara lenta.
    if (settings.extraHitStop > 0f || settings.slowMotionDuration > 0f)
    {
      HitStop.Extend(settings.extraHitStop, 0.05f, settings.slowMotionDuration, settings.slowMotionScale);
    }

    // Camara.
    var flat = hitDirection;
    flat.y = 0f;
    if (settings.cameraKick > 0f && flat.sqrMagnitude > 0.0001f)
    {
      TopDownCamera.AddKick(flat.normalized, settings.cameraKick);
    }

    if (settings.cameraZoomPunch > 0f)
    {
      TopDownCamera.AddZoomPunch(settings.cameraZoomPunch);
    }

    // Vibracion.
    if (settings.rumbleDuration > 0f && (settings.rumbleLow > 0f || settings.rumbleHigh > 0f))
    {
      Runner().Rumble(settings.rumbleLow, settings.rumbleHigh, settings.rumbleDuration);
    }

    // Sonido.
    if (settings.impactClip != null)
    {
      AudioSource.PlayClipAtPoint(settings.impactClip, hitPoint, settings.impactVolume);
    }
  }

  private static FeedbackRunner Runner()
  {
    if (_runner == null)
    {
      var go = new GameObject("[ImpactFeedback]");
      go.hideFlags = HideFlags.HideInHierarchy;
      Object.DontDestroyOnLoad(go);
      _runner = go.AddComponent<FeedbackRunner>();
    }

    return _runner;
  }

  private sealed class FeedbackRunner : MonoBehaviour
  {
    private Coroutine _rumbleRoutine;
    private float _rumbleEnd;
    private float _low;
    private float _high;

    public void Rumble(float low, float high, float duration)
    {
      var pad = Gamepad.current;
      if (pad == null)
      {
        return;
      }

      // Si ya vibra, se queda con lo mas fuerte y lo mas largo.
      var end = Time.unscaledTime + duration;
      if (_rumbleRoutine != null)
      {
        _low = Mathf.Max(_low, low);
        _high = Mathf.Max(_high, high);
        _rumbleEnd = Mathf.Max(_rumbleEnd, end);
      }
      else
      {
        _low = low;
        _high = high;
        _rumbleEnd = end;
        _rumbleRoutine = StartCoroutine(RumbleRoutine());
      }

      pad.SetMotorSpeeds(_low, _high);
    }

    private IEnumerator RumbleRoutine()
    {
      while (Time.unscaledTime < _rumbleEnd)
      {
        yield return null;
      }

      StopRumble();
    }

    private void StopRumble()
    {
      Gamepad.current?.SetMotorSpeeds(0f, 0f);
      _rumbleRoutine = null;
      _low = _high = 0f;
    }

    private void OnDisable()
    {
      // Nunca dejar el mando vibrando al salir de Play o cambiar de escena.
      if (_rumbleRoutine != null)
      {
        StopRumble();
      }
    }

    private void OnApplicationQuit()
    {
      Gamepad.current?.SetMotorSpeeds(0f, 0f);
    }
  }
}
