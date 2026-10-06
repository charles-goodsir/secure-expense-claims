namespace ExpenseClaims.Api.Domain;

public class Receipt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClaimId { get; set; }

    // Decided by the server from the file's first bytes, never taken from the request.

    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }

    // The uploader's file name, kept for display only. Never used to build a path.

    public required string OriginalFileName { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    // Blobs are named by the receipt's ID, so nothing in the upload can choose where it's stored.
    public string BlobName => Id.ToString();
}
