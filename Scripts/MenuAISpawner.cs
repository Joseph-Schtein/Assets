using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// MenuAISpawner — spawns N AI players with attached Spot Lights into any scene
/// except SampleScene. Each bike receives a distinct vibrant colour drawn from a
/// curated palette that avoids blue clusters. The spot light and trail both match
/// the bike colour and use a consistent brightness.
/// </summary>
public class MenuAISpawner : MonoBehaviour
{
    [Header("Prefab References")]
    [Tooltip("AI Player Variant prefab (must have LightcycleAI + PlayerEnergy).")]
    public GameObject aiPrefab;

    [Header("Spawn Settings")]
    public int spawnCount = 15;

    [Tooltip("Radius of the spawn ring around the arena centre.")]
    public float arenaSpawnRadius = 100f;


    // ── Curated hue palette — 20 evenly-distributed hues that skip the dense
    //    blue cluster (~0.57–0.70) and give good visual variety. ──────────────
    private static readonly float[] _hues = new float[]
    {
        0.00f,  // red
        0.05f,  // red-orange
        0.08f,  // orange
        0.11f,  // amber
        0.14f,  // yellow-orange
        0.17f,  // yellow
        0.22f,  // yellow-green
        0.28f,  // lime green
        0.33f,  // green
        0.40f,  // teal-green
        0.47f,  // cyan
        0.50f,  // sky-cyan
        0.53f,  // light blue (just before the dense blue cluster)
        0.73f,  // indigo-blue  (resume after the cluster)
        0.77f,  // blue-violet
        0.80f,  // violet
        0.84f,  // purple
        0.88f,  // magenta
        0.92f,  // rose
        0.96f,  // hot-pink
    };

    // ── Internal ─────────────────────────────────────────────────────────────
    private readonly List<GameObject> _spawnedAIs = new List<GameObject>();
    private readonly List<Color> _spawnedColors = new List<Color>();
    public static Transform MouseTarget;
    private static GameObject _mouseTargetGO;

    void Awake()
    {
        if (SceneManager.GetActiveScene().name == "SampleScene") { enabled = false; return; }
    }

    void Start()
    {
        if (aiPrefab == null)
        {
            Debug.LogError("MenuAISpawner: aiPrefab is not assigned!");
            return;
        }

        for (int i = 0; i < spawnCount; i++)
            SpawnAI(i);

        // Re-sync trail colours one frame later, after all PlayerEnergy.Start()
        // methods have run and cached their trail material instances.
        StartCoroutine(ResyncTrailColorsNextFrame());
    }

    /// <summary>
    /// Waits one frame so every AI's PlayerEnergy.Start() has finished, then
    /// re-applies the bike colour and clears any white trail segments.
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

            // Re-set the colour in case Start() overwrote it
            energy.fullEnergyColor = bikeColor;
            Color dimColor = bikeColor * 0.15f;
            dimColor.a = 1f;
            energy.lowEnergyColor = dimColor;

            // Clear any trail segments that were emitted with the wrong colour
            if (energy.sparkTrail != null)
                energy.sparkTrail.Clear();
        }
    }

    void Update()
    {
        if (Camera.main != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.up * 1f);
            if (groundPlane.Raycast(ray, out float distance))
            {
                if (_mouseTargetGO == null)
                {
                    _mouseTargetGO = new GameObject("MouseTarget");
                    MouseTarget = _mouseTargetGO.transform;
                }
                _mouseTargetGO.transform.position = ray.GetPoint(distance);
            }
        }
    }

    // ── Per-bike spawning ─────────────────────────────────────────────────────

    private readonly string[] aiNames = new string[]
    {
        "Alex", "Jordan", "Taylor", "Morgan", "Casey", "Riley", "Jamie", "Quinn", "Avery", "Skyler",
        "Cameron", "Drew", "Jesse", "Rowan", "Hayden", "Kendall", "Peyton", "Reese", "Dakota", "Micah",
        "Sam", "Charlie", "Blake", "Logan", "Dylan", "Finley", "Emerson", "Parker", "Phoenix", "River"
    };

    void SpawnAI(int index)
    {
        // 1. Spread bikes in a ring ────────────────────────────────────────────
        float angle = (index / (float)spawnCount) * 360f;
        Vector3 spawnPos = new Vector3(
            Mathf.Cos(angle * Mathf.Deg2Rad) * arenaSpawnRadius,
            1f,
            Mathf.Sin(angle * Mathf.Deg2Rad) * arenaSpawnRadius
        );
        Quaternion spawnRot = Quaternion.Euler(0f, angle + 90f, 0f);

        GameObject ai = Instantiate(aiPrefab, spawnPos, spawnRot);
        string randomName = aiNames[Random.Range(0, aiNames.Length)];
        ai.name = randomName;
        ai.tag = "AI";
        _spawnedAIs.Add(ai);
        _spawnedColors.Add(Color.black); // placeholder; updated below

        // 2. Pick colour from curated palette (wrap if spawnCount > palette size)
        float hue = _hues[index % _hues.Length];
        Color bikeColor = Color.HSVToRGB(hue, 0.95f, 1f);  // max saturation + value
        _spawnedColors[_spawnedColors.Count - 1] = bikeColor;

        // 3. Apply colour — same pattern as the game-scene AISpawner: set the
        //    energy colours and let PlayerEnergy.Start() + UpdateSparkVisuals()
        //    drive the trail material automatically (no manual overrides).
        PlayerEnergy energy = ai.GetComponent<PlayerEnergy>();
        if (energy != null)
        {
            energy.fullEnergyColor = bikeColor;
            Color dimColor = bikeColor * 0.15f;
            dimColor.a = 1f;
            energy.lowEnergyColor = dimColor;

            // Colour the spark (headlight-style) light
            if (energy.sparkLight != null)
                energy.sparkLight.color = bikeColor;
        }

        // Tint body mesh renderers
        ApplyColorToRenderers(ai, bikeColor);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Applies a colour to all non-trail renderers (body mesh).</summary>
    static void ApplyColorToRenderers(GameObject go, Color colour)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (r is TrailRenderer) continue;
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_Color", colour);
            block.SetColor("_BaseColor", colour);
            block.SetColor("_EmissionColor", colour * 2f);
            r.SetPropertyBlock(block);
        }

        Transform headlight = go.transform.Find("Headlight");
        if (headlight != null)
        {
            Light hl = headlight.GetComponent<Light>();
            if (hl != null) hl.color = Color.Lerp(Color.white, colour, 0.6f);
        }
    }
}
