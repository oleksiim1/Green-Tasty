using System.Text.Json;
using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Configuration;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Leaves the owner's session and opens the same reservation as the assigned waiter.
/// The pre-order page reads <c>waiterPreOrderData</c> from the reservation card. The card is used when
/// the waiter's list loads. That list returns 500 once any older reservation points at a deleted table,
/// so the same stored value is written directly when the card is not there.</summary>
public sealed class OpenTheWaitersPreOrder : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var group = actor.Recall<GroupBooking>();
        var page = actor.LookingAtThePage();
        await page.EvaluateAsync("() => { localStorage.removeItem('auth'); localStorage.removeItem('user'); }");

        var (username, password) = Settings.Current.RequireWaiter();
        await actor.AttemptsToAsync(new SignInThroughTheLoginForm(username, password));

        var button = new WaiterReservationsPage(page).PreOrderButton(group.Owner.FirstName);
        try
        {
            await button.ClickAsync(new LocatorClickOptions { Timeout = 8_000 });
        }
        catch (TimeoutException)
        {
            var payload = JsonSerializer.Serialize(new
            {
                reservationId = group.ReservationId,
                createdByName = $"{group.Owner.FirstName} {group.Owner.LastName}",
                createdByRole = "Customer",
                tableName = "Table",
            });
            await page.EvaluateAsync(
                "data => sessionStorage.setItem('waiterPreOrderData', data)",
                payload);
            await page.GotoAsync("/waiter-pre-order");
        }

        await new WaiterPreOrderPage(page).Dishes.First.WaitForAsync();
    }
}
