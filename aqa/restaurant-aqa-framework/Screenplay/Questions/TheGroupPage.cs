using System.Globalization;
using System.Text.RegularExpressions;
using Restaurant_Aqa_Framework.Support;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

public sealed record GuestCard(string Label, decimal Subtotal, IReadOnlyList<(string Dish, int Quantity)> Lines);

public sealed record GroupPageView(bool OwnerListed, decimal GrandTotal, IReadOnlyList<GuestCard> Guests);

/// <summary>The empty-state copy shown once nobody, including the owner, has any dishes left.</summary>
public sealed class TheGroupHasNoDishes : IQuestion<bool>
{
    public async Task<bool> AnsweredByAsync(Actor actor)
    {
        var notice = new GroupOrderPage(actor.LookingAtThePage()).NoDishes;
        await notice.WaitForAsync();
        return await notice.IsVisibleAsync();
    }
}

/// <summary>What the owner can see on the group page: the owner card, each guest card, and the grand total.
/// The grand total counts submitted carts only.</summary>
public sealed partial class TheGroupPage : IQuestion<GroupPageView>
{
    public async Task<GroupPageView> AnsweredByAsync(Actor actor)
    {
        var group = actor.Recall<GroupBooking>();
        var page = new GroupOrderPage(actor.LookingAtThePage());
        var guests = new List<GuestCard>();

        foreach (var seat in group.Guests)
        {
            if (await page.GuestHeading(seat.Name).CountAsync() == 0)
                continue;

            var card = page.GuestCard(seat.Name);
            var lines = new List<(string Dish, int Quantity)>();
            var items = card.Locator("li.item");
            var count = await items.CountAsync();
            for (var index = 0; index < count; index++)
            {
                var item = items.Nth(index);
                var name = (await item.Locator("p.name").InnerTextAsync()).Trim();
                var quantity = int.Parse(Quantity().Match(await item.Locator("p.qty").InnerTextAsync()).Value, CultureInfo.InvariantCulture);
                lines.Add((name, quantity));
            }

            var subtotal = MoneyText.Parse(await card.Locator("p.subtotal").InnerTextAsync());
            guests.Add(new GuestCard(seat.Label, subtotal, lines));
        }

        return new GroupPageView(
            await page.OwnerCard.CountAsync() > 0,
            MoneyText.Parse(await page.GrandTotal.InnerTextAsync()),
            guests);
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Quantity();
}
