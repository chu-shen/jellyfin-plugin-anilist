﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.AniList.Configuration;

//API v2
namespace Jellyfin.Plugin.AniList.Providers.AniList
{
    public class AniListSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private readonly IApplicationPaths _paths;
        private readonly ILogger<AniListSeriesProvider> _log;
        private readonly AniListApi _aniListApi;
        public int Order => -2;
        public string Name => "AniList";

        public AniListSeriesProvider(IApplicationPaths appPaths, ILogger<AniListSeriesProvider> logger)
        {
            _log = logger;
            _aniListApi = new AniListApi();
            _paths = appPaths;
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            Media media = null;

            var aid = info.ProviderIds.GetOrDefault(ProviderNames.AniList);
            if (!string.IsNullOrEmpty(aid))
            {
                media = await _aniListApi.GetAnime(aid);
            }
            else
            {
                string searchName = info.Name;      
                
                // quick 
//                 searchName = searchName.Split(':')[0]
//                 searchName = searchName.Split('：')[0]
                foreach(string c in "vol", "下巻", "上巻", "EPISODE","第1話")
                    searchName = Rgex.Split(searchName, c, RegexOptions.IgnoreCase)[0];

                
                // read filter remove list from config
                PluginConfiguration config = Plugin.Instance.Configuration;
                string[] filterRemoveList = config.FilterRemoveList.Split(',');
                foreach(string c in filterRemoveList)
                    searchName = Rgex.Replace(searchName, c, "", RegexOptions.IgnoreCase);
                
                // other replace
                searchName = searchName.Replace(".", " ");
                searchName = searchName.Replace("-", " ");
                searchName = searchName.Replace("_", " ");
                searchName = searchName.Replace("+", " ");
                searchName = searchName.Replace("`", "");
                searchName = searchName.Replace("'", "");
                searchName = searchName.Replace("&", " ");
                
                //
                string[] removeTime = searchName.Split(new char[2]{'[',']'});
                Regex onlyNum = new Regex(@"^[0-9]+$");
                searchName = "";
                foreach(string c in removeTime)
                    if (!onlyNum.IsMatch(c))
                    {
                        searchName += c;
                    }
                
                
                //
                searchName = searchName.Replace("(", "");
                searchName = searchName.Replace(")", "");
                searchName = searchName.Replace("[", "");
                searchName = searchName.Replace("]", "");
                searchName = searchName.Replace("【", "");
                searchName = searchName.Replace("】", "");
                
                //
                searchName = searchName.Trim();
                
                //TODO a better strategy
                // anime(title)->romaji;R18 anime(title episode)->japanese
                string numAndLetter = @"^[A-Za-z0-9]+$";
                Regex numAndLetterRegex = new Regex(numAndLetter);
                if (!numAndLetterRegex.IsMatch(searchName))
                {
                    // return string before first space
                    searchName = searchName.Split(' ')[0];
                }
                
                _log.LogInformation("Start AniList ... Searching the correct anime({Name})", searchName);  
                MediaSearchResult msr = await _aniListApi.Search_GetSeries(searchName, cancellationToken);
                if (msr != null)
                {
                    media = await _aniListApi.GetAnime(msr.id.ToString());
                }
            }

            if (media != null)
            {
                result.HasMetadata = true;
                result.Item = media.ToSeries();
                result.People = media.GetPeopleInfo();
                result.Provider = ProviderNames.AniList;
                StoreImageUrl(media.id.ToString(), media.GetImageUrl(), "image");
            }

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            var aid = searchInfo.ProviderIds.GetOrDefault(ProviderNames.AniList);
            if (!string.IsNullOrEmpty(aid))
            {
                Media aid_result = await _aniListApi.GetAnime(aid).ConfigureAwait(false);
                if (aid_result != null)
                {
                    results.Add(aid_result.ToSearchResult());
                }
            }

            if (!string.IsNullOrEmpty(searchInfo.Name))
            {
                List<MediaSearchResult> name_results = await _aniListApi.Search_GetSeries_list(searchInfo.Name, cancellationToken).ConfigureAwait(false);
                foreach (var media in name_results)
                {
                    results.Add(media.ToSearchResult());
                }
            }

            return results;
        }

        private void StoreImageUrl(string series, string url, string type)
        {
            var path = Path.Combine(_paths.CachePath, "anilist", type, series + ".txt");
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);

            File.WriteAllText(path, url);
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = Plugin.Instance.GetHttpClient();

            return await httpClient.GetAsync(url).ConfigureAwait(false);
        }
    }
}
