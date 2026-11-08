// CardPuzzleSetup.cs
// Monta el puzzle de las cartas en Nivel_CASA:
//   - Cada carta de "Cartas" se vuelve pieza agarrable con la mano (collider,
//     Rigidbody, PuzzlePiece, capa Grabbable). La carta se reconoce por su
//     material (Carta_12_oros, Carta_11_bastos...).
//   - Crea "PuzzleCartas" con 6 huecos sobre el paño (fila de izquierda a
//     derecha, o dos filas si no caben) en el orden pedido:
//     12 de oros, 11 de bastos, 10 de espadas, 8 de espadas, 3 de copas, 1 de copas.
//   - Pistas: mensaje al mirar la mesa y una nota (letrero) junto a la mesa.
//   - Recompensas: SOLO el escudo de Peru (Escudo_Peru.prefab) y una llave.
// Se puede volver a ejecutar: rehace los huecos y no duplica componentes.
//
// Menu: Tools > Nivel Casa > Billar: crear puzzle de cartas

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class CardPuzzleSetup
{
  private static readonly string[] Order = { "12_oros", "11_bastos", "10_espadas", "8_espadas", "3_copas", "1_copas" };
  // Solo el escudo (no todo el PuzzleArea del jardin): Escudo.prefab con el
  // material de Peru, igual que "Escudo_Peru" en Jardin_Principal.
  private const string EscudoPrefabGuid = "16b69273e9596e94696cffdfaeb50f52";   // Prefabs/Puzzles/Escudo.prefab
  private const string PeruMaterialGuid = "ac9f89d1782860c40b68086025c44a4e";  // Models/Materials/peru.mat
  private const string EscudoPeruPath = "Assets/Prefabs/Puzzles/Escudo_Peru.prefab";
  private const string GhostMaterialPath = "Assets/Materials/Carta_Hueco.mat";
  private const int GrabbableLayer = 8;

  [MenuItem("Tools/Nivel Casa/Billar: crear puzzle de cartas")]
  private static void Run()
  {
    var scene = EditorSceneManager.GetActiveScene();
    var roots = scene.GetRootGameObjects();
    var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

    var focus = roots.SelectMany(r => r.GetComponentsInChildren<InteractiveCameraFocus>(true)).FirstOrDefault();
    var cardsRoot = all.FirstOrDefault(t => t.name == "Cartas");

    if (focus == null || cardsRoot == null)
    {
      EditorUtility.DisplayDialog("Puzzle de cartas", "Necesito la mesa (con InteractiveCameraFocus) y el objeto 'Cartas' en la escena.", "OK");
      return;
    }

    // ---------- Piezas ----------
    var pieces = new Dictionary<string, PuzzlePiece>();
    Vector2 cardSize = Vector2.zero; // x = ancho, y = largo (metros)
    var cardLongIsZ = true;
    var surfaceY = 0f;

    foreach (var renderer in cardsRoot.GetComponentsInChildren<MeshRenderer>(true))
    {
      var id = CardId(renderer);

      if (id == null || pieces.ContainsKey(id))
      {
        continue;
      }

      var go = renderer.gameObject;
      var filter = go.GetComponent<MeshFilter>();
      var mesh = filter != null ? filter.sharedMesh : null;

      if (mesh != null)
      {
        var box = go.GetComponent<BoxCollider>();
        if (box == null) box = Undo.AddComponent<BoxCollider>(go);
        var size = mesh.bounds.size;
        var minThickness = 0.01f / Mathf.Max(0.0001f, Mathf.Abs(go.transform.lossyScale.y));
        size.y = Mathf.Max(size.y, minThickness);
        box.center = mesh.bounds.center;
        box.size = size;

        if (cardSize == Vector2.zero)
        {
          var s = Vector3.Scale(mesh.bounds.size, go.transform.lossyScale);
          cardLongIsZ = Mathf.Abs(s.z) >= Mathf.Abs(s.x);
          cardSize = cardLongIsZ ? new Vector2(Mathf.Abs(s.x), Mathf.Abs(s.z)) : new Vector2(Mathf.Abs(s.z), Mathf.Abs(s.x));
        }
      }

      var body = go.GetComponent<Rigidbody>();
      if (body == null) body = Undo.AddComponent<Rigidbody>(go);
      body.mass = 0.05f;
      body.isKinematic = true; // quietas hasta que la mano las suelte
      body.interpolation = RigidbodyInterpolation.Interpolate;
      body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

      var piece = go.GetComponent<PuzzlePiece>();
      if (piece == null) piece = Undo.AddComponent<PuzzlePiece>(go);
      var pso = new SerializedObject(piece);
      pso.FindProperty("_pieceId").stringValue = id;
      pso.ApplyModifiedProperties();

      Undo.RecordObject(go, "Puzzle de cartas");
      go.layer = GrabbableLayer;
      pieces[id] = piece;
      surfaceY += renderer.bounds.min.y;
    }

    if (pieces.Count == 0)
    {
      EditorUtility.DisplayDialog("Puzzle de cartas", "No encontre cartas con materiales 'Carta_...' dentro de 'Cartas'.", "OK");
      return;
    }

    surfaceY /= pieces.Count;
    var missing = Order.Where(o => !pieces.ContainsKey(o)).ToList();

    // ---------- Mesa ----------
    var felt = FeltBounds(focus);

    // ---------- Puzzle ----------
    var parent = cardsRoot.parent;
    var puzzleT = all.FirstOrDefault(t => t.name == "PuzzleCartas");

    if (puzzleT == null)
    {
      var go = new GameObject("PuzzleCartas");
      Undo.RegisterCreatedObjectUndo(go, "Puzzle de cartas");
      go.transform.SetParent(parent, false);
      puzzleT = go.transform;
    }

    puzzleT.position = new Vector3(felt.center.x, surfaceY, felt.center.z);
    puzzleT.rotation = Quaternion.identity;

    // Huecos anteriores fuera.
    for (var i = puzzleT.childCount - 1; i >= 0; i--)
    {
      Undo.DestroyObjectImmediate(puzzleT.GetChild(i).gameObject);
    }

    var manager = puzzleT.GetComponent<PuzzleManager>();
    if (manager == null) manager = Undo.AddComponent<PuzzleManager>(puzzleT.gameObject);
    var puzzle = puzzleT.GetComponent<CardOrderPuzzle>();
    if (puzzle == null) puzzle = Undo.AddComponent<CardOrderPuzzle>(puzzleT.gameObject);

    var slots = BuildSlots(puzzleT, felt, surfaceY, cardSize, cardLongIsZ);

    var mso = new SerializedObject(manager);
    SetArray(mso.FindProperty("_slots"), slots.Cast<Object>().ToArray());
    mso.FindProperty("_showGhostPreviews").boolValue = true;
    mso.FindProperty("_rewardPrefab").objectReferenceValue = null;
    mso.ApplyModifiedProperties();

    // Punto del mensaje: borde de arriba del paño (visto desde la camara).
    var messagePoint = new GameObject("PuntoMensaje").transform;
    Undo.RegisterCreatedObjectUndo(messagePoint.gameObject, "Puzzle de cartas");
    messagePoint.SetParent(puzzleT, false);
    messagePoint.position = new Vector3(felt.center.x, surfaceY + 0.02f, felt.center.z + felt.extents.z * 0.55f);

    // Recompensas junto a la mesa, del lado por el que llega la nutria.
    var rewardPoints = BuildRewardPoints(puzzleT, roots, focus, out var toPlayer);

    var cso = new SerializedObject(puzzle);
    cso.FindProperty("_focus").objectReferenceValue = focus;
    SetArray(cso.FindProperty("_slots"), slots.Cast<Object>().ToArray());
    SetArray(cso.FindProperty("_pieces"), pieces.Values.Cast<Object>().ToArray());
    cso.FindProperty("_messageView").objectReferenceValue = FindPrefabWith<WorldMessageView>("DoorMessageUI");
    cso.FindProperty("_messagePoint").objectReferenceValue = messagePoint;
    SetArray(cso.FindProperty("_rewards"), new Object[] { FindEscudoPeru(), FindKey() }.Where(o => o != null).ToArray());
    SetArray(cso.FindProperty("_rewardPoints"), rewardPoints.Cast<Object>().ToArray());
    cso.ApplyModifiedProperties();

    PlaceNote(scene, roots, focus, toPlayer);

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    Selection.activeObject = puzzleT.gameObject;

    var msg = $"Puzzle creado con {pieces.Count} cartas.";
    if (missing.Count > 0) msg += "\n\nNo encontre estas cartas: " + string.Join(", ", missing);
    if (FindEscudoPeru() == null) msg += "\n\nNo encontre Escudo.prefab para crear el escudo de Peru: asignalo en PuzzleCartas > Rewards.";
    if (FindKey() == null) msg += "\n\nNo encontre un prefab de llave (KeyPickup): asignalo en PuzzleCartas > Rewards.";
    EditorUtility.DisplayDialog("Puzzle de cartas", msg, "OK");
  }

  // ---------- Cartas ----------

  /// <summary>"12_oros" a partir del material Carta_12_oros (null si no es una cara de carta).</summary>
  private static string CardId(Renderer renderer)
  {
    foreach (var m in renderer.sharedMaterials)
    {
      if (m == null) continue;
      var n = m.name.Replace(" (Instance)", "").Trim();

      if (n.StartsWith("Carta_") && !n.ToLowerInvariant().Contains("reverso"))
      {
        return n.Substring("Carta_".Length);
      }
    }

    return null;
  }

  private static Bounds FeltBounds(InteractiveCameraFocus focus)
  {
    var so = new SerializedObject(focus);
    var target = so.FindProperty("_frameTarget").objectReferenceValue as Transform;

    if (target == null)
    {
      target = focus.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.ToLowerInvariant() == "mesa") ?? focus.transform;
    }

    var b = new Bounds(target.position, Vector3.zero);
    var found = false;

    foreach (var r in target.GetComponentsInChildren<Renderer>(true))
    {
      if (found) b.Encapsulate(r.bounds); else b = r.bounds;
      found = true;
    }

    // El paño: un poco menos que el tablero (sin las bandas).
    b.extents = new Vector3(b.extents.x * 0.85f, b.extents.y, b.extents.z * 0.85f);
    return b;
  }

  private static List<PuzzleSlot> BuildSlots(Transform parent, Bounds felt, float surfaceY, Vector2 card, bool longIsZ)
  {
    var slots = new List<PuzzleSlot>();
    var spacing = card.x * 1.35f;
    var oneRow = spacing * Order.Length <= felt.size.x * 0.95f;
    var cols = oneRow ? Order.Length : 3;
    var rows = oneRow ? 1 : 2;
    var rowGap = card.y * 1.3f;
    var ghostMat = GhostMaterial();

    for (var i = 0; i < Order.Length; i++)
    {
      var col = i % cols;
      var row = i / cols;
      var x = felt.center.x + (col - (cols - 1) * 0.5f) * spacing;
      var z = felt.center.z + ((rows - 1) * 0.5f - row) * rowGap; // fila 1 arriba

      var go = new GameObject($"Hueco_{i + 1}_{Order[i]}");
      Undo.RegisterCreatedObjectUndo(go, "Puzzle de cartas");
      go.transform.SetParent(parent, false);
      go.transform.position = new Vector3(x, surfaceY, z);
      // Lado largo de la carta hacia arriba de la pantalla (+Z).
      go.transform.rotation = longIsZ ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

      // Hueco visible: silueta de la carta.
      var ghost = GameObject.CreatePrimitive(PrimitiveType.Quad);
      ghost.name = "Silueta";
      Object.DestroyImmediate(ghost.GetComponent<Collider>());
      ghost.transform.SetParent(go.transform, false);
      ghost.transform.localPosition = new Vector3(0f, 0.003f, 0f);
      ghost.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
      ghost.transform.localScale = longIsZ ? new Vector3(card.x, card.y, 1f) : new Vector3(card.y, card.x, 1f);
      var r = ghost.GetComponent<MeshRenderer>();
      r.sharedMaterial = ghostMat;
      r.shadowCastingMode = ShadowCastingMode.Off;

      var slot = go.AddComponent<PuzzleSlot>();
      var so = new SerializedObject(slot);
      so.FindProperty("_acceptedPieceId").stringValue = Order[i];
      so.FindProperty("_snapRadius").floatValue = Mathf.Max(card.y * 0.6f, 0.12f);
      so.FindProperty("_ghostPreview").objectReferenceValue = ghost;
      so.ApplyModifiedProperties();
      slots.Add(slot);
    }

    return slots;
  }

  private static Material GhostMaterial()
  {
    var mat = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);

    if (mat != null)
    {
      return mat;
    }

    var shader = Shader.Find("Universal Render Pipeline/Unlit");
    Directory.CreateDirectory(Path.GetDirectoryName(GhostMaterialPath));
    mat = new Material(shader) { name = "Carta_Hueco" };
    mat.SetFloat("_Surface", 1f);
    mat.SetFloat("_Blend", 0f);
    mat.SetOverrideTag("RenderType", "Transparent");
    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
    mat.SetFloat("_ZWrite", 0f);
    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    mat.renderQueue = (int)RenderQueue.Transparent;
    mat.SetColor("_BaseColor", new Color(1f, 0.97f, 0.85f, 0.35f));
    AssetDatabase.CreateAsset(mat, GhostMaterialPath);
    return mat;
  }

  // ---------- Recompensas y nota ----------

  private static Vector3 ToPlayer(GameObject[] roots, Bounds table)
  {
    var spawn = roots.FirstOrDefault(g => g.name == "PuntoDeInicio");
    var player = roots.Select(g => g.GetComponentInChildren<Player>(true)).FirstOrDefault(p => p != null);
    var from = spawn != null ? spawn.transform.position : player != null ? player.transform.position : table.center - Vector3.forward * 5f;
    var dir = from - table.center;
    dir.y = 0f;
    return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.back;
  }

  private static Bounds TableBounds(InteractiveCameraFocus focus)
  {
    var b = new Bounds(focus.transform.position, Vector3.zero);
    var found = false;

    foreach (var r in focus.GetComponentsInChildren<Renderer>(true))
    {
      if (found) b.Encapsulate(r.bounds); else b = r.bounds;
      found = true;
    }

    return b;
  }

  private static List<Transform> BuildRewardPoints(Transform puzzle, GameObject[] roots, InteractiveCameraFocus focus, out Vector3 toPlayer)
  {
    var table = TableBounds(focus);
    toPlayer = ToPlayer(roots, table);
    var side = Vector3.Cross(Vector3.up, toPlayer);
    var reach = Mathf.Abs(Vector3.Dot(table.extents, new Vector3(Mathf.Abs(toPlayer.x), 0f, Mathf.Abs(toPlayer.z)))) + 0.9f;
    var points = new List<Transform>();

    for (var i = 0; i < 2; i++)
    {
      var p = new GameObject($"Recompensa_{i + 1}").transform;
      Undo.RegisterCreatedObjectUndo(p.gameObject, "Puzzle de cartas");
      p.SetParent(puzzle, false);
      var pos = table.center + toPlayer * reach + side * (i == 0 ? -0.6f : 0.6f);
      pos.y = table.min.y;
      p.position = pos;
      points.Add(p);
    }

    return points;
  }

  private static void PlaceNote(UnityEngine.SceneManagement.Scene scene, GameObject[] roots, InteractiveCameraFocus focus, Vector3 toPlayer)
  {
    if (roots.Any(g => g.name == "Nota_Billar"))
    {
      return;
    }

    var prefab = AssetDatabase.FindAssets("letrero t:Prefab")
      .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
      .FirstOrDefault(g => g != null && g.name == "letrero");

    if (prefab == null)
    {
      return;
    }

    var table = TableBounds(focus);
    var side = Vector3.Cross(Vector3.up, toPlayer);
    var note = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
    Undo.RegisterCreatedObjectUndo(note, "Nota del billar");
    note.name = "Nota_Billar";
    var pos = table.center + toPlayer * (Mathf.Max(table.extents.x, table.extents.z) + 1.4f) + side * 1.6f;
    pos.y = table.min.y;
    note.transform.position = pos;
    note.transform.rotation = Quaternion.LookRotation(toPlayer, Vector3.up);

    var collectible = note.GetComponentInChildren<Collectibles.Collectible>(true);

    if (collectible != null)
    {
      var so = new SerializedObject(collectible);
      so.FindProperty("_kind").enumValueIndex = 1; // Letrero
      so.FindProperty("_title").stringValue = "Nota del abuelo";
      so.FindProperty("_subtitle").stringValue = "Junto a la mesa de juego";
      so.FindProperty("_description").stringValue =
        "Mi abuelo nunca jugaba sin antes ordenar su mano.\n\n" +
        "Siempre empezaba por la carta <b>más alta</b>, el rey, y terminaba con <b>el as</b>.\n\n" +
        "<i>\"Del más grande al más pequeño, como deben ir las cosas.\"</i>";
      so.FindProperty("_promptText").stringValue = "Leer";
      so.FindProperty("_id").stringValue = "Nota_Billar";
      so.ApplyModifiedProperties();
    }
  }

  [MenuItem("Tools/Nivel Casa/Billar: recompensas = solo escudo de Peru + llave")]
  private static void FixRewards()
  {
    var scene = EditorSceneManager.GetActiveScene();
    var puzzle = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CardOrderPuzzle>(true)).FirstOrDefault();

    if (puzzle == null)
    {
      EditorUtility.DisplayDialog("Puzzle de cartas", "No encontre PuzzleCartas en esta escena. Abre Nivel_CASA.", "OK");
      return;
    }

    var shield = FindEscudoPeru();
    var key = FindKey();
    var so = new SerializedObject(puzzle);
    SetArray(so.FindProperty("_rewards"), new Object[] { shield, key }.Where(o => o != null).ToArray());
    so.ApplyModifiedProperties();

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    Selection.activeObject = puzzle.gameObject;

    var shieldText = shield != null ? AssetDatabase.GetAssetPath(shield) : "FALTA el escudo";
    var keyText = key != null ? AssetDatabase.GetAssetPath(key) : "FALTA la llave";
    EditorUtility.DisplayDialog("Puzzle de cartas", "Recompensas:\n- " + shieldText + "\n- " + keyText, "OK");
  }

  /// <summary>
  /// Prefab con SOLO el escudo de Peru (variante de Escudo.prefab con el
  /// material peru y pieza "Peru"). Si ya existe se reutiliza, asi se puede
  /// retocar a mano en el proyecto.
  /// </summary>
  private static GameObject FindEscudoPeru()
  {
    var existing = AssetDatabase.LoadAssetAtPath<GameObject>(EscudoPeruPath);

    if (existing != null)
    {
      return existing;
    }

    var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(EscudoPrefabGuid));
    var peru = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(PeruMaterialGuid));

    if (basePrefab == null)
    {
      return null;
    }

    var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
    instance.name = "Escudo_Peru";

    // La cara del escudo (polySurface2) lleva la bandera de Peru.
    if (peru != null)
    {
      foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
      {
        if (r.gameObject.name == "polySurface2")
        {
          var mats = r.sharedMaterials;
          mats[0] = peru;
          r.sharedMaterials = mats;
        }
      }
    }

    var piece = instance.GetComponent<PuzzlePiece>();

    if (piece != null)
    {
      var pso = new SerializedObject(piece);
      var id = pso.FindProperty("_pieceId");
      if (id != null) id.stringValue = "Peru";
      pso.ApplyModifiedPropertiesWithoutUndo();
    }

    var saved = PrefabUtility.SaveAsPrefabAsset(instance, EscudoPeruPath);
    Object.DestroyImmediate(instance);
    return saved;
  }

  private static GameObject FindKey()
  {
    GameObject fallback = null;

    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

      if (go == null || go.GetComponent<KeyPickup>() == null) continue;
      if (go.name == "Key (1)") return go;
      fallback ??= go;
    }

    return fallback;
  }

  private static T FindPrefabWith<T>(string name) where T : Component
  {
    foreach (var guid in AssetDatabase.FindAssets($"{name} t:Prefab"))
    {
      var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      if (go != null && go.TryGetComponent<T>(out var c)) return c;
    }

    return null;
  }

  private static void SetArray(SerializedProperty prop, Object[] values)
  {
    prop.arraySize = values.Length;

    for (var i = 0; i < values.Length; i++)
    {
      prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
  }
}
