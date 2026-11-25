// CorruptionExempt.cs
// Ponlo en cualquier objeto que no deba verse corrupto dentro de una zona
// corrupta (como las cajas y los letreros). Sus piezas visibles sin collider
// pasan a la capa "SinCorrupcion" al jugar.

using UnityEngine;

[DisallowMultipleComponent]
public class CorruptionExempt : MonoBehaviour
{
}
