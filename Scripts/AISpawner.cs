using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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

    // Track spawned AIs and their assigned colors for the deferred resync
    private readonly List<GameObject> _spawnedAIs = new List<GameObject>();
    private readonly List<Color> _spawnedColors = new List<Color>();

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
            newAI.tag = "AI"; // Ensure the tag is always set regardless of prefab default

            // Pick a bright neon color with slightly lower saturation so it blooms into a white core like the Player's yellow
            Color bikeColor = Random.ColorHSV(0f, 1f, 0.5f, 0.7f, 1f, 1f);
            _spawnedAIs.Add(newAI);
            _spawnedColors.Add(bikeColor);

            PlayerEnergy energy = newAI.GetComponent<PlayerEnergy>();
            if (energy != null)
            {
                // Set colors + light before Start() runs
                energy.fullEnergyColor = bikeColor;
                Color dimColor = bikeColor * 0.2f;
                dimColor.a = 1f;
                energy.lowEnergyColor = dimColor;

                if (energy.sparkLight != null)
                    energy.sparkLight.color = bikeColor;

                // First-pass trail sync (trailMat initialises from the prefab-assigned material)
                energy.SyncTrailColor(bikeColor, energy.trailBrightness);
            }
        }

        // Re-sync trail colors one frame later, after all PlayerEnergy.Start() methods
        // have run and fully initialised their trail material instances.
        StartCoroutine(ResyncTrailColorsNextFrame());
    }

    /// <summary>
    /// Waits one frame so every AI's PlayerEnergy.Start() has finished, then
    /// re-applies the bike colour so the trail gradient and material both match
    /// the spotlight color exactly.
    /// </summary>
    private IEnumerator ResyncTrailColorsNextFrame()
    {
        yield return null; // wait one full frame

        for (int i = 0; i < _spawnedAIs.Count; i++)
        {
            GameObject ai = _spawnedAIs[i];
            if (ai == null) continue;

            Color bikeColor = _spawnedColors[i];
            PlayerEnergy energy = ai.GetComponent<PlayerEnergy>();
            if (energy == null) continue;

            // Set the light color — use sparkLight if wired up, otherwise
            // find the first Light component in children as a fallback.
            if (energy.sparkLight != null)
            {
                energy.sparkLight.color = bikeColor;
            }
            else
            {
                // sparkLight reference may be broken; find it manually
                Light foundLight = ai.GetComponentInChildren<Light>(true);
                if (foundLight != null)
                {
                    energy.sparkLight = foundLight; // wire it up for runtime
                    foundLight.color = bikeColor;
                }
            }

            // Force fullEnergyColor to bikeColor so UpdateVisuals() fallback is correct
            energy.fullEnergyColor = bikeColor;

            // Clear any trail segments emitted with the wrong colour, then resync
            if (energy.sparkTrail != null)
                energy.sparkTrail.Clear();

            energy.SyncTrailColor(bikeColor, energy.trailBrightness);
        }
    }
}