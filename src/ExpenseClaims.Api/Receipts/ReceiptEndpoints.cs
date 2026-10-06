using System.Security.Claims;
using Azure.Storage.Blobs;
using ExpenseClaims.Api.Claims;
using ExpenseClaims.Api.Data;
using ExpenseClaims.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Claim = ExpenseClaims.Api.Domain.Claim;


namespace ExpenseClaims.Api.Receipts;

public static class ReceiptEndpoints
{
    public const long MaxBytes = 5 * 1024 * 1024;
    // File types are recognised by their first bytes ("magic numbers"), not by the file name
    // or the Content-Type header, which the uploader controls (threat T2).

    private static readonly (string ContentType, string Extension, byte[] Signature)[] AllowedTypes =
    [
        ("application/pdf", "pdf", "%PDF-"u8.ToArray()),
        ("image/png", "png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        ("image/jpeg", "jpg", [0xFF, 0xD8, 0xFF]),

    ];
    public static void MapReceiptEndpoints(this WebApplication app)
    {
        app.MapPost("/claims/{id:guid}/receipts", UploadAsync)
            .RequireAuthorization(policy => policy.RequireRole("Employee"))
            // Kestrel stops reading a request body past this size (threat D1). The extra 64 KB
            // leaves room for the multipart headers around the file.
            .WithMetadata(new RequestSizeLimitAttribute(MaxBytes + 64 * 1024))
            // The API authenticates with a header or bearer token, never a cookie, so a
            // cross-site request can't carry the user's identity. Antiforgery tokens protect
            // cookie-based forms and don't apply here.
            .DisableAntiforgery();
        app.MapGet("/claims/{id:guid}/receipts", ListAsync).RequireAuthorization();
        app.MapGet("/claims/{id:guid}/receipts/{receiptId:guid}", DownloadAsync).RequireAuthorization();
    }
    // The claims a caller may see receipts for: their own, their direct reports' (managers),
    // and approved or paid claims (finance). Anything else returns 404 (threat I2).

    private static IQueryable<Claim> VisibleTo(this IQueryable<Claim> claims, ClaimsPrincipal user)
    {
        var callerId = ClaimEndpoints.CallerId(user);
        var own = claims.Where(c => c.EmployeeId == callerId);
        if (user.IsInRole("Manager"))
        {
            own = own.Union(claims.ManagedBy(callerId));
        }
        if (user.IsInRole("Finance"))
        {
            own = own.Union(claims.PayableBy(callerId));
        }
        return own;
    }

    private static async Task<IResult> UploadAsync(
        Guid id, IFormFile file, ClaimsPrincipal user, ClaimsDbContext db, BlobContainerClient receipts)
    {
        var callerId = ClaimEndpoints.CallerId(user);
        var claim = await db.Claims.SingleOrDefaultAsync(c => c.Id == id && c.EmployeeId == callerId);
        if (claim is null)
        {
            return Results.NotFound();
        }
        if (claim.Status != ClaimStatus.Draft)
        {
            return Results.Conflict();
        }
        // Checked here as well, because not every host enforces the request size limit
        // (the in-memory test server doesn't).
        if (file.Length == 0)
        {
            return Results.BadRequest();

        }
        if (file.Length > MaxBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        }
        await using var upload = file.OpenReadStream();
        var header = new byte[8];
        var read = await upload.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        var type = AllowedTypes.FirstOrDefault(t => header.AsSpan(0, read).StartsWith(t.Signature));
        if (type.ContentType is null)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        }
        var receipt = new Receipt
        {
            ClaimId = claim.Id,
            ContentType = type.ContentType,
            SizeBytes = file.Length,
            OriginalFileName = Truncate(Path.GetFileName(file.FileName), 255),
        };
        upload.Position = 0;
        await receipts.UploadBlobAsync(receipt.BlobName, upload);
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync();

        return Results.Created($"/claims/{claim.Id}/receipts/{receipt.Id}", new { receipt.Id, receipt.ContentType, receipt.SizeBytes, receipt.OriginalFileName });
    }
    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static async Task<IResult> ListAsync(Guid id, ClaimsPrincipal user, ClaimsDbContext db)
    {
        if (!await db.Claims.VisibleTo(user).AnyAsync(c => c.Id == id))
        {
            return Results.NotFound();
        }
        var list = await db.Receipts.Where(r => r.ClaimId == id)
            .OrderBy(r => r.UploadedAt)
            .Select(r => new { r.Id, r.ContentType, r.SizeBytes, r.OriginalFileName })
            .ToListAsync();
        return Results.Ok(list);
    }
    private static async Task<IResult> DownloadAsync(Guid id, Guid receiptId, ClaimsPrincipal user, ClaimsDbContext db, BlobContainerClient receipts)
    {
        if (!await db.Claims.VisibleTo(user).AnyAsync(c => c.Id == id))
        {
            return Results.NotFound();
        }
        var receipt = await db.Receipts.SingleOrDefaultAsync(r => r.Id == receiptId && r.ClaimId == id);
        if (receipt is null)
        {
            return Results.NotFound();
        }
        var extension = AllowedTypes.First(t => t.ContentType == receipt.ContentType).Extension;
        var content = await receipts.GetBlobClient(receipt.BlobName).OpenReadAsync();
        // Served as a download with a name the server chose, so the browser never renders an
        // uploaded file inside the app's own origin.

        return Results.File(content, receipt.ContentType, $"receipt-{receipt.Id}.{extension}");

    }
}
