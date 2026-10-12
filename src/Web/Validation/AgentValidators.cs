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
        RuleFor(x => x.Description).MaximumLength(2_000);
        RuleFor(x => x.ProviderAccountId).NotEmpty();
        RuleFor(x => x.ModelId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).NotEmpty().MaximumLength(120);
        RuleFor(x => x.ContextCapacityTokens).InclusiveBetween(1_000, 10_000_000);
        RuleFor(x => x.Capabilities)
            .NotEmpty().WithMessage("An agent must declare at least one capability.")
            .Must(values => values.All(value => Enum.TryParse<Neura.Modules.AgentManagement.Domain.AgentCapability>(value, true, out var capability) && Enum.IsDefined(capability)))
            .WithMessage("One or more agent capabilities are not recognized.");
    }
}
