using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class KkdDistributionPolicyTests
{
    [Fact]
    public void Required_open_order_rejects_orderless_line()
    {
        var request = Request(Line(null, null));

        Assert.Throws<AppException>(() =>
            KkdDistributionService.ValidatePolicy(request, Employee(), Policy(requireOpenOrder: true)));
    }

    [Fact]
    public void Single_order_policy_rejects_multiple_order_numbers()
    {
        var request = Request(Line("SIP-1", 11), Line("SIP-2", 22));

        Assert.Throws<AppException>(() =>
            KkdDistributionService.ValidatePolicy(request, Employee(), Policy(allowMultipleOrders: false)));
    }

    [Fact]
    public void Order_number_and_line_must_be_supplied_together()
    {
        var request = Request(Line("SIP-1", null));

        Assert.Throws<AppException>(() => KkdDistributionService.ValidateCreateEnvelope(request));
    }

    [Fact]
    public void Valid_open_order_request_passes_envelope_and_policy_rules()
    {
        var request = Request(Line("SIP-1", 11));

        KkdDistributionService.ValidateCreateEnvelope(request);
        KkdDistributionService.ValidatePolicy(request, Employee(), Policy(requireOpenOrder: true));
    }

    private static KkdDistributionCreateRequest Request(params KkdDistributionLineCreateRequest[] lines) => new(
        Guid.NewGuid(), 1, 1, 1, DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, lines);

    private static KkdDistributionLineCreateRequest Line(string? orderNumber, long? orderLineId) => new(
        1, null, 1, "ADET", 1, orderNumber, orderLineId, false, null, null);

    private static KkdEmployee Employee() => new()
    {
        Id = 1,
        BranchCode = "0",
        CustomerId = 1,
        EmployeeCode = "P-1",
        FirstName = "Test",
        LastName = "Personel",
        QrCode = "QR-1",
        EmploymentStartDate = new DateOnly(2020, 1, 1),
        IsActive = true
    };

    private static KkdPolicyDto Policy(bool requireOpenOrder = true, bool allowMultipleOrders = true) => new(
        0, "0", requireOpenOrder, true, allowMultipleOrders, false, false, true, null, null);
}
