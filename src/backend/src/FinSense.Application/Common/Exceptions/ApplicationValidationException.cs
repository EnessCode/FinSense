using FluentValidation.Results;

namespace FinSense.Application.Common.Exceptions;

public class ApplicationValidationException : Exception
{
    public string ErrorCode => "VALIDATION_FAILED";
    public IDictionary<string, string[]> Errors { get; }

    public ApplicationValidationException()
        : base("Bir veya daha fazla doğrulama hatası oluştu.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ApplicationValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = failures
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(
                failureGroup => string.IsNullOrEmpty(failureGroup.Key)
                    ? "general"
                    : char.ToLowerInvariant(failureGroup.Key[0]) + failureGroup.Key[1..],
                failureGroup => failureGroup.Distinct().ToArray());
    }
}
