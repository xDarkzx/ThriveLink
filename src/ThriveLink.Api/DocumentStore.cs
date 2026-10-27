using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ThriveLink.Api;

/// <summary>Where claim documents (receipts, invoices, referrals) are kept.</summary>
public interface IDocumentStore
{
    bool IsConfigured { get; }
    Task SaveAsync(string blobName, Stream content, string contentType, CancellationToken ct);
}

/// <summary>Private Blob container, accessed with the app's managed identity (no storage keys).</summary>
public sealed class BlobDocumentStore(Uri blobEndpoint) : IDocumentStore
{
    private readonly BlobContainerClient _container =
        new BlobServiceClient(blobEndpoint, new DefaultAzureCredential()).GetBlobContainerClient("claim-documents");

    public bool IsConfigured => true;

    public async Task SaveAsync(string blobName, Stream content, string contentType, CancellationToken ct) =>
        await _container.GetBlobClient(blobName).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, ct);
}

/// <summary>Used when no storage is configured (e.g. local dev without Azurite): uploads are refused, not lost.</summary>
public sealed class UnconfiguredDocumentStore : IDocumentStore
{
    public bool IsConfigured => false;

    public Task SaveAsync(string blobName, Stream content, string contentType, CancellationToken ct) =>
        throw new InvalidOperationException("Document storage is not configured.");
}
