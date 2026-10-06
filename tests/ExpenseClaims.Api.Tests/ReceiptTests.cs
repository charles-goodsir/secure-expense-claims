using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExpenseClaims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseClaims.Api.Tests;

public class ReceiptTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record ClaimDto(Guid Id);
    private record ReceiptDto(Guid Id, string ContentType, long SizeBytes, string OriginalFileName);

    private static readonly byte[] Pdf = "%PDF-1.7\n% test receipt\n"u8.ToArray();
    private static readonly byte[] Html = "<html><script>alert(1)</script></html>"u8.ToArray();

    private async Task<Guid> AddUserAsync(Guid? managerId = null)
    {
        await using var db = factory.NewDbContext();
        var user = new User { DisplayName = "Test", Email = $"{Guid.NewGuid()}@example.com", ManagerId = managerId };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private HttpClient ClientFor(Guid userId, string roles = "Employee")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Dev-Roles", roles);
        return client;
    }

    private static async Task<Guid> DraftClaimAsync(HttpClient employee)
    {
        var created = await employee.PostAsJsonAsync("/claims", new { description = "Taxi", amount = 42.50m });
        return (await created.Content.ReadFromJsonAsync<ClaimDto>())!.Id;
    }

    private static Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid claimId, byte[] content, string fileName = "receipt.pdf", string contentType = "application/pdf")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return client.PostAsync($"/claims/{claimId}/receipts", form);
    }

    private static async Task<ReceiptDto> UploadPdfAsync(HttpClient client, Guid claimId)
    {
        var response = await UploadAsync(client, claimId, Pdf);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReceiptDto>())!;
    }

    [Fact]
    public async Task Owner_can_upload_and_download_a_pdf()
    {
        var alice = ClientFor(await AddUserAsync());
        var claimId = await DraftClaimAsync(alice);

        var receipt = await UploadPdfAsync(alice, claimId);
        var download = await alice.GetAsync($"/claims/{claimId}/receipts/{receipt.Id}");

        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"receipt-{receipt.Id}.pdf", download.Content.Headers.ContentDisposition!.FileName);
    }

    [Fact]
    public async Task Html_named_pdf_is_rejected()
    {
        var alice = ClientFor(await AddUserAsync());
        var claimId = await DraftClaimAsync(alice);

        var response = await UploadAsync(alice, claimId, Html, "receipt.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Content_type_comes_from_the_file_not_the_request()
    {
        var alice = ClientFor(await AddUserAsync());
        var claimId = await DraftClaimAsync(alice);

        var response = await UploadAsync(alice, claimId, Pdf, "receipt.png", "image/png");

        Assert.Equal("application/pdf", (await response.Content.ReadFromJsonAsync<ReceiptDto>())!.ContentType);
    }

    [Fact]
    public async Task Path_in_the_file_name_is_dropped()
    {
        var alice = ClientFor(await AddUserAsync());
        var claimId = await DraftClaimAsync(alice);

        var response = await UploadAsync(alice, claimId, Pdf, "../../etc/passwd.pdf");

        Assert.Equal("passwd.pdf", (await response.Content.ReadFromJsonAsync<ReceiptDto>())!.OriginalFileName);
    }

    [Fact]
    public async Task File_over_5_MB_is_rejected()
    {
        var alice = ClientFor(await AddUserAsync());
        var claimId = await DraftClaimAsync(alice);
        var tooBig = new byte[5 * 1024 * 1024 + 1];
        Pdf.CopyTo(tooBig, 0);

        var response = await UploadAsync(alice, claimId, tooBig);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Empty_file_is_rejected()
    {
        var alice = ClientFor(await AddUserAsync());
        var claimId = await DraftClaimAsync(alice);

        var response = await UploadAsync(alice, claimId, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_upload_to_someone_elses_claim()
    {
        var alice = ClientFor(await AddUserAsync());
        var bob = ClientFor(await AddUserAsync());
        var bobsClaim = await DraftClaimAsync(bob);

        var response = await UploadAsync(alice, bobsClaim, Pdf);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_download_someone_elses_receipt()
    {
        var alice = ClientFor(await AddUserAsync());
        var bob = ClientFor(await AddUserAsync());
        var bobsClaim = await DraftClaimAsync(bob);
        var bobsReceipt = await UploadPdfAsync(bob, bobsClaim);

        var download = await alice.GetAsync($"/claims/{bobsClaim}/receipts/{bobsReceipt.Id}");
        var list = await alice.GetAsync($"/claims/{bobsClaim}/receipts");

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (download.StatusCode, list.StatusCode));
    }

    [Fact]
    public async Task Manager_can_see_a_direct_reports_receipt_but_another_manager_cannot()
    {
        var managerId = await AddUserAsync();
        var employee = ClientFor(await AddUserAsync(managerId));
        var claimId = await DraftClaimAsync(employee);
        var receipt = await UploadPdfAsync(employee, claimId);

        var ownManager = await ClientFor(managerId, "Manager").GetAsync($"/claims/{claimId}/receipts/{receipt.Id}");
        var otherManager = await ClientFor(await AddUserAsync(), "Manager").GetAsync($"/claims/{claimId}/receipts/{receipt.Id}");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NotFound), (ownManager.StatusCode, otherManager.StatusCode));
    }

    [Fact]
    public async Task Finance_cannot_see_receipts_before_approval()
    {
        var employee = ClientFor(await AddUserAsync());
        var claimId = await DraftClaimAsync(employee);
        var receipt = await UploadPdfAsync(employee, claimId);

        var response = await ClientFor(await AddUserAsync(), "Finance").GetAsync($"/claims/{claimId}/receipts/{receipt.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Receipt_cannot_be_added_after_submission()
    {
        var alice = ClientFor(await AddUserAsync());
        var claimId = await DraftClaimAsync(alice);
        await using (var db = factory.NewDbContext())
        {
            await db.Claims.Where(c => c.Id == claimId)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.Status, ClaimStatus.Submitted));
        }

        var response = await UploadAsync(alice, claimId, Pdf);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
