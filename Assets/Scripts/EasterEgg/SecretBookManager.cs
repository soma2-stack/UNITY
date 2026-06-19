using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks the secret Pack-a-Punch easter egg: the player must find 6 books hidden
/// around the map. When all 6 are collected the hidden bookcase slides aside to
/// reveal the stairwell down to the teleporter room / Pack-a-Punch, and a
/// "A Door has opened" message appears bottom-left.
/// </summary>
public class SecretBookManager : MonoBehaviour
{
    public static SecretBookManager Instance { get; private set; }

    public const int TotalBooks = 6;

    public int Collected { get; private set; }
    public bool Unlocked { get; private set; }

    /// <summary>Raised when a book is collected, passing (collected, total).</summary>
    public static event System.Action<int, int> OnProgressChanged;

    private readonly HashSet<BookPickup> collectedBooks = new HashSet<BookPickup>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>Call when a book pickup is collected. Ignores duplicates.</summary>
    public void CollectBook(BookPickup book)
    {
        if (book == null || Unlocked)
        {
            return;
        }

        if (!collectedBooks.Add(book))
        {
            return; // already counted
        }

        Collected = Mathf.Min(Collected + 1, TotalBooks);
        OnProgressChanged?.Invoke(Collected, TotalBooks);

        if (Collected >= TotalBooks)
        {
            Unlock();
        }
        else
        {
            SecretEggHud.Ensure().ShowMessage($"BOOK  {Collected} / {TotalBooks}", 3f);
        }
    }

    private void Unlock()
    {
        if (Unlocked)
        {
            return;
        }
        Unlocked = true;

        // Slide every bookcase aside to reveal the secret stairwell.
        SecretBookcase[] bookcases = FindObjectsByType<SecretBookcase>(FindObjectsSortMode.None);
        foreach (SecretBookcase bookcase in bookcases)
        {
            if (bookcase != null)
            {
                bookcase.Open();
            }
        }

        SecretEggHud.Ensure().ShowMessage("A Door has opened", 5f);
    }
}
