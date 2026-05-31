using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LightSpawner : MonoBehaviour
{
    [Header("References")]
    public GameObject lightZonePrefab;   // Drag your Prefab here

    [Header("Pool Settings")]
    public int minLights = 3;    // Minimum lights always on the field
    public int maxLights = 5;    // Maximum simultaneous lights
    public float spawnIntervalMin = 10f;  // Min seconds between bonus spawns
    public float spawnIntervalMax = 20f;  // Max seconds between bonus spawns

    [Header("Field Bounds")]
    public float fieldMin = -150f;
    public float fieldMax = 150f;
    public float spawnHeight = 25f;

    [Tooltip("Padding from the edge of the arena so the light doesn't spawn into the wall.")]
    public float spawnPadding = 30f;

    // Live list — entries become null automatically when a light is destroyed
    private List<GameObject> activeLights = new List<GameObject>();

    void Start()
    {
        int aiCount = PlayerPrefs.GetInt("AIPlayerCount", 20);

        // Dynamically adjust bounds to match AISpawner
        float limit = Mathf.Lerp(100f, 200f, (aiCount - 5f) / 10f);
        float safeLimit = Mathf.Max(0, limit - spawnPadding);
        fieldMin = -safeLimit;
        fieldMax = safeLimit;

        // Dynamically adjust lights based on players (5 players -> 3 lights, 15 players -> 7 to 8 lights)
        minLights = Mathf.RoundToInt(Mathf.Lerp(3f, 7f, (aiCount - 5f) / 10f));
        maxLights = Mathf.RoundToInt(Mathf.Lerp(4f, 8f, (aiCount - 5f) / 10f));

        // ── Maintenance loop: keeps minLights alive throughout entire gameplay ──
        StartCoroutine(MaintenanceLoop());

        // ── Bonus loop: adds extra lights (up to maxLights) every 10-20 s ──────
        StartCoroutine(BonusSpawnLoop());
    }

    // ── Called by PlayerEnergy when a player fully charges ───────────────────
    public void SpawnNextLight()
    {
        activeLights.RemoveAll(l => l == null);
        if (activeLights.Count < maxLights)
            SpawnLight();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Runs every second — immediately replaces any dead lights to keep ≥ min
    // ─────────────────────────────────────────────────────────────────────────
    private IEnumerator MaintenanceLoop()
    {
        while (true)
        {
            activeLights.RemoveAll(l => l == null);

            while (activeLights.Count < minLights)
                SpawnLight();

            yield return new WaitForSeconds(1f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Randomly spawns a bonus light every 10–20 s (if under maxLights cap)
    // ─────────────────────────────────────────────────────────────────────────
    private IEnumerator BonusSpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(spawnIntervalMin, spawnIntervalMax));

            activeLights.RemoveAll(l => l == null);
            if (activeLights.Count < maxLights)
                SpawnLight();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void SpawnLight()
    {
        Vector3 bestPosition = Vector3.zero;
        float maxMinDistance = -1f;

        // Generate multiple candidate positions and pick the one furthest from existing lights
        int numCandidates = 20; // The higher the number, the more spread out they will be
        for (int i = 0; i < numCandidates; i++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(fieldMin, fieldMax),
                spawnHeight,
                Random.Range(fieldMin, fieldMax));

            // If there are no active lights, any position is fine
            if (activeLights.Count == 0)
            {
                bestPosition = candidate;
                break;
            }

            // Find the distance from this candidate to its closest existing light
            float minDistanceToExisting = float.MaxValue;
            foreach (GameObject light in activeLights)
            {
                if (light != null)
                {
                    float dist = Vector3.Distance(candidate, light.transform.position);
                    if (dist < minDistanceToExisting)
                    {
                        minDistanceToExisting = dist;
                    }
                }
            }

            // If this candidate is further from existing lights than previous candidates, pick it
            if (minDistanceToExisting > maxMinDistance)
            {
                maxMinDistance = minDistanceToExisting;
                bestPosition = candidate;
            }
        }

        GameObject newLight = Instantiate(lightZonePrefab, bestPosition, Quaternion.Euler(90, 0, 0));
        activeLights.Add(newLight);
    }
}