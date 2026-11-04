using UnityEngine;

public class BonusZoneTriggerScript : MonoBehaviour
{
    [SerializeField] private float bonusScore = 50f;
    [SerializeField] private bool disableAfterUse = true;

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerControllerScript player = other.GetComponentInParent<PlayerControllerScript>();
        if (player == null)
        {
            return;
        }

        player.AddScore(bonusScore);

        if (disableAfterUse)
        {
            gameObject.SetActive(false);
        }
    }
}
