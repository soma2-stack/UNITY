// Dev-only F8 test menu for School of the Dead.
//
// This ENTIRE file is compiled out of release builds: everything below lives inside
// `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, so a normal (non-development) player build contains
// no active behavior at all. It self-bootstraps only in the SchoolOfTheDead scene, draws a
// simple IMGUI window toggled with F8, and drives gameplay ONLY through existing public APIs.
//
// Multiplayer safety: authoritative actions (spawn/kill zombies, spawn power-ups, turn power on,
// grant perks) are gated to solo or the host/server via NetworkGameplayCoordinator. A pure client
// sees those buttons disabled with a note, so the menu can never silently desync a session.

#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public sealed class DevMenu : MonoBehaviour
{
    private const string TargetScene = "SchoolOfTheDead";

    private static DevMenu instance;

    // ---- Bootstrap (editor / development builds only) --------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (SceneManager.GetActiveScene().name == TargetScene)
        {
            EnsureInstance();
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == TargetScene)
        {
            EnsureInstance();
        }
    }

    private static void EnsureInstance()
    {
        if (instance != null)
        {
            return;
        }
        var go = new GameObject("DevMenu") { hideFlags = HideFlags.DontSave };
        instance = go.AddComponent<DevMenu>();
    }

    // ---- State ------------------------------------------------------------------------------

    private bool menuOpen;
    private bool wasOpen;
    private bool paused;
    private CursorLockMode prevLockState = CursorLockMode.Locked;
    private bool prevCursorVisible;

    private Rect windowRect = new Rect(20f, 20f, 360f, 560f);
    private Vector2 scroll;
    private Vector2 weaponScroll;
    private int selectedWeapon;
    private readonly List<Weapon> weaponChoices = new List<Weapon>();
    private string status = "";

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
        SceneManager.sceneLoaded -= OnSceneLoaded;
        // Never leave the game frozen behind us.
        if (paused)
        {
            Time.timeScale = 1f;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            SetOpen(!menuOpen);
        }

        // Keep the cursor usable while the menu is open (other scripts re-lock it each frame).
        if (menuOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void SetOpen(bool open)
    {
        if (open == menuOpen)
        {
            return;
        }
        menuOpen = open;

        if (open)
        {
            prevLockState = Cursor.lockState;
            prevCursorVisible = Cursor.visible;
            RefreshWeaponChoices();
        }
        else
        {
            // Restore cursor and un-pause so closing the menu always hands control back cleanly.
            Cursor.lockState = prevLockState;
            Cursor.visible = prevCursorVisible;
            if (paused)
            {
                paused = false;
                Time.timeScale = 1f;
            }
        }
    }

    // ---- IMGUI ------------------------------------------------------------------------------

    private void OnGUI()
    {
        if (!menuOpen)
        {
            return;
        }
        windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "Dev Menu (F8)");
    }

    private void DrawWindow(int id)
    {
        scroll = GUILayout.BeginScrollView(scroll);

        DrawStatus();
        GUILayout.Space(6f);
        DrawGeneral();
        GUILayout.Space(6f);
        DrawWeapons();
        GUILayout.Space(6f);
        DrawPoints();
        GUILayout.Space(6f);
        DrawZombies();
        GUILayout.Space(6f);
        DrawPowerAndSystems();

        if (!string.IsNullOrEmpty(status))
        {
            GUILayout.Space(6f);
            GUILayout.Label("> " + status);
        }

        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, 100000f, 20f));
    }

    // ---- Sections ---------------------------------------------------------------------------

    private void DrawStatus()
    {
        GUILayout.Label("— Status —");
        WeaponController wc = LocalPlayer.Weapon;
        if (wc != null && wc.HasWeapon)
        {
            GUILayout.Label("Weapon: " + wc.CurrentWeaponName +
                "   Ammo: " + wc.CurrentMagazineAmmo + " / " + wc.CurrentReserveAmmo);
        }
        else
        {
            GUILayout.Label("Weapon: (none)");
        }

        int points = PlayerPoints.Instance != null ? PlayerPoints.Instance.GetPoints() : 0;
        int round = RoundManager.Instance != null ? RoundManager.Instance.CurrentRound : 0;
        GUILayout.Label("Points: " + points + "    Round: " + round);
        GUILayout.Label("Zombies alive: " + CountZombies() + "    Power: " + (PowerState.IsOn ? "ON" : "off"));
        GUILayout.Label("Network: " + NetworkRoleLabel());
    }

    private void DrawGeneral()
    {
        GUILayout.Label("— General —");
        bool newPaused = GUILayout.Toggle(paused, paused ? "Paused (click to resume)" : "Pause game (timescale 0)");
        if (newPaused != paused)
        {
            paused = newPaused;
            Time.timeScale = paused ? 0f : 1f;
        }
    }

    private void DrawWeapons()
    {
        GUILayout.Label("— Weapons —");
        if (weaponChoices.Count == 0)
        {
            GUILayout.Label("(no weapons found — open the menu in-game)");
            if (GUILayout.Button("Rescan weapons"))
            {
                RefreshWeaponChoices();
            }
            return;
        }

        weaponScroll = GUILayout.BeginScrollView(weaponScroll, GUILayout.Height(120f));
        for (int i = 0; i < weaponChoices.Count; i++)
        {
            bool sel = GUILayout.Toggle(selectedWeapon == i, weaponChoices[i].weaponName);
            if (sel)
            {
                selectedWeapon = i;
            }
        }
        GUILayout.EndScrollView();

        if (GUILayout.Button("Give selected weapon"))
        {
            GiveSelectedWeapon();
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refill current ammo"))
        {
            RefillCurrentAmmo();
        }
        if (GUILayout.Button("Refill all ammo"))
        {
            RefillAllAmmo();
        }
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Rescan weapons"))
        {
            RefreshWeaponChoices();
        }
    }

    private void DrawPoints()
    {
        GUILayout.Label("— Points —");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+500"))
        {
            AddPoints(500);
        }
        if (GUILayout.Button("+5000"))
        {
            AddPoints(5000);
        }
        GUILayout.EndHorizontal();
    }

    private void DrawZombies()
    {
        GUILayout.Label("— Zombies —");
        bool canAuthor = CanDoServerActions();
        if (!canAuthor)
        {
            GUILayout.Label("(client: spawning/killing is host-only)");
        }
        GUI.enabled = canAuthor;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Spawn 1"))
        {
            SpawnZombies(1);
        }
        if (GUILayout.Button("Spawn 5"))
        {
            SpawnZombies(5);
        }
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Kill all zombies"))
        {
            KillAllZombies();
        }
        GUI.enabled = true;
    }

    private void DrawPowerAndSystems()
    {
        GUILayout.Label("— Power & Systems —");
        bool canAuthor = CanDoServerActions();
        if (!canAuthor)
        {
            GUILayout.Label("(client: power/power-ups/perks are host-only)");
        }
        GUI.enabled = canAuthor;
        if (GUILayout.Button("Turn power ON"))
        {
            TurnPowerOn();
        }
        if (GUILayout.Button("Spawn every power-up near player"))
        {
            SpawnAllPowerups();
        }
        if (GUILayout.Button("Give all perks"))
        {
            GiveAllPerks();
        }
        GUI.enabled = true;
    }

    // ---- Actions (all via existing public APIs) ---------------------------------------------

    private void RefreshWeaponChoices()
    {
        weaponChoices.Clear();
        var seen = new HashSet<string>();

        WeaponController wc = LocalPlayer.Weapon;
        if (wc != null && wc.weapons != null)
        {
            foreach (Weapon w in wc.weapons)
            {
                AddWeaponChoice(w, seen);
            }
        }

        MysteryBox box = Object.FindFirstObjectByType<MysteryBox>();
        if (box != null && box.weaponPool != null)
        {
            foreach (Weapon w in box.weaponPool)
            {
                AddWeaponChoice(w, seen);
            }
        }

        selectedWeapon = Mathf.Clamp(selectedWeapon, 0, Mathf.Max(0, weaponChoices.Count - 1));
    }

    private void AddWeaponChoice(Weapon w, HashSet<string> seen)
    {
        if (w == null || string.IsNullOrEmpty(w.weaponName) || !seen.Add(w.weaponName))
        {
            return;
        }
        weaponChoices.Add(w);
    }

    private void GiveSelectedWeapon()
    {
        WeaponController wc = LocalPlayer.Weapon;
        if (wc == null || selectedWeapon < 0 || selectedWeapon >= weaponChoices.Count)
        {
            status = "No local weapon controller / selection.";
            return;
        }
        // Clone so the shared list/pool template is never mutated by the grant.
        wc.GiveWeapon(weaponChoices[selectedWeapon].Clone());
        status = "Gave " + weaponChoices[selectedWeapon].weaponName + ".";
    }

    private void RefillCurrentAmmo()
    {
        WeaponController wc = LocalPlayer.Weapon;
        if (wc == null || !wc.HasWeapon)
        {
            status = "No current weapon.";
            return;
        }
        wc.RefillReserveAmmo(wc.CurrentWeaponName);
        status = "Refilled " + wc.CurrentWeaponName + " reserve.";
    }

    private void RefillAllAmmo()
    {
        WeaponController wc = LocalPlayer.Weapon;
        if (wc == null)
        {
            status = "No local weapon controller.";
            return;
        }
        wc.RefillAllAmmo();
        status = "Refilled all ammo.";
    }

    private void AddPoints(int amount)
    {
        if (PlayerPoints.Instance == null)
        {
            status = "PlayerPoints not ready.";
            return;
        }
        PlayerPoints.Instance.AddPoints(amount);
        status = "Added " + amount + " points.";
    }

    private void SpawnZombies(int count)
    {
        if (!CanDoServerActions())
        {
            status = "Spawning is host-only.";
            return;
        }

        GameObject prefab = ResolveZombiePrefab();
        if (prefab == null)
        {
            Debug.LogWarning("[DevMenu] No zombie prefab found (RoundManager.spawner.zombiePrefab or Resources/NetworkZombie). Skipping spawn.");
            status = "No zombie prefab found (see console).";
            return;
        }

        Transform player = LocalPlayer.Transform;
        if (player == null)
        {
            status = "No local player to spawn in front of.";
            return;
        }

        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            if (SpawnOneZombie(prefab, player, i))
            {
                spawned++;
            }
        }
        status = "Spawned " + spawned + "/" + count + " zombie(s).";
    }

    private bool SpawnOneZombie(GameObject prefab, Transform player, int index)
    {
        // Fan the spawns out slightly in front of the player and drop them onto the NavMesh.
        Vector3 basePos = player.position + player.forward * 3f;
        Vector3 offset = player.right * ((index % 5) - 2) * 1.2f;
        Vector3 want = basePos + offset;
        Vector3 pos = NavMesh.SamplePosition(want, out NavMeshHit hit, 6f, NavMesh.AllAreas) ? hit.position : want;

        GameObject obj = Instantiate(prefab, pos, player.rotation);
        ZombieAgent zombie = obj.GetComponent<ZombieAgent>();
        if (zombie == null)
        {
            Debug.LogWarning("[DevMenu] Zombie prefab has no ZombieAgent; destroying the instance.");
            Destroy(obj);
            return false;
        }

        RoundManager rm = RoundManager.Instance;
        int hp = rm != null ? Mathf.Max(1, rm.baseZombieHealth + rm.healthPerRound * Mathf.Max(0, rm.CurrentRound - 1)) : 150;
        float spd = rm != null ? Mathf.Min(rm.maxZombieSpeed, rm.baseZombieSpeed + rm.speedPerRound * Mathf.Max(0, rm.CurrentRound - 1)) : 3.0f;
        zombie.Configure(hp, spd);

        // Networked host: replicate. (Solo just uses the local instance.) These dev zombies are
        // intentionally not registered with the spawner, so they never affect round completion.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null && !netObj.IsSpawned)
            {
                netObj.Spawn(true);
            }
        }
        return true;
    }

    private void KillAllZombies()
    {
        if (!CanDoServerActions())
        {
            status = "Killing is host-only.";
            return;
        }
        ZombieAgent[] zombies = Object.FindObjectsByType<ZombieAgent>(FindObjectsSortMode.None);
        int killed = 0;
        foreach (ZombieAgent z in zombies)
        {
            if (z != null && !z.IsDead)
            {
                // Big unattributed damage routes through the normal death path (points, despawn).
                z.TakeDamage(999999, false, PlayerPoints.EveryoneClientId);
                killed++;
            }
        }
        status = "Killed " + killed + " zombie(s).";
    }

    private void TurnPowerOn()
    {
        if (!CanDoServerActions())
        {
            status = "Power is host-only.";
            return;
        }
        PowerState.TurnOn();
        if (NetworkGameplayCoordinator.IsNetworkActive)
        {
            NetworkGameplayCoordinator.BroadcastPower(); // replicate to clients (host-only, self-guards)
        }
        status = "Power ON.";
    }

    private void SpawnAllPowerups()
    {
        if (!CanDoServerActions())
        {
            status = "Power-ups are host-only.";
            return;
        }
        PowerupManager mgr = PowerupManager.Instance;
        Transform player = LocalPlayer.Transform;
        if (mgr == null || player == null)
        {
            status = "PowerupManager / player not ready.";
            return;
        }

        int i = 0;
        foreach (PowerupType type in System.Enum.GetValues(typeof(PowerupType)))
        {
            // Spread them in a small arc in front of the player so they don't stack.
            Vector3 pos = player.position + player.forward * 2.5f + player.right * (i - 2) * 1.0f;
            mgr.SpawnPowerupAt(type, pos);
            i++;
        }
        status = "Spawned " + i + " power-up(s).";
    }

    private void GiveAllPerks()
    {
        if (!CanDoServerActions())
        {
            status = "Perks are host-only here.";
            return;
        }
        PerkManager pm = PerkManager.Instance;
        if (pm == null)
        {
            status = "PerkManager not ready.";
            return;
        }

        bool networked = NetworkGameplayCoordinator.IsNetworkActive;
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        int granted = 0;
        foreach (PerkType perk in System.Enum.GetValues(typeof(PerkType)))
        {
            if (pm.TryGrant(perk)) // local ownership + effect (host is also a client)
            {
                granted++;
            }
            if (networked)
            {
                // Keep the server's authoritative per-client perk set in sync on the host.
                PerkManager.ServerGrantClientPerk(localId, perk);
            }
        }
        status = "Granted " + granted + " perk(s).";
    }

    // ---- Helpers ----------------------------------------------------------------------------

    // Solo, or the host/server in a networked session. Pure clients cannot run authoritative tools.
    private static bool CanDoServerActions()
    {
        return !NetworkGameplayCoordinator.IsNetworkActive || NetworkGameplayCoordinator.IsServer;
    }

    private static string NetworkRoleLabel()
    {
        if (!NetworkGameplayCoordinator.IsNetworkActive)
        {
            return "solo";
        }
        return NetworkGameplayCoordinator.IsServer ? "host/server" : "client";
    }

    private static int CountZombies()
    {
        RoundManager rm = RoundManager.Instance;
        if (rm != null && rm.spawner != null)
        {
            return rm.spawner.AliveCount;
        }
        return Object.FindObjectsByType<ZombieAgent>(FindObjectsSortMode.None).Length;
    }

    private static GameObject ResolveZombiePrefab()
    {
        RoundManager rm = RoundManager.Instance;
        if (rm != null && rm.spawner != null && rm.spawner.zombiePrefab != null)
        {
            return rm.spawner.zombiePrefab;
        }
        return Resources.Load<GameObject>("NetworkZombie");
    }
}
#endif
