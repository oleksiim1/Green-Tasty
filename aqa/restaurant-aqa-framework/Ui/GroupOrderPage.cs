using Microsoft.Playwright;

namespace Restaurant_Aqa_Framework.Ui;

/// <summary>The owner's group pre-order page, <c>/reservations/:id/group-order</c>.</summary>
public sealed class GroupOrderPage(IPage page) : BasePage(page)
{
    public ILocator Title => Page.GetByRole(AriaRole.Heading, new() { Name = "Group Pre-Order", Exact = true });

    public ILocator Loading => Page.GetByText("Loading");

    public ILocator LoadError => Page.GetByText("Could not load this group reservation.");

    public ILocator NoDishes => Page.GetByText("Participants haven't added any dishes yet");

    public ILocator OwnerCard => Page.GetByRole(AriaRole.Heading, new() { Name = "My Pre-Order", Exact = true });

    public ILocator GrandTotal => Page.Locator("p.grand span").Nth(1);

    public ILocator SentToTheRestaurant => Page.GetByText("Sent to Restaurant", new() { Exact = true });

    public ILocator SubmitButton => Page.GetByRole(AriaRole.Button, new() { Name = "Submit Pre-Order", Exact = true });

    public ILocator ConfirmSubmit => Page.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true });

    public ILocator ConfirmRemove => Page.GetByRole(AriaRole.Button, new() { Name = "Remove Guest", Exact = true });

    public Task OpenAsync(string reservationId) => NavigateToAsync($"/reservations/{reservationId}/group-order");

    public ILocator GuestHeading(string guestName) =>
        Page.GetByRole(AriaRole.Heading, new() { Name = $"Guest: {guestName}", Exact = true });

    public ILocator RemoveButton(string guestName) =>
        Page.GetByRole(AriaRole.Button, new() { Name = $"Remove {guestName}", Exact = true });

    public ILocator GuestCard(string guestName) =>
        Page.Locator("article.card").Filter(new() { Has = GuestHeading(guestName) });
}
