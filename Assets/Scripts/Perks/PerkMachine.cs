using UnityEngine;

/// <summary>
/// A buyable perk machine (Call of Duty Zombies style). Mirrors the interaction
/// pattern of <c>Door</c>: the player walks up, an on-screen "Press E  Buy ..."
/// prompt appears, and pressing E spends points via <see cref="PlayerPoints"/>
/// and grants the perk through <see cref="PerkManager"/>.
///
/// Once the perk is owned the prompt shows "owned" and further presses do
/// nothing. Everything is null-safe: with no PerkManager / PlayerPoints in the
/// scene the machine simply can't be bought.
///
/// Machines are created as placeholders by the editor tool
/// "Tools/School Of The Dead/Place Perk Machines" (PerkMachinePlacer); drag each
/// one to its real spot afterwards.
/// </summary>
public class PerkMachine : MonoBehaviour
{
    [Header("Perk")]
    [Tooltip("Which perk this machine sells.")]
    public PerkType perk = PerkType.VitalBoost;
    [Tooltip("Cost in points. Defaults follow the classic prices (see PerkManager.DefaultCost).")]
    public int cost = 2500;

    [Header("Interaction")]
    [Tooltip("How close the player must be (world units) to buy from this machine.")]
    public float interactionRange = 3f;
    [Tooltip("Key the player presses to buy the perk.")]
    public KeyCode interactKey = KeyCode.E;

    private Transform player;
    private bool playerInRange;
    private float nextPromptTime;

    private void Start()
    {
        FindPlayer();
    }

    private void Update()
    {
        if (player == null)
        {
            FindPlayer();
            if (player == null)
            {
                playerInRange = false;
                return;
            }
        }

        float distance = Vector3.Distance(transform.position, player.position);
        playerInRange = distance <= interactionRange;
        if (!playerInRange)
        {
            return;
        }

        // Power gate: the machine is completely inert until the map power is on
        // (mirrors MysteryBox / PackAPunchMachine). No purchase while powered down;
        // OnGUI shows a dimmed "(turn on power)" hint instead of the buy prompt.
        if (!PowerState.IsOn)
        {
            return;
        }

        // Already owned: nothing to buy.
        if (IsOwned())
        {
            return;
        }

        if (Input.GetKeyDown(interactKey))
        {
            TryBuy();
        }
    }

    private bool IsOwned()
    {
        return PerkManager.Instance != null && PerkManager.Instance.HasPerk(perk);
    }

    private void TryBuy()
    {
        // No perk system in the scene -> can't grant anything.
        if (PerkManager.Instance == null)
        {
            Debug.LogWarning("[PerkMachine] No PerkManager in scene; cannot buy " + perk + ".");
            return;
        }

        // Charge points (PlayerPoints is optional; treat its absence as "free").
        if (cost > 0 && PlayerPoints.Instance != null)
        {
            if (!PlayerPoints.Instance.TrySpend(cost))
            {
                if (Time.time >= nextPromptTime)
                {
                    Debug.Log("Need " + cost + " points for " + PerkManager.PerkDisplayName(perk));
                    nextPromptTime = Time.time + 1.5f;
                }
                return;
            }
        }

        bool granted = PerkManager.Instance.TryGrant(perk);
        if (!granted)
        {
            // Shouldn't normally happen (owned machines don't reach here), but if
            // it does, refund so the player isn't charged for nothing.
            if (cost > 0 && PlayerPoints.Instance != null)
            {
                PlayerPoints.Instance.Add(cost);
            }
        }
    }

    private void OnGUI()
    {
        if (!playerInRange)
        {
            return;
        }

        string label;
        Color textColor;
        if (!PowerState.IsOn)
        {
            // Power gate: dimmed hint, no purchase possible yet.
            label = PerkManager.PerkDisplayName(perk) + "  (turn on power)";
            textColor = new Color(0.55f, 0.55f, 0.55f, 1f); // dimmed grey
        }
        else
        {
            bool owned = IsOwned();
            label = owned
                ? PerkManager.PerkDisplayName(perk) + "  (owned)"
                : "Press E   Buy " + PerkManager.PerkDisplayName(perk) + "   [" + cost + "]";
            textColor = owned ? new Color(0.7f, 0.95f, 0.7f, 1f) : new Color(0.96f, 0.93f, 0.86f, 1f);
        }

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };

        float w = 460f;
        float h = 34f;
        Rect rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.62f, w, h);

        // Drop shadow then the label in its state colour for readability over any background.
        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), label, style);
        GUI.color = textColor;
        GUI.Label(rect, label, style);
        GUI.color = prev;
    }

    private void FindPlayer()
    {
        // Prefer the CharacterController player (project convention, mirrors Door).
        CharacterController controller = FindFirstObjectByType<CharacterController>();
        if (controller != null)
        {
            player = controller.transform;
            return;
        }

        if (Camera.main != null)
        {
            player = Camera.main.transform;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = PerkManager.PerkColor(perk);
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}
