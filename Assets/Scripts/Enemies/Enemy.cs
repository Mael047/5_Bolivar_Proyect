using UnityEngine;

public class Enemy : MonoBehaviour
{
    [Header("Patrulla y movimiento")]
    public int rutina;
    public float cronometro;
    public Quaternion angulo;
    public float grado;
    public int speed = 1;
    public int speedDetected = 4;


    [Header("Visión y detección")]
    public GameObject target;
    public float viewRadius = 5f;
    public float viewAngle = 90f;
    public LayerMask obstacleMask;

    private Rigidbody rb;
    public bool attack;


    void Start()
    {
        
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        Comportamiento();
    }



    public void Comportamiento()
    {
        // Revisa si el jugador está dentro del cono de visión
        if (CanSeePlayer())
        {
            if(AudioManager.Instance != null)
            {
                AudioManager.Instance.SetCombatState(true);
            }
            if (Vector3.Distance(transform.position, target.transform.position) > 1f && !attack)
            {
                var lookPos = target.transform.position - transform.position;
                lookPos.y = 0;
                var rotation = Quaternion.LookRotation(lookPos);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, rotation, 3f);

                transform.Translate(Vector3.forward * speedDetected * Time.deltaTime);
            }
            else
            {
                attack = true;
            }
        }
        else
        {
            if(AudioManager.Instance != null)
            {
                AudioManager.Instance.SetCombatState(false);
            }
            //En caso de que no sea detectado 
            cronometro += Time.deltaTime;
            if (cronometro >= 3)
            {
                rutina = Random.Range(0, 2);
                cronometro = 0;
            }

            switch (rutina)
            {
                case 0:
                    break;
                case 1:
                    grado = Random.Range(0, 360);
                    angulo = Quaternion.Euler(0, grado, 0);
                    rutina++;
                    break;
                case 2:
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, angulo, 0.5f);
                    transform.Translate(Vector3.forward * speed * Time.deltaTime);
                    break;
            }
        }
    }

    // Comprueba si el jugador está dentro del radio, ángulo de visión y no hay paredes
    public bool CanSeePlayer()
    {
        if (target == null) return false;

        Vector3 dirToTarget = (target.transform.position - transform.position);
        float distanceToTarget = dirToTarget.magnitude;

        // 1. Verifica la distancia
        if (distanceToTarget <= viewRadius)
        {
            // 2. Verifica el ángulo del cono (frente al enemigo)
            float angleToTarget = Vector3.Angle(transform.forward, dirToTarget.normalized);

            if (angleToTarget <= viewAngle / 2f)
            {
                // 3. Opcional: Raycast para evitar que vea a través de paredes
                if (!Physics.Raycast(transform.position + Vector3.up, dirToTarget.normalized, distanceToTarget, obstacleMask))
                {
                    return true; // Jugador detectado dentro del cono
                }
            }
        }

        return false; // El jugador está fuera de rango, a la espalda o detrás de un obstáculo
    }

    public void Final_Ani()
    {
        attack = false;
    }

   
    private void OnDrawGizmosSelected()
    {
        // Radio de visión
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewRadius);

        // Líneas del cono de visión
        Vector3 leftBoundary = DirFromAngle(-viewAngle / 2f);
        Vector3 rightBoundary = DirFromAngle(viewAngle / 2f);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, transform.position + leftBoundary * viewRadius);
        Gizmos.DrawLine(transform.position, transform.position + rightBoundary * viewRadius);

        // Si detecta al jugador, pinta una línea hacia él
        if (target != null && CanSeePlayer())
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, target.transform.position);
        }
    }

    // Convierte un ángulo en vector de dirección relativo a la rotación del enemigo
    private Vector3 DirFromAngle(float angleInDegrees)
    {
        angleInDegrees += transform.eulerAngles.y;
        return new Vector3(Mathf.Sin(angleInDegrees * Mathf.Deg2Rad), 0, Mathf.Cos(angleInDegrees * Mathf.Deg2Rad));
    }
}
