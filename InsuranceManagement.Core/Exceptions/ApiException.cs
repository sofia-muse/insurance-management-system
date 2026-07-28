namespace InsuranceManagement.Core.Exceptions;

public abstract class ApiException : Exception
{
    protected ApiException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
