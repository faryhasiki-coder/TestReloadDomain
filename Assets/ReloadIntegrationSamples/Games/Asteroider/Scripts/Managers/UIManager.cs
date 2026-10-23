using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    private Label scoreLabel;
    private Label highScoreLabel;
    private Label fuelLabel;
    private Button restartButton;

    private float highScore;

    void Awake()
    {
        Instance = this;
        var root = GetComponent<UIDocument>().rootVisualElement;
        scoreLabel = root.Q<Label>("ScoreLabel");
        highScoreLabel = root.Q<Label>("HighScoreLabel");
        fuelLabel = root.Q<Label>("FuelLabel");
        restartButton = root.Q<Button>("RestartButton");

        restartButton.style.display = DisplayStyle.None;
        restartButton.clicked += OnRestartClicked;
        restartButton.RegisterCallback<PointerEnterEvent>(_ => OnRestartHover(true));
        restartButton.RegisterCallback<PointerLeaveEvent>(_ => OnRestartHover(false));

        highScoreLabel.style.display = DisplayStyle.None;
        highScore = PlayerPrefs.GetFloat("HighScore", 0f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void UpdateScore(float score)
    {
        scoreLabel.text = "Score: " + score;
    }

    public void UpdateFuel(float currentFuel, float maxFuel)
    {
        int filled = Mathf.FloorToInt(currentFuel);
        string text = "Fuel: ";
        for (int i = 0; i < maxFuel; i++)
            text += i < filled ? "I" : ".";
        fuelLabel.text = text;
    }

    public void ShowDeathUI(float score)
    {
        if (score > highScore)
        {
            highScore = score;
            PlayerPrefs.SetFloat("HighScore", highScore);
            PlayerPrefs.Save();
        }

        highScoreLabel.text = "High Score: " + highScore;
        highScoreLabel.style.display = DisplayStyle.Flex;
        restartButton.style.display = DisplayStyle.Flex;
    }

    private void OnRestartHover(bool hovering)
    {
        if (hovering)
            restartButton.style.backgroundColor = new StyleColor(new Color(1f, 0.4f, 0.4f));
        else
            restartButton.style.backgroundColor = new StyleColor(new Color(232f / 255f, 28f / 255f, 55f / 255f));
    }

    private void OnRestartClicked()
    {
        restartButton.style.color = new StyleColor(new Color(232f / 255f, 28f / 255f, 55f / 255f));
        restartButton.style.backgroundColor = StyleKeyword.None;
        restartButton.style.backgroundColor = new StyleColor(Color.white);
        restartButton.style.borderTopColor = new StyleColor(new Color(232f / 255f, 28f / 255f, 55f / 255f));
        restartButton.style.borderRightColor = new StyleColor(new Color(232f / 255f, 28f / 255f, 55f / 255f));
        restartButton.style.borderBottomColor = new StyleColor(new Color(232f / 255f, 28f / 255f, 55f / 255f));
        restartButton.style.borderLeftColor = new StyleColor(new Color(232f / 255f, 28f / 255f, 55f / 255f));

        Invoke(nameof(ResetGame), 0.15f);
    }

    private void ResetGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
