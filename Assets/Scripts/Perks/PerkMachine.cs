using System.Collections.Generic;
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
    private static readonly Dictionary<string, PerkMachine> Registry = new Dictionary<string, PerkMachine>();

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
    private string networkKey;

    public string NetworkKey
    {
        get
        {
            if (string.IsNullOrEmpty(networkKey))
            {
                networkKey = BuildNetworkKey(transform);
            }
            return networkKey;
        }
    }

    private void OnEnable()
    {
        Registry[NetworkKey] = this;
    }

    private void OnDisable()
    {
        if (!string.IsNullOrEmpty(networkKey) &&
            Registry.TryGetValue(networkKey, out PerkMachine registered) &&
            registered == this)
        {
            Registry.Remove(networkKey);
        }
    }

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
            if (NetworkGameplayCoordinator.IsNetworkActive)
            {
                NetworkGameplayCoordinator.RequestPerk(this);
            }
            else
            {
                TryBuy();
            }
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

    private GUIStyle promptStyle;

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

        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }
        GUIStyle style = promptStyle;

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
        // Prefer the LOCAL player so each client interacts with its own player.
        Transform local = LocalPlayer.Transform;
        if (local != null)
        {
            player = local;
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

    public static bool TryFind(string key, out PerkMachine machine)
    {
        return Registry.TryGetValue(key, out machine);
    }

    private static string BuildNetworkKey(Transform target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        string key = target.name;
        Transform parent = target.parent;
        while (parent != null)
        {
            key = parent.name + "/" + key;
            parent = parent.parent;
        }
        return key;
    }
}
