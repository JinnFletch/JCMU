using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.AddonManager.Models;

/// <summary>
/// Defines the static configuration for how the addon should be registered in the Windows Context Menu.
/// </summary>
public record MenuDefinition
{
    /// <summary>
    /// The text displayed to the user in the right-click menu.
    /// </summary>
    public required string MenuItemName { get; init; }

    /// <summary>
    /// An optional absolute path to an .ico file to display next to the menu item.
    /// </summary>
    public string? IconPath { get; init; }

    /// <summary>
    /// Determines the sorting order of this item relative to other items in the same menu.
    /// Lower numbers appear higher in the list.
    /// </summary>
    public required int Ordinal { get; init; }

    /// <summary>
    /// The name of the sub-menu folder this item should be grouped into (e.g., "Docker Tools").
    /// If null, the item is placed directly in the main root menu.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional child menus. If populated, clicking this item will expand a sub-menu 
    /// instead of immediately executing logic.
    /// </summary>
    public IEnumerable<MenuDefinition>? SubItems { get; init; }

    /// <summary>
    /// If true, the JCMU Core will execute this addon in the background without 
    /// showing a console window. If false, a console window will appear.
    /// </summary>
    public bool RunInBackground { get; init; } = false;

    /// <summary>
    /// Validates the menu definition and its sub-items recursively.
    /// </summary>
    /// <returns>A parameterless monad representing the validation result.</returns>
    public Maybe Validate()
    {
        if (string.IsNullOrWhiteSpace(MenuItemName))
            return Maybe.Fail("MenuItemName cannot be null or whitespace.");

        // Ensure categories don't have crazy characters that break the Registry
        if (!string.IsNullOrWhiteSpace(Category) && Category.Any(c => !char.IsLetterOrDigit(c) && c != ' '))
            return Maybe.Fail("Category names must contain only letters, numbers, and spaces.");

        if (SubItems == null || !SubItems.Any())
            return Maybe.SUCCESS;

        // Evaluates all validations and returns a single Success Maybe 
        // ONLY if every item in the sequence succeeded. 
        // (If one fails, Sequence() returns that specific failure).
        return SubItems
            .Select(sub => sub.Validate())
            .Sequence();
    }
}
