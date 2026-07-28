namespace InsuranceManagement.Core.Exceptions;

public class ConflictException : ApiException
{
    public ConflictException(string code, string message) : base(code, message)
    {
    }
}
