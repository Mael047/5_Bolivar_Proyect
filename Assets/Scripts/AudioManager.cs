using UnityEngine;
using System.Collections;

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

    private void Start()
    {
        ambientSource.volume = 1f;
        combatSource.volume = 0f;

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

    private IEnumerator CrossfadeMusic(float targetAmbient, float targetCombat)
    {
        float timer = 0f;
        float startAmbient = ambientSource.volume;
        float startCombat = combatSource.volume;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / fadeDuration;

            ambientSource.volume = Mathf.Lerp(startAmbient, targetAmbient, progress);
            combatSource.volume = Mathf.Lerp(startCombat, targetCombat, progress);

            yield return null;
        }

        ambientSource.volume = targetAmbient;
        combatSource.volume = targetCombat;
    }
}
