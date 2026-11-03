using Restaurant_Aqa_Framework.Api.Dtos.Dishes;

namespace Restaurant_Aqa_Framework.Screenplay;

/// <summary>One dish the scenario created, addressed by the label used in the feature table ("first", "second").</summary>
public sealed record NamedDish(string Label, string Id, string Name, decimal Price, CreateDishRequest? Request = null);

/// <summary>A row from the feature table: which guest ordered which labelled dish.</summary>
public sealed record GuestLine(string Guest, string DishLabel, int Quantity, bool Submitted);

/// <summary>A guest who joined the group. <see cref="Name"/> is what the page shows; <see cref="Label"/> is the
/// short name in the feature ("Ada").</summary>
public sealed record GuestSeat(string Label, string Name, string SessionId, string SessionToken);

public sealed record GroupCustomer(string Email, string Password, string IdToken, string FirstName, string LastName);

/// <summary>The group this scenario is working with. A second, independent group is remembered under a name.</summary>
public sealed class GroupBooking
{
    public const int Seats = 4;

    public required string ReservationId { get; init; }

    public required string GroupReservationId { get; init; }

    public required string LocationId { get; init; }

    public required GroupCustomer Owner { get; init; }

    public required IReadOnlyList<NamedDish> Dishes { get; init; }

    public required IReadOnlyList<GuestLine> Lines { get; init; }

    public required IReadOnlyList<GuestSeat> Guests { get; init; }

    public NamedDish Dish(string label) =>
        Dishes.FirstOrDefault(dish => dish.Label == label)
        ?? throw new InvalidOperationException($"This group has no dish labelled '{label}'.");

    public GuestSeat Guest(string label) =>
        Guests.FirstOrDefault(guest => guest.Label == label)
        ?? throw new InvalidOperationException($"This group has no guest called '{label}'.");

    public decimal Subtotal(string guestLabel)
    {
        var lines = Lines.Where(line => line.Guest == guestLabel).ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException($"This group has no dishes for '{guestLabel}'.");

        return lines.Sum(line => line.Quantity * Dish(line.DishLabel).Price);
    }
}

/// <summary>The grand total read from the page immediately before a guest was removed.</summary>
public sealed record GroupTotalBefore(decimal Amount);

/// <summary>The guest the owner removed, by the label used in the feature.</summary>
public sealed record RemovedGuest(string Label);
