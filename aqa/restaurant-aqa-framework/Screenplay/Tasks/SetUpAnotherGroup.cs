using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Api.Dtos.GuestSessions;
using Restaurant_Aqa_Framework.Api.Dtos.Reservations;
using Restaurant_Aqa_Framework.Api.Dtos.Tables;
using Restaurant_Aqa_Framework.Api.Seeding;
using Restaurant_Aqa_Framework.Screenplay.Questions;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>A second group, on its own table, so a guest of that group can be aimed at the first group.
/// Remembers it under "other" and leaves the original group as the current one. Shares the original
/// dishes and does not schedule them for removal again.</summary>
public sealed class SetUpAnotherGroup(string guestLabel) : ITask
{
    public const string MemoryName = "other";

    public async Task PerformAsAsync(Actor actor)
    {
        var primary = actor.Recall<GroupBooking>();
        var api = actor.UsingTheApi();
        var response = actor.TheLastResponse();
        var waiter = await actor.AsksForAsync(new TheWaiterSession());

        var tables = new TablesApi(api);
        await response.CaptureAsync(await tables.CreateTableAsync(
            SampleDataFactory.CreateTableRequest(primary.LocationId, GroupBooking.Seats), waiter.IdToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Adding the other table failed with {response.Status}: {response.Body}");

        var table = response.Json<TableResponse>();
        if (string.IsNullOrWhiteSpace(table.Id))
            throw new InvalidOperationException("Adding the other table returned no id.");

        var visitor = SampleDataFactory.RegisterRequest();
        var auth = new AuthApi(api);
        await response.CaptureAsync(await auth.RegisterAsync(visitor));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Registering the other owner failed with {response.Status}: {response.Body}");

        await response.CaptureAsync(await auth.SignInAsync(new SignInRequest(visitor.Email, visitor.Password)));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Signing in the other owner failed with {response.Status}: {response.Body}");

        var owner = new GroupCustomer(
            visitor.Email, visitor.Password, response.Json<SignInResponse>().IdToken, visitor.FirstName, visitor.LastName);

        var start = DateTime.UtcNow.Date.AddDays(Random.Shared.Next(21, 40)).AddHours(19);
        var reservationRequest = SampleDataFactory.CreateReservationRequest(table.Id, start, GroupBooking.Seats) with
        {
            CustomerEmail = owner.Email,
        };
        await response.CaptureAsync(await new ReservationsApi(api).CreateReservationAsync(reservationRequest, waiter.IdToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Booking the other table failed with {response.Status}: {response.Body}");

        var reservation = response.Json<ReservationResponse>();
        var sessions = new GuestSessionsApi(api);
        await response.CaptureAsync(await sessions.GetInviteTokenAsync(reservation.Id, owner.IdToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Reading the other invite failed with {response.Status}: {response.Body}");

        var token = response.Json<InviteTokenResponse>().Token;
        await response.CaptureAsync(await sessions.GetInviteAsync(token));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Opening the other invite failed with {response.Status}: {response.Body}");

        var groupId = response.Json<GroupInviteResponse>().GroupReservationId;
        var name = $"{SampleDataFactory.Prefix}{guestLabel}-{Guid.NewGuid():N}"[..24];
        await response.CaptureAsync(await sessions.CreateSessionAsync(new CreateGuestSessionRequest(groupId, name)));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Seating {guestLabel} failed with {response.Status}: {response.Body}");

        var created = response.Json<CreateGuestSessionResponse>();
        var dish = primary.Dishes[0];
        var seat = new GuestSeat(guestLabel, name, created.SessionId, created.SessionToken);
        await LocationMenu.UseAsync(async () =>
        {
            await SetUpGroupPreOrder.EnsureOnTheMenuAsync(actor, primary.LocationId, [dish], owner.IdToken);
            await SetUpGroupPreOrder.AddWhenTheLocationListsItAsync(sessions, response, seat, dish.Id, 1, guestLabel);
        });

        actor.Remember(MemoryName, new GroupBooking
        {
            ReservationId = reservation.Id,
            GroupReservationId = groupId,
            LocationId = primary.LocationId,
            Owner = owner,
            Dishes = primary.Dishes,
            Lines = [new GuestLine(guestLabel, dish.Label, 1, false)],
            Guests = [new GuestSeat(guestLabel, name, created.SessionId, created.SessionToken)],
        });
        actor.Remember(primary);

        var cleanup = actor.Recall<CleanupScope>();
        cleanup.TrackDelete(
            $"reservation:{reservation.Id}",
            () => new ReservationsApi(api).CancelReservationAsync(reservation.Id, owner.IdToken));
        cleanup.TrackDelete(
            $"guest:{created.SessionId}",
            () => sessions.DeleteSessionAsync(groupId, created.SessionId, owner.IdToken));
    }
}
