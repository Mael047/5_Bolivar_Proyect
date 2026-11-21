// MusicSource.cs
// Ponlo en cualquier AudioSource que sea musica (fuera del AudioManager):
// usa el volumen de Musica de Ajustes en vez del de Efectos.
// "Volume" es el volumen base; el slider lo multiplica.

using UnityEngine;

[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class MusicSource : MonoBehaviour
{
  [SerializeField, Range(0f, 1f)] private float _volume = 1f;

  private AudioSource _source;

  /// <summary>Volumen base (0..1) antes del slider de musica.</summary>
  public float Volume
  {
    get => _volume;
    set
    {
      _volume = Mathf.Clamp01(value);
      Apply();
    }
  }

  private void Awake()
  {
    _source = GetComponent<AudioSource>();
    _source.ignoreListenerVolume = true;
  }

  private void OnEnable()
  {
    GameAudio.Changed += Apply;
    Apply();
  }

  private void OnDisable() => GameAudio.Changed -= Apply;

  private void Apply()
  {
    if (_source != null)
    {
      _source.volume = _volume * GameAudio.Music;
    }
  }
}
