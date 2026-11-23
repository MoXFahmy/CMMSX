using FourC.Cmms.Core.Purchasing;

namespace FourC.Cmms.Tests;

/// <summary>0.16.0: a request's status follows how many of its lines are on live orders.</summary>
public class PurchaseRequestStatusTests
{
    [Fact]
    public void Ordering_moves_approved_to_partly_ordered_and_ordered()
    {
        Assert.Equal(PurchaseRequestStatus.PartlyOrdered, PurchaseRequestRules.StatusAfterOrdering(PurchaseRequestStatus.Approved, 3, 1));
        Assert.Equal(PurchaseRequestStatus.Ordered, PurchaseRequestRules.StatusAfterOrdering(PurchaseRequestStatus.PartlyOrdered, 3, 3));
        Assert.Equal(PurchaseRequestStatus.PartlyOrdered, PurchaseRequestRules.StatusAfterOrdering(PurchaseRequestStatus.Ordered, 3, 2));
        Assert.Equal(PurchaseRequestStatus.Approved, PurchaseRequestRules.StatusAfterOrdering(PurchaseRequestStatus.PartlyOrdered, 3, 0));
    }

    [Fact]
    public void Other_statuses_are_left_alone()
    {
        Assert.Equal(PurchaseRequestStatus.Closed, PurchaseRequestRules.StatusAfterOrdering(PurchaseRequestStatus.Closed, 3, 0));
        Assert.Equal(PurchaseRequestStatus.Cancelled, PurchaseRequestRules.StatusAfterOrdering(PurchaseRequestStatus.Cancelled, 2, 0));
        Assert.Equal(PurchaseRequestStatus.Submitted, PurchaseRequestRules.StatusAfterOrdering(PurchaseRequestStatus.Submitted, 1, 0));
    }
}
