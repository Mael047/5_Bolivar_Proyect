// CardSlotAlign.cs
// Tools > Nivel Casa > Billar: ajustar huecos a las marcas
//
// La carta encaja con su PUNTO DE ORIGEN (pivote) en el hueco, no con su
// centro. Si el pivote no esta en el centro de la carta, al agrandarla el
// desfase crece y la carta queda corrida respecto a la marca.
//
// Esta herramienta mueve cada hueco (Hueco_N_id) para que su carta quede
// centrada justo sobre su marca (el fantasma/quad del hueco), con el tamaño
// actual de las cartas. Las marcas no se mueven. Tambien ajusta el radio de
// encaje al tamaño de la carta.
//
// Uso: coloca/agranda las marcas donde las quieras y ejecuta el menu.
// Si cambias otra vez el tamaño de las cartas, vuelve a ejecutarlo.

using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CardSlotAlign
{
  [MenuItem("Tools/Nivel Casa/Billar: ajustar huecos a las marcas")]
  private static void Run()
  {
    if (EditorApplication.isPlaying)
    {
      EditorUtility.DisplayDialog("Cartas", "Sal del modo Play primero.", "OK");
      return;
    }

    var scene = EditorSceneManager.GetActiveScene();
    var puzzle = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CardOrderPuzzle>(true)).FirstOrDefault();

    if (puzzle == null)
    {
      EditorUtility.DisplayDialog("Cartas", "No encontre PuzzleCartas en esta escena. Abre Nivel_CASA.", "OK");
      return;
    }

    var pieces = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PuzzlePiece>(true)).ToArray();
    var slots = puzzle.GetComponentsInChildren<PuzzleSlot>(true);
    var done = 0;
    var missing = "";

    foreach (var slot in slots)
    {
      var piece = pieces.FirstOrDefault(p => p.PieceId == slot.AcceptedPieceId);
      var cardRenderer = piece != null ? piece.GetComponentInChildren<Renderer>() : null;

      if (cardRenderer == null)
      {
        missing += "\n- " + slot.name;
        continue;
      }

      var slotSo = new SerializedObject(slot);
      var ghostProp = slotSo.FindProperty("_ghostPreview");
      var ghost = ghostProp.objectReferenceValue as GameObject;

      // Donde debe quedar el centro de la carta: el centro de su marca.
      var ghostRenderer = ghost != null ? ghost.GetComponentInChildren<Renderer>(true) : null;
      var target = ghostRenderer != null ? ghostRenderer.bounds.center : slot.transform.position;

      // Desfase entre el pivote de la carta y su centro, en el espacio de la carta.
      var card = piece.transform;
      var localOffset = Quaternion.Inverse(card.rotation) * (cardRenderer.bounds.center - card.position);

      // La carta termina con la rotacion del hueco: el pivote va al centro - desfase.
      var newPosition = target - slot.transform.rotation * localOffset;

      // Mover el hueco sin mover su marca (la marca es hija del hueco).
      Vector3 ghostPos = default;
      Quaternion ghostRot = default;

      if (ghost != null)
      {
        ghostPos = ghost.transform.position;
        ghostRot = ghost.transform.rotation;
      }

      Undo.RecordObject(slot.transform, "Ajustar huecos");
      slot.transform.position = newPosition;

      if (ghost != null && ghost.transform.IsChildOf(slot.transform))
      {
        Undo.RecordObject(ghost.transform, "Ajustar huecos");
        ghost.transform.SetPositionAndRotation(ghostPos, ghostRot);
      }

      // Radio de encaje acorde al tamaño de la carta.
      var size = cardRenderer.bounds.size;
      slotSo.FindProperty("_snapRadius").floatValue = Mathf.Max(0.15f, Mathf.Max(size.x, size.z) * 0.6f);
      slotSo.ApplyModifiedProperties();
      done++;
    }

    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);

    EditorUtility.DisplayDialog("Cartas",
      "Huecos ajustados: " + done + (missing.Length > 0 ? "\nSin carta encontrada:" + missing : ""), "OK");
  }
}
