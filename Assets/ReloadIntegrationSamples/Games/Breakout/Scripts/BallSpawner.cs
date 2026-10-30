// BallSpawner.cs

using UnityEngine;

public class BallSpawner : MonoBehaviour
{
    // public
    public static BallSpawner Instance;

    [Header("References")]
    public GameObject ballPrefab;

    // unity

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        SpawnBall();
    }

    // custom

    public GameObject SpawnBall()
    {
        if (ballPrefab == null)
        {
            Debug.LogError("Ball prefab is not assigned");
            return null;
        }

        return Instantiate(ballPrefab, transform.position, Quaternion.identity);
    }
}