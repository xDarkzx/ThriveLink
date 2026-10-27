using System.Collections.Concurrent;

namespace ThriveLink.Api.Tests;

public sealed class InMemoryDocumentStore : IDocumentStore
{
    public ConcurrentDictionary<string, (string ContentType, int Length)> Saved { get; } = new();

    public bool IsConfigured => true;

    public async Task SaveAsync(string blobName, Stream content, string contentType, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        Saved[blobName] = (contentType, (int)buffer.Length);
    }
}
