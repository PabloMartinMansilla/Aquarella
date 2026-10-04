using System.ComponentModel.DataAnnotations;

namespace Aquarella.Models;

// Optional contact values validate only when the user supplies a value.
public sealed class OptionalUrlAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => string.IsNullOrWhiteSpace(value as string) || new UrlAttribute().IsValid(value);
}

public sealed class OptionalEmailAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => string.IsNullOrWhiteSpace(value as string) || new EmailAddressAttribute().IsValid(value);
}
