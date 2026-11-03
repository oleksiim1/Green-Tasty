using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Api;

public sealed class WaitersApi(IAPIRequestContext api) : ApiClientBase(api)
{
    public Task<IAPIResponse> GetLocationAsync(string userId, string bearerToken) =>
        GetAsync(Url($"/waiters/{userId}/location"), new APIRequestContextOptions { Headers = Headers(bearerToken) });
}
