using RestSharp;
using TenorSharp;

namespace Valour.Client.Tenor;

public class TenorService
{
    /// <summary>
    /// The key for Valour using the Tenor API
    /// </summary>
    private const string TenorKey = "AIzaSyCpYasE9IZNecc7ZPEjHTpOVssJT1aUC_4";
    
    /// <summary>
    /// Client for interacting with the Tenor API
    /// </summary>
    public TenorClient Client => _client;
    
    private readonly HttpClient _httpClient;
    private readonly TenorClient _client;

    public TenorService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        var rest = new RestClient(_httpClient, new RestClientOptions("https://tenor.googleapis.com/v2/"));

        _client = new TenorClient(TenorKey, testClient: rest);
    }
}