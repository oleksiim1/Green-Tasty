using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Ui;

/// <summary>The pre-order a waiter opens from a reservation card. Dish name and quantity are what the
/// restaurant received.</summary>
public sealed class WaiterPreOrderPage(IPage page) : BasePage(page)
{
    public ILocator Dishes => Page.Locator("app-dish-card");
}
