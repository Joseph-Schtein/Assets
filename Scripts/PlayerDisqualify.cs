using UnityEngine;

public class PlayerDisqualify : MonoBehaviour
{
    void OnCollisionEnter(Collision collision)
    {
        // Check if the object we hit is a trail
        if (collision.gameObject.CompareTag("DeadlyTrail"))
        {
            Disqualify();
        }
    }

    void Disqualify()
    {
        Debug.Log(gameObject.name + " crashed into a trail and is disqualified!");

        // Add your specific game-over logic here:
        // e.g., Disable movement, trigger an explosion particle effect, or destroy the object
        Destroy(gameObject);
    }
}