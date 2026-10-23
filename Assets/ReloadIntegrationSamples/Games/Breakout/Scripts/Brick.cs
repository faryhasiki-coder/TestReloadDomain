// Brick.cs

using System.Collections;
using UnityEngine;

public class Brick : MonoBehaviour
{
    // public
    [Header("Animation")]
    public float frameTime = 0.03f;

    // private
    private SpriteRenderer sr;
    private Collider2D col;

    // breaking
    private bool isBreaking = false;
    private Sprite[] breakFrames;

    // unity

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isBreaking)
            return;

        if (collision.gameObject.CompareTag("Ball"))
        {
            StartCoroutine(BreakRoutine());
        }
    }

    // custom

    public void Setup(Sprite initialSprite, Sprite[] frames)
    {
        sr.sprite = initialSprite;
        breakFrames = frames;
    }

    private IEnumerator BreakRoutine()
    {
        isBreaking = true;

        if (col != null)
            col.enabled = false;

        if (breakFrames != null)
        {
            foreach (Sprite frame in breakFrames)
            {
                if (frame != null)
                    sr.sprite = frame;

                yield return new WaitForSeconds(frameTime);
            }
        }

        if (PowerUp.Instance != null)
            PowerUp.Instance.Apply();
        Destroy(gameObject);
    }
}