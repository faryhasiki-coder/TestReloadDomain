using UnityEngine;

public class BoosterFlameEvents : MonoBehaviour
{
    [Header("Burst Effect")]
    public float burstScaleMultiplier = 1.5f;
    public float burstDuration = 0.1f;

    private Vector3 originalScale;

    void Start()
    {
        originalScale = transform.localScale;
    }

    public void OnBoostFlameBurst()
    {
        Debug.Log("Animation Event: Boost flame burst!");
        transform.localScale = originalScale * burstScaleMultiplier;
        Invoke(nameof(ResetScale), burstDuration);
    }

    private void ResetScale()
    {
        transform.localScale = originalScale;
    }
}
