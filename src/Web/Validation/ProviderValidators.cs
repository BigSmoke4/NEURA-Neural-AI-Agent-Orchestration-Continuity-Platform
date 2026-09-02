using FluentValidation;

namespace Neura.Web.Validation;

public sealed class ConnectProviderRequest
{
    public Guid UserId { get; set; }
    public string Kind { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string ApiKey { get; set; } = default!;
}

public sealed class ConnectProviderValidator : AbstractValidator<ConnectProviderRequest>
{
    public ConnectProviderValidator()
    {
        RuleFor(x => x.Kind).Must(k => new[] { "OpenAI", "Anthropic", "Google", "LocalModel", "Simulation" }.Contains(k))
            .WithMessage("Unknown provider kind.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.ApiKey).NotEmpty().MinimumLength(8)
            .WithMessage("API key looks too short to be valid — check you copied the full value.");
    }
}
