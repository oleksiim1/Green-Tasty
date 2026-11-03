using Restaurant_Aqa_Framework.Support;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Removes a guest from the open group page and remembers the total from just before the click.</summary>
public sealed class RemoveGuestOnTheGroupPage(string guestLabel) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var group = actor.Recall<GroupBooking>();
        var guest = group.Guest(guestLabel);
        var page = new GroupOrderPage(actor.LookingAtThePage());

        actor.Remember(new GroupTotalBefore(MoneyText.Parse(await page.GrandTotal.InnerTextAsync())));
        actor.Remember(new RemovedGuest(guestLabel));

        await page.RemoveButton(guest.Name).ClickAsync();
        await page.ConfirmRemove.ClickAsync();
        await page.GuestHeading(guest.Name).WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Hidden });
    }
}
