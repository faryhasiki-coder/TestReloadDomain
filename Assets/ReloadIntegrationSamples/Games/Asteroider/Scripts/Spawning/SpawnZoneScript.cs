using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(BoxCollider2D))]
public class SpawnZoneScript : MonoBehaviour
{
    [Header("Spawn Setup")]
    [SerializeField] private GameObject obstaclePrefab;
    [SerializeField] private Transform spawnParent;

    [Header("Timing (max = current, base = easy start)")]
    [SerializeField] private float baseSpawnInterval = 6f;
    [SerializeField] private float spawnInterval = 2f;

    [Header("Amount Per Wave (max = current, base = easy start)")]
    [SerializeField] private int baseMinSpawnCount = 1;
    [SerializeField] private int baseMaxSpawnCount = 2;
    [SerializeField] private int minSpawnCount = 2;
    [SerializeField] private int maxSpawnCount = 5;

    [Header("Direction Spread")]
    [SerializeField] private float directionSpreadAngle = 30f;

    [Header("Bonus")]
    [SerializeField] private float bonusChance = 0.15f;

    [Header("Chaser Enemy")]
    [SerializeField] private GameObject chaserPrefab;
    [SerializeField][Range(0f, 1f)] private float chaserChance = 0.1f;

    [Header("Spacing")]
    [SerializeField] private float minSeparation = 1.5f;
    [SerializeField] private int maxSpawnPositionAttempts = 10;

    private BoxCollider2D spawnZone;
    private float spawnTimer;

    private void Awake()
    {
        spawnZone = GetComponent<BoxCollider2D>();
        spawnZone.isTrigger = true;
    }

    private void Start()
    {
        if (obstaclePrefab == null)
        {
            Debug.LogWarning($"SpawnZoneScript on {name} needs an obstacle prefab assigned.", this);
            return;
        }

        spawnTimer = 1f;
    }

    private void Update()
    {
        if (obstaclePrefab == null) return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f) return;

        SpawnWave();

        float t = DifficultyManager.Instance != null ? DifficultyManager.Instance.DifficultyT : 1f;
        float interval = Mathf.Lerp(baseSpawnInterval, spawnInterval, t);
        spawnTimer = interval;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerControllerScript player = other.GetComponent<PlayerControllerScript>();
        if (player != null)
        {
            player.Die();
        }
    }

    private void SpawnWave()
    {
        float t = DifficultyManager.Instance != null ? DifficultyManager.Instance.DifficultyT : 1f;
        int minCount = Mathf.RoundToInt(Mathf.Lerp(baseMinSpawnCount, minSpawnCount, t));
        int maxCount = Mathf.RoundToInt(Mathf.Lerp(baseMaxSpawnCount, maxSpawnCount, t));
        int spawnCount = Random.Range(minCount, maxCount + 1);
        List<Vector2> usedPositions = new();

        for (int i = 0; i < spawnCount; i++)
        {
            Vector2 spawnPosition = GetSpawnPosition(usedPositions);
            GameObject obstacle = Instantiate(obstaclePrefab, spawnPosition, Quaternion.identity, spawnParent);

            ObstacleScript obstacleScript = obstacle.GetComponent<ObstacleScript>();
            if (obstacleScript != null)
            {
                Vector2 directionToCenter = ((Vector2)Vector2.zero - spawnPosition).normalized;
                float angleOffset = Random.Range(-directionSpreadAngle, directionSpreadAngle);
                Vector2 finalDirection = Quaternion.Euler(0, 0, angleOffset) * directionToCenter;
                obstacleScript.SetInitialDirection(finalDirection);

                if (Random.value < bonusChance)
                {
                    obstacleScript.SetBonus(true);
                }
            }
        }

        if (chaserPrefab != null && Random.value < chaserChance)
        {
            Vector2 chaserPos = GetSpawnPosition(new List<Vector2>());
            Instantiate(chaserPrefab, chaserPos, Quaternion.identity, spawnParent);
        }
    }

    private Vector2 GetSpawnPosition(List<Vector2> usedPositions)
    {
        Bounds bounds = spawnZone.bounds;
        Vector2 spawnPoint = Vector2.zero;

        for (int attempt = 0; attempt < maxSpawnPositionAttempts; attempt++)
        {
            float perimeter = 2f * (bounds.size.x + bounds.size.y);
            float randomPoint = Random.Range(0f, perimeter);

            if (randomPoint < bounds.size.x)
            {
                float t = randomPoint / bounds.size.x;
                spawnPoint = new Vector2(bounds.min.x + t * bounds.size.x, bounds.max.y);
            }
            else if (randomPoint < bounds.size.x + bounds.size.y)
            {
                float t = (randomPoint - bounds.size.x) / bounds.size.y;
                spawnPoint = new Vector2(bounds.max.x, bounds.min.y + t * bounds.size.y);
            }
            else if (randomPoint < 2f * bounds.size.x + bounds.size.y)
            {
                float t = (randomPoint - bounds.size.x - bounds.size.y) / bounds.size.x;
                spawnPoint = new Vector2(bounds.max.x - t * bounds.size.x, bounds.min.y);
            }
            else
            {
                float t = (randomPoint - 2f * bounds.size.x - bounds.size.y) / bounds.size.y;
                spawnPoint = new Vector2(bounds.min.x, bounds.max.y - t * bounds.size.y);
            }

            if (IsFarEnoughFromOthers(spawnPoint, usedPositions))
            {
                break;
            }
        }

        usedPositions.Add(spawnPoint);
        return spawnPoint;
    }

    private bool IsFarEnoughFromOthers(Vector2 candidate, List<Vector2> usedPositions)
    {
        foreach (Vector2 used in usedPositions)
        {
            if (Vector2.Distance(candidate, used) < minSeparation)
            {
                return false;
            }
        }

        return true;
    }
}
