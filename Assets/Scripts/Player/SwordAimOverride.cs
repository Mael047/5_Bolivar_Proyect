// SwordAimOverride.cs
// Corrige la orientacion de la espada durante ataques concretos del combo sin
// tocar la animacion. En la estocada (Attack3) la animacion de Mixamo deja la
// muneca de forma que la espada queda levantada; este componente la gira para
// que la punta apunte hacia delante del personaje mientras dura el golpe.
//
// Funciona "por direccion", no por angulos: mide hacia donde apunta la hoja
// (de SwordBase a SwordTip) despues de que el Animator coloca la mano, y la
// rota lo justo para que apunte al objetivo. Asi no importa en que eje local
// este modelada la espada ni como gire la muneca en el clip.
//
// Solo toca el SwordHolder (objeto propio, no un hueso animado), y cada frame
// parte de su rotacion original, asi que nunca acumula error.

using UnityEngine;

[DefaultExecutionOrder(100)] // despues del Animator y de PlayerLookAt
public class SwordAimOverride : MonoBehaviour
{
  [System.Serializable]
  public class AimProfile
  {
    [Tooltip("Nombre exacto del estado del Animator (capa 0).")]
    public string stateName = "Attack3";

    [Tooltip("Cuanto se corrige la espada a lo largo del clip. X = tiempo normalizado (0..1), " +
             "Y = peso (0 = animacion original, 1 = apunta totalmente al frente).")]
    public AnimationCurve weightCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.1f, 0f),
        new Keyframe(0.22f, 1f),
        new Keyframe(0.5f, 1f),
        new Keyframe(0.72f, 0f),
        new Keyframe(1f, 0f));

    [Tooltip("Grados. Positivo = la punta baja hacia el suelo, negativo = sube.")]
    [Range(-60f, 60f)] public float pitch = 8f;

    [Tooltip("Grados. Desvia la punta a izquierda (-) o derecha (+) del frente del personaje.")]
    [Range(-60f, 60f)] public float yaw = 0f;

    [Tooltip("Grados. Gira la espada sobre su propio eje (filo horizontal/vertical).")]
    [Range(-180f, 180f)] public float roll = 0f;

    [Header("Posicion")]
    [Tooltip("Gira la espada alrededor de la empunadura (SwordBase) en vez del origen del " +
             "SwordHolder. Suele eliminar casi todo el desplazamiento respecto a la mano.")]
    public bool pivotOnGrip = false;

    [Tooltip("Desplazamiento extra en metros, relativo al personaje: X = derecha, " +
             "Y = arriba, Z = frente. Se aplica con el mismo peso que la rotacion. " +
             "Valores tipicos: 0.01 - 0.1.")]
    public Vector3 positionOffset = Vector3.zero;

    [System.NonSerialized] public int hash;
  }

  [Header("Referencias (se buscan solas si se dejan vacias)")]
  [Tooltip("Objeto que se rota: el contenedor de la espada dentro de la mano.")]
  [SerializeField] private Transform _swordPivot;
  [Tooltip("Marcador en la empunadura.")]
  [SerializeField] private Transform _bladeBase;
  [Tooltip("Marcador en la punta.")]
  [SerializeField] private Transform _bladeTip;
  [SerializeField] private Animator _animator;
  [Tooltip("Define 'el frente'. Vacio = este objeto (el Player).")]
  [SerializeField] private Transform _characterRoot;

  [Header("Ataques")]
  [SerializeField] private AimProfile[] _profiles = { new AimProfile() };

  [Tooltip("Rapidez con que entra/sale la correccion si el ataque se interrumpe (peso por segundo).")]
  [SerializeField, Min(0.1f)] private float _blendSpeed = 10f;

  [Header("Ajuste en vivo")]
  [Tooltip("En Play: fuerza el primer perfil con peso 1 todo el tiempo, para ajustar " +
           "pitch / yaw / roll viendo el resultado sin tener que atacar.")]
  [SerializeField] private bool _preview;

  private Quaternion _baseLocalRotation;
  private Vector3 _baseLocalPosition;
  private float _weight;
  private AimProfile _activeProfile;

  private void Awake()
  {
    if (_characterRoot == null) _characterRoot = transform;
    if (_animator == null) _animator = GetComponentInChildren<Animator>();
    if (_swordPivot == null) _swordPivot = FindDeep(transform, "SwordHolder");
    if (_bladeBase == null) _bladeBase = FindDeep(transform, "SwordBase");
    if (_bladeTip == null) _bladeTip = FindDeep(transform, "SwordTip");

    if (_swordPivot == null)
    {
      Debug.LogWarning("[SwordAimOverride] No se encontro el SwordHolder; el componente queda inactivo.", this);
      enabled = false;
      return;
    }

    _baseLocalRotation = _swordPivot.localRotation;
    _baseLocalPosition = _swordPivot.localPosition;

    foreach (var profile in _profiles)
    {
      if (profile != null) profile.hash = Animator.StringToHash(profile.stateName);
    }
  }

  private void OnDisable()
  {
    if (_swordPivot != null)
    {
      _swordPivot.localRotation = _baseLocalRotation;
      _swordPivot.localPosition = _baseLocalPosition;
    }
    _weight = 0f;
  }

  private void LateUpdate()
  {
    // Siempre se parte de la pose original: nada se acumula entre frames.
    _swordPivot.localRotation = _baseLocalRotation;
    _swordPivot.localPosition = _baseLocalPosition;

    var target = ResolveTargetWeight(out var profile);
    if (profile != null) _activeProfile = profile;

    _weight = Mathf.MoveTowards(_weight, target, _blendSpeed * Time.deltaTime);
    if (_preview) _weight = 1f;

    if (_weight <= 0.0001f || _activeProfile == null)
    {
      return;
    }

    var bladeDir = CurrentBladeDirection();
    if (bladeDir.sqrMagnitude < 0.0001f)
    {
      return;
    }

    var aimDir = DesiredDirection(_activeProfile);

    var aim = Quaternion.FromToRotation(bladeDir, aimDir);
    var roll = Quaternion.AngleAxis(_activeProfile.roll, aimDir);
    var correction = roll * aim;

    var partial = Quaternion.Slerp(Quaternion.identity, correction, _weight);

    // Punto sobre el que gira: el origen del holder o la empunadura.
    var pivotPoint = _activeProfile.pivotOnGrip && _bladeBase != null
        ? _bladeBase.position
        : _swordPivot.position;
    var holderPosition = _swordPivot.position;

    _swordPivot.rotation = partial * _swordPivot.rotation;
    _swordPivot.position = pivotPoint + partial * (holderPosition - pivotPoint)
        + _characterRoot.TransformDirection(_activeProfile.positionOffset) * _weight;
  }

  private float ResolveTargetWeight(out AimProfile profile)
  {
    profile = null;

    if (_preview)
    {
      profile = _profiles.Length > 0 ? _profiles[0] : null;
      return 1f;
    }

    if (_animator == null) return 0f;

    // En una transicion del combo manda el estado de destino.
    if (_animator.IsInTransition(0))
    {
      var next = _animator.GetNextAnimatorStateInfo(0);
      profile = Find(next.shortNameHash);
      if (profile != null)
      {
        return profile.weightCurve.Evaluate(Mathf.Clamp01(next.normalizedTime));
      }
    }

    var current = _animator.GetCurrentAnimatorStateInfo(0);
    profile = Find(current.shortNameHash);
    return profile != null ? profile.weightCurve.Evaluate(Mathf.Clamp01(current.normalizedTime)) : 0f;
  }

  private AimProfile Find(int hash)
  {
    foreach (var profile in _profiles)
    {
      if (profile != null && profile.hash == hash) return profile;
    }

    return null;
  }

  private Vector3 CurrentBladeDirection()
  {
    if (_bladeBase != null && _bladeTip != null)
    {
      return (_bladeTip.position - _bladeBase.position).normalized;
    }

    // Sin marcadores: la hoja del modelo apunta a -Y local de la espada.
    return -_swordPivot.up;
  }

  private Vector3 DesiredDirection(AimProfile profile)
  {
    var forward = _characterRoot.forward;
    forward.y = 0f;
    forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

    var dir = Quaternion.AngleAxis(profile.yaw, Vector3.up) * forward;
    var right = Vector3.Cross(Vector3.up, dir);
    return Quaternion.AngleAxis(profile.pitch, right) * dir;
  }

  private static Transform FindDeep(Transform root, string name)
  {
    foreach (var t in root.GetComponentsInChildren<Transform>(true))
    {
      if (t.name == name) return t;
    }

    return null;
  }
}
