using UnityEngine;

public class DifficultyManager : MonoBehaviour
{
    public static DifficultyManager Instance { get; private set; }

    [Header("Progression")]
    public float difficultyInterval = 5f;
    public int maxDifficultyLevel = 6;

    private float elapsedTime;

    public float DifficultyT => Mathf.Clamp01(elapsedTime / (difficultyInterval * maxDifficultyLevel));

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (elapsedTime < difficultyInterval * maxDifficultyLevel)
            elapsedTime += Time.deltaTime;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
