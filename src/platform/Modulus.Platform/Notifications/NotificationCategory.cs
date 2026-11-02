namespace Modulus.Notifications;

using System.Text.RegularExpressions;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>Checks a notification category: dotted lower-case words such as <c>orders.shipped</c>.</summary>
public static class NotificationCategory
{
    public const int MaxLength = 100;

    private static readonly Regex Pattern = new(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)*$", RegexOptions.CultureInvariant);

    /// <summary>Returns the category, or throws <see cref="ValidationException"/> when it is not dotted lower-case words.</summary>
    public static string Require(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || category.Length > MaxLength || !Pattern.IsMatch(category))
            throw new ValidationException([$"A category is dotted lower-case words, such as orders.shipped, at most {MaxLength} characters."]);
        return category;
    }
}
