using System.ComponentModel.DataAnnotations;

namespace ShopNGo.BuildingBlocks.Core;

public static class ValidationExtensions
{
    public static Dictionary<string, string[]>? ValidateWithDataAnnotations<T>(this T instance)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(instance!);
        if (Validator.TryValidateObject(instance!, context, results, validateAllProperties: true))
        {
            return null;
        }

        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [string.Empty]).Select(member => new { member, r.ErrorMessage }))
            .GroupBy(x => x.member)
            .ToDictionary(
                g => string.IsNullOrWhiteSpace(g.Key) ? "request" : g.Key,
                g => g.Select(x => x.ErrorMessage ?? "Validation error").Distinct().ToArray());
    }
}
