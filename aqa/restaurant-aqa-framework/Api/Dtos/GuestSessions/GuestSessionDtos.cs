namespace Restaurant_Aqa_Framework.Api.Dtos.GuestSessions;

public sealed record InviteTokenResponse(string Token);

public sealed record GroupInviteResponse(string GroupReservationId);

public sealed record CreateGuestSessionRequest(string GroupReservationId, string GuestName);

public sealed record CreateGuestSessionResponse(string SessionId, string SessionToken);

public sealed record GuestDishRequest(string DishId, int Quantity);

public sealed record SubmitGroupPreOrderRequest(string ReservationId);
