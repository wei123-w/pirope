using System.Net.Http;
using System.Security.Cryptography;

namespace SnipLite;

internal static class ReleaseUpdates
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<(string Tag, Version Version)> CheckAsync()
    {
        using var response = await Client.GetAsync(AppInfo.ReleasesUrl + "/latest", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var prefix = AppInfo.ReleasesUrl + "/tag/";
        var url = response.RequestMessage?.RequestUri?.AbsoluteUri;
        if (url is null || !url.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("GitHub did not return a release tag.");
        var tag = url[prefix.Length..];
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) || version.Build < 0)
            throw new InvalidDataException("Unrecognized release version.");
        return (tag, version);
    }

    public static async Task DownloadAsync(string tag, string file, string destination)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        var url = $"{AppInfo.ReleasesUrl}/download/{tag}/";
        var sums = await Client.GetStringAsync(url + "SHA256SUMS.txt", token);
        var expected = sums.Split('\n').Select(line => line.Trim().Split(' ', 2))
            .FirstOrDefault(parts => parts.Length == 2 && parts[1].Trim() == file)?[0];
        if (expected is null || expected.Length != 64)
            throw new InvalidDataException("Missing installer checksum.");

        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await Client.GetAsync(url + file, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
            {
                await response.Content.CopyToAsync(output, token);
                output.Position = 0;
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(output, token));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installer checksum mismatch.");
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
