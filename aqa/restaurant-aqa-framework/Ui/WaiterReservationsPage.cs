using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Ui;

/// <summary>The waiter's reservation list. The assigned waiter opens a pre-order from the reservation card,
/// which is the only way the pre-order page learns which reservation it is.</summary>
public sealed class WaiterReservationsPage(IPage page) : BasePage(page)
{
    private ILocator Cards => Page.Locator("ul.reservation-list li.card");

    public ILocator CardFor(string ownerName) => Cards.Filter(new() { HasText = ownerName });

    public ILocator PreOrderButton(string ownerName) =>
        CardFor(ownerName).GetByRole(AriaRole.Button, new() { Name = "Pre-order", Exact = true });
}
