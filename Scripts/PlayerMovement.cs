using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Basic Movement")]
    public float moveSpeed = 10f;
    public float gravity = -20f;

    [Header("Mouse Steering (Sneak.io style)")]
    public float mouseTurnSpeed = 5f;

    [Header("Dash Settings")]
    public float dashSpeedMultiplier = 1.6f;

    private CharacterController controller;
    private PlayerEnergy energy;
    private Vector3 velocity;       // For gravity

    // The actual direction the player is moving/facing right now
    private Vector3 currentMoveDirection = Vector3.forward;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        energy = GetComponent<PlayerEnergy>();

        string savedName = PlayerPrefs.GetString("PlayerName", "Player 1");
        if (!string.IsNullOrWhiteSpace(savedName))
        {
            gameObject.name = savedName;
        }
    }

    void OnEnable()
    {
        // When respawning (script is re-enabled), reset our movement to face North
        currentMoveDirection = Vector3.forward;
    }

    void Update()
    {
        HandleMouseInput();

        // ── Dash (Space + energy) ────────────────────────────────────────────────
        bool isDashing = Input.GetKey(KeyCode.Space) && energy != null && energy.currentEnergy > 0;
        float currentSpeed = isDashing ? moveSpeed * dashSpeedMultiplier : moveSpeed;
        if (isDashing && energy != null)
            energy.UseEnergy(energy.drainRate * Time.deltaTime);

        if (controller == null || !controller.enabled) return;

        // ── Gravity & Movement ───────────────────────────────────────────────
        if (controller.isGrounded && velocity.y < 0)
            velocity.y = -2f;
        velocity.y += gravity * Time.deltaTime;

        Vector3 move = currentMoveDirection * currentSpeed;
        move.y = velocity.y;

        controller.Move(move * Time.deltaTime);

        // Force player to stay exactly at Y=3 as requested
        Vector3 pos = transform.position;
        pos.y = 3f;
        transform.position = pos;

        // ── Rotation ─────────────────────────────────────────────────────────
        // Visually rotate the bike to face the direction of movement
        if (currentMoveDirection.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(currentMoveDirection);
        }
    }

    // ── Mouse input (Sneak.io / Slither.io Style) ────────────────────────────
    // Smoothly and continuously rotate the player toward the mouse cursor.
    void HandleMouseInput()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Project mouse onto the horizontal plane at the player's Y position
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));

        if (!groundPlane.Raycast(ray, out float distance)) return;

        Vector3 worldMousePos = ray.GetPoint(distance);
        Vector3 toMouse = worldMousePos - transform.position;
        toMouse.y = 0f;

        // If the mouse is far enough away from the center of the player, steer toward it
        if (toMouse.sqrMagnitude > 0.1f)
        {
            Vector3 desiredDir = toMouse.normalized;
            // Smoothly curve our movement direction toward the mouse cursor
            currentMoveDirection = Vector3.Slerp(currentMoveDirection, desiredDir, mouseTurnSpeed * Time.deltaTime);
        }
    }
}