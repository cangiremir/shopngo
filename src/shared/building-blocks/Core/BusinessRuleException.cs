namespace ShopNGo.BuildingBlocks.Core;

public sealed class BusinessRuleException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
