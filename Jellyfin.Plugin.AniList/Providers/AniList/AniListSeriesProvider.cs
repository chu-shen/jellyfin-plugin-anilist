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
                //https://github.com/jellyfin/jellyfin/blob/master/Emby.Naming/TV/SeriesInfo.cs
                //https://github.com/jellyfin/jellyfin/blob/f863ca1f2d00839fd78a7655c759e25e9815483f/Emby.Naming/TV/SeriesResolver.cs#L43
                // always get true file name(without extension) from path, not info.Name(from ohter metadata plugin).
                string searchName = Path.GetFileNameWithoutExtension(info.Path);
                _log.LogDebug("Start AniList... before Searching ({Name})", searchName); 
                searchName = Anitomy.AnitomyHelper.ExtractAnimeTitle(searchName);
                _log.LogInformation("Start AniList... Searching({Name})", searchName);
                var elementsOutput = Anitomy.AnitomyHelper.ElementsOutput(searchName);
                elementsOutput.ForEach(x => _log.LogDebug("AnitomySharp Elements:" + x.Category + ": " + x.Value));
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
