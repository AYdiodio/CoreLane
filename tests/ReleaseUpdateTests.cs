using System;
using System.Net;
using System.Text;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void ReleaseUpdateTests()
        {
            var installed = new Version(0, 13, 14, 0);
            Func<string, byte[]> reply = tag => Encoding.UTF8.GetBytes(
                "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://example.invalid/\"}");
            Case("updates: numeric version comparison and project-controlled release link", delegate
            {
                ReleaseUpdate update = ReleaseUpdate.ReadResponse(HttpStatusCode.OK, reply("v0.13.20"), installed);
                Check(update.Status == UpdateStatus.Available && update.LatestVersion == new Version(0, 13, 20), "newer release available");
                Check(update.Page.AbsoluteUri == ReleaseUpdate.ReleasesPage.AbsoluteUri + "/tag/v0.13.20", "untrusted html_url cannot change the destination");
                Check(ReleaseUpdate.ReadResponse(HttpStatusCode.OK, reply("v0.13.14"), installed).Status == UpdateStatus.Current, "assembly revision is ignored when checking the release");
                Check(ReleaseUpdate.ReadResponse(HttpStatusCode.OK, reply("0.13.9"), installed).Status == UpdateStatus.Current, "older version cannot be offered as an update");
            });
            Case("updates: missing, private, preview and malformed releases never report up to date", delegate
            {
                ReleaseUpdate missing = ReleaseUpdate.ReadResponse(HttpStatusCode.NotFound, null, installed);
                Check(missing.Status == UpdateStatus.NoReleaseInformation && missing.Page == ReleaseUpdate.ReleasesPage, "browser access remains available after a 404");
                foreach (string flag in new[] { "draft", "prerelease" })
                {
                    string json = Encoding.UTF8.GetString(reply("v0.14.0")).Replace("\"" + flag + "\":false", "\"" + flag + "\":true");
                    Check(ReleaseUpdate.ReadResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes(json), installed).Status == UpdateStatus.NoReleaseInformation, "skip " + flag);
                }
                Throws(delegate { ReleaseUpdate.ReadResponse(HttpStatusCode.OK, reply("v0.14.0/elsewhere"), installed); });
                Throws(delegate { ReleaseUpdate.ReadResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes("{}"), installed); });
                Throws(delegate { ReleaseUpdate.ReadResponse(HttpStatusCode.Forbidden, null, installed); });
            });
        }
    }
}
