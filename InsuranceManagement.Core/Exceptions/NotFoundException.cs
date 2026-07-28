namespace InsuranceManagement.Core.Exceptions;

public class NotFoundException : ApiException
{
    public NotFoundException(string code, string message) : base(code, message)
    {
    }
}
