using FluentValidation;
using RagAgent.Api.Dtos;

namespace RagAgent.Api.Validators;

public sealed class AskRequestDtoValidator : AbstractValidator<AskRequestDto>
{
    public AskRequestDtoValidator()
    {
        RuleFor(x => x.Question)
            .NotEmpty()
            .WithMessage("Question cannot be empty");
    }
}
