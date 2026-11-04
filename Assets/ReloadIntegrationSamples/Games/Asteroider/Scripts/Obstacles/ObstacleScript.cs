using UnityEngine;


[RequireComponent(typeof(Rigidbody2D))]
public class ObstacleScript : MonoBehaviour
{
    [Header("Size")]
    public float minSize = 0.5f;
    public float maxSize = 1f;

    [Header("Movement (max = current, base = easy start)")]
    public float baseMinSpeed = 18f;
    public float baseMaxSpeed = 42f;
    public float minSpeed = 50f;
    public float maxSpeed = 100f;
    public float maxSpinSpeed = 10f;

    [Header("Effects")]
    public GameObject bounceEffectPrefab;

    [Header("Bonus")]
    [SerializeField] private bool isBonus;
    [SerializeField] private Color bonusColor = new Color(0.2f, 0.6f, 1f);

    private Rigidbody2D rb;
    private bool hasForcedDirection;
    private Vector2 forcedDirection;
    private SpriteRenderer spriteRenderer;

    public bool IsBonus => isBonus;

    public void SetBonus(bool value)
    {
        isBonus = value;
        if (isBonus && spriteRenderer != null)
        {
            spriteRenderer.color = bonusColor;
        }
    }

    public void SetInitialDirection(Vector2 direction)
    {
        if (direction == Vector2.zero)
        {
            hasForcedDirection = false;
            return;
        }

        forcedDirection = direction.normalized;
        hasForcedDirection = true;
    }

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (isBonus && spriteRenderer != null)
        {
            spriteRenderer.color = bonusColor;
        }
        ApplyRandomSizeAndMovement();
    }

    void Update()
    {
        Vector3 viewPos = Camera.main.WorldToViewportPoint(transform.position);
        if (viewPos.x < -0.5f || viewPos.x > 1.5f || viewPos.y < -0.5f || viewPos.y > 1.5f)
        {
            Destroy(gameObject);
        }
    }

    private void ApplyRandomSizeAndMovement()
    {
        float randomSize = Random.Range(minSize, maxSize);
        transform.localScale = new Vector3(randomSize, randomSize, randomSize);

        float t = DifficultyManager.Instance != null ? DifficultyManager.Instance.DifficultyT : 1f;
        float currentMinSpeed = Mathf.Lerp(baseMinSpeed, minSpeed, t);
        float currentMaxSpeed = Mathf.Lerp(baseMaxSpeed, maxSpeed, t);
        float randomSpeed = Random.Range(currentMinSpeed, currentMaxSpeed) / randomSize;
        Vector2 randomDirection = hasForcedDirection
            ? forcedDirection
            : Random.insideUnitCircle.normalized;
        float randomTorque = Random.Range(-maxSpinSpeed, maxSpinSpeed);

        rb = GetComponent<Rigidbody2D>();
        rb.AddForce(randomDirection * randomSpeed);
        rb.AddTorque(randomTorque);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (bounceEffectPrefab == null || collision.contactCount == 0)
        {
            return;
        }

        Vector2 contactPoint = collision.GetContact(0).point;
        GameObject bounceEffect = Instantiate(bounceEffectPrefab, contactPoint, Quaternion.identity);
        Destroy(bounceEffect, 1f);
    }
}
