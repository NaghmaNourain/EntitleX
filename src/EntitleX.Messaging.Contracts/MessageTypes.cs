namespace EntitleX.Messaging.Contracts;

public static class MessageTypes
{
    public const string FulfillmentCommand = "entitlex.fulfillment.command";
    public const string ProviderLicenseExtensionCommand = "entitlex.provider.license-extension.command";
    public const string ProviderLicenseExtensionCompleted = "entitlex.provider.license-extension.completed";
    public const string ProviderLicenseExtensionFailed = "entitlex.provider.license-extension.failed";
}
