// DeathZone.cs

using UnityEngine;

public class DeathZone : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Ball"))
            return;

        Destroy(other.gameObject);

        // respawn only last ball
        int ballCount = FindObjectsByType<Ball>(FindObjectsSortMode.None).Length;
        if (ballCount <= 1 && BallSpawner.Instance != null)
            BallSpawner.Instance.SpawnBall();
    }
}