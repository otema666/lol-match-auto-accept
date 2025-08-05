using System.Net.Http;
using System.Threading.Tasks;

namespace LoLAutoWinForms
{
    public static class HttpClientExtensions
    {
        public static Task<HttpResponseMessage> PatchAsync(
            this HttpClient client,
            string requestUri,
            HttpContent content)
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUri)
            {
                Content = content
            };
            return client.SendAsync(request);
        }
    }
}
