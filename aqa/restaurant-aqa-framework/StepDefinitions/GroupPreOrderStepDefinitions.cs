using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Screenplay.Tasks;

namespace Restaurant_Aqa_Framework.StepDefinitions;

[Binding]
public sealed class GroupPreOrderStepDefinitions(Actor actor) : StepDefinitionBase(actor)
{
    [Given("a group pre-order with these guests")]
    public Task GivenGroupPreOrder(DataTable table) =>
        Actor.AttemptsToAsync(new SetUpGroupPreOrder(ReadLines(table)));

    [Given("a separate group pre-order with a guest called {string}")]
    public Task GivenSeparateGroup(string guest) =>
        Actor.AttemptsToAsync(new SetUpAnotherGroup(guest));

    [When("the owner reviews the group pre-order")]
    public Task WhenOwnerReviews() => Actor.AttemptsToAsync(new OpenTheGroupPreOrder());

    [When("the owner removes {string}")]
    public Task WhenOwnerRemoves(string guest) => Actor.AttemptsToAsync(new RemoveGuestOnTheGroupPage(guest));

    [When("the owner sends the pre-order to the restaurant")]
    public Task WhenOwnerSends() => Actor.AttemptsToAsync(new SendTheGroupPreOrder());

    [When("the waiter opens that pre-order")]
    public Task WhenWaiterOpens() => Actor.AttemptsToAsync(new OpenTheWaitersPreOrder());

    [When("another customer tries to remove {string}")]
    public Task WhenAnotherCustomerRemoves(string guest) =>
        Actor.AttemptsToAsync(RemoveGuestThroughTheService.AsAnotherCustomer(guest));

    [When("the waiter tries to remove {string}")]
    public Task WhenWaiterRemoves(string guest) =>
        Actor.AttemptsToAsync(RemoveGuestThroughTheService.AsTheWaiter(guest));

    [When("a visitor who is not signed in tries to remove {string}")]
    public Task WhenAnonymousRemoves(string guest) =>
        Actor.AttemptsToAsync(RemoveGuestThroughTheService.WithoutSigningIn(guest));

    [When("the owner tries to remove a guest who does not exist")]
    public Task WhenOwnerRemovesNobody() =>
        Actor.AttemptsToAsync(RemoveGuestThroughTheService.MissingGuest());

    [When("the owner tries to remove a guest from a group that does not exist")]
    public Task WhenOwnerRemovesFromNowhere() =>
        Actor.AttemptsToAsync(RemoveGuestThroughTheService.MissingGroup());

    [When("the owner removes a guest of the separate group")]
    public Task WhenOwnerRemovesAGuestOfTheOtherGroup() =>
        Actor.AttemptsToAsync(RemoveGuestThroughTheService.GuestOfTheOtherGroup());

    [Then("the owner is still listed")]
    public async Task ThenOwnerIsListed()
    {
        var page = await Actor.AsksForAsync(new TheGroupPage());
        await Assert.That(page.OwnerListed).IsTrue().Because("the owner card was not on the page");
    }

    [Then("the owner sees every guest's dishes")]
    public async Task ThenOwnerSeesEveryGuest()
    {
        var group = Recall<GroupBooking>();
        var page = await Actor.AsksForAsync(new TheGroupPage());
        await Assert.That(page.OwnerListed).IsTrue().Because("the owner card was not on the page");

        foreach (var guest in group.Guests)
            await AssertGuestMatches(group, page, guest.Label);
    }

    [Then("{string} is still in the group with the same dishes")]
    public async Task ThenGuestStillHasTheSameDishes(string guest)
    {
        var group = Recall<GroupBooking>();
        var page = await Actor.AsksForAsync(new TheGroupPage());
        await AssertGuestMatches(group, page, guest);
    }

    [Then("{string} is no longer in the group")]
    public async Task ThenGuestIsGone(string guest)
    {
        var page = await Actor.AsksForAsync(new TheGroupPage());
        await Assert.That(page.Guests.Any(card => card.Label == guest)).IsFalse()
            .Because($"{guest} is still listed: {string.Join(", ", page.Guests.Select(card => card.Label))}");
    }

    [Then("the group total is unchanged")]
    public async Task ThenTotalIsUnchanged()
    {
        var before = Recall<GroupTotalBefore>().Amount;
        var page = await Actor.AsksForAsync(new TheGroupPage());
        await Assert.That(page.GrandTotal).IsEqualTo(before);
    }

    [Then("the group total no longer includes {string}")]
    public async Task ThenTotalExcludes(string guest)
    {
        var group = Recall<GroupBooking>();
        var before = Recall<GroupTotalBefore>().Amount;
        var page = await Actor.AsksForAsync(new TheGroupPage());
        await Assert.That(page.GrandTotal).IsEqualTo(before - group.Subtotal(guest));
    }

    [Then("the group total is zero")]
    public async Task ThenTotalIsZero()
    {
        var page = await Actor.AsksForAsync(new TheGroupPage());
        await Assert.That(page.GrandTotal).IsEqualTo(0m);
    }

    [Then("nobody has added any dishes yet")]
    public async Task ThenNobodyHasAddedDishes()
    {
        await Assert.That(await Actor.AsksForAsync(new TheGroupHasNoDishes())).IsTrue();
    }

    [Then("the waiter sees only the dishes that were left")]
    public async Task ThenWaiterSeesWhatWasLeft()
    {
        var group = Recall<GroupBooking>();
        var removed = Recall<RemovedGuest>().Label;
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in group.Lines.Where(line => line.Guest != removed && line.Submitted))
        {
            var name = group.Dish(line.DishLabel).Name;
            expected[name] = expected.GetValueOrDefault(name) + line.Quantity;
        }

        var actual = await Actor.AsksForAsync(new TheWaiterPreOrder());
        await Assert.That(actual.Count).IsEqualTo(expected.Count)
            .Because($"waiter saw [{string.Join(", ", actual.Select(dish => $"{dish.Key} x{dish.Value}"))}]");
        foreach (var (name, quantity) in expected)
            await Assert.That(actual[name]).IsEqualTo(quantity).Because($"dish '{name}'");
    }

    private static async Task AssertGuestMatches(GroupBooking group, GroupPageView page, string guest)
    {
        var card = page.Guests.FirstOrDefault(item => item.Label == guest);
        await Assert.That(card).IsNotNull().Because($"{guest} is not listed");

        var expected = group.Lines
            .Where(line => line.Guest == guest)
            .Select(line => (Dish: group.Dish(line.DishLabel).Name, line.Quantity))
            .OrderBy(line => line.Dish, StringComparer.Ordinal)
            .ToList();
        var actual = card!.Lines.OrderBy(line => line.Dish, StringComparer.Ordinal).ToList();

        await Assert.That(actual).IsEquivalentTo(expected);
        await Assert.That(card.Subtotal).IsEqualTo(group.Subtotal(guest));
    }

    private static List<GuestLine> ReadLines(DataTable table)
    {
        var submitted = table.Header.Contains("submitted");
        return table.Rows.Select(row => new GuestLine(
            row["guest"],
            row["dish"],
            int.Parse(row["quantity"], System.Globalization.CultureInfo.InvariantCulture),
            submitted && row["submitted"].Equals("yes", StringComparison.OrdinalIgnoreCase))).ToList();
    }
}
