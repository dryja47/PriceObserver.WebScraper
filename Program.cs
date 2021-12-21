using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HtmlAgilityPack;
using RestSharp;

namespace PriceObserver.WebScraper
{
    class Program
    {
        private static List<Proxy> _proxyList;
        private static int _proxyId;
        private static RestClient _client;
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Working...");

            _client = new RestClient();
            _client.Timeout = 5000;
            _proxyList = await GetProxyList();
            if (_proxyList != null)
            {
                _proxyId = -1;
            }
            
            var startTime = DateTime.Now;
            const string url = "https://korshop.ru";
            var siteMapUrl = await GetSiteMap(url) ?? url + "/sitemap.xml";
            var urlList = await ParseSiteMap(siteMapUrl);

            var products = await GetProducts(urlList);

            var endTime = DateTime.Now;
            Console.WriteLine("Writing file...");
            await ListProducts(products, startTime, endTime);
            
            Console.WriteLine("Complete.");
            Console.ReadKey();

        }

        private static async Task<bool> CheckProxy()
        {
            var page = await GetUrl("https://ya.ru");
            return page != null;
        }

        private static async Task ChangeProxy()
        {
            if (_proxyId != _proxyList.Count - 1)
            {
                _proxyId += 1;
            }
            else
                _proxyId = 0;
            _client = new RestClient();
            _client.Timeout = 20000;
            Console.WriteLine("Changing proxy to #" + _proxyId + ", IP: " + _proxyList[_proxyId].Address);
            _client.Proxy = new WebProxy(_proxyList[_proxyId].Address, Convert.ToInt32(_proxyList[_proxyId].Port))
                { BypassProxyOnLocal = false };
            if (!await CheckProxy())
                await ChangeProxy();
        }

        private static async Task<List<Proxy>> GetProxyList()
        {
            var proxyPage = await GetUrl("https://www.sslproxies.org", false);
            if (proxyPage == null)
                return null;
            var proxyNodes = proxyPage.DocumentNode.SelectSingleNode("//section[@id='list']")
                .SelectNodes("//tbody/tr");
            var proxyList = new List<Proxy>();
            foreach (HtmlNode proxyNode in proxyNodes)
            {
                var proxyRow = proxyNode.SelectNodes("td");
                if (proxyRow[4].InnerText != "transparent" && proxyRow[6].InnerText == "yes")
                {
                    proxyList.Add(new Proxy()
                    {
                        Address = proxyRow[0].InnerText.Replace("\n", "").Trim(),
                        Port = proxyRow[1].InnerText.Replace("\n", "").Trim(),
                        Code = proxyRow[2].InnerText.Replace("\n", "").Trim(),
                        Country = proxyRow[3].InnerText,
                        Anonymity = proxyRow[4].InnerText == "anonymous" ? Proxy.AnonymityLevels.Anonymous : Proxy.AnonymityLevels.Elite,
                        Https = true
                    });
                }
            }

            return proxyList;
        }

        private static async Task ListProducts(List<Product> products, DateTime startTime, DateTime endTime)
        {
            var file = File.Create("E:\\log.txt");
            var fileWriter = new StreamWriter(file);
            await fileWriter.WriteLineAsync(startTime + " -- Started");

            foreach (var product in products)
            {
                await fileWriter.WriteLineAsync("Name: " + product.Name + ", Price: " + product.Price + ", Url: " + product.Url);
            }

            await fileWriter.WriteLineAsync(endTime + " -- Ended");
            await fileWriter.DisposeAsync();
        }
        
        private static async Task<HtmlDocument> GetUrl(string url, bool retry = true)
        {
            IRestResponse response;
            try
            {
                response = await _client.ExecuteAsync(new RestRequest(url, Method.GET));
            }
            catch
            {
                response = null;
            }            

            if (response == null || string.IsNullOrEmpty(response.Content))
            {
                if (!retry) return null;
                await ChangeProxy();
                return await GetUrl(url);
            }
            var pageDocument = new HtmlDocument();
            pageDocument.LoadHtml(response.Content);
            return pageDocument;
        }

        private static async Task<string> GetSiteMap(string url)
        {
            var robots = await GetUrl(url + "/robots.txt");
            if (robots == null) return null;
            var robotsArr = robots.ToString()?.Split("\n");

            return (from robotsLine in robotsArr
                where robotsLine.Contains("Sitemap: ")
                select robotsLine.Replace("Sitemap: ", "")).FirstOrDefault();
        }

        private static async Task<List<string>> ParseSiteMap(string url)
        {
            var siteMap = await GetUrl(url);
            var urlList = new List<string>();

            var locs = siteMap.DocumentNode.SelectNodes("//loc");
            if (locs != null)
            {
                urlList.AddRange(locs.Select(loc => loc.InnerText));
            }

            return urlList;
        }

        private static async Task<List<Product>> GetProducts(List<string> urlList)
        {
            var products = new List<Product>();
            var batchSize = 100;
            int numberOfBatches = (int)Math.Ceiling((double)urlList.Count() / batchSize);
            var pages = new List<HtmlDocument>();
            for(int i = 0; i < numberOfBatches; i++)
            {
                Console.WriteLine("Adding pages from " + i * batchSize + " to " + (i + 1) * batchSize);
                var currentIds = urlList.Skip(i * batchSize).Take(batchSize);
                var tasks = currentIds.Select(url => GetUrl(url));
                pages.AddRange(await Task.WhenAll(tasks));
            }

            foreach (var page in pages)
            {
                var productNodes = page?.DocumentNode.SelectNodes("//*[contains(@itemtype,'schema.org/Product')]");
                if (productNodes == null) continue;
                foreach (var productNode in productNodes)
                {
                    products.Add(new Product
                    {
                        Id = products.Count, Name = GetProductName(productNode), 
                        Price = GetProductPrice(productNode)
                    });
                    Console.WriteLine("Added product #" + products.Count);
                }
            }

            return products;
        }

        private static string GetProductName(HtmlNode productNode)
        {
            var name = productNode.SelectSingleNode(".//*[@itemprop='name']")?.InnerText ?? "";
            if (name != "") return name;
            name = productNode.SelectSingleNode(".//*[@itemprop='name']")?.GetAttributeValue("content", "") ?? "";
            if (name != "") return name;
            name = productNode.SelectSingleNode("//h1")?.InnerText ?? "";
            return name;
        }

        private static float GetProductPrice(HtmlNode productNode)
        {
            float price = 0;
            if (productNode.SelectNodes(".//*[@itemprop='price']") != null)
            {
                if (productNode.SelectSingleNode(".//*[@itemprop='price']").InnerText != "")
                {
                    if (float.TryParse(productNode.SelectSingleNode(".//*[@itemprop='price']").InnerText, out price) &&
                        price > 0)
                        return price;
                }

                price = productNode.SelectSingleNode(".//*[@itemprop='price']").GetAttributeValue("content", 0);
            }

            if (price > 0) return price;
            var priceBlocks = productNode.SelectNodes("//*[contains(@class,'price')]");
            if (priceBlocks != null)
            {
                if (priceBlocks.Any(priceBlock => float.TryParse(priceBlock.InnerText, out price) && price > 0))
                {
                    return price;
                }
            }

            priceBlocks = productNode.SelectNodes("//script");
            if (priceBlocks != null)
            {
                foreach (var priceBlock in priceBlocks)
                {
                    if (priceBlock.InnerHtml.Contains("\"price\":"))
                    {
                        var tmpPrice = priceBlock.InnerHtml.Remove(0,
                            priceBlock.InnerHtml.IndexOf("\"price\":", StringComparison.Ordinal) + 8);
                        tmpPrice = tmpPrice.Remove(tmpPrice.IndexOf(','));
                        if (float.TryParse(tmpPrice.Trim(), out price) && price > 0)
                        {
                            return price;
                        }
                    }
                }
            }

            return 0;
        }
    }
}
