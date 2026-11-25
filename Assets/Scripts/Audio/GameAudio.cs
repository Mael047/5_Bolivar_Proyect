// GameAudio.cs
// Volumen de musica y de efectos (0..1), guardado entre partidas.
//   - Efectos: todo el sonido del juego (AudioListener.volume).
//   - Musica: las fuentes de musica (AudioManager y las que tengan
//     MusicSource) ignoran el volumen de efectos y usan este.
// Lo cambian los sliders de Ajustes del menu de pausa.

using System;
using UnityEngine;

public static class GameAudio
{
  private const string MusicKey = "audio_musica";
  private const string SfxKey = "audio_efectos";

  /// <summary>Se avisa al cambiar cualquier volumen.</summary>
  public static event Action Changed;

  public static float Music
  {
    get => PlayerPrefs.GetFloat(MusicKey, 1f);
    set
    {
      PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value));
      Apply();
    }
  }

  public static float Sfx
  {
    get => PlayerPrefs.GetFloat(SfxKey, 1f);
    set
    {
      PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value));
      Apply();
    }
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  private static void Init()
  {
    Changed = null;
    AudioListener.volume = Sfx;
  }

  private static void Apply()
  {
    AudioListener.volume = Sfx;
    Changed?.Invoke();
  }

  /// <summary>Guarda en disco (al cerrar Ajustes).</summary>
  public static void Save() => PlayerPrefs.Save();
}
