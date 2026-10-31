using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestSessions;

public record GetAllSessionsResponse(IEnumerable<Participant> Participants, decimal Total);

public record ParticipantDishResponse(string Id, string Name, decimal Price);

public record ParticipantItemResponse(ParticipantDishResponse Dish, int Quantity);

public record Participant(
    string? Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ParticipantType>))] ParticipantType Type,
    string Name,
    IEnumerable<ParticipantItemResponse> Items,
    decimal Total,
    bool IsSubmitted);

public enum ParticipantType
{
    Owner,
    Guest
}
