// BrickSpawner.cs

using UnityEngine;

public class BrickSpawner : MonoBehaviour
{
    [System.Serializable]
    public class BrickData
    {
        public string colorName;
        public Sprite initialSprite;
        public Sprite[] breakFrames;
    }

    // public
    [Header("Prefab")]
    public GameObject brickPrefab;

    [Header("Brick Types")]
    public BrickData[] brickTypes;

    [Header("Start Position")]
    public Vector2 startPosition = new Vector2(-7f, 7.5f);

    [Header("Layout")]
    public int columns = 8;

    private void Start()
    {
        SpawnGrid();
    }

    private void SpawnGrid()
    {
        if (brickPrefab == null)
        {
            Debug.LogError("BrickSpawner: brickPrefab is not assigned");
            return;
        }

        if (brickTypes == null || brickTypes.Length == 0)
        {
            Debug.LogError("BrickSpawner: no brick types assigned");
            return;
        }

        SpriteRenderer sr = brickPrefab.GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            Debug.LogError("BrickSpawner: brickPrefab must have a SpriteRenderer");
            return;
        }

        Sprite sprite = brickTypes[0].initialSprite;
        if (sprite == null)
        {
            Debug.LogError("BrickSpawner: first brick type has no initial sprite");
            return;
        }

        Vector3 scale = brickPrefab.transform.localScale;
        float stepX = sprite.rect.width / sprite.pixelsPerUnit * scale.x;
        float stepY = sprite.rect.height / sprite.pixelsPerUnit * scale.y;

        for (int row = 0; row < brickTypes.Length; row++)
        {
            BrickData data = brickTypes[row];

            for (int col = 0; col < columns; col++)
            {
                Vector3 spawnPos = new Vector3(
                    startPosition.x + col * stepX,
                    startPosition.y - row * stepY,
                    0f
                );

                GameObject brickObj = Instantiate(brickPrefab, spawnPos, Quaternion.identity, transform);

                Brick brick = brickObj.GetComponent<Brick>();
                if (brick != null)
                {
                    brick.Setup(data.initialSprite, data.breakFrames);
                }
            }
        }
    }
}