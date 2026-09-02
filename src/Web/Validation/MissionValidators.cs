using FluentValidation;

namespace Neura.Web.Validation;

public sealed class CreateMissionRequest
{
    public string Title { get; set; } = default!;
    public string Objective { get; set; } = default!;
    public string Mode { get; set; } = "Simulation";
}

public sealed class CreateMissionValidator : AbstractValidator<CreateMissionRequest>
{
    public CreateMissionValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Objective).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Mode).Must(m => m is "Simulation" or "Real")
            .WithMessage("Mode must be 'Simulation' or 'Real'.");
    }
}
