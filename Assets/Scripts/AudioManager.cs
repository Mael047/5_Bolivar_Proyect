using UnityEngine;
using System.Collections;

// Musica de ambiente y de combate (con fundido entre ellas) y efectos sueltos.
// La musica usa el volumen de Musica de Ajustes (GameAudio.Music) y no le
// afecta el de Efectos; playClip si usa el de Efectos.
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Audio Sources")]
    [SerializeField] private AudioSource ambientSource;
    [SerializeField] private AudioSource combatSource;
    [SerializeField] private AudioSource sfxSource;

    [Header("Configuración de Transiciones")]
    [SerializeField] private float fadeDuration = 1.5f;

    private Coroutine activeFade;
    private bool inCombat = false;

    // Volumen de cada pista antes del slider de musica (0..1, lo anima el fundido).
    private float ambientLevel = 1f;
    private float combatLevel = 0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        GameAudio.Changed += ApplyMusicVolume;
    }

    private void OnDisable()
    {
        GameAudio.Changed -= ApplyMusicVolume;
    }

    private void Start()
    {
        // La musica no depende del volumen de efectos.
        ambientSource.ignoreListenerVolume = true;
        combatSource.ignoreListenerVolume = true;

        ambientLevel = 1f;
        combatLevel = 0f;
        ApplyMusicVolume();

        ambientSource.loop = true;
        combatSource.loop = true;

        ambientSource.Play();
        combatSource.Play();
    }

    public void SetCombatState(bool enableCombat)
    {
        if (inCombat == enableCombat) return;

        inCombat = enableCombat;

        if (activeFade != null)
        {
            StopCoroutine(activeFade);
        }

        float targetAmbientVolume = inCombat ? 0f : 1f;
        float targetCombatVolume = inCombat ? 1f : 0f;

        activeFade = StartCoroutine(CrossfadeMusic(targetAmbientVolume, targetCombatVolume));
    }

    public void playClip(AudioClip clip)
    {
        if (clip == null) return;

        sfxSource.PlayOneShot(clip);
    }

    private void ApplyMusicVolume()
    {
        var music = GameAudio.Music;
        if (ambientSource != null) ambientSource.volume = ambientLevel * music;
        if (combatSource != null) combatSource.volume = combatLevel * music;
    }

    private IEnumerator CrossfadeMusic(float targetAmbient, float targetCombat)
    {
        float timer = 0f;
        float startAmbient = ambientLevel;
        float startCombat = combatLevel;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / fadeDuration;

            ambientLevel = Mathf.Lerp(startAmbient, targetAmbient, progress);
            combatLevel = Mathf.Lerp(startCombat, targetCombat, progress);
            ApplyMusicVolume();

            yield return null;
        }

        ambientLevel = targetAmbient;
        combatLevel = targetCombat;
        ApplyMusicVolume();
    }
}
