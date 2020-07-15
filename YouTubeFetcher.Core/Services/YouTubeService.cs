﻿using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using YouTubeFetcher.Core.DTOs;
using YouTubeFetcher.Core.Exceptions;
using YouTubeFetcher.Core.Factories.Interfaces;
using YouTubeFetcher.Core.Services.Interfaces;
using YouTubeFetcher.Core.Settings;

namespace YouTubeFetcher.Core.Services
{
    public class YouTubeService : IYouTubeService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDecryptorService _decryptorService;
        private readonly YouTubeSettings _settings;

        public YouTubeService(IHttpClientFactory httpClientFactory, IDecryptorService decryptorService, IOptions<YouTubeSettings> options)
        {
            _httpClientFactory = httpClientFactory;
            _decryptorService = decryptorService;
            _settings = options.Value;
        }

        public async Task<VideoInformation?> GetInformationAsync(string id)
        {
            using var client = _httpClientFactory.CreateClient();
            var result = await client.GetAsync(string.Format(_settings.VideoInfoUri.OriginalString, id));
            if (!result.IsSuccessStatusCode)
                throw new YouTubeServiceException($"There was a problem fetching video information for {id}. {result.ReasonPhrase}");

            var content = await result.Content.ReadAsStringAsync();
            if (content.Contains(_settings.ErrorCodeKey))
                return null; // if the errorcode is given the video wasn't found by the api endpoint

            var query = HttpUtility.ParseQueryString(content);
            var playerResponse = query.Get(_settings.PlayerResponseKey);
            if (string.IsNullOrEmpty(playerResponse))
                throw new YouTubeServiceException($"Couldn't find player response for {id}");

            return JsonConvert.DeserializeObject<VideoInformation>(playerResponse);
        }

        public async Task<VideoDetail?> GetVideoDetailsAsync(string id)
        {
            var videoInformation = await GetInformationAsync(id);
            if (!videoInformation.HasValue)
                return null;

            return videoInformation.Value.VideoDetails;
        }

        public async Task<StreamingData?> GetStreamingDataAsync(string id)
        {
            var videoInformation = await GetInformationAsync(id);
            if (!videoInformation.HasValue)
                return null;

            return videoInformation.Value.StreamingData;
        }

        public async Task<Format?> GetFormatByITagAsync(string id, int itag)
        {
            var streamingData = await GetStreamingDataAsync(id);
            if (!streamingData.HasValue)
                return null;

            var streamingDataVal = streamingData.Value;
            var format = streamingDataVal.Formats.Concat(streamingDataVal.AdaptiveFormats).FirstOrDefault(f => f.ITag == itag);
            if (format.ITag == default) // If the itag has its default value (0) then the format wasn't found. A null check isn't possible because a struct can never be null unless ist a nullable struct
                return null;

            return format;
        }

        public async Task<Stream> GetStreamAsync(string id, int itag)
        {
            var format = await GetFormatByITagAsync(id, itag);
            if (!format.HasValue)
                return null;

            return await GetStreamAsync(id, format.Value.Location);
        }

        public async Task<Stream> GetStreamAsync(string id, Location location)
        {
            var url = await GetStreamUrlAsync(id, location);
            using var client = _httpClientFactory.CreateClient();
            return await client.GetStreamAsync(url);
        }

        public async Task<string> GetStreamUrlAsync(string id, int itag)
        {
            var format = await GetFormatByITagAsync(id, itag);
            if (!format.HasValue)
                return null;

            return await GetStreamUrlAsync(id, format.Value.Location);
        }

        public async Task<string> GetStreamUrlAsync(string id, Location location)
        {
            if (!location.IsEncrypted)
                return location.Url;

            var jsPlayer = await GetJsPlayerAsync(id);
            return _decryptorService.DecryptLocation(jsPlayer, location).Url;
        }

        private async Task<string> GetJsPlayerAsync(string id)
        {
            using var client = _httpClientFactory.CreateClient();
            var result = await client.GetAsync(string.Format(_settings.VideoUri.OriginalString, id));
            if (!result.IsSuccessStatusCode)
                throw new YouTubeServiceException($"The embed site for {id} couldn't be loaded");

            var content = await result.Content.ReadAsStringAsync();
            string jsPlayerUrlRelative = GetJsPlayerUrl(content);
            if (string.IsNullOrWhiteSpace(jsPlayerUrlRelative))
                throw new YouTubeServiceException($"The JsPlayer url wasn't found in the embedded site");

            result = await client.GetAsync(new Uri(_settings.BaseUri, jsPlayerUrlRelative).AbsoluteUri);
            if (!result.IsSuccessStatusCode)
                throw new YouTubeServiceException("Couldn't get the JsPlayer over the given url");

            return await result.Content.ReadAsStringAsync();
        }

        private string GetJsPlayerUrl(string content)
        {
            var contentWithKey = content.Split('<', '>').FirstOrDefault(c => c.Contains(_settings.JsPlayerKey));
            if (string.IsNullOrEmpty(contentWithKey))
                throw new YouTubeServiceException($"The key {_settings.JsPlayerKey} wasn't present in the content");

            var srcFindKey = "src=\"";
            var fromSrcValue = contentWithKey.Substring(contentWithKey.IndexOf(srcFindKey) + srcFindKey.Length);
            var resultLink = fromSrcValue.Substring(0, fromSrcValue.IndexOf("\""));

            return resultLink ?? string.Empty;
        }
    }
}