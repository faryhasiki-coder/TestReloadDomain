using UnityEngine;

public class PlayerDeathHandler : MonoBehaviour
{
    public GameObject explosionEffect;
    public AudioSource explosionAudioSource;
    public GameObject borderParent;

    private SpriteRenderer spriteRenderer;
    private Collider2D col;
    private Rigidbody2D rb;
    private PlayerControllerScript player;
    private PlayerFuelSystem fuelSystem;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        player = GetComponent<PlayerControllerScript>();
        fuelSystem = GetComponent<PlayerFuelSystem>();
    }

    public void Die()
    {
        player.IsDead = true;
        spriteRenderer.enabled = false;
        col.enabled = false;
        rb.simulated = false;
        fuelSystem.ResetOnDeath();

        Instantiate(explosionEffect, transform.position, transform.rotation);
        borderParent.SetActive(false);
        explosionAudioSource.Play();

        UIManager.Instance?.ShowDeathUI(player.Score);
    }
}
