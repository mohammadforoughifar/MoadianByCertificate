namespace Inventory.Client.Services;

// Minimal transport contract for compiling the actual ListOrPaged parser in this
// relational console test. No production HTTP calls or credentials are needed.
public interface IApiClient
{
    Task<T> GetAsync<T>(string path);
}
