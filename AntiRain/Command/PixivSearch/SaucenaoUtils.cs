﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using PyLibSharp.Requests;
using Sora.Entities.CQCodes;
using Sora.EventArgs.SoraEvent;
using YukariToolBox.FormatLog;

namespace AntiRain.Command.PixivSearch
{
    public static class SaucenaoUtils
    {
        public static async ValueTask<List<CQCode>> SearchByUrl(string apiKey, string url,
                                                                GroupMessageEventArgs eventArgs)
        {
            List<CQCode> message = new();
            Log.Debug("pic", "send api request");
            var req =
                await
                    Requests.PostAsync($"http://saucenao.com/search.php?output_type=2&numres=16&db=5&api_key={apiKey}&url={url}",
                                       new ReqParams {Timeout = 20000});

            var res     = req.Json();
            var resCode = Convert.ToInt32(res?["header"]?["status"] ?? -1);
            Log.Debug("pic", $"get api result code [{resCode}]");

            if (res == null || resCode != 0)
            {
                message.Add(CQCode.CQAt(eventArgs.Sender));
                message.Add(CQCode.CQText("图片获取失败"));
                return message;
            }

            var resData = res["results"]?.ToObject<List<SaucenaoResult>>();

            if (resData == null)
            {
                message.Add(CQCode.CQAt(eventArgs.Sender));
                message.Add(CQCode.CQText("处理API返回发生错误"));
                return message;
            }

            if (resData.Count == 0)
            {
                message.Add(CQCode.CQAt(eventArgs.Sender));
                message.Add(CQCode.CQText("查询到的图片相似度过低，请尝试别的图片"));
                return message;
            }

            var parsedPic = resData.OrderByDescending(pic => Convert.ToDouble(pic.Header.Similarity))
                                   .First();


            var (success, msg, urls) = await GetPixivCatInfo(parsedPic.PixivData.PixivId);
            if (!success)
            {
                message.Add(CQCode.CQAt(eventArgs.Sender));
                message.Add(CQCode.CQText($"处理代理连接发生错误\r\nApi Message:{msg}"));
                return message;
            }

            message.Add(CQCode.CQAt(eventArgs.Sender));
            message.Add(CQCode.CQText("\r\n"));
            message.Add(CQCode.CQText($"图片名:{parsedPic.PixivData.Title}\r\n"));
            message.Add(CQCode.CQImage(urls[0]));
            message.Add(CQCode.CQText($"id:{parsedPic.PixivData.PixivId}\r\n"));
            message.Add(CQCode.CQText($"相似度:{parsedPic.Header.Similarity}%"));


            return message;
        }

        /// <summary>
        /// PixivCat代理连接生成
        /// </summary>
        /// <param name="pid">pid</param>
        private static async ValueTask<(bool success, string message, List<string> urls)>
            GetPixivCatInfo(long pid)
        {
            try
            {
                var res = await Requests.PostAsync("https://api.pixiv.cat/v1/generate", new ReqParams
                {
                    Header = new Dictionary<HttpRequestHeader, string>
                    {
                        {HttpRequestHeader.ContentType, "application/x-www-form-urlencoded; charset=UTF-8"}
                    },
                    PostContent =
                        new FormUrlEncodedContent(new[] {new KeyValuePair<string, string>("p", pid.ToString())}),
                    Timeout = 5000
                });
                if (res.StatusCode != HttpStatusCode.OK) return (false, $"pixivcat respose ({res.StatusCode})", null);
                //检查返回数据
                var proxyJson = res.Json();
                if (proxyJson == null) return (false, "get null respose from pixivcat", null);
                if (!Convert.ToBoolean(proxyJson["success"] ?? false))
                    return (false, $"pixivcat failed({proxyJson["error"]})", null);
                //是否为多张图片
                var urls = Convert.ToBoolean(proxyJson["multiple"] ?? false)
                    ? proxyJson["original_urls_proxy"]?.ToObject<List<string>>()
                    : new List<string> {proxyJson["original_url_proxy"]?.ToString() ?? string.Empty};
                return (true, "OK", urls);
            }
            catch (Exception e)
            {
                Log.Error("pixiv api error", Log.ErrorLogBuilder(e));
                return (false, $"pixiv api error ({e})", null);
            }
        }
    }
}