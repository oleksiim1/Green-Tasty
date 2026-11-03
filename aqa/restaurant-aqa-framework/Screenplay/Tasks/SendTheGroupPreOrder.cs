using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Orders;
using Restaurant_Aqa_Framework.Ui;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Sends the consolidated pre-order. The restaurant keeps an order row for the reservation, so one is
/// created first when the booking does not have one yet; confirming replaces its dishes with the submitted carts.</summary>
public sealed class SendTheGroupPreOrder : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var group = actor.Recall<GroupBooking>();
        await EnsureOrderExistsAsync(actor, group);

        var page = new GroupOrderPage(actor.LookingAtThePage());
        await page.SubmitButton.ClickAsync();
        await page.ConfirmSubmit.ClickAsync();
        await page.SentToTheRestaurant.WaitForAsync();
    }

    private static async Task EnsureOrderExistsAsync(Actor actor, GroupBooking group)
    {
        var orders = new OrdersApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();
        await response.CaptureAsync(await orders.GetOrderAsync(group.ReservationId, group.Owner.IdToken));
        if (response.Succeeded)
            return;

        if (response.Status != 404)
            throw new InvalidOperationException($"Reading the order failed with {response.Status}: {response.Body}");

        var dish = group.Dishes[0];
        await response.CaptureAsync(await orders.CreateOrderAsync(
            new CreateOrderRequest(group.ReservationId, [new OrderItemRequest(dish.Id, 1)]),
            group.Owner.IdToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Creating the order failed with {response.Status}: {response.Body}");
    }
}
