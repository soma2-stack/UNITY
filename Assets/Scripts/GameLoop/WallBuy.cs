using UnityEngine;

/// <summary>
/// A WALL BUY (Call of Duty Zombies style): a specific weapon bolted to a wall.
/// Press E to buy it (full price) the first time, or to refill its ammo (cheaper)
/// once you already own it — handled by <see cref="WeaponController.GiveWeapon"/>,
/// which tops up ammo when the player already has that weapon.
///
/// Each WallBuy is fully self-contained: drop several around the map, give each one a
/// different <see cref="weaponName"/> + stats + <see cref="weaponModel"/> and they sell
/// different guns. Assign a <see cref="chalkSprite"/> to draw the classic chalk outline
/// on the wall.
/// </summary>
public class WallBuy : InteractableBase
{
    [Header("Wall Buy")]
    [Tooltip("Display name of the weapon sold here.")]
    public string weaponName = "Rifle";
    [Tooltip("Full price to buy the weapon the first time.")]
    public int buyCost = 1500;
    [Tooltip("Cheaper price to refill ammo once owned.")]
    public int ammoCost = 500;

    [Header("Weapon Stats")]
    public int damage = 40;
    public float fireRate = 8f;
    public bool automatic = true;
    public float range = 120f;
    public float spread = 1.5f;
    public int magazineSize = 30;
    public int reserveAmmo = 240;
    public float reloadTime = 2.2f;

    [Header("In-Hand Model")]
    [Tooltip("First-person weapon model prefab handed to the player on purchase. It is " +
             "spawned under the WeaponController's WeaponHolder when equipped. Leave null " +
             "for no view model.")]
    public GameObject weaponModel;

    [Header("Chalk Drawing")]
    [Tooltip("Optional chalk-outline sprite drawn on the wall for this weapon. Assign a " +
             "transparent PNG imported as a Sprite. Shown automatically at runtime.")]
    public Sprite chalkSprite;
    [Tooltip("Local position offset of the chalk drawing relative to this object.")]
    public Vector3 chalkLocalOffset = new Vector3(0f, 1.2f, 0.03f);
    [Tooltip("Local euler rotation of the chalk drawing.")]
    public Vector3 chalkLocalEuler = Vector3.zero;
    [Tooltip("Uniform scale of the chalk drawing.")]
    public float chalkScale = 1f;
    [Tooltip("Chalk tint and opacity.")]
    public Color chalkColor = new Color(0.95f, 0.95f, 0.92f, 0.85f);

    private GameObject chalkObject;

    protected override void Start()
    {
        base.Start();
        CreateChalk();
    }

    // Spawn a SpriteRenderer child showing the chalk drawing on the wall. No-op without a
    // sprite. Safe to call again (it rebuilds the chalk object).
    private void CreateChalk()
    {
        if (chalkObject != null)
        {
            Destroy(chalkObject);
            chalkObject = null;
        }
        if (chalkSprite == null)
        {
            return;
        }

        chalkObject = new GameObject("Chalk_" + weaponName);
        chalkObject.transform.SetParent(transform, false);
        chalkObject.transform.localPosition = chalkLocalOffset;
        chalkObject.transform.localEulerAngles = chalkLocalEuler;
        chalkObject.transform.localScale = Vector3.one * Mathf.Max(0.01f, chalkScale);

        SpriteRenderer sr = chalkObject.AddComponent<SpriteRenderer>();
        sr.sprite = chalkSprite;
        sr.color = chalkColor;
    }

    private bool Owns(WeaponController wc)
    {
        if (wc == null || wc.weapons == null)
        {
            return false;
        }
        foreach (Weapon w in wc.weapons)
        {
            if (w != null && w.weaponName == weaponName)
            {
                return true;
            }
        }
        return false;
    }

    protected override string GetPromptText()
    {
        WeaponController wc = LocalPlayer.Weapon;
        bool owns = Owns(wc);
        int price = owns ? ammoCost : buyCost;
        string verb = owns ? "Buy Ammo" : "Buy";
        return "Press E   " + verb + " " + weaponName + "   [" + price + "]";
    }

    protected override void OnInteract()
    {
        WeaponController wc = LocalPlayer.Weapon;
        if (wc == null)
        {
            Debug.LogWarning("[WallBuy] No WeaponController in scene.");
            return;
        }

        bool owns = Owns(wc);
        if (NetworkGameplayCoordinator.IsNetworkActive)
        {
            NetworkGameplayCoordinator.RequestWallBuy(this, owns);
            return;
        }

        int price = owns ? ammoCost : buyCost;

        if (!TryCharge(price))
        {
            return;
        }

        ApplyPurchaseResult(owns);
    }

    public void ApplyPurchaseResult(bool owns)
    {
        WeaponController wc = LocalPlayer.Weapon;
        if (wc == null)
        {
            Debug.LogWarning("[WallBuy] No WeaponController in scene.");
            return;
        }

        // SELF-CORRECT against the buyer's ACTUAL inventory rather than blindly trusting the
        // requested action: the purchase is already PAID by the time this runs, so it must
        // never no-op. If we're told "refill" but the gun isn't actually carried any more
        // (e.g. it was replaced via the weapon slot cap between request and grant), give the
        // weapon instead; if we're told "give" but it IS carried, top up its reserves.
        bool actuallyOwns = Owns(wc);
        if (actuallyOwns)
        {
            // Already own it: top up RESERVES only (CoD wall-buy ammo never reloads
            // the current magazine).
            wc.RefillReserveAmmo(weaponName);
            Debug.Log("[WallBuy] Refilled reserves for " + weaponName);
        }
        else
        {
            wc.GiveWeapon(BuildWeapon());
            Debug.Log("[WallBuy] Bought " + weaponName);
        }
    }

    // Build the Weapon handed to the player, including the in-hand model so the
    // first-person view model shows when equipped.
    private Weapon BuildWeapon()
    {
        return new Weapon
        {
            weaponName = weaponName,
            damage = damage,
            fireRate = fireRate,
            automatic = automatic,
            range = range,
            spread = spread,
            magazineSize = magazineSize,
            reserveAmmo = reserveAmmo,
            reloadTime = reloadTime,
            weaponModel = weaponModel,
        };
    }
}
