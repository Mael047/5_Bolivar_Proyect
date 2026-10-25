// TelekinesisVFX.cs
// Efectos de telequinesis para la mano (todo procedural, sin texturas ni
// modelos externos):
//   Cursor   - nucleo brillante + halo, anillos giratorios sobre el suelo que
//              se cierran al hacer puño, chispas que orbitan y son atraidas al
//              centro, y una luz que tiñe el entorno.
//   Apuntar  - al pasar la mano sobre un objeto agarrable, el anillo salta a
//              rodearlo (se puede agarrar).
//   Sostener - lazo de energia ondulante desde la mano del personaje hasta el
//              objeto, aura de particulas orbitando el objeto y anillo en el
//              suelo bajo el (ayuda a ver donde caera).
//   Agarrar / soltar - estallido de particulas.
// El color cambia con el estado (abierta, cerrada, apuntando, sosteniendo).
//
// Va en el HandSystem (Tools > Telequinesis > Agregar efectos al HandSystem).
// Material: uno aditivo (URP Particles/Unlit). La herramienta del menu crea y
// asigna uno; si falta, se crea en tiempo de ejecucion.

using UnityEngine;
using UnityEngine.Rendering;

namespace NuiGrab
{
  public class TelekinesisVFX : MonoBehaviour
  {
    [Header("Referencias (vacias = se buscan solas)")]
    [SerializeField] private HandTracker _handTracker;
    [SerializeField] private HandGrabManager _grabManager;
    [SerializeField] private Camera _camera;

    [SerializeField, Tooltip("Punto desde el que sale el lazo (p. ej. la mano izquierda del personaje). Vacio = mano izquierda del Animator humanoide o el pecho del jugador.")]
    private Transform _tetherOrigin;

    [SerializeField, Tooltip("Material aditivo para todos los efectos (URP Particles/Unlit, Additive).")]
    private Material _additiveMaterial;

    [Header("Colores")]
    [SerializeField, ColorUsage(true, true)] private Color _openColor = new Color(0.3f, 0.9f, 1f) * 1.6f;
    [SerializeField, ColorUsage(true, true)] private Color _closedColor = new Color(1f, 0.55f, 0.15f) * 1.6f;
    [SerializeField, ColorUsage(true, true)] private Color _hoverColor = new Color(0.4f, 1f, 0.5f) * 1.6f;
    [SerializeField, ColorUsage(true, true)] private Color _holdingColor = new Color(1f, 0.85f, 0.3f) * 1.8f;
    [SerializeField, Range(1f, 30f)] private float _colorSpeed = 10f;

    [Header("Cursor")]
    [SerializeField, Tooltip("Altura (m) del cursor sobre el suelo.")]
    private float _cursorHeight = 0.35f;
    [SerializeField, Range(1f, 40f)] private float _followSpeed = 18f;
    [SerializeField, Min(0f)] private float _coreSize = 0.45f;
    [SerializeField, Min(0f)] private float _haloSize = 1.4f;
    [SerializeField, Min(0.05f), Tooltip("Radio del anillo con la mano abierta.")]
    private float _ringRadiusOpen = 0.65f;
    [SerializeField, Min(0.05f), Tooltip("Radio del anillo con la mano cerrada (se 'aprieta').")]
    private float _ringRadiusClosed = 0.32f;
    [SerializeField, Min(0.005f)] private float _ringWidth = 0.06f;
    [SerializeField] private float _ringSpin = 120f;
    [SerializeField, Min(0f)] private float _lightIntensity = 3f;
    [SerializeField, Min(0f)] private float _lightRange = 3.5f;
    [SerializeField, Min(0f)] private float _motesRate = 45f;

    [Header("Apuntar")]
    [SerializeField, Tooltip("El anillo rodea al objeto agarrable apuntado.")]
    private bool _ringSnapsToTarget = true;

    [Header("Lazo")]
    [SerializeField] private bool _tether = true;
    [SerializeField, Min(0.005f)] private float _tetherWidth = 0.14f;
    [SerializeField, Min(0f), Tooltip("Altura del arco del lazo (m).")]
    private float _tetherArc = 0.8f;
    [SerializeField, Min(0f), Tooltip("Ondulacion del lazo (m).")]
    private float _tetherWobble = 0.1f;
    [SerializeField] private float _tetherScroll = 4f;

    [Header("Aura del objeto sostenido")]
    [SerializeField, Min(0f)] private float _auraRate = 60f;
    [SerializeField, Min(0f)] private int _burstCount = 40;

    // Piezas
    private Transform _root;
    private Transform _cursor;
    private Renderer _core;
    private Renderer _halo;
    private Transform _ringRoot;
    private LineRenderer[] _arcs;
    private LineRenderer _innerRing;
    private Light _light;
    private ParticleSystem _motes;
    private LineRenderer _tetherLine;
    private ParticleSystem _aura;
    private ParticleSystem _burst;
    private Material _lineMat;
    private Material _tetherMat;
    private MaterialPropertyBlock _block;

    // Estado
    private float _visible;
    private Color _color;
    private Vector3 _cursorPos;
    private Vector3 _ringCenter;
    private float _ringRadius;
    private float _ringSpinAngle;
    private bool _wasHolding;
    private Rigidbody _lastHeld;
    private Transform _autoOrigin;
    private bool _autoOriginIsHand;

    private const int ArcCount = 3;
    private const int ArcPoints = 16;
    private const int TetherPoints = 28;

    private void Awake()
    {
      if (_handTracker == null) _handTracker = FindAnyObjectByType<HandTracker>();
      if (_grabManager == null) _grabManager = FindAnyObjectByType<HandGrabManager>();
      if (_camera == null) _camera = Camera.main;

      _block = new MaterialPropertyBlock();
      _color = _openColor;
      Build();
      SetVisibleImmediate(false);
    }

    private void OnDestroy()
    {
      if (_root != null) Destroy(_root.gameObject);
    }

    // ---------- Construccion ----------

    private void Build()
    {
      var baseMat = _additiveMaterial != null ? _additiveMaterial : CreateAdditiveMaterial();
      var soft = SoftCircleTexture();

      var spriteMat = new Material(baseMat) { name = "TK_Soft" };
      spriteMat.SetTexture("_BaseMap", soft);
      spriteMat.mainTexture = soft;

      _lineMat = new Material(baseMat) { name = "TK_Line" };
      _lineMat.SetTexture("_BaseMap", LineTexture());
      _lineMat.mainTexture = _lineMat.GetTexture("_BaseMap");

      _tetherMat = new Material(baseMat) { name = "TK_Tether" };
      var stripes = StripeTexture();
      _tetherMat.SetTexture("_BaseMap", stripes);
      _tetherMat.mainTexture = stripes;

      _root = new GameObject("TelekinesisVFX").transform;
      _cursor = new GameObject("Cursor").transform;
      _cursor.SetParent(_root, false);

      _halo = Quad("Halo", _cursor, spriteMat);
      _core = Quad("Core", _cursor, spriteMat);

      _ringRoot = new GameObject("Ring").transform;
      _ringRoot.SetParent(_root, false);
      _arcs = new LineRenderer[ArcCount];

      for (var i = 0; i < ArcCount; i++)
      {
        _arcs[i] = Line($"Arc{i}", _ringRoot, _lineMat, ArcPoints, _ringWidth);
      }

      _innerRing = Line("InnerRing", _ringRoot, _lineMat, 40, _ringWidth * 0.4f);
      _innerRing.loop = true;

      var lightGo = new GameObject("Light");
      lightGo.transform.SetParent(_cursor, false);
      _light = lightGo.AddComponent<Light>();
      _light.type = LightType.Point;
      _light.shadows = LightShadows.None;

      _motes = Particles("Motes", _cursor, spriteMat, ParticleSystemSimulationSpace.Local);
      ConfigureMotes();

      _tetherLine = Line("Tether", _root, _tetherMat, TetherPoints, _tetherWidth);
      _tetherLine.textureMode = LineTextureMode.Tile;
      _tetherLine.widthCurve = new AnimationCurve(
        new Keyframe(0f, 0.35f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.55f));

      _aura = Particles("Aura", _root, spriteMat, ParticleSystemSimulationSpace.World);
      ConfigureAura();

      _burst = Particles("Burst", _root, spriteMat, ParticleSystemSimulationSpace.World);
      ConfigureBurst();
    }

    private Renderer Quad(string quadName, Transform parent, Material material)
    {
      var go = new GameObject(quadName);
      go.transform.SetParent(parent, false);
      go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
      var renderer = go.AddComponent<MeshRenderer>();
      renderer.sharedMaterial = material;
      renderer.shadowCastingMode = ShadowCastingMode.Off;
      renderer.receiveShadows = false;
      return renderer;
    }

    private static LineRenderer Line(string lineName, Transform parent, Material material, int points, float width)
    {
      var go = new GameObject(lineName);
      go.transform.SetParent(parent, false);
      var line = go.AddComponent<LineRenderer>();
      line.sharedMaterial = material;
      line.positionCount = points;
      line.widthMultiplier = width;
      line.useWorldSpace = true;
      line.numCapVertices = 4;
      line.alignment = LineAlignment.View;
      line.shadowCastingMode = ShadowCastingMode.Off;
      line.receiveShadows = false;
      return line;
    }

    private static ParticleSystem Particles(string psName, Transform parent, Material material, ParticleSystemSimulationSpace space)
    {
      var go = new GameObject(psName);
      go.transform.SetParent(parent, false);
      var ps = go.AddComponent<ParticleSystem>();
      ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

      var main = ps.main;
      main.simulationSpace = space;
      main.playOnAwake = false;
      main.maxParticles = 400;

      var renderer = go.GetComponent<ParticleSystemRenderer>();
      renderer.sharedMaterial = material;
      renderer.renderMode = ParticleSystemRenderMode.Billboard;
      renderer.shadowCastingMode = ShadowCastingMode.Off;
      renderer.receiveShadows = false;

      var col = ps.colorOverLifetime;
      col.enabled = true;
      var fade = new Gradient();
      fade.SetKeys(
        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
      col.color = new ParticleSystem.MinMaxGradient(fade);

      var size = ps.sizeOverLifetime;
      size.enabled = true;
      size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
        new Keyframe(0f, 0.4f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));

      return ps;
    }

    private void ConfigureMotes()
    {
      var main = _motes.main;
      main.loop = true;
      main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
      main.startSpeed = 0f;
      main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);

      var emission = _motes.emission;
      emission.rateOverTime = _motesRate;

      var shape = _motes.shape;
      shape.shapeType = ParticleSystemShapeType.Circle;
      shape.radius = _ringRadiusOpen;
      shape.radiusThickness = 0.2f;
      shape.rotation = new Vector3(90f, 0f, 0f);

      // Giran alrededor del cursor y son atraidas al centro: "succion".
      var velocity = _motes.velocityOverLifetime;
      velocity.enabled = true;
      velocity.space = ParticleSystemSimulationSpace.Local;
      // Unity exige el mismo modo en X/Y/Z (aqui: dos constantes) y en las tres orbitales.
      velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
      velocity.y = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
      velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
      velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
      velocity.orbitalY = new ParticleSystem.MinMaxCurve(3.5f);
      velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
      velocity.radial = new ParticleSystem.MinMaxCurve(-0.9f);
    }

    private void ConfigureAura()
    {
      var main = _aura.main;
      main.loop = true;
      main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
      main.startSpeed = 0f;
      main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);

      var emission = _aura.emission;
      emission.rateOverTime = _auraRate;

      var shape = _aura.shape;
      shape.shapeType = ParticleSystemShapeType.Sphere;
      shape.radius = 0.4f;
      shape.radiusThickness = 0.1f;

      var velocity = _aura.velocityOverLifetime;
      velocity.enabled = true;
      velocity.space = ParticleSystemSimulationSpace.Local;
      velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
      velocity.y = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
      velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
      velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
      velocity.orbitalY = new ParticleSystem.MinMaxCurve(4f);
      velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
      velocity.radial = new ParticleSystem.MinMaxCurve(-0.4f);
    }

    private void ConfigureBurst()
    {
      var main = _burst.main;
      main.loop = false;
      main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
      main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
      main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
      main.gravityModifier = 0.3f;

      var emission = _burst.emission;
      emission.rateOverTime = 0f;

      var shape = _burst.shape;
      shape.shapeType = ParticleSystemShapeType.Sphere;
      shape.radius = 0.2f;
    }

    // ---------- Update ----------

    private void LateUpdate()
    {
      if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
      if (_camera == null) return;

      var dt = Time.deltaTime;
      var menus = Menus.MainMenu.IsOpen || Menus.PauseMenu.IsOpen || Menus.CameraSetup.IsOpen ||
                  Collectibles.CollectibleInspector.IsOpen;
      var detected = !menus && _handTracker != null && _handTracker.isActiveAndEnabled && _handTracker.IsHandDetected;
      var closed = detected && _handTracker.IsHandClosed;
      var holding = _grabManager != null && _grabManager.IsHolding && _grabManager.Held != null && !menus;
      var hovered = !holding && _grabManager != null ? _grabManager.Hovered : null;

      // Visibilidad (fundido)
      var targetVisible = detected || holding ? 1f : 0f;
      _visible = Mathf.MoveTowards(_visible, targetVisible, dt * 4f);

      // Color del estado
      var stateColor = holding ? _holdingColor : hovered != null ? _hoverColor : closed ? _closedColor : _openColor;
      _color = Color.Lerp(_color, stateColor, 1f - Mathf.Exp(-_colorSpeed * dt));

      // Cursor: rayo de la mano contra un plano a _cursorHeight sobre el suelo.
      if (detected)
      {
        var hand = _handTracker.HandViewportPosition;
        var ray = _camera.ScreenPointToRay(new Vector3(hand.x * _camera.pixelWidth, hand.y * _camera.pixelHeight, 0f));
        var groundY = GroundHeightUnder(ray);

        if (new Plane(Vector3.up, new Vector3(0f, groundY + _cursorHeight, 0f)).Raycast(ray, out var d) && d > 0f)
        {
          var target = ray.GetPoint(d);
          _cursorPos = _visible <= 0.01f ? target : Vector3.Lerp(_cursorPos, target, 1f - Mathf.Exp(-_followSpeed * dt));
        }
      }

      _cursor.position = _cursorPos;

      UpdateCore(dt, closed, holding);
      UpdateRing(dt, closed, holding, hovered);
      UpdateTether(holding);
      UpdateAura(holding);
      UpdateTransitions(holding);
      SetEmission(_motes, _visible > 0.5f && !holding, _motesRate);
    }

    private void UpdateCore(float dt, bool closed, bool holding)
    {
      var camRot = _camera.transform.rotation;
      var pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.08f;
      var squeeze = closed || holding ? 0.75f : 1f;

      _core.transform.rotation = camRot;
      _halo.transform.rotation = camRot * Quaternion.Euler(0f, 0f, Time.time * 40f);
      _core.transform.localScale = Vector3.one * (_coreSize * pulse * squeeze * _visible);
      _halo.transform.localScale = Vector3.one * (_haloSize * (2f - pulse) * squeeze * _visible);

      SetColor(_core, Color.Lerp(_color, Color.white, 0.55f), _visible);
      SetColor(_halo, _color, 0.55f * _visible);

      _light.color = _color;
      _light.intensity = _lightIntensity * _visible * (holding ? 1.3f : 1f);
      _light.range = _lightRange;
    }

    private void UpdateRing(float dt, bool closed, bool holding, Rigidbody hovered)
    {
      Vector3 center;
      float radius;

      if (holding)
      {
        // Bajo el objeto sostenido, en el suelo.
        var held = _grabManager.Held;
        var extent = ObjectRadius(held);
        var pos = held.position;
        pos.y = GroundHeightAt(pos, held.transform) + 0.03f;
        center = pos;
        radius = extent * 1.3f;
      }
      else if (hovered != null && _ringSnapsToTarget)
      {
        var extent = ObjectRadius(hovered);
        var pos = hovered.position;
        pos.y = GroundHeightAt(pos, hovered.transform) + 0.03f;
        center = pos;
        radius = extent * 1.5f;
      }
      else
      {
        center = _cursorPos - Vector3.up * (_cursorHeight - 0.03f);
        radius = closed ? _ringRadiusClosed : _ringRadiusOpen;
      }

      var k = 1f - Mathf.Exp(-14f * dt);
      _ringCenter = _visible <= 0.01f ? center : Vector3.Lerp(_ringCenter, center, k);
      _ringRadius = Mathf.Lerp(_ringRadius <= 0f ? radius : _ringRadius, radius, k);
      _ringSpinAngle += _ringSpin * (closed || holding ? 2.2f : 1f) * dt;

      var r = _ringRadius * (0.85f + 0.15f * _visible);

      for (var i = 0; i < ArcCount; i++)
      {
        var start = _ringSpinAngle + i * (360f / ArcCount);
        var span = 360f / ArcCount * 0.62f;
        var line = _arcs[i];

        for (var p = 0; p < ArcPoints; p++)
        {
          var a = (start + span * p / (ArcPoints - 1)) * Mathf.Deg2Rad;
          line.SetPosition(p, _ringCenter + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r);
        }

        SetLineColor(line, _color, _visible);
      }

      var innerR = r * 0.72f;
      var count = _innerRing.positionCount;

      for (var p = 0; p < count; p++)
      {
        var a = (-_ringSpinAngle * 0.6f + 360f * p / count) * Mathf.Deg2Rad;
        _innerRing.SetPosition(p, _ringCenter + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * innerR);
      }

      SetLineColor(_innerRing, _color, 0.45f * _visible);

      var shape = _motes.shape;
      shape.radius = Mathf.Max(0.05f, r);
    }

    private void UpdateTether(bool holding)
    {
      var show = _tether && holding;
      _tetherLine.enabled = show;

      if (!show)
      {
        return;
      }

      var from = TetherOrigin();
      var to = _grabManager.Held.position;
      var mid = (from + to) * 0.5f + Vector3.up * _tetherArc;
      var side = Vector3.Cross((to - from).normalized, Vector3.up);
      if (side.sqrMagnitude < 0.001f) side = Vector3.right;
      side.Normalize();

      for (var i = 0; i < TetherPoints; i++)
      {
        var t = i / (float)(TetherPoints - 1);

        // Curva cuadratica + ondulacion que viaja hacia el objeto.
        var p = (1 - t) * (1 - t) * from + 2 * (1 - t) * t * mid + t * t * to;
        var envelope = Mathf.Sin(t * Mathf.PI);
        var wave = Mathf.Sin(t * 14f - Time.time * 12f) * _tetherWobble * envelope;
        var wave2 = Mathf.Sin(t * 9f + Time.time * 7f) * _tetherWobble * 0.6f * envelope;
        _tetherLine.SetPosition(i, p + side * wave + Vector3.up * wave2);
      }

      _tetherLine.widthMultiplier = _tetherWidth * (0.9f + Mathf.Sin(Time.time * 18f) * 0.1f);
      _tetherMat.mainTextureOffset = new Vector2(-Time.time * _tetherScroll, 0f);
      _tetherMat.SetTextureOffset("_BaseMap", _tetherMat.mainTextureOffset);
      _tetherMat.mainTextureScale = new Vector2(Mathf.Max(1f, Vector3.Distance(from, to) * 1.5f), 1f);
      _tetherMat.SetTextureScale("_BaseMap", _tetherMat.mainTextureScale);
      SetLineColor(_tetherLine, _color, 1f);
    }

    private void UpdateAura(bool holding)
    {
      if (holding)
      {
        var held = _grabManager.Held;
        _aura.transform.position = held.position;
        var shape = _aura.shape;
        shape.radius = ObjectRadius(held) * 1.1f;
        var main = _aura.main;
        main.startColor = _color;
      }

      SetEmission(_aura, holding, _auraRate);

      var motesMain = _motes.main;
      motesMain.startColor = _color;
    }

    private void UpdateTransitions(bool holding)
    {
      if (holding && !_wasHolding)
      {
        _lastHeld = _grabManager.Held;
        Burst(_lastHeld.position, _holdingColor);
      }
      else if (!holding && _wasHolding && _lastHeld != null)
      {
        Burst(_lastHeld.position, _color);
      }

      _wasHolding = holding;
    }

    private void Burst(Vector3 position, Color color)
    {
      if (_burstCount <= 0) return;

      _burst.transform.position = position;
      var main = _burst.main;
      main.startColor = color;
      _burst.Emit(_burstCount);
    }

    // ---------- Utilidades ----------

    private Vector3 TetherOrigin()
    {
      if (_tetherOrigin != null) return _tetherOrigin.position;

      if (_autoOrigin == null)
      {
        var player = FindAnyObjectByType<global::Player>();

        if (player != null)
        {
          var animator = player.GetComponentInChildren<Animator>();
          _autoOrigin = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
          _autoOriginIsHand = _autoOrigin != null;
          if (_autoOrigin == null) _autoOrigin = player.transform;
        }
      }

      if (_autoOrigin == null) return _cursorPos;

      // Si no hay hueso de la mano, sale a la altura del pecho.
      return _autoOriginIsHand ? _autoOrigin.position : _autoOrigin.position + Vector3.up * 1.1f;
    }

    private static float ObjectRadius(Rigidbody body)
    {
      var collider = body.GetComponent<Collider>();
      if (collider == null) return 0.3f;
      var e = collider.bounds.extents;
      return Mathf.Max(0.15f, Mathf.Max(e.x, e.z));
    }

    // Capas que no cuentan como suelo: Grabbable (8), Player (3) y Enemy (10).
    private const int NotGround = ~((1 << 8) | (1 << 3) | (1 << 10));

    /// <summary>Altura del suelo bajo el rayo de la mano (el primer obstaculo que no sea agarrable).</summary>
    private float GroundHeightUnder(Ray ray)
    {
      if (Physics.Raycast(ray, out var hit, _camera.farClipPlane, NotGround, QueryTriggerInteraction.Ignore))
      {
        return hit.point.y;
      }

      return 0f;
    }

    private static float GroundHeightAt(Vector3 position, Transform ignore)
    {
      var origin = position + Vector3.up * 0.1f;
      var hits = Physics.RaycastAll(origin, Vector3.down, 30f, NotGround, QueryTriggerInteraction.Ignore);
      var best = float.NegativeInfinity;

      foreach (var h in hits)
      {
        if (ignore != null && h.transform.IsChildOf(ignore)) continue;
        if (h.point.y > best) best = h.point.y;
      }

      return float.IsNegativeInfinity(best) ? position.y - 0.5f : best;
    }

    private static void SetEmission(ParticleSystem ps, bool on, float rate)
    {
      var emission = ps.emission;
      emission.rateOverTime = on ? rate : 0f;

      if (on && !ps.isPlaying) ps.Play();
    }

    private void SetColor(Renderer renderer, Color color, float alpha)
    {
      color.a = Mathf.Clamp01(alpha);
      renderer.GetPropertyBlock(_block);
      _block.SetColor("_BaseColor", color);
      _block.SetColor("_Color", color);
      renderer.SetPropertyBlock(_block);
    }

    private static void SetLineColor(LineRenderer line, Color color, float alpha)
    {
      var start = color;
      start.a = Mathf.Clamp01(alpha);
      var end = start;
      line.startColor = start;
      line.endColor = end;
    }

    private void SetVisibleImmediate(bool visible)
    {
      _visible = visible ? 1f : 0f;
      _tetherLine.enabled = false;
    }

    // ---------- Recursos procedurales ----------

    private static Material CreateAdditiveMaterial()
    {
      var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
      if (shader == null) shader = Shader.Find("Sprites/Default");

      var mat = new Material(shader) { name = "TK_Additive" };
      ConfigureAdditive(mat);
      return mat;
    }

    /// <summary>Configura un material URP Particles/Unlit como transparente aditivo.</summary>
    public static void ConfigureAdditive(Material mat)
    {
      mat.SetFloat("_Surface", 1f);
      mat.SetFloat("_Blend", 2f);
      mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
      mat.SetFloat("_DstBlend", (float)BlendMode.One);
      mat.SetFloat("_ZWrite", 0f);
      mat.SetFloat("_Cull", (float)CullMode.Off);
      mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
      mat.EnableKeyword("_BLENDMODE_ADD");
      mat.renderQueue = (int)RenderQueue.Transparent;
    }

    private static Texture2D SoftCircleTexture()
    {
      const int size = 64;
      var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "TK_SoftCircle" };
      var pixels = new Color32[size * size];

      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var dx = (x + 0.5f) / size * 2f - 1f;
          var dy = (y + 0.5f) / size * 2f - 1f;
          var d = Mathf.Sqrt(dx * dx + dy * dy);
          var a = Mathf.Clamp01(1f - d);
          a = a * a * (3f - 2f * a);
          a = Mathf.Pow(a, 1.6f);
          pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
      }

      tex.SetPixels32(pixels);
      tex.Apply();
      return tex;
    }

    /// <summary>Linea con centro brillante y bordes suaves (para anillos).</summary>
    private static Texture2D LineTexture()
    {
      const int w = 4, h = 32;
      var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "TK_Line" };
      var pixels = new Color32[w * h];

      for (var y = 0; y < h; y++)
      {
        var v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
        var a = Mathf.Pow(Mathf.Clamp01(1f - v), 2f);

        for (var x = 0; x < w; x++)
        {
          pixels[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
      }

      tex.SetPixels32(pixels);
      tex.Apply();
      return tex;
    }

    /// <summary>Bandas que se desplazan por el lazo (energia fluyendo).</summary>
    private static Texture2D StripeTexture()
    {
      const int w = 64, h = 32;
      var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, name = "TK_Stripes" };
      var pixels = new Color32[w * h];

      for (var y = 0; y < h; y++)
      {
        var v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
        var edge = Mathf.Pow(Mathf.Clamp01(1f - v), 1.5f);

        for (var x = 0; x < w; x++)
        {
          var u = x / (float)w;
          var band = 0.45f + 0.55f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2f * 2f), 3f);
          pixels[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(edge * band) * 255));
        }
      }

      tex.SetPixels32(pixels);
      tex.Apply();
      return tex;
    }
  }
}
