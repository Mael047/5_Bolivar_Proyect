// StatusPage.cs
// Pantalla "Estado" del menu de pausa: modelo del personaje (PlayerPortrait en
// modo Animacion Propia), casillas de equipamiento (espada / escudo, segun
// PlayerEquipment) y cuadricula de coleccionables recogidos. Al seleccionar
// cualquier casilla llena se vuelve a abrir su vista de inspeccion encima del
// menu.
//
// Va en la pantalla de Estado (junto a PauseMenuPage). La cuadricula se crea
// duplicando la casilla plantilla hasta 'Min Slots' (crece si hay mas).

using System.Collections.Generic;
using Collectibles;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Menus
{
  [RequireComponent(typeof(PauseMenuPage))]
  public class StatusPage : MonoBehaviour
  {
    [Header("Equipamiento")]
    [SerializeField, Tooltip("Casillas de equipamiento (cada una con su 'Equipment' = Espada / Escudo).")]
    private CollectibleSlot[] _equipmentSlots;

    [Header("Coleccionables")]
    [SerializeField, Tooltip("Casilla de ejemplo; se duplica en su mismo contenedor.")]
    private CollectibleSlot _slotTemplate;

    [SerializeField, Min(1), Tooltip("Casillas visibles aunque esten vacias (4 x 2 = 8).")]
    private int _minSlots = 8;

    private readonly List<CollectibleSlot> _slots = new List<CollectibleSlot>();
    private PauseMenuPage _page;
    private PlayerEquipment _equipment;
    private CollectibleSlot _reopenFrom;
    private bool _inspecting;

    private void Awake()
    {
      _page = GetComponent<PauseMenuPage>();

      if (_slotTemplate != null)
      {
        _slots.Add(_slotTemplate);
        Hook(_slotTemplate);
      }

      if (_equipmentSlots != null)
      {
        foreach (var slot in _equipmentSlots)
        {
          if (slot != null) Hook(slot);
        }
      }

      CollectibleJournal.Changed += Refresh;
    }

    private void OnDestroy()
    {
      CollectibleJournal.Changed -= Refresh;

      if (_equipment != null)
      {
        _equipment.Changed -= Refresh;
      }
    }

    private void OnEnable()
    {
      Refresh();
    }

    private void Update()
    {
      // Al cerrar la inspeccion, devuelve el foco a la casilla de la que se abrio.
      if (_inspecting && !CollectibleInspector.IsOpen)
      {
        _inspecting = false;

        if (_reopenFrom != null && EventSystem.current != null)
        {
          EventSystem.current.SetSelectedGameObject(_reopenFrom.gameObject);
        }
      }
    }

    private void Hook(CollectibleSlot slot)
    {
      slot.Button.onClick.RemoveAllListeners();
      slot.Button.onClick.AddListener(() => OnSlotClicked(slot));
    }

    private void Refresh()
    {
      RefreshEquipment();

      if (_slotTemplate == null)
      {
        return;
      }

      var entries = CollectibleJournal.Entries;
      var needed = Mathf.Max(_minSlots, entries.Count);

      while (_slots.Count < needed)
      {
        var slot = Instantiate(_slotTemplate, _slotTemplate.transform.parent);
        slot.name = $"{_slotTemplate.name} ({_slots.Count + 1})";
        Hook(slot);
        _slots.Add(slot);
      }

      for (var i = 0; i < _slots.Count; i++)
      {
        _slots[i].Set(i < entries.Count ? entries[i] : null);
      }

      // Con mando, la pantalla empieza en la primera casilla con algo.
      _page.firstSelected = FirstFilled();
    }

    private void RefreshEquipment()
    {
      if (_equipment == null)
      {
        _equipment = PlayerEquipment.Instance != null
          ? PlayerEquipment.Instance
          : FindAnyObjectByType<PlayerEquipment>();

        if (_equipment != null)
        {
          _equipment.Changed += Refresh;
        }
      }

      if (_equipmentSlots == null)
      {
        return;
      }

      foreach (var slot in _equipmentSlots)
      {
        if (slot != null)
        {
          var item = _equipment != null ? _equipment.ItemIn(slot.equipment) : null;
          slot.Set(item);
        }
      }
    }

    private UnityEngine.UI.Selectable FirstFilled()
    {
      if (_equipmentSlots != null)
      {
        foreach (var slot in _equipmentSlots)
        {
          if (slot != null && slot.Collectible != null) return slot.Button;
        }
      }

      foreach (var slot in _slots)
      {
        if (slot.Collectible != null) return slot.Button;
      }

      return null;
    }

    private void OnSlotClicked(CollectibleSlot slot)
    {
      if (slot.Collectible == null || CollectibleInspector.IsOpen)
      {
        return;
      }

      _reopenFrom = slot;
      _inspecting = true;
      CollectibleInspector.Instance.Open(slot.Collectible, null, keepGameState: true);
    }
  }
}
