// Platform.cs

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class Platform : MonoBehaviour
{
    // public
    [Header("Basic")]
    public float speed = 10f;
    public float limitX = 4f;

    [Header("Sprites")]
    public Sprite normalSprite;
    public Sprite wideSprite;

    [Header("Collider Sizes")]
    public Vector2 normalColliderSize = new Vector2(1.28f, 0.32f);
    public Vector2 wideColliderSize = new Vector2(2.24f, 0.32f);

    // private 
    private Rigidbody2D rb;
    private BoxCollider2D col;
    private SpriteRenderer sr;

    // coroutine
    private Coroutine widthRoutine;

    // unity

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<BoxCollider2D>();
        sr = GetComponent<SpriteRenderer>();

        SetNormal();
    }

    private void FixedUpdate()
    {
        float input = 0f;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed)
                input = -1f;

            if (Keyboard.current.dKey.isPressed)
                input = 1f;
        }

        Vector2 pos = rb.position;
        pos.x += input * speed * Time.fixedDeltaTime;

        float halfWidth = col.size.x * 0.5f;

        pos.x = Mathf.Clamp(pos.x, -limitX + halfWidth, limitX - halfWidth);

        rb.MovePosition(pos);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ball"))
        {
            Ball ball = collision.gameObject.GetComponent<Ball>();
            if (ball != null)
                ball.BounceFromPlatform(transform, col.size.x);
        }
    }

    // custom

    public void MakeWider(float duration)
    {
        if (widthRoutine != null)
            StopCoroutine(widthRoutine);

        widthRoutine = StartCoroutine(WiderRoutine(duration));
    }

    private IEnumerator WiderRoutine(float duration)
    {
        SetWide();

        yield return new WaitForSeconds(duration);

        SetNormal();
        widthRoutine = null;
    }

    private void SetNormal()
    {
        if (normalSprite != null)
            sr.sprite = normalSprite;

        col.size = normalColliderSize;
    }

    private void SetWide()
    {
        if (wideSprite != null)
            sr.sprite = wideSprite;

        col.size = wideColliderSize;
    }
}