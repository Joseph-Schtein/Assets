using UnityEngine;
public class AISpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    public GameObject aiPrefab;
    public int spawnCount = 15;
    public float arenaLimit = 150f;
    public float spawnBuffer = 20f; // Distance from the edge to avoid spawning into walls

    private readonly string[] aiNames = new string[]
    {
        "Alex", "Jordan", "Taylor", "Morgan", "Casey", "Riley", "Jamie", "Quinn", "Avery", "Skyler",
        "Cameron", "Drew", "Jesse", "Rowan", "Hayden", "Kendall", "Peyton", "Reese", "Dakota", "Micah",
        "Sam", "Charlie", "Blake", "Logan", "Dylan", "Finley", "Emerson", "Parker", "Phoenix", "River"
    };

    void Start()
    {
        if (aiPrefab == null)
        {
            Debug.LogWarning("AISpawner: No AI Prefab assigned!");
            return;
        }

        // Destroy any pre-existing AI players manually placed in the scene
        GameObject[] existingAIs = GameObject.FindGameObjectsWithTag("AI");
        foreach (GameObject ai in existingAIs)
        {
            Destroy(ai);
        }

        // Read the AI count from settings (default to inspector value if not set)
        spawnCount = PlayerPrefs.GetInt("AIPlayerCount", spawnCount);

        // Dynamically adjust arena size based on AI count (5 players -> 100 limit, 15 players -> 200 limit)
        arenaLimit = Mathf.Lerp(100f, 200f, (spawnCount - 5f) / 10f);

        // Scale the GameField object visually (Assuming Plane 10x10)
        GameObject gameField = GameObject.Find("GameField");
        if (gameField != null)
        {
            float scale = arenaLimit / 5f; // (arenaLimit * 2) / 10
            gameField.transform.localScale = new Vector3(scale, 1f, scale);
        }



        float spawnRange = Mathf.Max(0, arenaLimit - spawnBuffer);
        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 spawnPos = new Vector3(
                Random.Range(-spawnRange, spawnRange),
                1f,
                Random.Range(-spawnRange, spawnRange)
            );
            Quaternion spawnRot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            GameObject newAI = Instantiate(aiPrefab, spawnPos, spawnRot);

            // Assign a random human name to the AI player
            string randomName = aiNames[Random.Range(0, aiNames.Length)];
            newAI.name = randomName;

            // Assign a random color — ApplyRandomColor() updates trail material, renderers & lights
            PlayerEnergy energy = newAI.GetComponent<PlayerEnergy>();
            if (energy != null)
            {
                energy.ApplyRandomColor();
            }
        }
    }
}