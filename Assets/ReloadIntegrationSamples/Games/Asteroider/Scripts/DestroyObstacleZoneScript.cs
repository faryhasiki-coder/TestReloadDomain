using UnityEngine;

public class DestroyObstacleZoneScript : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        ObstacleScript obstacle = other.GetComponentInParent<ObstacleScript>();

        if (obstacle != null)
        {
            Destroy(obstacle.gameObject);
        }
    }
}
