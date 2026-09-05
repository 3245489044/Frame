
namespace TConfig.Abstractions
{
    public interface IJsonValidator
    {
        JsonValidationResult ValidateSyntax(string json);
        JsonValidationResult ValidateStructure<T>(string json);
        JsonValidationResult ValidateModel<T>(T model);
    }
}
