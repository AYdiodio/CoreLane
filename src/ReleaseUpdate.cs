using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CoreAllocator
{
    internal enum UpdateStatus { Current, Available, NoReleaseInformation }

    internal sealed class ReleaseUpdate
    {
        internal static readonly Uri ReleasesPage = new Uri("https://github.com/AYdiodio/CoreLane/releases");
        private static readonly Uri LatestRelease = new Uri("https://api.github.com/repos/AYdiodio/CoreLane/releases/latest");
        private static readonly Regex StableTag = new Regex(@"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant);

        [DataContract]
        private sealed class ReleaseInfo
        {
            [DataMember(Name = "tag_name", IsRequired = true)] public string Tag { get; set; }
            [DataMember(Name = "draft", IsRequired = true)] public bool Draft { get; set; }
            [DataMember(Name = "prerelease", IsRequired = true)] public bool Prerelease { get; set; }
        }

        internal readonly UpdateStatus Status;
        internal readonly Version LatestVersion;
        internal readonly Uri Page;

        private ReleaseUpdate(UpdateStatus status, Version latestVersion = null, Uri page = null)
        {
            Status = status;
            LatestVersion = latestVersion;
            Page = page ?? ReleasesPage;
        }

        internal static async Task<ReleaseUpdate> CheckAsync(Version installed, CancellationToken cancellationToken)
        {
            using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }))
            {
                client.Timeout = TimeSpan.FromSeconds(8);
                client.MaxResponseContentBufferSize = 512 * 1024;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(Program.DisplayName + "/" + installed.ToString(3));
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                using (HttpResponseMessage response = await client.GetAsync(LatestRelease, cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound) return ReadResponse(response.StatusCode, null, installed);
                    response.EnsureSuccessStatusCode();
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return ReadResponse(response.StatusCode, bytes, installed);
                }
            }
        }

        internal static ReleaseUpdate ReadResponse(HttpStatusCode status, byte[] content, Version installed)
        {
            if (status == HttpStatusCode.NotFound) return new ReleaseUpdate(UpdateStatus.NoReleaseInformation);
            if (status != HttpStatusCode.OK || content == null) throw new InvalidDataException("无法读取发布信息。");
            ReleaseInfo release;
            using (var stream = new MemoryStream(content, false))
                release = (ReleaseInfo)new DataContractJsonSerializer(typeof(ReleaseInfo)).ReadObject(stream);
            if (release == null || string.IsNullOrEmpty(release.Tag)) throw new InvalidDataException("发布版本无效。");
            if (release.Draft || release.Prerelease) return new ReleaseUpdate(UpdateStatus.NoReleaseInformation);
            Version latest;
            if (!StableTag.IsMatch(release.Tag) || !Version.TryParse(release.Tag.TrimStart('v'), out latest))
                throw new InvalidDataException("发布版本无效。");
            Version installedRelease = new Version(installed.Major, installed.Minor, installed.Build);
            if (latest <= installedRelease) return new ReleaseUpdate(UpdateStatus.Current, latest);
            return new ReleaseUpdate(UpdateStatus.Available, latest, new Uri(ReleasesPage.AbsoluteUri + "/tag/" + release.Tag));
        }
    }
}
