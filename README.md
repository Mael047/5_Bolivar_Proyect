# Documentación de Arquitectura y Diseño - Proyecto Bolívar

## 1. Diagrama de Clases (UML)

```mermaid
classDiagram
    class MonoBehaviour {
        <<UnityEngine>>
    }

    class IDamageable {
        <<Interface>>
        +TakeDamage(int damageAmount)*
    }

    class Player {
        -float _walkSpeed
        -float _runSpeed
        -float _attackSpeedFactor
        -float _blockSpeedFactor
        -bool _isAttacking
        -bool _blockHeld
        -Rigidbody rb
        -Animator _animator
        -PlayerLookAt _lookAt
        -PlayerInput _playerInput
        -MeleeHitbox playerSwordHitbox
        +bool RotateToMovement
        +bool IsBusy
        +bool IsAttacking
        +bool IsBlocking
        +AE_StartAttack()
        +AE_EndAttack()
        +OnMove(InputValue value)
        +TriggerAttack()
        -OnAttackPerformed(CallbackContext context)
        -OnBlockStarted(CallbackContext context)
        -OnBlockCanceled(CallbackContext context)
        -IsInAttackingState() bool
    }

    class Enemy {
        +int rutina
        +float cronometro
        +int speed
        +int speedDetected
        +GameObject target
        +float viewRadius
        +float viewAngle
        +bool attack
        -Rigidbody rb
        +Comportamiento()
        +CanSeePlayer() bool
        +Final_Ani()
    }

    class Health {
        -int maxHealth
        -int currentHealth
        +int CurrentHealth
        +TakeDamage(int damageAmount)
        -Die()
    }

    class MeleeHitbox {
        +int damage
        -LayerMask targetLayer
        -List~Collider~ alreadyHit
        -Collider hitboxCollider
        +EnableHitbox()
        +DisableHitbox()
        -OnTriggerEnter(Collider other)
    }

    class PuzzleManager {
        -PuzzleSlot[] _slots
        -HandGrabManager _grabManager
        -GameObject _rewardPrefab
        -Transform _rewardSpawnPoint
        -float _snapDuration
        -HashSet~PuzzlePiece~ _snapping
        +bool IsComplete
        +Completed event
        +SetGhostPreviewsEnabled(bool show)
        -HandlePickedUp(Rigidbody body)
        -HandleDropInterceptor(Rigidbody body) bool
        -FindNearestFreeSlot(PuzzlePiece piece, Vector3 position) PuzzleSlot
        -SnapRoutine(PuzzlePiece piece, PuzzleSlot slot) IEnumerator
        -CheckCompletion()
        -SpawnReward()
    }

    class PuzzleSlot {
        -string _acceptedPieceId
        -float _snapRadius
        -GameObject _ghostPreview
        +string AcceptedPieceId
        +PuzzlePiece Occupant
        +bool GhostsEnabled
        +float SnapRadius
        +bool IsOccupied
        +bool IsCorrect
        +CanAccept(PuzzlePiece piece) bool
        +Fill(PuzzlePiece piece)
        +Clear(PuzzlePiece piece)
        +SetGhost(GameObject ghost)
        +UpdateGhost()
    }

    class PuzzlePiece {
        -string _pieceId
        -Rigidbody _rb
        +string PieceId
        +bool IsLocked
        +bool IsSnapping
        +PuzzleSlot CurrentSlot
        +Rigidbody Body
        +Lock()
    }

    class AudioManager {
        +static AudioManager Instance
        -AudioSource ambientSource
        -AudioSource combatSource
        -AudioSource sfxSource
        -float fadeDuration
        -bool inCombat
        +SetCombatState(bool enableCombat)
        +playClip(AudioClip clip)
        -CrossfadeMusic(float targetAmbient, float targetCombat) IEnumerator
    }

    %% Herencia de MonoBehavior
    MonoBehaviour <|-- Player
    MonoBehaviour <|-- Enemy
    MonoBehaviour <|-- Health
    MonoBehaviour <|-- MeleeHitbox
    MonoBehaviour <|-- PuzzleManager
    MonoBehaviour <|-- PuzzleSlot
    MonoBehaviour <|-- PuzzlePiece
    MonoBehaviour <|-- AudioManager

    %% Implementación de interfaces
    IDamageable <|.. Health

    %% Relaciones del código
    Player --> MeleeHitbox : Activa/Desactiva via Animation Events (AE)
    MeleeHitbox ..> IDamageable : Aplica daño a objetivos colisionados
    Enemy --> AudioManager : Cambia estado a combate (SetCombatState)
    Enemy --> GameObject : Target (Player)
    PuzzleManager --> HandGrabManager : Intercepta eventos de Agarre/Soltar
    PuzzleManager "1" o-- "*" PuzzleSlot : Gestiona lista de slots
    PuzzleManager "1" o-- "*" PuzzlePiece : Gestiona piezas en animación/snap
    PuzzleSlot "1" <--> "0..1" PuzzlePiece : Ocupación bidireccional (Occupant / CurrentSlot)
```

---

## 2. Arquitectura MVC (Modelo - Controlador - Vista)

```mermaid
graph LR
    subgraph MODEL ["Modelo (Datos)"]
        direction TB
        M_Health["Health.cs / IDamageable<br/>(Salud, Daño, Muerte)"]
        M_PuzzleState["PuzzlePiece.cs & PuzzleSlot.cs<br/>(IDs y Ocupación)"]
    end

    subgraph CONTROLLER ["Controladores (Lógica)"]
        direction TB
        C_Player["Player.cs<br/>(InputSystem, Rigidbody)"]
        C_Enemy["Enemy.cs<br/>(IA y Visión)"]
        C_PuzzleMgr["PuzzleManager.cs<br/>(Físicas Snap)"]
    end

    subgraph VIEW ["Vista (Visual y Audio)"]
        direction TB
        V_Animator["Animator Controller"]
        V_Hitbox["MeleeHitbox.cs"]
        V_Audio["AudioManager.cs"]
        V_Ghosts["Ghost Previews"]
    end

    %% Relaciones
    C_Player -->|Actualiza| M_Health
    C_Player -->|Triggers| V_Animator
    V_Animator -->|AE_Events| V_Hitbox
    V_Hitbox -->|TakeDamage| M_Health

    C_Enemy -->|Detecta Target| V_Audio
    C_Enemy -->|Ataca| M_Health

    C_PuzzleMgr -->|Consulta| M_PuzzleState
    M_PuzzleState -->|Render| V_Ghosts
```

---

## 3. Diagrama de Secuencia: Sistema de Combate

```mermaid
sequenceDiagram
    autonumber
    actor Player as Jugador
    participant PScript as Player.cs
    participant Anim as Animator
    participant Hitbox as MeleeHitbox.cs
    participant Health as Health.cs (Enemigo)

    Player->>PScript: Presiona "Attack"
    PScript->>Anim: Trigger("Attack")
    Anim->>PScript: Evento: AE_StartAttack()
    PScript->>Hitbox: EnableHitbox()
    Hitbox->>Health: OnTriggerEnter() -> TakeDamage(damage)
    
    alt currentHealth <= 0
        Health-->>Health: Llama a Die() y destruye GameObject
    else currentHealth > 0
        Note over Health: Resta el daño a currentHealth
    end

    Anim->>PScript: Evento: AE_EndAttack()
    PScript->>Hitbox: DisableHitbox()
```

---

## 4. Máquina de Estados Finitos (FSM): IA del Enemigo

```mermaid
stateDiagram-v2
    [*] --> Patrulla : Start / Awake

    state Patrulla {
        [*] --> Esperando : Cronómetro < 3s
        Esperando --> Girando : Cronómetro >= 3s (Rutina 1)
        Girando --> Moviendo : Ángulo aleatorio elegido (Rutina 2)
        Moviendo --> Esperando : Fin del movimiento
    }

    Patrulla --> Persecucion : CanSeePlayer() == true
    note right of Persecucion
        Cambia música a Combate
        (AudioManager.SetCombatState)
    end note

    state Persecucion {
        [*] --> Acercandose : Distancia > 1m
        Acercandose --> PreparandoAtaque : Distancia <= 1m
    }

    Persecucion --> Ataque : attack = true
    Ataque --> Persecucion : Final_Ani() llamado

    Persecucion --> Patrulla : CanSeePlayer() == false
    note left of Patrulla
        Restaura música Ambiental
        (AudioManager.SetCombatState)
    end note
```
