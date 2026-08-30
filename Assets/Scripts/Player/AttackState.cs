using Unity.VisualScripting;
using UnityEngine;

public class AttackState : State
{
    float timePassed;
    float clipLength;
    float clipSpeed;
    bool attack;


    public AttackState(Player _character, StateMachine _stateMachine) : base(_character, _stateMachine)
    {
       player = _character;
    }
}
