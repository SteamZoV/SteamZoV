﻿using HtmlAgilityPack;
using LogicObjects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TryParseSteam.LogicObjects;

namespace TryParseSteam
{
    public enum eProxyRegion
    {
        NONE,
        USA,
        TUR,
        KZ
    }

    public class PageReader
    {
        public PageReader(eProxyRegion proxyType)
        {
            switch (proxyType)
            {
                case eProxyRegion.NONE:
                    proxy = null;
                    _currency = "&cc=ru";
                    break;
                case eProxyRegion.USA:
                    proxy = new WebProxy(new Uri("http://80.73.244.40:59244"), true, null, new NetworkCredential("ehG9tnGk", "YFUE4KS3"));
                    _currency = "&cc=us";

                    break;
                case eProxyRegion.KZ:
                    proxy = new WebProxy(new Uri("http://45.152.214.36:52900"), true, null, new NetworkCredential("ehG9tnGk", "YFUE4KS3"));
                    _currency = "&cc=kz";

                    break;
                case eProxyRegion.TUR:
                    proxy = new WebProxy(new Uri("http://45.149.131.12:48563"), true, null, new NetworkCredential("ehG9tnGk", "YFUE4KS3"));
                    _currency = "&cc=tr";

                    break;
            }

        }

        #region Fields

        LinkedList<string> plainText = new LinkedList<string>();
        object locker = new object();
        string _json = "";
        string[] _resultPrices;
        string _currency = "";
        string steamAccountUrl = "https://steam-account.ru/steam_kluchi_kupit/page/";
        string steamKeyUrl = "https://steamkey.com/catalog?category=3&page=200";
        //string steamBuyUrl = "https://steambuy.com/catalog/?platforms=windows&activation=Steam&page=";
        string onlyNamesUrl = "http://api.steampowered.com/ISteamApps/GetAppList/v0002/?key=STEAMKEY&format=json";
        string url = "https://store.steampowered.com/search/results/?page=1&count=100&sort_by=_ASC&ignore_preferences=1";
        ConcurrentBag<GameItem> _items = new ConcurrentBag<GameItem>();
        WebProxy proxy = new WebProxy(new Uri("http://80.73.244.40:59244"), true, null, new NetworkCredential("ehG9tnGk", "YFUE4KS3"));
        NameList _simpleList = null;

        #endregion

        public ConcurrentBag<GameItem> Items { get => _items; set => _items = value; }
        public List<App> SimpleList { get => _simpleList!=null? _simpleList.applist.apps:null;  }
        public string ResultJsonIDs { get => _json; set => _json = value; }
        public string[] ResultPrices { get => _resultPrices; set => _resultPrices = value; }
        public ConcurrentBag<OtherSiteItem> OtherSiteItems { get => _itemsSteamAccount; set => _itemsSteamAccount = value; }

        ConcurrentBag<OtherSiteItem> _itemsSteamAccount = new ConcurrentBag<OtherSiteItem>();
        Regex regex = new Regex(@"^[0-9]*(?:\.[0-9]+)?$");

        public void ReadSteamAccountPages()
        {
            try
            {
                using (HttpClientHandler hdl = new HttpClientHandler
                {
                    AllowAutoRedirect = false
                    ,
                    AutomaticDecompression = System.Net.DecompressionMethods.Deflate
                    | System.Net.DecompressionMethods.GZip
                    | System.Net.DecompressionMethods.None
                })
                {

                    using (var clnt = new HttpClient(hdl) { Timeout = TimeSpan.FromMinutes(5) })
                    {
                        Parallel.For(0, 50,  (int currentPage, ParallelLoopState state) =>
                        {
                            using (HttpResponseMessage resp = clnt.GetAsync(steamAccountUrl + currentPage).Result)
                            {
                                if (resp.IsSuccessStatusCode)
                                {
                                    var html = resp.Content.ReadAsStringAsync().Result;
                                    if (!string.IsNullOrEmpty(html))
                                    {
                                        HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                                        doc.LoadHtml(html);
                                        var nodes = doc.DocumentNode.Descendants().Where(x => x.HasClass("game-item"));
                                        if (nodes.Count() == 0)
                                            return;
                                        foreach(var node in nodes)
                                        {
                                            string href = node.Descendants("a").SingleOrDefault().GetAttributeValue("href","");
                                            string title = node.Descendants("a").SingleOrDefault().GetAttributeValue("title", "no-title");
                                            string priceStr = node.Descendants().Where(x => x.HasClass("price-span")).SingleOrDefault().InnerText;
                                            //string priseTrimmed = Regex.Match(priceStr, "[0-9]+[.[0-9]+]?").Value;
                                            float price = (float)Math.Round(Convert.ToDouble(priceStr.Split(' ')[0], CultureInfo.InvariantCulture), 3) ;
                                            //float price = (float)Convert.ToDouble(.Value);
                                            //if(title.Contains("Metal"))
                                            //{
                                            //    Debug.WriteLine(currentPage);
                                            //}
                                            if(_itemsSteamAccount.Where(x=>x.Name==title).FirstOrDefault()==null)
                                               _itemsSteamAccount.Add(new OtherSiteItem(title, href, price));
                                        }
                                    }
                                }
                            }
                        });
                        int b = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        public void ReadSteamKeyPages()
        {
            _itemsSteamAccount = new ConcurrentBag<OtherSiteItem>();
            var options = new ChromeOptions();
            options.AddArguments(new List<string>() { "headless", "disable-gpu" });
            var chromeDriverService = ChromeDriverService.CreateDefaultService();
            chromeDriverService.HideCommandPromptWindow = true;
            var browser = new ChromeDriver(chromeDriverService, options);
            browser.Navigate().GoToUrl(steamKeyUrl);
            Thread.Sleep(2000);

            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(browser.PageSource);
            var nodes = doc.DocumentNode.Descendants().Where(x => x.HasClass("product"));
            foreach(var node in nodes)
            {
                string href = node.Descendants("a").FirstOrDefault().GetAttributeValue("href", "");
                string title = node.Descendants().Where(x => x.HasClass("product-title")).FirstOrDefault().InnerHtml;
                string priceStr = node.Descendants().Where(x => x.HasClass("product-price")).FirstOrDefault().InnerHtml;
                float price = (float)Math.Round(Convert.ToDouble(priceStr.Split(' ')[0], CultureInfo.InvariantCulture), 3);
                _itemsSteamAccount.Add(new OtherSiteItem(title, href, price));
            }
        }

        public void ReadAllNamesProxy()
        {
            try
            {
                using (HttpClientHandler hdl = new HttpClientHandler
                {
                    Proxy = proxy,
                    AllowAutoRedirect = false
                    ,
                    AutomaticDecompression = System.Net.DecompressionMethods.Deflate
                    | System.Net.DecompressionMethods.GZip
                    | System.Net.DecompressionMethods.None
                })
                {
                    ProcessHttpClient(hdl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
        
        public void ReadAllPrices(string[] querryArray)
        {
            try
            {
                using (HttpClientHandler hdl = new HttpClientHandler
                {
                    Proxy = proxy,
                    AllowAutoRedirect = false
                    ,
                    AutomaticDecompression = System.Net.DecompressionMethods.Deflate
                    | System.Net.DecompressionMethods.GZip
                    | System.Net.DecompressionMethods.None
                })
                {
                    using (var clnt = new HttpClient(hdl) { Timeout = TimeSpan.FromMinutes(5) })
                    {
                        _resultPrices = new string[querryArray.Length];
                        Parallel.For(0, querryArray.Length, i =>
                        {

                            using (HttpResponseMessage resp = clnt.GetAsync(querryArray[i]+_currency).Result)
                            {
                                if (resp.IsSuccessStatusCode)
                                {
                                    var html = resp.Content.ReadAsStringAsync().Result;
                                    if (!string.IsNullOrEmpty(html))
                                    {
                                        _resultPrices[i] = html;

                                        //Debug.WriteLine(i);
                                    }
                                }
                                else
                                {
                                    throw new Exception("Bad request");
                                }
                            }

                        });
                        //for (int i = 0; i < querryArray.Length; i++)
                        //{

                        //}

                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private void ProcessHttpClient(HttpClientHandler hdl)
        {
            using (var clnt = new HttpClient(hdl) { Timeout = TimeSpan.FromMinutes(5) })
            {
                using (HttpResponseMessage resp = clnt.GetAsync(onlyNamesUrl).Result)
                {
                    if (resp.IsSuccessStatusCode)
                    {
                        ReadJson(resp);
                    }
                }
            }
        }
        

        private void ReadJson(HttpResponseMessage resp)
        {
            var html = resp.Content.ReadAsStringAsync().Result;
            if (!string.IsNullOrEmpty(html))
            {
                _json = html;
            }
        }

        void ReadPageProxyAsText(string urlPage, int page)
        {
            try
            {
                using (HttpClientHandler hdl = new HttpClientHandler
                {
                    Proxy = proxy,
                    AllowAutoRedirect = false
                    ,
                    AutomaticDecompression = System.Net.DecompressionMethods.Deflate
                    | System.Net.DecompressionMethods.GZip
                    | System.Net.DecompressionMethods.None
                })
                {
                    using (var clnt = new HttpClient(hdl) { Timeout = TimeSpan.FromMinutes(1) })
                    {


                        using (HttpResponseMessage resp = clnt.GetAsync(urlPage).Result)
                        {

                            if (resp.IsSuccessStatusCode)
                            {
                                var html = resp.Content.ReadAsStringAsync().Result;
                                if (!string.IsNullOrEmpty(html))
                                {
                                    HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                                    doc.LoadHtml(html);
                                    string parsedJson = doc.DocumentNode.Descendants().Where(x => x.HasClass("ignore_preferences")).FirstOrDefault().Descendants("div").ToList()[2].InnerHtml; ;
                                    lock (locker)
                                    {
                                        plainText.AddLast(parsedJson);
                                    }

                                }
                                else
                                {
                                    Debug.WriteLine("#### EMPTY HTML");
                                }
                            }
                            else
                            {
                                Debug.WriteLine("#### ERROR ACCESS");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.InnerException);
            }
        }
        void ParseString()
        {

            //plainText.Clear();
            
            Parallel.ForEach(plainText, textBlock => 
            {
                HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(textBlock);
                IEnumerable<HtmlNode> nodes = doc.DocumentNode.Descendants("a");
                foreach(var node in nodes)
                {
                    var title = node.Descendants().Where(x => x.HasClass("title")).FirstOrDefault();
                    var pri = node.Descendants().Where(x => x.HasClass("search_price")).FirstOrDefault();
                    //var disc = n.Descendants().Where(x => x.HasClass("search_discount")).FirstOrDefault();
                    var img = node.Descendants("img").FirstOrDefault();
                    string app_id = node.GetAttributeValue("data-ds-appid", "no-id");
                    string bundle_id = node.GetAttributeValue("data-ds-bundleid", "no-id");
                    string name = "";
                    string price = "";
                    string discount = "";
                    string image = "";

                    if (title != null)
                        name = title.InnerHtml;
                    if (pri != null)
                        price = pri.InnerHtml.Contains("strike") ? pri.InnerHtml.Split('>').Last() : pri.InnerHtml;
                    if (img != null)
                        image = img.GetAttributeValue("src", "no-image");



                    _items.Add(new GameItem(name, price, discount, image, app_id, bundle_id));
                }

            });

        }

        void ParseStringSimple()
        {

            //plainText.Clear();

            Parallel.ForEach(plainText, textBlock =>
            {
                HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(textBlock);
                IEnumerable<HtmlNode> nodes = doc.DocumentNode.Descendants("a");
                foreach (var node in nodes)
                {
                    //var title = node.Descendants().Where(x => x.HasClass("title")).FirstOrDefault();
                    var pri = node.Descendants().Where(x => x.HasClass("search_price")).FirstOrDefault();
                    //var disc = n.Descendants().Where(x => x.HasClass("search_discount")).FirstOrDefault();
                    //var img = node.Descendants("img").FirstOrDefault();
                    string app_id = node.GetAttributeValue("data-ds-appid", "no-id");
                    string bundle_id = node.GetAttributeValue("data-ds-bundleid", "no-id");
                    //string name = "";
                    string price = "";
                    //string discount = "";
                    //string image = "";

                    //if (title != null)
                    //    name = title.InnerHtml;
                    if (pri != null)
                        price = pri.InnerHtml.Contains("strike") ? pri.InnerHtml.Split('>').Last() : pri.InnerHtml;
                    //if (img != null)
                    //    image = img.GetAttributeValue("src", "no-image");



                    _items.Add(new GameItem("", price, "", "", app_id, bundle_id));
                }

            });

        }

        void ReadPageProxy(string url, int page)
        {
            try
            {
                using (HttpClientHandler hdl = new HttpClientHandler
                {
                    Proxy = proxy,
                    AllowAutoRedirect = false
                    ,
                    AutomaticDecompression = System.Net.DecompressionMethods.Deflate
                    | System.Net.DecompressionMethods.GZip
                    | System.Net.DecompressionMethods.None
                })
                {
                    using (var clnt = new HttpClient(hdl) { Timeout = TimeSpan.FromMinutes(1)})
                    {


                        using (HttpResponseMessage resp = clnt.GetAsync(url).Result)
                        {

                            if (resp.IsSuccessStatusCode)
                            {
                                var html = resp.Content.ReadAsStringAsync().Result;
                                if (!string.IsNullOrEmpty(html))
                                {
                                    HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                                    dynamic res = JObject.Parse(html);
                                    string parsedJson = res.results_html;

                                    doc.LoadHtml(parsedJson);
                                    IEnumerable<HtmlNode> nodes = doc.DocumentNode.Descendants("a");

                                    AddItemsToList(nodes);
                                }
                                else
                                {
                                    Debug.WriteLine("#### EMPTY HTML");
                                }
                            }
                            else
                            {
                                Debug.WriteLine("#### ERROR ACCESS");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.InnerException);
            }
        }

        private void AddItemsToList(IEnumerable<HtmlNode> nodes)
        {
            foreach (var n in nodes)
            {
                var title = n.Descendants().Where(x => x.HasClass("title")).FirstOrDefault();
                var pri = n.Descendants().Where(x => x.HasClass("search_price")).FirstOrDefault();
                //var disc = n.Descendants().Where(x => x.HasClass("search_discount")).FirstOrDefault();
                var img = n.Descendants("img").FirstOrDefault();
                string app_id = n.GetAttributeValue("data-ds-appid", "no-id");
                string bundle_id = n.GetAttributeValue("data-ds-bundleid", "no-id");
                string name = "";
                string price = "";
                string discount = "";
                string image = "";

                if (title != null)
                    name = title.InnerHtml;
                if (pri != null)
                    price = pri.InnerHtml.Contains("strike") ? pri.InnerHtml.Split('>').Last() : pri.InnerHtml;
                if (img != null)
                    image = img.GetAttributeValue("src", "no-image");



                _items.Add(new GameItem(name, price, discount, image, app_id, bundle_id));
            }
        }

    }
}
