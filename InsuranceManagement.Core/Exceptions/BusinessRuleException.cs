namespace InsuranceManagement.Core.Exceptions;

public class BusinessRuleException : ApiException
{
    public BusinessRuleException(string code, string message) : base(code, message)
    {
    }
}
