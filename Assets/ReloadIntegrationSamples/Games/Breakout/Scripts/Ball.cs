// Ball.cs

using UnityEngine;

public class Ball : MonoBehaviour
{
    // public
    [Header("Basic")]
    public float speed = 6f;
    public float minBounceY = 0.35f;

    // private
    private Rigidbody2D body;

    // unity

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    private void Start()
    {
        Launch();
    }

    private void FixedUpdate()
    {
        if (body.linearVelocity.sqrMagnitude > 0.001f)
        {
            body.linearVelocity = body.linearVelocity.normalized * speed;
        }
    }

    // custom

    public void Launch()
    {
        Vector2 dir = new Vector2(Random.Range(-0.7f, 0.7f), 1f).normalized;
        body.linearVelocity = dir * speed;
    }

    public void BounceFromPlatform(Transform platform, float platformWidth)
    {
        float offset = (transform.position.x - platform.position.x) / (platformWidth * 0.5f);
        offset = Mathf.Clamp(offset, -1f, 1f);

        Vector2 dir = new Vector2(offset, 1f).normalized;

        if (dir.y < minBounceY)
        {
            dir.y = minBounceY;
            dir = dir.normalized;
        }

        body.linearVelocity = dir * speed;
    }
}