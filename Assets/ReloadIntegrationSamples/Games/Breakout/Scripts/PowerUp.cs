// PowerUp.cs

using UnityEngine;

public class PowerUp : MonoBehaviour
{
    // public
    public static PowerUp Instance;

    [Header("Settings")]
    [Range(0f, 1f)] public float chance = 0.25f;
    public float widePlatformDuration = 8f;

    // private
    private Platform platform;

    // unity

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        platform = FindFirstObjectByType<Platform>();
    }

    // custom

    public void Apply()
    {
        if (Random.value > chance)
            return;

        int power = Random.Range(0, 2);

        switch (power)
        {
            // extra ball
            case 0:
                if (BallSpawner.Instance != null)
                    BallSpawner.Instance.SpawnBall();
                break;

            // wider platform
            case 1:
                if (platform != null)
                    platform.MakeWider(widePlatformDuration);
                break;
        }
    }
}
