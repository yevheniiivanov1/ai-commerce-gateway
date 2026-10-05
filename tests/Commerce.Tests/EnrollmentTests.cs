using Commerce.Core.Catalog;
using Commerce.Core.Enrollment;
using Commerce.Core.Scheduling;

namespace Commerce.Tests;

public class EnrollmentTests
{
    [Fact]
    public void A_sold_out_offer_is_reported_as_sold_out_not_as_open()
    {
        var document = TestCatalog.LoadDocument();
        var product = document.Products[0];
        var soldOut = product.Offers[0] with { Id = "vsa-double-axel-club-12m", Name = "12-Month Package", Availability = OfferAvailability.SoldOut };
        var catalog = new Catalog(document with { Products = [product with { Offers = [.. product.Offers, soldOut] }, .. document.Products.Skip(1)] });
        var clock = TestCatalog.Clock();
        var service = new EnrollmentService(catalog, new AvailabilityService(clock), new DirectCheckoutLinks(TestCatalog.Providers()), new InMemoryFunnelLog(), clock);

        var outcome = service.Start(new EnrollmentRequest(product.Id, soldOut.Id, null, null, "test"));

        Assert.Equal(EnrollmentError.NotOpen, outcome.Error);
        Assert.Equal("The 12-Month Package of 6-Month Double Axel Club is sold out. Open offers: vsa-double-axel-club-6m.", outcome.Message);
    }
}
