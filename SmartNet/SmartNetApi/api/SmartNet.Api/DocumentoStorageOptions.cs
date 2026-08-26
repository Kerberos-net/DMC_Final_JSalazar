namespace SmartNet.Api;

/// <summary>
/// Shared-storage root resolution (design D2, ADR 0013 "Disco compartido: la base guarda la ruta
/// relativa a una raíz configurable entregada a ambos runtimes"). Same resolution pattern as
/// <see cref="ApiConnectionOptions"/>/<see cref="ApiKeyRingOptions"/> -- an environment variable, no
/// default, no committed fallback, startup failure with a usage message if absent.
///
/// Deliberately a DIFFERENT variable name from the worker's own
/// <c>SMARTNET_WORKER_STORAGE_ROOT</c> (<c>SmartNet/SmartNetWorker/src/smartnet_worker/config.py</c>): both
/// processes read the SAME physical volume (ADR 0013), but each resolves its own path independently
/// -- same reasoning as <see cref="ApiConnectionOptions"/>'s deliberately different variable name
/// from the runner's, so an operator who exports one runtime's value cannot silently misconfigure
/// the other's.
/// </summary>
public static class DocumentoStorageOptions
{
    public const string StorageRootKey = "SMARTNET_API_STORAGE_ROOT";

    public const string Usage =
        $"Missing required configuration '{StorageRootKey}'. Set the {StorageRootKey} environment " +
        "variable to the root directory of the shared document volume (ADR 0013) that " +
        "SmartNet.Worker also writes to.";

    /// <summary>
    /// Resolves the storage root from <paramref name="configuration"/>. Throws
    /// <see cref="InvalidOperationException"/> with <see cref="Usage"/> if absent -- a startup
    /// failure, never a silent default that could serve bytes from an unintended directory.
    /// </summary>
    public static string Resolve(IConfiguration configuration)
    {
        var value = configuration[StorageRootKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(Usage);
        }

        return value;
    }
}
