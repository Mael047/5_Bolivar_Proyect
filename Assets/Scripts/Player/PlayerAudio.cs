// PlayerAudio.cs
// Sonidos del jugador, asignables desde el Inspector:
//   - Espadazos: uno al azar en cada golpe (nunca el mismo dos veces seguidas).
//     Momento configurable: al empezar el golpe o cuando la espada corta.
//   - Pasos: mientras el personaje se mueve, a un ritmo que se acelera al correr.
//   - Bloqueo: uno al azar cuando el escudo detiene un golpe.
// Cada grupo tiene volumen y variacion de tono para que no suene repetido.
//
// Recorte automatico de silencio: muchos efectos traen silencio al principio
// (los espadazos tenian entre 0,1 y 0,8 s), lo que hace que suenen tarde.
// Al arrancar se analiza cada clip y se reproduce desde donde empieza a sonar.
// Va en el Player (junto a Player).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[RequireComponent(typeof(Player))]
public class PlayerAudio : MonoBehaviour
{
  public enum SwingMoment
  {
    [Tooltip("En cuanto empieza cada golpe del combo (respuesta inmediata al pulsar).")]
    AlEmpezarElGolpe,
    [Tooltip("Cuando la espada empieza a cortar (evento AE_StartAttack del clip).")]
    AlCortar
  }

  [System.Serializable]
  public class SoundGroup
  {
    [Tooltip("Clips posibles; se elige uno al azar cada vez.")]
    public AudioClip[] clips;

    [Range(0f, 1f)] public float volume = 0.8f;

    [Tooltip("Variacion aleatoria del tono (+/-). 0.05 = 5 %.")]
    [Range(0f, 0.3f)] public float pitchVariation = 0.06f;

    [System.NonSerialized] public int last = -1;

    public bool HasClips => clips != null && clips.Length > 0;

    /// <summary>Clip al azar sin repetir el anterior (si hay mas de uno).</summary>
    public AudioClip Pick()
    {
      if (!HasClips) return null;
      if (clips.Length == 1) return clips[0];

      int index;

      if (last < 0 || last >= clips.Length)
      {
        index = Random.Range(0, clips.Length);
      }
      else
      {
        // Elige entre los demas: salta el anterior.
        index = Random.Range(0, clips.Length - 1);
        if (index >= last) index++;
      }

      last = index;
      return clips[index];
    }
  }

  [Header("Espadazos")]
  [SerializeField] private SoundGroup _swings = new SoundGroup { volume = 0.8f, pitchVariation = 0.08f };

  [SerializeField, Tooltip("Cuando suena el espadazo.")]
  private SwingMoment _swingMoment = SwingMoment.AlEmpezarElGolpe;

  [SerializeField, Range(0f, 0.5f), Tooltip("Retraso extra (s) para sincronizar con la animacion si suena antes de tiempo.")]
  private float _swingDelay = 0f;

  [Header("Pasos")]
  [SerializeField] private SoundGroup _footsteps = new SoundGroup { volume = 0.45f, pitchVariation = 0.1f };

  [SerializeField, Min(0.05f), Tooltip("Segundos entre pasos caminando despacio.")]
  private float _walkStepInterval = 0.55f;

  [SerializeField, Min(0.05f), Tooltip("Segundos entre pasos corriendo.")]
  private float _runStepInterval = 0.3f;

  [SerializeField, Min(0f), Tooltip("Velocidad (m/s) a partir de la que suenan pasos.")]
  private float _minStepSpeed = 0.6f;

  [SerializeField, Min(0.1f), Tooltip("Velocidad (m/s) que se considera correr (ritmo mas rapido).")]
  private float _runSpeed = 6f;

  [Header("Golpe bloqueado con el escudo")]
  [SerializeField] private SoundGroup _blocks = new SoundGroup { volume = 0.9f, pitchVariation = 0.05f };

  [Header("Recorte de silencio")]
  [SerializeField, Tooltip("Empezar cada clip donde realmente suena (salta el silencio inicial).")]
  private bool _trimLeadingSilence = true;

  [SerializeField, Range(0.01f, 0.5f), Tooltip("Que tan fuerte (respecto al pico del clip) se considera 'ya suena'.")]
  private float _silenceThreshold = 0.08f;

  [SerializeField, Range(0f, 0.1f), Tooltip("Segundos que se conservan antes del inicio detectado (para no cortar el ataque del sonido).")]
  private float _preRoll = 0.02f;

  [Header("Salida")]
  [SerializeField, Tooltip("Grupo del Audio Mixer para estos sonidos (opcional, p. ej. SFX).")]
  private AudioMixerGroup _output;

  [SerializeField, Range(0f, 1f), Tooltip("0 = sonido 2D (igual en ambos oidos), 1 = 3D posicionado en el personaje.")]
  private float _spatialBlend = 0f;

  private Player _player;
  private Rigidbody _rb;
  private AudioSource _swingSource;
  private AudioSource _blockSource;
  private AudioSource _stepSource;
  private float _stepTimer;
  private float _pendingSwing = -1f;
  private readonly Dictionary<AudioClip, float> _startOffsets = new Dictionary<AudioClip, float>();

  private void Awake()
  {
    _player = GetComponent<Player>();
    _rb = GetComponent<Rigidbody>();
    _swingSource = CreateSource("SwingAudio");
    _blockSource = CreateSource("BlockAudio");
    _stepSource = CreateSource("StepAudio");

    Prepare(_swings);
    Prepare(_footsteps);
    Prepare(_blocks);
  }

  private void OnEnable()
  {
    _player.AttackStarted += OnAttackStarted;
    _player.AttackSwing += OnAttackSwing;
    _player.BlockedHit += PlayBlock;
  }

  private void OnDisable()
  {
    _player.AttackStarted -= OnAttackStarted;
    _player.AttackSwing -= OnAttackSwing;
    _player.BlockedHit -= PlayBlock;
  }

  private AudioSource CreateSource(string sourceName)
  {
    var go = new GameObject(sourceName);
    go.transform.SetParent(transform, false);
    var source = go.AddComponent<AudioSource>();
    source.playOnAwake = false;
    source.spatialBlend = _spatialBlend;
    source.outputAudioMixerGroup = _output;
    return source;
  }

  // ---------- Recorte de silencio ----------

  /// <summary>Carga los clips ya (sin esperar al primer uso) y calcula donde empieza cada uno.</summary>
  private void Prepare(SoundGroup group)
  {
    if (!group.HasClips)
    {
      return;
    }

    foreach (var clip in group.clips)
    {
      if (clip == null || _startOffsets.ContainsKey(clip))
      {
        continue;
      }

      if (clip.loadState != AudioDataLoadState.Loaded)
      {
        clip.LoadAudioData();
      }

      _startOffsets[clip] = _trimLeadingSilence ? FindSoundStart(clip) : 0f;
    }
  }

  private float FindSoundStart(AudioClip clip)
  {
    var channels = Mathf.Max(1, clip.channels);
    var frames = clip.samples;

    if (frames <= 0)
    {
      return 0f;
    }

    var data = new float[frames * channels];

    // Solo funciona con clips descomprimidos al cargar (Load Type = Decompress On Load).
    if (!clip.GetData(data, 0))
    {
      return 0f;
    }

    var peak = 0f;

    for (var i = 0; i < data.Length; i++)
    {
      var v = Mathf.Abs(data[i]);
      if (v > peak) peak = v;
    }

    if (peak <= 0f)
    {
      return 0f;
    }

    var threshold = peak * _silenceThreshold;

    for (var i = 0; i < data.Length; i++)
    {
      if (Mathf.Abs(data[i]) >= threshold)
      {
        var seconds = (float)(i / channels) / clip.frequency;
        return Mathf.Max(0f, seconds - _preRoll);
      }
    }

    return 0f;
  }

  // ---------- Espadazos ----------

  private void OnAttackStarted()
  {
    if (_swingMoment == SwingMoment.AlEmpezarElGolpe) QueueSwing();
  }

  private void OnAttackSwing()
  {
    if (_swingMoment == SwingMoment.AlCortar) QueueSwing();
  }

  private void QueueSwing()
  {
    if (_swingDelay <= 0f)
    {
      Play(_swingSource, _swings);
    }
    else
    {
      _pendingSwing = _swingDelay;
    }
  }

  private void PlayBlock() => Play(_blockSource, _blocks);

  /// <summary>Tambien se puede llamar desde un evento de animacion (p. ej. en el frame del pie).</summary>
  public void AE_Footstep() => Play(_stepSource, _footsteps);

  private void Update()
  {
    if (_pendingSwing >= 0f)
    {
      _pendingSwing -= Time.deltaTime;

      if (_pendingSwing < 0f)
      {
        Play(_swingSource, _swings);
      }
    }

    UpdateFootsteps();
  }

  // ---------- Pasos ----------

  private void UpdateFootsteps()
  {
    if (!_footsteps.HasClips || _rb == null || Time.timeScale <= 0f)
    {
      return;
    }

    var velocity = _rb.linearVelocity;
    velocity.y = 0f;
    var speed = velocity.magnitude;

    // Quieto o esquivando (la esquiva es un deslizamiento, no pasos).
    if (speed < _minStepSpeed || (_player.IsBusy && !_player.IsAttacking))
    {
      // El primer paso al arrancar suena enseguida.
      _stepTimer = Mathf.Min(_stepTimer, 0.1f);
      return;
    }

    _stepTimer -= Time.deltaTime;

    if (_stepTimer <= 0f)
    {
      Play(_stepSource, _footsteps);
      var run01 = Mathf.InverseLerp(_minStepSpeed, _runSpeed, speed);
      _stepTimer = Mathf.Lerp(_walkStepInterval, _runStepInterval, run01);
    }
  }

  // ---------- Reproduccion ----------

  private void Play(AudioSource source, SoundGroup group)
  {
    var clip = group.Pick();

    if (clip == null || source == null)
    {
      return;
    }

    if (!_startOffsets.TryGetValue(clip, out var offset))
    {
      Prepare(group);
      _startOffsets.TryGetValue(clip, out offset);
    }

    // Play (no PlayOneShot) para poder empezar en 'offset': cada grupo tiene
    // su propia fuente y un sonido nuevo corta la cola del anterior del mismo grupo.
    source.Stop();
    source.clip = clip;
    source.volume = group.volume;
    source.pitch = 1f + Random.Range(-group.pitchVariation, group.pitchVariation);
    source.time = Mathf.Clamp(offset, 0f, Mathf.Max(0f, clip.length - 0.01f));
    source.Play();
  }
}
