using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

public class PlayerFuelSystem : MonoBehaviour
{
    [Header("Fuel")]
    public float maxFuel = 3f;
    public float fuelRegenRate = 0.5f;

    [Header("Boost")]
    public float boostSpeedMultiplier = 3f;
    public string boostAnimatorBoolName = "IsBoosting";
    public string normalAnimationStateName = "Normal";
    public string boostAnimationStateName = "Boost";

    public GameObject boosterFlame;

    private float currentFuel;
    private float boostUnitTimer;
    private bool isBoostActive;
    private Animator flameAnimator;
    private bool hasAnimatorParameter;
    private int normalStateHash;
    private int boostStateHash;
    private PlayerControllerScript player;

    public bool IsBoosting => isBoostActive;
    public float CurrentFuel => currentFuel;
    public float BoostSpeedMultiplier => boostSpeedMultiplier;

    void Awake()
    {
        player = GetComponent<PlayerControllerScript>();
    }

    void Start()
    {
        currentFuel = maxFuel;
        UIManager.Instance?.UpdateFuel(currentFuel, maxFuel);

        if (boosterFlame != null)
        {
            flameAnimator = boosterFlame.GetComponent<Animator>();
            hasAnimatorParameter = flameAnimator != null
                && flameAnimator.parameters.Any(p => p.name == boostAnimatorBoolName);
            normalStateHash = Animator.StringToHash(normalAnimationStateName);
            boostStateHash = Animator.StringToHash(boostAnimationStateName);
            if (flameAnimator != null && flameAnimator.HasState(0, normalStateHash))
                flameAnimator.CrossFade(normalStateHash, 0f, 0);
        }
    }

    void Update()
    {
        if (player.IsDead) return;

        UpdateFuel();
        UpdateBoostState();
        HandleBoostInput();
        UpdateFlameVisual();
    }

    public void ResetOnDeath()
    {
        isBoostActive = false;
        if (boosterFlame != null)
            boosterFlame.SetActive(false);
        if (flameAnimator != null && flameAnimator.HasState(0, normalStateHash))
            flameAnimator.CrossFade(normalStateHash, 0f, 0);
    }

    private void HandleBoostInput()
    {
        if (Keyboard.current == null) return;

        bool spacePressed = Keyboard.current.spaceKey.isPressed;

        if (spacePressed && !isBoostActive && currentFuel >= 1f)
        {
            currentFuel -= 1f;
            boostUnitTimer = 1f;
            SetBoostActive(true);
        }

        if (!spacePressed && isBoostActive)
            SetBoostActive(false);
    }

    private void UpdateBoostState()
    {
        if (!isBoostActive) return;

        boostUnitTimer -= Time.deltaTime;
        if (boostUnitTimer > 0f) return;

        if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed && currentFuel >= 1f)
        {
            currentFuel -= 1f;
            boostUnitTimer = 1f;
        }
        else
        {
            SetBoostActive(false);
        }
    }

    private void UpdateFuel()
    {
        if (!isBoostActive && currentFuel < maxFuel)
        {
            currentFuel += fuelRegenRate * Time.deltaTime;
            if (currentFuel > maxFuel) currentFuel = maxFuel;
        }

        UIManager.Instance?.UpdateFuel(currentFuel, maxFuel);
    }

    private void SetBoostActive(bool active)
    {
        isBoostActive = active;
        UpdateFlameVisual();

        if (flameAnimator == null) return;

        if (hasAnimatorParameter)
            flameAnimator.SetBool(boostAnimatorBoolName, active);

        int stateHash = active ? boostStateHash : normalStateHash;
        if (flameAnimator.HasState(0, stateHash))
            flameAnimator.CrossFade(stateHash, 0f, 0);
    }

    private void UpdateFlameVisual()
    {
        if (boosterFlame == null || Mouse.current == null) return;

        bool shouldShow = Mouse.current.leftButton.isPressed || isBoostActive;
        boosterFlame.SetActive(shouldShow);

        if (flameAnimator != null && shouldShow)
        {
            int stateHash = isBoostActive ? boostStateHash : normalStateHash;
            if (flameAnimator.HasState(0, stateHash))
                flameAnimator.CrossFade(stateHash, 0f, 0);
        }
    }
}
