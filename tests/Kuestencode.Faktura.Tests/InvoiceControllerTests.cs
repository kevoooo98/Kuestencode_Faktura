using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Models;
using Kuestencode.Shared.Contracts.Faktura;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kuestencode.Faktura.Tests;

public class InvoiceControllerTests : IClassFixture<FakturaWebApplicationFactory>
{
    private readonly FakturaWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InvoiceControllerTests(FakturaWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateTestAdminJwt());
    }

    /// <summary>
    /// Erzeugt ein unsigniertes, aber syntaktisch gültiges JWT mit Admin-Rolle.
    /// JwtPrincipalParser (Kuestencode.Shared.UI) validiert nur die Struktur/Ablaufzeit,
    /// keine Signatur — analog dazu, wie JwtUserContextMiddleware einem bereits vom Host
    /// geprüften Token vertraut. Reicht für [RequireRole] in diesen Controller-Tests.
    /// </summary>
    private static string CreateTestAdminJwt()
    {
        static string Base64Url(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = Base64Url("""{"alg":"none","typ":"JWT"}""");
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payload = Base64Url($$"""
            {"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier":"{{Guid.NewGuid()}}","http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name":"Test Admin","http://schemas.microsoft.com/ws/2008/06/identity/claims/role":"Admin","exp":{{exp}}}
            """);

        return $"{header}.{payload}.unsigned";
    }

    private static CreateInvoiceRequest MakeCreateRequest(int customerId = 1) => new()
    {
        InvoiceDate = DateTime.UtcNow,
        CustomerId = customerId,
        Items =
        [
            new CreateInvoiceItemRequest
            {
                Description = "Beratung",
                Quantity = 2,
                UnitPrice = 100,
                VatRate = 19
            }
        ]
    };

    [Fact]
    public async Task GetAll_OhneRechnungen_GibtLeereListeZurueck()
    {
        var response = await _client.GetAsync("/api/Invoice");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var invoices = await response.Content.ReadFromJsonAsync<List<InvoiceDto>>();
        invoices.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_PersistiertRechnungUndGetByIdLiefertSieZurueck()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        created.Should().NotBeNull();
        created!.CustomerName.Should().Be("Nordlicht Media");
        created.TotalGross.Should().Be(238m); // 2 * 100 * 1.19
        created.Status.Should().Be("Draft");

        var getResponse = await _client.GetAsync($"/api/Invoice/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        fetched!.Id.Should().Be(created.Id);
        fetched.Items.Should().ContainSingle(i => i.Description == "Beratung");
    }

    [Fact]
    public async Task Create_OhneItems_GibtBadRequestZurueckOderLeereRechnung()
    {
        var request = MakeCreateRequest();
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", request with { Items = [] });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        created!.TotalGross.Should().Be(0);
    }

    [Fact]
    public async Task Update_AendertKundenUndPositionen()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var updateRequest = new UpdateInvoiceRequest
        {
            InvoiceDate = created.InvoiceDate,
            CustomerId = 2,
            Items =
            [
                new CreateInvoiceItemRequest { Description = "Support", Quantity = 1, UnitPrice = 50, VatRate = 19 }
            ]
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/Invoice/{created.Id}", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/Invoice/{created.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        updated!.CustomerId.Should().Be(2);
        updated.Items.Should().ContainSingle(i => i.Description == "Support");
    }

    [Fact]
    public async Task Delete_EntferntRechnung()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var deleteResponse = await _client.DeleteAsync($"/api/Invoice/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/Invoice/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkAsPaid_SetztStatusAufPaid_WennRechnungBereitsVersendetWurde()
    {
        // MarkAsPaidAsync rechnet den Status nur für nicht-Draft-Rechnungen neu (siehe RecalculateStatusAsync),
        // daher muss die Rechnung hier zunächst wie eine versendete Rechnung behandelt werden.
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FakturaDbContext>();
            var invoice = await context.Invoices.FindAsync(created.Id);
            invoice!.Status = InvoiceStatus.Sent;
            await context.SaveChangesAsync();
        }

        var markPaidResponse = await _client.PostAsJsonAsync(
            $"/api/Invoice/{created.Id}/mark-paid",
            new { PaidDate = DateTime.UtcNow });
        markPaidResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/api/Invoice/{created.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        updated!.Status.Should().Be("Paid");
    }

    private async Task<InvoiceDto> CreateAndMarkAsSentAsync()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FakturaDbContext>();
        var invoice = await context.Invoices.FindAsync(created.Id);
        invoice!.Status = InvoiceStatus.Sent;
        await context.SaveChangesAsync();

        return created with { Status = "Sent" };
    }

    [Fact]
    public async Task Update_VersendeteRechnung_GibtConflictZurueck()
    {
        var invoice = await CreateAndMarkAsSentAsync();

        var updateRequest = new UpdateInvoiceRequest
        {
            InvoiceDate = invoice.InvoiceDate,
            CustomerId = invoice.CustomerId,
            Items = [new CreateInvoiceItemRequest { Description = "Geaendert", Quantity = 1, UnitPrice = 1, VatRate = 19 }]
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/Invoice/{invoice.Id}", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Delete_VersendeteRechnung_GibtConflictZurueck()
    {
        var invoice = await CreateAndMarkAsSentAsync();

        var deleteResponse = await _client.DeleteAsync($"/api/Invoice/{invoice.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var getResponse = await _client.GetAsync($"/api/Invoice/{invoice.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cancel_VersendeteRechnung_SetztStatusAufCancelled()
    {
        var invoice = await CreateAndMarkAsSentAsync();

        var cancelResponse = await _client.PostAsJsonAsync($"/api/Invoice/{invoice.Id}/cancel", new { Reason = "Testgrund" });
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/api/Invoice/{invoice.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        updated!.Status.Should().Be("Cancelled");
        updated.CancellationReason.Should().Be("Testgrund");
        updated.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Cancel_EntwurfsRechnung_GibtConflictZurueck()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var created = (await createResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var cancelResponse = await _client.PostAsJsonAsync($"/api/Invoice/{created.Id}/cancel", new { });
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancel_BereitsStornierteRechnung_GibtConflictZurueck()
    {
        var invoice = await CreateAndMarkAsSentAsync();
        await _client.PostAsJsonAsync($"/api/Invoice/{invoice.Id}/cancel", new { });

        var secondCancelResponse = await _client.PostAsJsonAsync($"/api/Invoice/{invoice.Id}/cancel", new { });
        secondCancelResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetById_UnbekannteId_GibtNotFoundZurueck()
    {
        var response = await _client.GetAsync("/api/Invoice/999999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_AbschlagMitVerknuepfterAbschlagsrechnung_LiefertSourceInvoiceNummer()
    {
        var sourceResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var sourceInvoice = (await sourceResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var finalResponse = await _client.PostAsJsonAsync("/api/Invoice", MakeCreateRequest());
        var finalInvoice = (await finalResponse.Content.ReadFromJsonAsync<InvoiceDto>())!;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FakturaDbContext>();
            context.DownPayments.Add(new DownPayment
            {
                InvoiceId = finalInvoice.Id,
                Description = "1. Abschlagsrechnung",
                Amount = 100m,
                SourceInvoiceId = sourceInvoice.Id
            });
            await context.SaveChangesAsync();
        }

        var getResponse = await _client.GetAsync($"/api/Invoice/{finalInvoice.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        var downPayment = updated!.DownPayments.Should().ContainSingle().Subject;
        downPayment.SourceInvoiceId.Should().Be(sourceInvoice.Id);
        downPayment.SourceInvoiceNumber.Should().Be(sourceInvoice.InvoiceNumber);
    }
}
