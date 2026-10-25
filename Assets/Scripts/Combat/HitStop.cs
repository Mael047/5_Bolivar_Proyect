// HitStop.cs
// "Congelado" breve del tiempo al conectar un golpe (hit-stop / freeze frame),
// opcionalmente seguido de una cola de camara lenta que vuelve poco a poco a
// velocidad normal (para golpes finales).
//
// Varias llamadas seguidas se combinan: se queda con el congelado mas largo, el
// tiempo mas lento y la cola mas larga, en vez de encadenarlas. Extend() suma
// tiempo a un congelado en curso. Solo restaura el timeScale si nadie mas lo
// cambio mientras tanto (por ejemplo un menu de pausa).

using System.Collections;
using UnityEngine;

public static class HitStop
{
  private static HitStopRunner _runner;

  /// <summary>
  /// Congela el juego <paramref name="duration"/> segundos reales a
  /// <paramref name="timeScale"/> (0 = totalmente quieto). Si
  /// <paramref name="tailDuration"/> &gt; 0, despues pasa a
  /// <paramref name="tailScale"/> y acelera hasta velocidad normal.
  /// </summary>
  public static void Play(float duration, float timeScale = 0f, float tailDuration = 0f, float tailScale = 1f)
  {
    if (duration <= 0f && tailDuration <= 0f)
    {
      return;
    }

    Runner().Play(Mathf.Max(0f, duration), Mathf.Clamp01(timeScale), Mathf.Max(0f, tailDuration), Mathf.Clamp01(tailScale), false);
  }

  /// <summary>
  /// Como Play, pero si ya hay un congelado en curso le SUMA
  /// <paramref name="extraDuration"/> en vez de quedarse con el maximo.
  /// </summary>
  public static void Extend(float extraDuration, float timeScale = 0f, float tailDuration = 0f, float tailScale = 1f)
  {
    if (extraDuration <= 0f && tailDuration <= 0f)
    {
      return;
    }

    Runner().Play(Mathf.Max(0f, extraDuration), Mathf.Clamp01(timeScale), Mathf.Max(0f, tailDuration), Mathf.Clamp01(tailScale), true);
  }

  private static HitStopRunner Runner()
  {
    if (_runner == null)
    {
      var go = new GameObject("[HitStop]");
      go.hideFlags = HideFlags.HideInHierarchy;
      Object.DontDestroyOnLoad(go);
      _runner = go.AddComponent<HitStopRunner>();
    }

    return _runner;
  }

  private sealed class HitStopRunner : MonoBehaviour
  {
    private float _freezeEnd;
    private float _freezeScale = 1f;
    private float _tailDuration;
    private float _tailScale = 1f;
    private float _scaleBefore = 1f;
    private float _lastApplied = -1f;
    private Coroutine _routine;

    public void Play(float duration, float timeScale, float tailDuration, float tailScale, bool additive)
    {
      var now = Time.unscaledTime;

      if (_routine == null)
      {
        _scaleBefore = Time.timeScale;
        _freezeEnd = now + duration;
        _freezeScale = timeScale;
        _tailDuration = tailDuration;
        _tailScale = tailScale;
        _routine = StartCoroutine(Routine());
        return;
      }

      // Ya hay uno en curso: se combinan.
      var baseEnd = Mathf.Max(_freezeEnd, now);
      _freezeEnd = additive ? baseEnd + duration : Mathf.Max(_freezeEnd, now + duration);
      _freezeScale = Mathf.Min(_freezeScale, timeScale);
      _tailDuration = Mathf.Max(_tailDuration, tailDuration);
      _tailScale = Mathf.Min(_tailScale, tailScale);
    }

    private bool Apply(float scale)
    {
      // Si otro sistema toco el timeScale (pausa), abandonamos sin pisarlo.
      if (_lastApplied >= 0f && !Mathf.Approximately(Time.timeScale, _lastApplied))
      {
        return false;
      }

      Time.timeScale = scale;
      _lastApplied = scale;
      return true;
    }

    private IEnumerator Routine()
    {
      _lastApplied = -1f;

      // Fase 1: congelado. Se reevalua cada frame porque otro golpe puede
      // alargarlo o hacerlo mas lento.
      while (Time.unscaledTime < _freezeEnd)
      {
        if (!Apply(_freezeScale))
        {
          Finish(false);
          yield break;
        }

        yield return null;
      }

      // Fase 2: camara lenta que acelera hasta la velocidad original.
      var elapsed = 0f;
      while (elapsed < _tailDuration)
      {
        elapsed += Time.unscaledDeltaTime;

        // Si llega otro golpe durante la cola, vuelve a congelar.
        if (Time.unscaledTime < _freezeEnd)
        {
          if (!Apply(_freezeScale))
          {
            Finish(false);
            yield break;
          }

          elapsed = 0f;
          yield return null;
          continue;
        }

        var t = Mathf.Clamp01(elapsed / _tailDuration);
        var eased = t * t; // lenta al principio, recupera rapido al final
        if (!Apply(Mathf.Lerp(_tailScale, _scaleBefore, eased)))
        {
          Finish(false);
          yield break;
        }

        yield return null;
      }

      Finish(true);
    }

    private void Finish(bool restore)
    {
      if (restore && Mathf.Approximately(Time.timeScale, _lastApplied))
      {
        Time.timeScale = _scaleBefore;
      }

      _routine = null;
      _lastApplied = -1f;
      _tailDuration = 0f;
      _tailScale = 1f;
    }

    private void OnDisable()
    {
      if (_routine != null)
      {
        Finish(true);
      }
    }
  }
}
