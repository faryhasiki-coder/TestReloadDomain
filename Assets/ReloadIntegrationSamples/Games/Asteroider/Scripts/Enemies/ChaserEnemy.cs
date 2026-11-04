using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class ChaserEnemy : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 15f;
    public float acceleration = 1f;
    public float rotationSpeed = 2f;

    [Header("Visual")]
    public Color gizmoColor = new Color(1f, 0.2f, 0.2f);

    private Rigidbody2D rb;
    private Transform player;
    private Collider2D col;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0;

        col = GetComponent<Collider2D>();
        col.isTrigger = true;

        PlayerControllerScript playerScript = FindFirstObjectByType<PlayerControllerScript>();
        if (playerScript != null)
        {
            player = playerScript.transform;
        }

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = gizmoColor;
        }
    }

    void FixedUpdate()
    {
        if (player == null)
        {
            return;
        }

        Vector2 directionToPlayer = ((Vector2)player.position - (Vector2)transform.position).normalized;

        rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, directionToPlayer * speed, acceleration * Time.fixedDeltaTime);

        if (rb.linearVelocity.magnitude > 0.1f)
        {
            float targetAngle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg - 90f;
            rb.rotation = Mathf.LerpAngle(rb.rotation, targetAngle, rotationSpeed * Time.fixedDeltaTime);
        }
    }

    void Update()
    {
        Vector3 viewPos = Camera.main.WorldToViewportPoint(transform.position);
        if (viewPos.x < -0.5f || viewPos.x > 1.5f || viewPos.y < -0.5f || viewPos.y > 1.5f)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerControllerScript playerScript = other.GetComponent<PlayerControllerScript>();
        if (playerScript == null)
        {
            return;
        }

        if (playerScript.IsBoosting)
        {
            Destroy(gameObject);
            return;
        }

        playerScript.Die();
    }
}
