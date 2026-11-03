using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Api.Seeding;
using Restaurant_Aqa_Framework.Screenplay.Questions;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Deletes a guest session and leaves the response as the scenario's last outcome. A refusal is a
/// successful outcome for the scenarios that expect one, so those pass <paramref name="mustSucceed"/> false.</summary>
public sealed class RemoveGuestThroughTheService(
    string groupReservationId,
    string guestSessionId,
    string? bearerToken,
    bool mustSucceed = true) : ITask
{
    public static RemoveGuestThroughTheService AsTheOwner(string guestLabel)
    {
        return new RemoveGuestThroughTheService("", "", null) { Kind = RemovalKind.Owner, GuestLabel = guestLabel };
    }

    public static RemoveGuestThroughTheService AsTheWaiter(string guestLabel) =>
        new(RemovalKind.Waiter, guestLabel);

    public static RemoveGuestThroughTheService AsAnotherCustomer(string guestLabel) =>
        new(RemovalKind.AnotherCustomer, guestLabel);

    public static RemoveGuestThroughTheService WithoutSigningIn(string guestLabel) =>
        new(RemovalKind.Anonymous, guestLabel);

    public static RemoveGuestThroughTheService MissingGuest() =>
        new(RemovalKind.MissingGuest, null);

    public static RemoveGuestThroughTheService MissingGroup() =>
        new(RemovalKind.MissingGroup, null);

    public static RemoveGuestThroughTheService GuestOfTheOtherGroup() =>
        new(RemovalKind.OtherGroup, null);

    private enum RemovalKind
    {
        Direct,
        Owner,
        Waiter,
        AnotherCustomer,
        Anonymous,
        MissingGuest,
        MissingGroup,
        OtherGroup,
    }

    private RemovalKind Kind { get; init; } = RemovalKind.Direct;

    private string? GuestLabel { get; init; }

    private RemoveGuestThroughTheService(RemovalKind kind, string? guestLabel)
        : this("", "", null, mustSucceed: false)
    {
        Kind = kind;
        GuestLabel = guestLabel;
    }

    public async Task PerformAsAsync(Actor actor)
    {
        var (groupId, sessionId, token, succeed) = await ResolveAsync(actor);
        var response = actor.TheLastResponse();
        await response.CaptureAsync(
            await new GuestSessionsApi(actor.UsingTheApi()).DeleteSessionAsync(groupId, sessionId, token));

        if (succeed && !response.Succeeded)
            throw new InvalidOperationException($"Removing the guest failed with {response.Status}: {response.Body}");
    }

    private async Task<(string GroupId, string SessionId, string? Token, bool Succeed)> ResolveAsync(Actor actor)
    {
        if (Kind == RemovalKind.Direct)
            return (groupReservationId, guestSessionId, bearerToken, mustSucceed);

        var group = actor.Recall<GroupBooking>();
        var guest = GuestLabel is null ? null : group.Guest(GuestLabel);

        return Kind switch
        {
            RemovalKind.Owner => (group.GroupReservationId, guest!.SessionId, group.Owner.IdToken, true),
            RemovalKind.Waiter => (group.GroupReservationId, guest!.SessionId,
                (await actor.AsksForAsync(new TheWaiterSession())).IdToken, false),
            RemovalKind.AnotherCustomer => (group.GroupReservationId, guest!.SessionId,
                await AnotherCustomerTokenAsync(actor), false),
            RemovalKind.Anonymous => (group.GroupReservationId, guest!.SessionId, null, false),
            RemovalKind.MissingGuest => (group.GroupReservationId, Guid.NewGuid().ToString(), group.Owner.IdToken, false),
            RemovalKind.MissingGroup => (Guid.NewGuid().ToString(), guest?.SessionId ?? Guid.NewGuid().ToString(),
                group.Owner.IdToken, false),
            RemovalKind.OtherGroup => (
                group.GroupReservationId,
                actor.Recall<GroupBooking>(SetUpAnotherGroup.MemoryName).Guests[0].SessionId,
                group.Owner.IdToken,
                false),
            _ => throw new InvalidOperationException($"Unknown removal '{Kind}'."),
        };
    }

    private static async Task<string> AnotherCustomerTokenAsync(Actor actor)
    {
        var visitor = SampleDataFactory.RegisterRequest();
        var auth = new AuthApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await auth.RegisterAsync(visitor));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Registering the other customer failed with {response.Status}: {response.Body}");

        await response.CaptureAsync(await auth.SignInAsync(new SignInRequest(visitor.Email, visitor.Password)));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Signing in the other customer failed with {response.Status}: {response.Body}");

        var token = response.Json<SignInResponse>().IdToken;
        return token;
    }
}
