using FluentValidation;

namespace Neura.Web.Validation;

public sealed class CreateAgentRequest
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public Guid ProviderAccountId { get; set; }
    public string ModelId { get; set; } = default!;
    public string Role { get; set; } = default!;
    public int ContextCapacityTokens { get; set; }
    public string[] Capabilities { get; set; } = Array.Empty<string>();
}

public sealed class CreateAgentValidator : AbstractValidator<CreateAgentRequest>
{
    public CreateAgentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.ModelId).NotEmpty();
        RuleFor(x => x.Role).NotEmpty();
        RuleFor(x => x.ContextCapacityTokens).GreaterThan(0);
        RuleFor(x => x.Capabilities).NotEmpty()
            .WithMessage("An agent must declare at least one capability.");
    }
}
