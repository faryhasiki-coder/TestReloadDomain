using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerControllerScript : MonoBehaviour
{
    [Header("Movement")]
    public float thrustForce = 1f;
    public float maxSpeed = 5f;
    public float scoreMultiplier = 10f;

    public float bonusObstacleScore = 100f;

    private PlayerFuelSystem fuelSystem;
    private PlayerDeathHandler deathHandler;
    private Rigidbody2D rb;

    private float score;
    private float elapsedTime;
    private float bonusScore;

    public bool IsDead { get; set; }
    public bool IsBoosting => fuelSystem != null && fuelSystem.IsBoosting;
    public float Score => score;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        fuelSystem = GetComponent<PlayerFuelSystem>();
        deathHandler = GetComponent<PlayerDeathHandler>();
    }

    void Start()
    {
        scoreMultiplier = 10f;
    }

    void Update()
    {
        if (IsDead) return;
        UpdateScore();
    }

    private void FixedUpdate()
    {
        if (IsDead) return;
        MovePlayer();
    }

    private void MovePlayer()
    {
        if (!Mouse.current.leftButton.isPressed) return;

        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.value);
        Vector2 direction = ((Vector2)mousePos - (Vector2)transform.position).normalized;

        transform.up = direction;

        float multiplier = IsBoosting ? fuelSystem.BoostSpeedMultiplier : 1f;
        rb.AddForce(direction * thrustForce * multiplier);

        float currentMaxSpeed = maxSpeed * (IsBoosting ? fuelSystem.BoostSpeedMultiplier : 1f);
        if (rb.linearVelocity.magnitude > currentMaxSpeed)
            rb.linearVelocity = rb.linearVelocity.normalized * currentMaxSpeed;
    }

    private void UpdateScore()
    {
        elapsedTime += Time.deltaTime;
        score = Mathf.Floor(elapsedTime * scoreMultiplier) + bonusScore;
        UIManager.Instance?.UpdateScore(score);
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        ObstacleScript obstacle = collision.collider.GetComponentInParent<ObstacleScript>();
        if (obstacle == null) return;

        if (obstacle.IsBonus)
        {
            Destroy(obstacle.gameObject);
            AddScore(bonusObstacleScore);
            return;
        }

        if (IsBoosting)
        {
            Destroy(obstacle.gameObject);
            return;
        }

        Die();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        ChaserEnemy chaser = other.GetComponent<ChaserEnemy>();
        if (chaser != null && IsBoosting)
            Destroy(chaser.gameObject);
    }

    public void Die()
    {
        deathHandler?.Die();
    }

    public void AddScore(float amount)
    {
        bonusScore += amount;
        score += amount;
        UIManager.Instance?.UpdateScore(score);
    }

    private void OnApplicationQuit()
    {
        PlayerPrefs.DeleteKey("HighScore");
        PlayerPrefs.Save();
    }
}
