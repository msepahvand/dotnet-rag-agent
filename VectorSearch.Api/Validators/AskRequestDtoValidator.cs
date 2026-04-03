using FluentValidation;
using VectorSearch.Api.Dtos;

namespace VectorSearch.Api.Validators;

public sealed class AskRequestDtoValidator : AbstractValidator<AskRequestDto>
{
    public AskRequestDtoValidator()
    {
        RuleFor(x => x.Question)
            .NotEmpty()
            .WithMessage("Question cannot be empty");
    }
}
