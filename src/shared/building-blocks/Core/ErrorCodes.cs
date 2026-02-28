namespace ShopNGo.BuildingBlocks.Core;

public static class ErrorCodes
{
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string OrderNotFound = "ORDER_NOT_FOUND";
    public const string OrderInvalidState = "ORDER_INVALID_STATE";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string StockAdmissionLimited = "STOCK_ADMISSION_LIMITED";
    public const string StockGuardrailUnavailable = "STOCK_GUARDRAIL_UNAVAILABLE";
    public const string StockGuardrailBlocked = "STOCK_GUARDRAIL_BLOCKED";
    public const string NotificationInvalidTarget = "NOTIFICATION_INVALID_TARGET";
    public const string NotificationInvalidChannel = "NOTIFICATION_INVALID_CHANNEL";
}
