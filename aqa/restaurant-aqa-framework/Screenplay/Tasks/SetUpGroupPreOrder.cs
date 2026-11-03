using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Api.Dtos.Dishes;
using Restaurant_Aqa_Framework.Api.Dtos.GuestSessions;
using Restaurant_Aqa_Framework.Api.Dtos.Reservations;
using Restaurant_Aqa_Framework.Api.Dtos.Tables;
using Restaurant_Aqa_Framework.Api.Dtos.Waiters;
using Restaurant_Aqa_Framework.Api.Seeding;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Support;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Books a group on the standing waiter's location and seats the guests from the feature table.
/// The waiter creates the reservation, so it stays assigned to that waiter. Two guests need four seats:
/// the owner occupies one, and a guest can join only while <c>joined + 1</c> is still under the guest count.</summary>
public sealed class SetUpGroupPreOrder(IReadOnlyList<GuestLine> lines) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("A group pre-order needs at least one guest line.");

        var unknown = lines.Select(line => line.DishLabel).Distinct()
            .Except(["first", "second"], StringComparer.Ordinal)
            .ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"Unknown dish label '{unknown[0]}'. Use 'first' or 'second'.");

        var api = actor.UsingTheApi();
        var response = actor.TheLastResponse();
        var waiter = await actor.AsksForAsync(new TheWaiterSession());
        var waiterId = JwtSubject.Read(waiter.IdToken);

        await response.CaptureAsync(await new WaitersApi(api).GetLocationAsync(waiterId, waiter.IdToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Reading the waiter's location failed with {response.Status}: {response.Body}");

        var locationId = response.Json<WaiterLocationResponse>().LocationId;
        NamedDish first = null!;
        NamedDish second = null!;
        await LocationMenu.UseAsync(async () =>
        {
            first = await CreateDishAsync(actor, locationId, "first", 10m, waiter.IdToken);
            second = await CreateDishAsync(actor, locationId, "second", 20m, waiter.IdToken);
            await EnsureOnTheMenuAsync(actor, locationId, [first, second], waiter.IdToken);
        });
        var table = await CreateTableAsync(actor, locationId);
        var owner = await RegisterOwnerAsync(actor);
        var reservation = await BookAsync(actor, table.Id, owner.Email, waiter.IdToken);
        var groupId = await GroupIdAsync(actor, reservation.Id, owner.IdToken);
        IReadOnlyList<GuestSeat> guests = [];
        await LocationMenu.UseAsync(async () =>
        {
            await EnsureOnTheMenuAsync(actor, locationId, [first, second], waiter.IdToken);
            guests = await SeatGuestsAsync(actor, groupId, lines, first, second);
        });

        actor.Remember(new GroupBooking
        {
            ReservationId = reservation.Id,
            GroupReservationId = groupId,
            LocationId = locationId,
            Owner = owner,
            Dishes = [first, second],
            Lines = lines,
            Guests = guests,
        });

        var cleanup = actor.Recall<CleanupScope>();
        cleanup.TrackDelete(
            $"reservation:{reservation.Id}",
            () => new ReservationsApi(api).CancelReservationAsync(reservation.Id, owner.IdToken));
        foreach (var seat in guests)
        {
            var sessionId = seat.SessionId;
            cleanup.TrackDelete(
                $"guest:{sessionId}",
                () => new GuestSessionsApi(api).DeleteSessionAsync(groupId, sessionId, owner.IdToken));
        }
    }

    private static async Task<NamedDish> CreateDishAsync(
        Actor actor, string locationId, string label, decimal price, string waiterToken)
    {
        var api = new DishesApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();
        var request = SampleDataFactory.CreateDishRequest([locationId]) with
        {
            Price = price,
            Name = $"{SampleDataFactory.Prefix}{label}-{Guid.NewGuid():N}"[..40],
        };

        await response.CaptureAsync(await api.CreateDishAsync(request));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Adding the {label} dish failed with {response.Status}: {response.Body}");

        var dish = response.Json<DishResponse>();
        actor.Recall<CleanupScope>().TrackDelete(
            $"dish:{dish.Id}",
            () => LocationMenu.UseAsync(() => api.DeleteDishAsync(dish.Id, force: true, bearerToken: waiterToken)));
        return new NamedDish(label, dish.Id, dish.Name, price, request);
    }

    /// <summary>The location keeps its menu as one list, and a stale write can drop a dish that was just added.
    /// Put the dish back on that list until a read shows it.</summary>
    internal static async Task EnsureOnTheMenuAsync(
        Actor actor, string locationId, IReadOnlyList<NamedDish> dishes, string waiterToken)
    {
        var locations = new LocationsApi(actor.UsingTheApi());
        var menu = new DishesApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            await response.CaptureAsync(await locations.GetDishesForLocationAsync(locationId));
            if (!response.Succeeded)
                throw new InvalidOperationException($"Reading the menu failed with {response.Status}: {response.Body}");

            var listed = response.Json<List<DishListResponse>>().Select(dish => dish.Id).ToHashSet(StringComparer.Ordinal);
            var missing = dishes.Where(dish => !listed.Contains(dish.Id)).ToList();
            if (missing.Count == 0)
                return;

            foreach (var dish in missing)
            {
                if (dish.Request is null)
                    throw new InvalidOperationException($"Dish '{dish.Name}' has no saved request, so it cannot be put back on the menu.");

                await response.CaptureAsync(await menu.UpdateDishAsync(dish.Id, dish.Request, waiterToken));
                if (!response.Succeeded)
                    throw new InvalidOperationException($"Linking '{dish.Name}' to the location failed with {response.Status}: {response.Body}");
            }

            if (attempt == 6)
                throw new InvalidOperationException($"The menu is still missing {string.Join(", ", missing.Select(dish => dish.Name))}.");
        }
    }

    private static async Task<TableResponse> CreateTableAsync(Actor actor, string locationId)
    {
        var api = new TablesApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();
        await response.CaptureAsync(await api.CreateTableAsync(SampleDataFactory.CreateTableRequest(locationId, GroupBooking.Seats)));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Adding the table failed with {response.Status}: {response.Body}");

        var table = response.Json<TableResponse>();
        if (string.IsNullOrWhiteSpace(table.Id))
            throw new InvalidOperationException("Adding the table returned no id.");
        return table;
    }

    private static async Task<GroupCustomer> RegisterOwnerAsync(Actor actor)
    {
        var visitor = SampleDataFactory.RegisterRequest() with
        {
            FirstName = UniqueFirstName(),
        };
        var auth = new AuthApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await auth.RegisterAsync(visitor));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Registering the owner failed with {response.Status}: {response.Body}");

        await response.CaptureAsync(await auth.SignInAsync(new SignInRequest(visitor.Email, visitor.Password)));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Signing in the owner failed with {response.Status}: {response.Body}");

        return new GroupCustomer(
            visitor.Email,
            visitor.Password,
            response.Json<SignInResponse>().IdToken,
            visitor.FirstName,
            visitor.LastName);
    }

    /// <summary>A first name the registration rules accept (letters and hyphens only) and that is unique
    /// enough for the waiter to find this reservation among the others.</summary>
    private static string UniqueFirstName()
    {
        const string letters = "abcdefghijklmnopqrstuvwxyz";
        var suffix = new char[8];
        for (var index = 0; index < suffix.Length; index++)
            suffix[index] = letters[Random.Shared.Next(letters.Length)];

        return SampleDataFactory.Prefix + new string(suffix);
    }

    private static async Task<ReservationResponse> BookAsync(Actor actor, string tableId, string customerEmail, string waiterToken)
    {
        var start = DateTime.UtcNow.Date.AddDays(Random.Shared.Next(1, 20)).AddHours(18)
            .AddMinutes(Random.Shared.Next(0, 30));
        if (start < DateTime.UtcNow.AddMinutes(16))
            start = DateTime.UtcNow.AddHours(20);

        var request = SampleDataFactory.CreateReservationRequest(tableId, start, GroupBooking.Seats) with
        {
            CustomerEmail = customerEmail,
        };
        var response = actor.TheLastResponse();
        await response.CaptureAsync(await new ReservationsApi(actor.UsingTheApi()).CreateReservationAsync(request, waiterToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Booking the table failed with {response.Status}: {response.Body}");

        return response.Json<ReservationResponse>();
    }

    private static async Task<string> GroupIdAsync(Actor actor, string reservationId, string ownerToken)
    {
        var api = new GuestSessionsApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await api.GetInviteTokenAsync(reservationId, ownerToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Reading the invite failed with {response.Status}: {response.Body}");

        var token = response.Json<InviteTokenResponse>().Token;
        await response.CaptureAsync(await api.GetInviteAsync(token));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Opening the invite failed with {response.Status}: {response.Body}");

        return response.Json<GroupInviteResponse>().GroupReservationId;
    }

    private static async Task<IReadOnlyList<GuestSeat>> SeatGuestsAsync(
        Actor actor, string groupId, IReadOnlyList<GuestLine> lines, NamedDish first, NamedDish second)
    {
        var api = new GuestSessionsApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();
        var seats = new List<GuestSeat>();

        foreach (var guestLabel in lines.Select(line => line.Guest).Distinct())
        {
            var name = $"{SampleDataFactory.Prefix}{guestLabel}-{Guid.NewGuid():N}"[..24];
            await response.CaptureAsync(await api.CreateSessionAsync(new CreateGuestSessionRequest(groupId, name)));
            if (!response.Succeeded)
                throw new InvalidOperationException($"Seating {guestLabel} failed with {response.Status}: {response.Body}");

            var created = response.Json<CreateGuestSessionResponse>();
            var seat = new GuestSeat(guestLabel, name, created.SessionId, created.SessionToken);
            seats.Add(seat);

            foreach (var line in lines.Where(line => line.Guest == guestLabel))
            {
                var dish = line.DishLabel == first.Label ? first : second;
                await AddWhenTheLocationListsItAsync(api, response, seat, dish.Id, line.Quantity, guestLabel);
            }

            if (lines.Any(line => line.Guest == guestLabel && line.Submitted))
            {
                await response.CaptureAsync(await api.SubmitCartAsync(seat.SessionId, seat.SessionToken));
                if (!response.Succeeded)
                    throw new InvalidOperationException($"Submitting {guestLabel}'s cart failed with {response.Status}: {response.Body}");
            }
        }

        return seats;
    }

    /// <summary>A dish can be missing from the location's menu for a moment after it is created, because that
    /// menu is a scan. Try again until it is listed, and fail immediately on any other refusal.</summary>
    internal static async Task AddWhenTheLocationListsItAsync(
        GuestSessionsApi api,
        Contexts.ApiResponseContext response,
        GuestSeat seat,
        string dishId,
        int quantity,
        string guestLabel)
    {
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            await response.CaptureAsync(await api.AddDishAsync(
                seat.SessionId, new GuestDishRequest(dishId, quantity), seat.SessionToken));
            if (response.Succeeded)
                return;

            var stillIndexing = response.Status == 400
                && response.Body.Contains("not available at this location", StringComparison.Ordinal);
            if (!stillIndexing || attempt == 8)
                throw new InvalidOperationException($"Adding a dish for {guestLabel} failed with {response.Status}: {response.Body}");

            await Task.Delay(TimeSpan.FromMilliseconds(400));
        }
    }
}
