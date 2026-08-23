// -----------------------------------------------------------------------
// Copyright (c) 2026 JBX7. All rights reserved.
// -----------------------------------------------------------------------

using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace AutoClicker;

internal static class UpdatePackageDownloader
{
    internal const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    internal static Task<string> DownloadInstallerAsync(
        Uri downloadUri,
        string versionTag,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken) =>
        DownloadInstallerAsync(
            downloadUri,
            versionTag,
            expectedSize,
            expectedSha256,
            HttpClient,
            Path.Combine(Path.GetTempPath(), AppIdentity.Name, "Updates"),
            cancellationToken);

    internal static async Task<string> DownloadInstallerAsync(
        Uri downloadUri,
        string versionTag,
        long expectedSize,
        string expectedSha256,
        HttpClient client,
        string updateDirectory,
        CancellationToken cancellationToken)
    {
        if (!UpdateService.TryParseVersion(versionTag, out _)
            || !UpdateService.IsOfficialDownloadUrl(downloadUri, versionTag, UpdateService.InstallerAssetName))
            throw new InvalidOperationException("The update download is not an official AutoClicker installer for the selected release.");
        if (expectedSize <= 0 || expectedSize > MaximumInstallerBytes)
            throw new InvalidDataException("The update installer size is missing or outside the allowed range.");
        if (!TryParseSha256(expectedSha256, out var expectedHash))
            throw new InvalidDataException("The update installer does not have a valid SHA-256 digest.");

        Directory.CreateDirectory(updateDirectory);
        var safeTag = string.Concat(versionTag.Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_'));
        var destination = Path.Combine(updateDirectory, $"AutoClicker-Setup-x64-{safeTag}.exe");
        RemoveStaleUpdateFiles(updateDirectory, destination);
        if (File.Exists(destination))
        {
            try
            {
                await VerifyInstallerAsync(destination, expectedSize, expectedHash, cancellationToken);
                return destination;
            }
            catch (OperationCanceledException) { throw; }
            catch { TryDelete(destination); }
        }

        var partial = $"{destination}.partial-{Guid.NewGuid():N}";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DownloadTimeout);
            using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } contentLength && contentLength != expectedSize)
                throw new InvalidDataException($"The update installer size did not match its release metadata ({contentLength} instead of {expectedSize} bytes).");

            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(81_920);
            long bytesWritten = 0;
            try
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token);
                    if (read == 0) break;
                    bytesWritten += read;
                    if (bytesWritten > expectedSize || bytesWritten > MaximumInstallerBytes)
                        throw new InvalidDataException("The update installer exceeded its expected size.");
                    hasher.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
                await target.FlushAsync(timeout.Token);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            await target.DisposeAsync();

            if (bytesWritten != expectedSize)
                throw new InvalidDataException($"The update installer was incomplete ({bytesWritten} instead of {expectedSize} bytes).");
            var actualHash = hasher.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                throw new InvalidDataException("The update installer failed SHA-256 verification.");
            if (!HasPortableExecutableHeader(partial))
                throw new InvalidDataException("The downloaded update is not a valid Windows executable.");

            File.Move(partial, destination, overwrite: true);
            RemoveStaleUpdateFiles(updateDirectory, destination);
            return destination;
        }
        finally { TryDelete(partial); }
    }

    internal static async Task VerifyInstallerAsync(string path, long expectedSize, string expectedSha256, CancellationToken cancellationToken)
    {
        if (!TryParseSha256(expectedSha256, out var expectedHash))
            throw new InvalidDataException("The update installer does not have a valid SHA-256 digest.");
        await VerifyInstallerAsync(path, expectedSize, expectedHash, cancellationToken);
    }

    internal static bool HasPortableExecutableHeader(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < 68) return false;
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x5A4D) return false;
            stream.Position = 0x3C;
            var headerOffset = reader.ReadInt32();
            if (headerOffset < 64 || headerOffset > stream.Length - sizeof(uint)) return false;
            stream.Position = headerOffset;
            return reader.ReadUInt32() == 0x00004550;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal static bool TryParseSha256(string digest, out byte[] hash)
    {
        hash = [];
        var value = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : digest;
        if (value.Length != 64) return false;
        try
        {
            hash = Convert.FromHexString(value);
            return hash.Length == 32;
        }
        catch (FormatException)
        {
            hash = [];
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoClicker-update-installer");
        return client;
    }

    private static async Task VerifyInstallerAsync(string path, long expectedSize, byte[] expectedHash, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != expectedSize || expectedSize <= 0 || expectedSize > MaximumInstallerBytes)
            throw new InvalidDataException("The update installer size does not match its release metadata.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            throw new InvalidDataException("The update installer failed SHA-256 verification.");
        if (!HasPortableExecutableHeader(path))
            throw new InvalidDataException("The downloaded update is not a valid Windows executable.");
    }

    private static void RemoveStaleUpdateFiles(string directory, string keepPath)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "AutoClicker-Setup-x64-*"))
                if (!path.Equals(keepPath, StringComparison.OrdinalIgnoreCase)) TryDelete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
