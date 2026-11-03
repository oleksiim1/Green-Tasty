using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Signs the owner in through the storefront and opens the group pre-order.</summary>
public sealed class OpenTheGroupPreOrder : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var group = actor.Recall<GroupBooking>();
        await actor.AttemptsToAsync(new SignInThroughTheLoginForm(group.Owner.Email, group.Owner.Password));

        var page = new GroupOrderPage(actor.LookingAtThePage());
        await page.OpenAsync(group.ReservationId);
        await page.Title.WaitForAsync();
        await page.Loading.WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Hidden });
        if (await page.LoadError.IsVisibleAsync())
            throw new InvalidOperationException("The group pre-order page could not be loaded.");
    }
}
