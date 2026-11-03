using System.Globalization;
using System.Text.RegularExpressions;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The dishes on the waiter's pre-order page, keyed by dish name.</summary>
public sealed partial class TheWaiterPreOrder : IQuestion<IReadOnlyDictionary<string, int>>
{
    public async Task<IReadOnlyDictionary<string, int>> AnsweredByAsync(Actor actor)
    {
        var page = new WaiterPreOrderPage(actor.LookingAtThePage());
        var dishes = page.Dishes;
        var count = await dishes.CountAsync();
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < count; index++)
        {
            var dish = dishes.Nth(index);
            var name = (await dish.Locator(".dish-name").InnerTextAsync()).Trim();
            var quantity = int.Parse(Quantity().Match(await dish.Locator(".pre-order-quantity").InnerTextAsync()).Value, CultureInfo.InvariantCulture);
            totals[name] = totals.GetValueOrDefault(name) + quantity;
        }

        return totals;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Quantity();
}
